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
        private const double PlaybackDrainEpsilon = 0.03;

        private readonly Func<OpenAICompatibleClient> _clientAccessor;
        private readonly ConcurrentQueue<TtsJob> _pendingJobs = new ConcurrentQueue<TtsJob>();
        private readonly ConcurrentDictionary<int, TtsJob> _completedJobs = new ConcurrentDictionary<int, TtsJob>();
        private readonly SemaphoreSlim _generationSemaphore;
        private readonly ResourceMonitor _resourceMonitor;
        private readonly object _sessionLock = new object();
        private readonly object _playbackLock = new object();
        private readonly object _stateLock = new object();
        private readonly object _inFlightLock = new object();
        private readonly HashSet<string> _inFlightSentences = new HashSet<string>(StringComparer.Ordinal);
        private Action<Action> _mainThreadDispatcher;
        private SynchronizationContext _mainThreadContext;
        private int _mainThreadId;

        private TtsPipelineConfig _config;
        private double _targetBufferedSeconds;
        private string _projectRootPath;
        private CancellationTokenSource _sessionCts;
        private Task _orchestratorTask = Task.CompletedTask;
        private long _sessionId;
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
            _generationSemaphore = new SemaphoreSlim(_resourceMonitor.CurrentParallelism);
            _config = TtsPipelineConfig.Default;
            _targetBufferedSeconds = _config.StreamingBufferSeconds;
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

                if (config.MaxParallelGenerations > 0)
                {
                    // Semaphore doesn't support dynamic resize, we'll respect it on new sessions
                }

                if (config.UseLocalTts && config.LocalSynthesizer != null)
                {
                    AttachLocalSynthCallbacks(config.LocalSynthesizer);
                }

                _targetBufferedSeconds = Mathf.Clamp(config.StreamingBufferSeconds, 0.05f, 0.4f);
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
            Task taskToWait;
            long oldSessionId;

            lock (_sessionLock)
            {
                oldSessionId = _sessionId;

                if (_sessionCts != null)
                {
                    try
                    {
                        if (!_sessionCts.IsCancellationRequested)
                        {
                            _sessionCts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    _sessionCts.Dispose();
                    _sessionCts = null;
                }

                taskToWait = _orchestratorTask;
            }

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

            Task taskToWait;
            lock (_sessionLock)
            {
                taskToWait = _orchestratorTask;
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

            lock (_sessionLock)
            {
                if (_sessionCts != null)
                {
                    try
                    {
                        if (!_sessionCts.IsCancellationRequested)
                        {
                            _sessionCts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    _sessionCts.Dispose();
                    _sessionCts = null;
                }
            }

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
