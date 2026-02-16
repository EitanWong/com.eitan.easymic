#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.Components;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// High-performance TTS pipeline with parallel generation and ordered sequential playback.
    /// Dynamically adjusts parallelism based on system resources.
    /// </summary>
    internal sealed partial class ChatTtsPipeline : IChatTtsPipeline
    {
        private const string RemoteFormat = "pcm";
        private const int RemoteDefaultSampleRate = 24000;
        private const int RemoteDefaultChannels = 1;
        private const int MaxQueuedJobs = 100;
        private const int PlaybackPollDelayMs = 15;
        private const double PlaybackDrainEpsilon = 0.08;
        private const double AdaptiveBufferDefaultSeconds = 0.18;
        private const double AdaptiveBufferMinSeconds = 0.10;
        private const double AdaptiveBufferMaxSeconds = 0.36;
        private const double AdaptiveBufferIncreaseStep = 0.03;
        private const double AdaptiveBufferDecreaseStep = 0.01;
        private const double AdaptiveUnderrunThresholdSeconds = 0.10;
        private const int AdaptiveUnderrunsBeforeIncrease = 2;
        private const int AdaptiveStableCyclesBeforeDecrease = 90;

        private readonly Func<OpenAICompatibleClient> _clientAccessor;
        private readonly ConcurrentQueue<TtsJob> _pendingJobs = new ConcurrentQueue<TtsJob>();
        private readonly ConcurrentDictionary<int, TtsJob> _completedJobs = new ConcurrentDictionary<int, TtsJob>();
        private readonly SemaphoreSlim _generationSemaphore;
        private readonly ResourceMonitor _resourceMonitor;
        private readonly TtsPipelineSession _session = new TtsPipelineSession();
        private readonly object _playbackLock = new object();
        private readonly object _stateLock = new object();
        private readonly object _inFlightLock = new object();
        private readonly object _adaptiveBufferLock = new object();
        private readonly HashSet<string> _inFlightSentences = new HashSet<string>(StringComparer.Ordinal);
        private Action<Action> _mainThreadDispatcher;
        private SynchronizationContext _mainThreadContext;
        private int _mainThreadId;

        private TtsPipelineConfig _config;
        private double _adaptiveBufferSeconds = AdaptiveBufferDefaultSeconds;
        private int _adaptiveUnderrunSignals;
        private int _adaptiveStableCycles;
        private string _projectRootPath;
        private int _nextSequenceNumber;
        private int _nextPlaybackSequence;
        private volatile bool _disposed;
        private volatile bool _isSpeaking;

        private PlaybackSink _playbackSink;
        private PlaybackAudioSourceBehaviour _playbackSource;
        private bool _playbackInitialized;
        private long _playbackSessionId;

        private bool _localSynthCallbacksBound;
        private SpeechSynthesizer _boundLocalSynthesizer;

        public event Action<bool> OnSpeakingStateChanged;
        public event Action<string> OnSentenceStarted;
        public event Action<string> OnSentenceCompleted;
        public event Action<float> OnBufferProgress;

        public bool IsSpeaking => _isSpeaking;

        public int QueuedSentenceCount
        {
            get
            {
                int pending = _pendingJobs.Count;
                int completed = _completedJobs.Count;
                return pending + completed;
            }
        }

        public ChatTtsPipeline(Func<OpenAICompatibleClient> clientAccessor)
        {
            _clientAccessor = clientAccessor ?? throw new ArgumentNullException(nameof(clientAccessor));
            _resourceMonitor = new ResourceMonitor();
            _generationSemaphore = new SemaphoreSlim(1);
            _config = TtsPipelineConfig.Default;
            ResetAdaptiveBufferState();
        }

        public void Configure(TtsPipelineConfig config)
        {
            lock (_stateLock)
            {
                DetachLocalSynthCallbacks();

                _config = config;
                _mainThreadDispatcher = config.MainThreadDispatcher;
                CacheMainThreadContext();
                CacheProjectRootPath();
                ConfigurePlayback(config);

                if (config.UseLocalTts && config.LocalSynthesizer != null)
                {
                    AttachLocalSynthCallbacks(config.LocalSynthesizer);
                }
            }

            ResetAdaptiveBufferState();
        }

        private void CacheProjectRootPath()
        {
            if (!string.IsNullOrEmpty(_projectRootPath))
            {
                return;
            }

            try
            {
                _projectRootPath = System.IO.Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
            }
            catch
            {
                _projectRootPath = null;
            }

            if (string.IsNullOrWhiteSpace(_projectRootPath))
            {
                _projectRootPath = System.Environment.CurrentDirectory;
            }
        }

        private void ResetAdaptiveBufferState()
        {
            lock (_adaptiveBufferLock)
            {
                _adaptiveBufferSeconds = AdaptiveBufferDefaultSeconds;
                _adaptiveUnderrunSignals = 0;
                _adaptiveStableCycles = 0;
            }
        }

        private double GetAdaptiveBufferBudgetSeconds()
        {
            lock (_adaptiveBufferLock)
            {
                return _adaptiveBufferSeconds;
            }
        }

        private void ReportAdaptiveUnderrun()
        {
            double updatedBudget = 0d;
            bool changed = false;

            lock (_adaptiveBufferLock)
            {
                _adaptiveStableCycles = 0;
                _adaptiveUnderrunSignals++;

                if (_adaptiveUnderrunSignals < AdaptiveUnderrunsBeforeIncrease)
                {
                    return;
                }

                _adaptiveUnderrunSignals = 0;
                double next = Math.Min(AdaptiveBufferMaxSeconds, _adaptiveBufferSeconds + AdaptiveBufferIncreaseStep);
                if (next > _adaptiveBufferSeconds + 0.0001d)
                {
                    _adaptiveBufferSeconds = next;
                    updatedBudget = next;
                    changed = true;
                }
            }

            if (changed && _config.LogSentences)
            {
                Debug.Log($"[ParallelTtsPipeline] Adaptive buffer increased to {updatedBudget:0.00}s");
            }
        }

        private void ReportAdaptiveStability(double bufferedSeconds)
        {
            double updatedBudget = 0d;
            bool changed = false;

            lock (_adaptiveBufferLock)
            {
                _adaptiveUnderrunSignals = 0;

                if (bufferedSeconds < (_adaptiveBufferSeconds * 0.85d))
                {
                    _adaptiveStableCycles = 0;
                    return;
                }

                _adaptiveStableCycles++;
                if (_adaptiveStableCycles < AdaptiveStableCyclesBeforeDecrease)
                {
                    return;
                }

                _adaptiveStableCycles = 0;
                double next = Math.Max(AdaptiveBufferMinSeconds, _adaptiveBufferSeconds - AdaptiveBufferDecreaseStep);
                if (next < _adaptiveBufferSeconds - 0.0001d)
                {
                    _adaptiveBufferSeconds = next;
                    updatedBudget = next;
                    changed = true;
                }
            }

            if (changed && _config.LogSentences)
            {
                Debug.Log($"[ParallelTtsPipeline] Adaptive buffer decreased to {updatedBudget:0.00}s");
            }
        }

        public void Enqueue(string sentence)
        {
            if (_disposed || string.IsNullOrWhiteSpace(sentence))
            {
                return;
            }

            string trimmed = sentence.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }

            if (_config.UseLocalTts && _config.LocalSynthesizer != null)
            {
                _config.LocalSynthesizer.EnqueueSentence(trimmed);
                return;
            }

            if (_pendingJobs.Count >= MaxQueuedJobs)
            {
                Debug.LogWarning("[ParallelTtsPipeline] Queue full, dropping sentence.");
                return;
            }

            if (!TryRegisterInFlightSentence(trimmed))
            {
                return;
            }

            int seq = Interlocked.Increment(ref _nextSequenceNumber);
            var job = new TtsJob(seq, trimmed);
            _pendingJobs.Enqueue(job);

            EnsureOrchestratorRunning();
        }

        public void Stop()
        {
            _ = StopAndWaitAsync();
        }

        public async Task StopAndWaitAsync()
        {
            var stopState = _session.CancelAndGetTask();
            long oldSessionId = stopState.sessionId;
            Task taskToWait = stopState.task;

            ClearQueues();

            if (_config.UseLocalTts && _config.LocalSynthesizer != null)
            {
                try
                {
                    await _config.LocalSynthesizer.StopAndWaitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParallelTtsPipeline] Error stopping local synth: {ex.Message}");
                }
            }

            if (taskToWait != null && !taskToWait.IsCompleted)
            {
                try
                {
                    await taskToWait.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParallelTtsPipeline] Error waiting for orchestrator: {ex.Message}");
                }
            }

            lock (_playbackLock)
            {
                if (_playbackSessionId == oldSessionId)
                {
                    DisposePlaybackUnsafe();
                }
            }

            NotifySpeakingState(false);
        }

        public async Task WaitForIdleAsync()
        {
            if (_config.UseLocalTts)
            {
                return;
            }

            Task taskToWait = _session.GetTask();

            if (taskToWait != null && !taskToWait.IsCompleted)
            {
                try
                {
                    await taskToWait.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ParallelTtsPipeline] Error waiting for idle: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _session.Dispose();

            ClearQueues();
            DetachLocalSynthCallbacks();

            lock (_playbackLock)
            {
                DisposePlaybackUnsafe();
            }

            _generationSemaphore.Dispose();
        }
    }
}
#endif
