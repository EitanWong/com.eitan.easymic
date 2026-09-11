#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.Components;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
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
        private const int MaxParallelTtsRequests = 2;
        private const int PlaybackPollDelayMs = 5;
        private const double PlaybackDrainEpsilon = 0.001;
        private const double InitialStreamingPrebufferSeconds = 0.04;
        private const double MinimumStreamingChunkSeconds = 0.03;
        private const double BufferedPlaybackChunkSeconds = 0.06;
        private const int StreamingFlushTimeoutMs = 15;
        private const double StreamingStallWarningSeconds = 1.2;
        private const double StreamingStallAbortSeconds = 8.0;
        private const double AdaptiveBufferDefaultSeconds = 0.08;
        private const double AdaptiveBufferMinSeconds = 0.06;
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
        private readonly object _queueStateLock = new object();
        private readonly object _playbackLock = new object();
        private readonly object _stateLock = new object();
        private readonly object _speakingStateLock = new object();
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
        private float _playbackVolume = 1f;
        private bool _playbackInitialized;
        private long _playbackSessionId;
        private long _activeTurnId;
        private long _highestTurnId;
        private long _completedInputTurnId;
        private long _speakingTurnId;
        private int _restartAfterCurrentSessionRequested;
        private int _generationSemaphoreDisposeRequested;

        private bool _localSynthCallbacksBound;
        private SpeechSynthesizer _boundLocalSynthesizer;

        public event Action<long, bool> OnSpeakingStateChanged;
        public event Action<long, string> OnSentenceStarted;
        public event Action<long, string> OnSentenceCompleted;
        public event Action<long, float> OnBufferProgress;
        public event Action<float[], int, int, int> OnPlaybackAudioQueued;

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
            _generationSemaphore = new SemaphoreSlim(MaxParallelTtsRequests, MaxParallelTtsRequests);
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

        public void ConfigureDiagnostics(bool logSentences, bool enableDiagnostics)
        {
            lock (_stateLock)
            {
                _config.LogSentences = logSentences;
                _config.EnableDiagnostics = enableDiagnostics;
            }
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

        private TtsPipelineConfig GetConfigSnapshot()
        {
            lock (_stateLock)
            {
                return _config;
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

            if (changed && GetConfigSnapshot().LogSentences)
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

            if (changed && GetConfigSnapshot().LogSentences)
            {
                Debug.Log($"[ParallelTtsPipeline] Adaptive buffer decreased to {updatedBudget:0.00}s");
            }
        }

        public bool BeginTurn(long turnId)
        {
            if (_disposed || turnId <= 0)
            {
                return false;
            }

            lock (_queueStateLock)
            {
                if (_disposed || turnId <= _highestTurnId)
                {
                    return false;
                }

                _highestTurnId = turnId;
                Volatile.Write(ref _activeTurnId, turnId);
                Volatile.Write(ref _completedInputTurnId, 0);
                return true;
            }
        }

        public bool Enqueue(long turnId, string sentence)
        {
            if (_disposed || turnId <= 0 || string.IsNullOrWhiteSpace(sentence))
            {
                return false;
            }

            string trimmed = sentence.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            var config = GetConfigSnapshot();
            bool useLocalTts = config.UseLocalTts && config.LocalSynthesizer != null;
            lock (_queueStateLock)
            {
                if (_disposed || turnId != Volatile.Read(ref _activeTurnId))
                {
                    return false;
                }

                if (useLocalTts)
                {
                    config.LocalSynthesizer.EnqueueSentence(trimmed);
                }
                else
                {
                    if (_pendingJobs.Count >= MaxQueuedJobs)
                    {
                        Debug.LogWarning("[ParallelTtsPipeline] Queue full, dropping sentence.");
                        return false;
                    }

                    if (!TryRegisterInFlightSentence(trimmed))
                    {
                        return false;
                    }

                    int seq = Interlocked.Increment(ref _nextSequenceNumber);
                    var job = new TtsJob(seq, turnId, trimmed);
                    _pendingJobs.Enqueue(job);
                }
            }

            NotifySpeakingState(turnId, true);
            if (!useLocalTts)
            {
                EnsureOrchestratorRunning();
            }

            return true;
        }

        public void CompleteTurn(long turnId)
        {
            if (_disposed || turnId <= 0)
            {
                return;
            }

            lock (_queueStateLock)
            {
                if (_disposed || turnId != Volatile.Read(ref _activeTurnId))
                {
                    return;
                }

                Volatile.Write(ref _completedInputTurnId, turnId);
            }

            if (!_pendingJobs.IsEmpty || !_completedJobs.IsEmpty || IsSpeaking)
            {
                EnsureOrchestratorRunning();
            }
        }

        private bool IsTurnInputComplete(long turnId)
        {
            return turnId > 0 && Interlocked.Read(ref _completedInputTurnId) == turnId;
        }

        public void Stop()
        {
            _ = StopAndWaitAsync();
        }

        public async Task StopAndWaitAsync()
        {
            var config = GetConfigSnapshot();
            (long sessionId, Task task) stopState;
            long oldTurnId;
            lock (_queueStateLock)
            {
                stopState = _session.CancelAndGetTask();
                oldTurnId = Volatile.Read(ref _activeTurnId);
                Volatile.Write(ref _activeTurnId, 0);
                Volatile.Write(ref _completedInputTurnId, 0);
                ClearQueuesUnsafe();
            }

            long oldSessionId = stopState.sessionId;
            Task taskToWait = stopState.task;

            lock (_playbackLock)
            {
                if (_playbackSessionId == oldSessionId)
                {
                    DisposePlaybackUnsafe();
                }
            }

            NotifySpeakingState(oldTurnId, false);

            if (config.UseLocalTts && config.LocalSynthesizer != null)
            {
                try
                {
                    await config.LocalSynthesizer.StopAndWaitAsync().ConfigureAwait(false);
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

            if (!_disposed && !GetConfigSnapshot().UseLocalTts && !_pendingJobs.IsEmpty)
            {
                EnsureOrchestratorRunning();
            }
        }

        public async Task WaitForIdleAsync()
        {
            if (GetConfigSnapshot().UseLocalTts)
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
            var stopState = _session.CancelAndGetTask();

            ClearQueues();
            DetachLocalSynthCallbacks();

            lock (_playbackLock)
            {
                DisposePlaybackUnsafe();
            }

            DisposeGenerationSemaphoreWhenTaskCompletes(stopState.task);
        }

        private void DisposeGenerationSemaphoreWhenTaskCompletes(Task task)
        {
            if (Interlocked.Exchange(ref _generationSemaphoreDisposeRequested, 1) == 1)
            {
                return;
            }

            if (task == null || task.IsCompleted)
            {
                _generationSemaphore.Dispose();
                return;
            }

            task.ContinueWith(
                _ => _generationSemaphore.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
#endif
