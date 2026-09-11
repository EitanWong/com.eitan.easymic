#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.Internal;
using Eitan.EasyMic.Runtime.Mono.Components;
using Eitan.SherpaONNXUnity.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS
{

    [AddComponentMenu("SherpaONNX/Speech Synthesis/EasyMic Playback Speech Synthesizer")]
    [RequireComponent(typeof(PlaybackAudioSourceBehaviour))]
    public class SpeechSynthesizer : MonoBehaviour
    {
        #region Public Properties & Events
        [Header("Initialization")]
        [SerializeField] private bool _initOnAwake = true;
        public bool InitOnAwake => _initOnAwake;
        public bool Initialized { get; private set; }
        public bool IsProcessingTTS => _ttsInProgress > 0;

        [Header("Configuration")]
        [SerializeField] private SpeechSynthesizerConfiguration _ttsConfig;

        [Header("Playback")]
        [SerializeField] private PlaybackAudioSourceBehaviour _playbackSource;
        [Tooltip("Balance local model loudness with bounded automatic gain, a silence gate and peak protection.")]
        [SerializeField] private bool _normalizeOutput = true;
        [Tooltip("Local speech volume after automatic levelling. 0 mutes; 1 is the normal level. Peak protection stays active.")]
        [Range(0f, 2f)] [SerializeField] private float _playbackVolume = 1f;
        [Range(0.02f, 0.5f)]
        [SerializeField, HideInInspector] private float _targetBufferedSeconds = 0.12f;

        [Header("Performance")]
        [Range(1, 8)]
        [SerializeField, HideInInspector] private int _maxParallelSynthesis = 2;

        [Header("Logging")]
        [SerializeField] private bool _enableLog = false;

        public bool EnableLog { get => _enableLog; set => _enableLog = value; }
        public bool NormalizeOutput { get => _normalizeOutput; set => _normalizeOutput = value; }
        public float PlaybackVolume
        {
            get => _playbackVolume;
            set => _playbackVolume = float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp(value, 0f, 2f);
        }
        public float OutputInputRmsDb => _outputInputRmsDb;
        public float OutputPeakDb => _outputPeakDb;
        public float OutputGainDb => _outputGainDb;
        private volatile float _outputInputRmsDb = -120f;
        private volatile float _outputPeakDb = -120f;
        private volatile float _outputGainDb;

        public event Action<string, float> OnLoadingProgressFeedback;
        public event Action<FailedFeedback> OnLoadingFailedFeedback;
        public event Action<SuccessFeedback> OnLoadingSuccessedFeedback;
        public event Action<bool> OnSynthesizerInitialized;
        public event Action<bool> OnTTSStateChanged;
        public event Action<string> OnSentenceStarted;
        public event Action<string> OnSentenceFinished;
        /// <summary>Raised on the Unity thread when a sentence's first audio is accepted for playback.</summary>
        public event Action<string> OnSentencePlaybackStarted;
        public PlaybackAudioSourceBehaviour PlaybackSource => _playbackSource;
        public SpeechSynthesizerConfiguration TtsConfig => _ttsConfig ??= SpeechSynthesizerConfiguration.CreateDefault();
        public int ActiveSynthesisJobs => Volatile.Read(ref _activeSynthesisJobs);
        public int MaxParallelSynthesis
        {
            get => _maxParallelSynthesis;
            set
            {
                _maxParallelSynthesis = ClampInt(value, 1, MaxParallelCap);
                InitializeAdaptiveScheduling();
            }
        }
        #endregion

        #region Private Fields
        private Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis _speechSynthesis;
        private readonly SemaphoreSlim _configurationReloadGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private CancellationTokenSource _componentEnabledCts = new CancellationTokenSource();
        private Coroutine _modelInitializationCoroutine;
        private int _modelInitializationGeneration;
        private int _modelInitializationCompletedGeneration;
        private int _destroyState;
        private bool _restartInitializationOnEnable;
        private volatile bool _initializing;
        private int _ttsInProgress; // 使用Interlocked操作

        private readonly ConcurrentQueue<string> _sentenceQueue = new ConcurrentQueue<string>();
        private Coroutine _ttsPumpCoroutine;

        private ModelLoadProgressRouter _modelLoadProgress;

        private const int PlaybackPollDelayMs = 20;
        private const float AutoTuneIntervalSeconds = 0.5f;
        private const float MinBufferedSeconds = 0.02f;
        private const float MaxBufferedSeconds = 0.5f;
        private const int MaxParallelCap = 8;
        private int _activeSynthesisJobs;
        private float _adaptiveBufferedSeconds;
        private int _adaptiveMaxParallel;
        private Coroutine _adaptiveTuningCoroutine;
        private AdaptiveSynthesisScheduler _adaptiveScheduler;

        // 会话管理 - 核心修复
        private readonly object _sessionLock = new object();
        private CancellationTokenSource _sessionCts;
        private Task _currentSessionTask = Task.CompletedTask;
        private long _sessionId; // 用于识别当前会话

        private readonly object _playbackLock = new object();
        private long _playbackSessionId = -1; // 跟踪playback属于哪个会话

        private string _logPrefix;
        private string LogPrefix => !string.IsNullOrEmpty(_logPrefix) ? _logPrefix : "[SpeechSynthesizer] ";

        private int _unityThreadId;
        private SynchronizationContext _unityContext;
        private int _processorCount = 1;
        #endregion

        #region Logging
        private void LogWarning(string message)
        {
            if (!_enableLog)
            {
                return;
            }
            Debug.LogWarning(message, this);
        }

        private void LogError(string message)
        {
            Debug.LogError(message, this);
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            CaptureUnityThreadContext();
            _processorCount = Math.Max(1, SystemInfo.processorCount);
            _ttsConfig ??= SpeechSynthesizerConfiguration.CreateDefault();
            _modelLoadProgress = new ModelLoadProgressRouter();
            _logPrefix = $"[SpeechSynthesizer:{name}] ";
            _playbackSource ??= GetComponent<PlaybackAudioSourceBehaviour>();
            if (_playbackSource == null)
            {
                LogError($"{LogPrefix}PlaybackAudioSourceBehaviour component is required.");
            }

            InitializeAdaptiveScheduling();
            _modelLoadProgress.OnProgress += (msg, progress) => OnLoadingProgressFeedback?.Invoke(msg, progress);
            _modelLoadProgress.OnFailed += feedback => OnLoadingFailedFeedback?.Invoke(feedback);
            _modelLoadProgress.OnSuccess += feedback => OnLoadingSuccessedFeedback?.Invoke(feedback);
        }

        private void OnEnable()
        {
            if (IsDestroyed)
            {
                return;
            }

            EnsureComponentEnabledCancellationSource();

            CaptureUnityThreadContext();
            EnsurePlaybackSource(_sessionId);
            if (_speechSynthesis != null && _ttsPumpCoroutine == null)
            {
                _ttsPumpCoroutine = StartCoroutine(TTSPumpLoop());
            }
            StartAdaptiveScheduling();

            if (_restartInitializationOnEnable)
            {
                _restartInitializationOnEnable = false;
                Init();
            }
        }

        private void OnDisable()
        {
            bool restartInitialization = _initOnAwake && _initializing;
            CancelCancellationSource(_componentEnabledCts);
            Stop();
            if (_ttsPumpCoroutine != null)
            {
                StopCoroutine(_ttsPumpCoroutine);
                _ttsPumpCoroutine = null;
            }
            StopAdaptiveScheduling();
            if (_initializing)
            {
                ResetModelInitialization();
            }

            _restartInitializationOnEnable |= restartInitialization;
        }

        private System.Collections.IEnumerator Start()
        {
            if (_initOnAwake)
            {
                Init();
                yield return new WaitUntil(() => Initialized || !_initializing);
            }
        }

        private void OnDestroy()
        {
            if (Interlocked.Exchange(ref _destroyState, 1) != 0)
            {
                return;
            }

            CancelCancellationSource(_lifetimeCts);
            CancelCancellationSource(_componentEnabledCts);

            CancelCurrentSession(out long oldSessionId);
            while (_sentenceQueue.TryDequeue(out _)) { }
            StopAndResetCurrentPlayback(oldSessionId);
            Interlocked.Exchange(ref _ttsInProgress, 0);

            if (_ttsPumpCoroutine != null)
            {
                StopCoroutine(_ttsPumpCoroutine);
                _ttsPumpCoroutine = null;
            }
            StopAdaptiveScheduling();

            ResetModelInitialization();
        }
        #endregion

        #region Public API
        public void ApplyConfiguration(SpeechSynthesizerConfiguration configuration)
        {
            _ttsConfig = configuration ?? SpeechSynthesizerConfiguration.CreateDefault();
            if (Initialized)
            {
                LogWarning($"{LogPrefix}Configuration updated after initialization; call ReloadConfigurationAsync() to reload models.");
            }
        }

        /// <summary>
        /// Stops active synthesis and rebuilds the model from the current configuration.
        /// </summary>
        public Task ReloadConfigurationAsync()
            => ReloadConfigurationAsync(CancellationToken.None);

        public async Task ReloadConfigurationAsync(CancellationToken cancellationToken)
        {
            EnsureComponentEnabledCancellationSource();
            CancellationToken enabledToken = _componentEnabledCts?.Token ?? CancellationToken.None;
            using var reloadCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCts.Token,
                enabledToken);
            CancellationToken reloadToken = reloadCts.Token;

            await _configurationReloadGate.WaitAsync(reloadToken);
            try
            {
                await StopAndWaitAsync();
                reloadToken.ThrowIfCancellationRequested();
                if (this == null || !isActiveAndEnabled)
                {
                    throw new OperationCanceledException(reloadToken);
                }

                ResetModelInitialization();
                reloadToken.ThrowIfCancellationRequested();

                Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis synthesis;
                int generation;
                try
                {
                    synthesis = BeginModelInitialization(out generation);
                }
                catch
                {
                    ResetModelInitialization();
                    throw;
                }

                try
                {
                    await AwaitWithCancellation(
                        synthesis.InitializationTask,
                        reloadToken);
                    reloadToken.ThrowIfCancellationRequested();

                    if (!IsCurrentModelInitialization(synthesis, generation))
                    {
                        throw new OperationCanceledException(reloadToken);
                    }

                    if (!synthesis.Initialized)
                    {
                        throw new InvalidOperationException(
                            "Speech synthesis model initialization failed.",
                            synthesis.InitializationException);
                    }

                    StopModelInitializationWatcher();
                    ReportModelInitialization(synthesis, generation, succeeded: true);
                }
                catch (OperationCanceledException)
                {
                    if (IsCurrentModelInitialization(synthesis, generation))
                    {
                        ResetModelInitialization();
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    if (IsCurrentModelInitialization(synthesis, generation))
                    {
                        StopModelInitializationWatcher();
                        LogError($"{LogPrefix}Model initialization failed: {ex.Message}");
                        ReportModelInitialization(synthesis, generation, succeeded: false);
                        ResetModelInitialization();
                    }

                    throw;
                }
            }
            finally
            {
                _configurationReloadGate.Release();
            }
        }

        public void Init()
        {
            if (IsDestroyed || !isActiveAndEnabled || Initialized || _initializing)
            {
                return;
            }
            ResetModelInitialization();
            try
            {
                BeginModelInitialization(out _);
            }
            catch (Exception ex)
            {
                LogError($"{LogPrefix}Init failed: {ex}");
                ResetModelInitialization();
                OnSynthesizerInitialized?.Invoke(false);
            }
        }

        private Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis BeginModelInitialization(
            out int generation)
        {
            if (IsDestroyed || !isActiveAndEnabled)
            {
                throw new InvalidOperationException(
                    "SpeechSynthesizer must be active before model initialization.");
            }

            _initializing = true;
            Initialized = false;
            generation = ++_modelInitializationGeneration;

            var reporter = new SherpaONNXFeedbackReporter(null, _modelLoadProgress);
            var synthesis = new Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis(
                _ttsConfig.ModelId,
                _ttsConfig.SampleRates,
                reporter);
            _speechSynthesis = synthesis;
            EnsurePlaybackSource(_sessionId);

            if (_ttsPumpCoroutine == null)
            {
                _ttsPumpCoroutine = StartCoroutine(TTSPumpLoop());
            }

            _modelInitializationCoroutine = StartCoroutine(
                WaitForModelInitialization(synthesis, generation));
            return synthesis;
        }

        /// <summary>
        /// 停止当前所有TTS任务，清空队列。用于中断。
        /// </summary>
        public void Stop()
        {
            _ = StopAndWaitAsync();
        }

        /// <summary>
        /// 异步停止并等待当前任务完成。
        /// </summary>
        public async Task StopAndWaitAsync()
        {
            Task taskToWait = CancelCurrentSession(out long oldSessionId);

            // 清空队列
            while (_sentenceQueue.TryDequeue(out _)) { }

            // 等待当前任务完成
            if (taskToWait != null && !taskToWait.IsCompleted)
            {
                try
                {
                    await taskToWait.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    LogWarning($"{LogPrefix}Error waiting for session to stop: {ex.Message}");
                }
            }

            if (IsDestroyed)
            {
                return;
            }

            PostToUnityThread(() => StopAndResetCurrentPlayback(oldSessionId));
            UpdateTtsState(false);
        }

        private Task CancelCurrentSession(out long oldSessionId)
        {
            lock (_sessionLock)
            {
                oldSessionId = _sessionId;
                CancelCancellationSource(_sessionCts);
                return _currentSessionTask;
            }
        }

        private static void CancelCancellationSource(CancellationTokenSource source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                if (!source.IsCancellationRequested)
                {
                    source.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void EnsureComponentEnabledCancellationSource()
        {
            if (IsDestroyed || !isActiveAndEnabled)
            {
                return;
            }

            if (_componentEnabledCts == null || _componentEnabledCts.IsCancellationRequested)
            {
                _componentEnabledCts = new CancellationTokenSource();
            }
        }

        public void EnqueueSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }


            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return;
            }


            _sentenceQueue.Enqueue(trimmed);
        }

        public void EnqueueText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }


            string[] sentences = SpeechTextPreprocessor.SplitSentences(text);
            foreach (string sentence in sentences)
            {
                EnqueueSentence(sentence);
            }
        }
        #endregion

        #region Adaptive Scheduling
        private void InitializeAdaptiveScheduling()
        {
            float baseBuffer = Mathf.Clamp(_targetBufferedSeconds, MinBufferedSeconds, MaxBufferedSeconds);
            int baseParallel = Mathf.Clamp(_maxParallelSynthesis, 1, MaxParallelCap);
            _adaptiveBufferedSeconds = baseBuffer;
            _adaptiveMaxParallel = baseParallel;
            _adaptiveScheduler = new AdaptiveSynthesisScheduler(
                baseBuffer,
                baseParallel);
        }

        private void StartAdaptiveScheduling()
        {
            if (_adaptiveScheduler == null || _adaptiveTuningCoroutine != null)
            {
                return;
            }

            _adaptiveTuningCoroutine = StartCoroutine(AdaptiveSchedulingLoop());
        }

        private void StopAdaptiveScheduling()
        {
            if (_adaptiveTuningCoroutine == null)
            {
                return;
            }

            StopCoroutine(_adaptiveTuningCoroutine);
            _adaptiveTuningCoroutine = null;
        }

        private System.Collections.IEnumerator AdaptiveSchedulingLoop()
        {
            var wait = new WaitForSecondsRealtime(AutoTuneIntervalSeconds);
            while (true)
            {
                _adaptiveScheduler.Sample();
                Volatile.Write(ref _adaptiveBufferedSeconds, _adaptiveScheduler.TargetBufferedSeconds);
                Volatile.Write(ref _adaptiveMaxParallel, _adaptiveScheduler.MaxParallel);
                yield return wait;
            }
        }

        private int GetAdaptiveMaxParallel()
        {
            int hardMax = ClampInt(_maxParallelSynthesis, 1,
                ClampInt(Math.Max(1, _processorCount - 1), 1, MaxParallelCap));
            int maxParallel = _adaptiveScheduler != null
                ? Volatile.Read(ref _adaptiveMaxParallel)
                : _maxParallelSynthesis;
            return ClampInt(maxParallel, 1, hardMax);
        }

        private float GetAdaptiveBufferedSeconds()
        {
            float bufferedSeconds = _adaptiveScheduler != null
                ? Volatile.Read(ref _adaptiveBufferedSeconds)
                : _targetBufferedSeconds;
            return ClampFloat(bufferedSeconds, MinBufferedSeconds, MaxBufferedSeconds);
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static float ClampFloat(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
        #endregion

        #region Playback Management
        private PlaybackAudioSourceBehaviour EnsurePlaybackSource(long sessionId)
        {
            lock (_playbackLock)
            {
                if (_playbackSource == null)
                {
                    _playbackSource = GetComponent<PlaybackAudioSourceBehaviour>();
                }

                if (_playbackSource == null)
                {
                    LogError($"{LogPrefix}PlaybackAudioSourceBehaviour is required for playback.");
                    return null;
                }

                if (_playbackSource.Source == null)
                {
                    _playbackSource.Play();
                }
                else if (!_playbackSource.IsPlaying)
                {
                    _playbackSource.Resume();
                }

                _playbackSessionId = sessionId;
                return _playbackSource;
            }
        }

        private PlaybackAudioSourceBehaviour GetPlaybackSourceForSession(long sessionId)
        {
            lock (_playbackLock)
            {
                if (_playbackSessionId != sessionId)
                {
                    return null;
                }

                return _playbackSource;
            }
        }

        private void StopAndResetCurrentPlayback(long sessionId)
        {
            lock (_playbackLock)
            {
                if (_playbackSessionId != sessionId)
                {
                    return;
                }

                try
                {
                    _playbackSource?.Stop();
                }
                catch (Exception ex)
                {
                    LogWarning($"{LogPrefix}Failed to stop playback source: {ex.Message}");
                }
            }
        }
        #endregion

        #region TTS Processing
        private System.Collections.IEnumerator WaitForModelInitialization(
            Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis synthesis,
            int generation)
        {
            Task initializationTask = synthesis.InitializationTask;
            while (IsCurrentModelInitialization(synthesis, generation) &&
                   !initializationTask.IsCompleted)
            {
                yield return null;
            }

            if (!IsCurrentModelInitialization(synthesis, generation))
            {
                yield break;
            }

            _modelInitializationCoroutine = null;
            if (synthesis.Initialized)
            {
                ReportModelInitialization(synthesis, generation, succeeded: true);
            }
            else
            {
                string detail = synthesis.InitializationException?.Message;
                LogError(string.IsNullOrWhiteSpace(detail)
                    ? $"{LogPrefix}Model initialization failed."
                    : $"{LogPrefix}Model initialization failed: {detail}");
                ReportModelInitialization(synthesis, generation, succeeded: false);
            }
        }

        private bool IsCurrentModelInitialization(
            Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis synthesis,
            int generation)
            => generation == _modelInitializationGeneration &&
               ReferenceEquals(synthesis, _speechSynthesis);

        private void ReportModelInitialization(
            Eitan.SherpaONNXUnity.Runtime.Modules.SpeechSynthesis synthesis,
            int generation,
            bool succeeded)
        {
            if (!IsCurrentModelInitialization(synthesis, generation))
            {
                return;
            }

            _initializing = false;
            Initialized = succeeded;
            if (_modelInitializationCompletedGeneration == generation)
            {
                return;
            }

            _modelInitializationCompletedGeneration = generation;
            OnSynthesizerInitialized?.Invoke(succeeded);
        }

        private void StopModelInitializationWatcher()
        {
            if (_modelInitializationCoroutine == null)
            {
                return;
            }

            StopCoroutine(_modelInitializationCoroutine);
            _modelInitializationCoroutine = null;
        }

        private void ResetModelInitialization()
        {
            _modelInitializationGeneration++;
            StopModelInitializationWatcher();

            var synthesis = _speechSynthesis;
            _speechSynthesis = null;
            _initializing = false;
            Initialized = false;
            synthesis?.Dispose();
        }

        private static async Task AwaitWithCancellation(
            Task task,
            CancellationToken cancellationToken)
        {
            if (task == null)
            {
                return;
            }

            if (!cancellationToken.CanBeCanceled)
            {
                await task;
                return;
            }

            var cancellationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(
                       () => cancellationCompletion.TrySetResult(true)))
            {
                Task completedTask = await Task.WhenAny(task, cancellationCompletion.Task);
                if (!ReferenceEquals(completedTask, task))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            await task;
        }

        private System.Collections.IEnumerator TTSPumpLoop()
        {
            while (true)
            {
                // 等待：没有活动任务 + 队列有数据 + 已初始化
                yield return new WaitUntil(() =>
                    !IsSessionRunning() &&
                    !_sentenceQueue.IsEmpty &&
                    Initialized);

                // 开始新的处理会话
                StartNewProcessingSession();
            }
        }

        private bool IsSessionRunning()
        {
            lock (_sessionLock)
            {
                return _currentSessionTask != null && !_currentSessionTask.IsCompleted;
            }
        }

        private void StartNewProcessingSession()
        {
            lock (_sessionLock)
            {
                // 创建新会话
                _sessionId++;
                var currentSessionId = _sessionId;

                // 清理旧的CTS
                if (_sessionCts != null)
                {
                    try { _sessionCts.Dispose(); } catch { }
                }
                _sessionCts = new CancellationTokenSource();
                var token = _sessionCts.Token;

                // 启动处理任务
                _currentSessionTask = ProcessSentenceQueueAsync(currentSessionId, token);
            }
        }

        private async Task ProcessSentenceQueueAsync(long sessionId, CancellationToken cancellationToken)
        {
            using var workersCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken = workersCts.Token;
            UpdateTtsState(true);

            try
            {
                if (EnsurePlaybackSource(sessionId) == null)
                {
                    while (_sentenceQueue.TryDequeue(out _)) { }
                    return;
                }

                var results = new ConcurrentDictionary<int, SpeechSynthesisResult>();
                int generationDone = 0;

                Task generationTask = RunSynthesisSchedulerAsync(
                    results,
                    cancellationToken,
                    () => Interlocked.Exchange(ref generationDone, 1));

                Task playbackTask = RunPlaybackWorkerAsync(
                    sessionId,
                    results,
                    () => Interlocked.CompareExchange(ref generationDone, 0, 0) == 1,
                    cancellationToken);

                Task firstCompleted = await Task.WhenAny(generationTask, playbackTask).ConfigureAwait(false);
                if (firstCompleted.IsFaulted || firstCompleted.IsCanceled)
                {
                    workersCts.Cancel();
                }
                await Task.WhenAll(generationTask, playbackTask).ConfigureAwait(false);
            }
            finally
            {
                // 会话结束，完成playback stream
                CompletePlaybackStreamForSession(sessionId);
                UpdateTtsState(false);
            }
        }

        private async Task RunSynthesisSchedulerAsync(
            ConcurrentDictionary<int, SpeechSynthesisResult> results,
            CancellationToken cancellationToken,
            Action markCompleted)
        {
            var tasks = new List<Task>();
            int sequence = 0;
            Interlocked.Exchange(ref _activeSynthesisJobs, 0);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_sentenceQueue.TryDequeue(out string sentence))
                    {
                        // Streamed text can arrive while the previous sentence is still being queued.
                        if (results.IsEmpty && Volatile.Read(ref _activeSynthesisJobs) == 0)
                        {
                            break;
                        }

                        try
                        {
                            await Task.Delay(PlaybackPollDelayMs, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(sentence))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(SpeechTextPreprocessor.CleanForTts(sentence)))
                    {
                        continue;
                    }

                    bool slotAcquired = await WaitForSynthesisSlotAsync(cancellationToken).ConfigureAwait(false);
                    if (!slotAcquired)
                    {
                        break;
                    }

                    int currentSequence = sequence++;
                    var result = new SpeechSynthesisResult(currentSequence, sentence);
                    results[currentSequence] = result;

                    var job = new SpeechSynthesisJob(
                        sentence,
                        _speechSynthesis,
                        _ttsConfig,
                        s => SafeInvokeOnMainThread(() => OnSentenceStarted?.Invoke(s)),
                        s => SafeInvokeOnMainThread(() => OnSentenceFinished?.Invoke(s)),
                        LogWarning,
                        LogError,
                        result);

                    tasks.Add(RunSynthesisJobAsync(job, result, cancellationToken));
                }

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                markCompleted?.Invoke();
            }
        }

        private async Task RunSynthesisJobAsync(
            SpeechSynthesisJob job,
            SpeechSynthesisResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                await job.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result?.MarkFailed(new OperationCanceledException());
            }
            catch (Exception ex)
            {
                result?.MarkFailed(ex);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSynthesisJobs);
            }
        }

        private async Task<bool> WaitForSynthesisSlotAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int maxParallel = GetAdaptiveMaxParallel();
                int current = Volatile.Read(ref _activeSynthesisJobs);
                if (current < maxParallel)
                {
                    if (Interlocked.Increment(ref _activeSynthesisJobs) <= maxParallel)
                    {
                        return true;
                    }

                    Interlocked.Decrement(ref _activeSynthesisJobs);
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return false;
        }

        private async Task RunPlaybackWorkerAsync(
            long sessionId,
            ConcurrentDictionary<int, SpeechSynthesisResult> results,
            Func<bool> generationDone,
            CancellationToken cancellationToken)
        {
            int expectedSequence = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (results.TryGetValue(expectedSequence, out var result))
                {
                    await PlaySynthesisResultAsync(sessionId, result, cancellationToken).ConfigureAwait(false);
                    results.TryRemove(expectedSequence, out _);
                    expectedSequence++;
                    continue;
                }

                if (generationDone != null && generationDone() && results.IsEmpty)
                {
                    break;
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            await WaitForPlaybackDrainAsync(sessionId, cancellationToken).ConfigureAwait(false);
        }

        private async Task PlaySynthesisResultAsync(
            long sessionId,
            SpeechSynthesisResult result,
            CancellationToken cancellationToken)
        {
            var playbackSource = GetPlaybackSourceForSession(sessionId);
            if (playbackSource == null || result == null)
            {
                return;
            }

            int channels = result.Channels > 0 ? result.Channels : 1;
            int sampleRate = result.SampleRate > 0 ? result.SampleRate : Math.Max(8000, _ttsConfig.SampleRates);
            bool stallWarned = false;
            bool playbackStarted = false;
            // Model callbacks can contain several seconds; the playback queue is bounded.
            var playbackChunk = new float[Math.Max(1, sampleRate / 50) * channels];
            var outputLeveler = new SpeechOutputLeveler();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (result.IsFailed && !result.HasPendingChunks)
                {
                    if (result.Error != null)
                    {
                        LogWarning($"{LogPrefix}Synthesis failed: {result.Error.Message}");
                    }
                    break;
                }

                if (result.TryDequeueChunk(out var samples))
                {
                    if (samples != null && samples.Length > 0)
                    {
                        for (int offset = 0; offset < samples.Length;)
                        {
                            int count = Math.Min(playbackChunk.Length, samples.Length - offset);
                            var source = playbackSource.Source;
                            if (source == null)
                            {
                                return;
                            }

                            // FreeSamples and SamplesWritten use the output format after conversion.
                            int outputSamples = (int)Math.Ceiling(
                                (count / channels) * (double)source.SampleRate / sampleRate) * source.Channels;
                            double bufferBudget = Math.Max(0.05, GetAdaptiveBufferedSeconds());
                            await WaitForBufferBudgetAsync(
                                playbackSource, bufferBudget, cancellationToken, outputSamples).ConfigureAwait(false);
                            if (cancellationToken.IsCancellationRequested ||
                                !ReferenceEquals(GetPlaybackSourceForSession(sessionId), playbackSource))
                            {
                                return;
                            }

                            Array.Copy(samples, offset, playbackChunk, 0, count);
                            // Process before mixing so AEC receives the same levelled signal as the speaker.
                            outputLeveler.Process(playbackChunk, count, channels, sampleRate, _normalizeOutput, _playbackVolume);
                            _outputInputRmsDb = outputLeveler.InputRmsDb;
                            _outputPeakDb = outputLeveler.OutputPeakDb;
                            _outputGainDb = outputLeveler.AppliedGainDb;
                            var enqueueResult = playbackSource.TryEnqueue(playbackChunk, count, channels, sampleRate, false);
                            if (enqueueResult.SamplesWritten != outputSamples)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                throw new InvalidOperationException(
                                    $"Local TTS playback accepted {enqueueResult.SamplesWritten}/{outputSamples} output samples ({enqueueResult.Status}).");
                            }
                            offset += count;
                            if (!playbackStarted && enqueueResult.WroteAnySamples)
                            {
                                playbackStarted = true;
                                SafeInvokeOnMainThread(() =>
                                {
                                    if (!cancellationToken.IsCancellationRequested &&
                                        ReferenceEquals(GetPlaybackSourceForSession(sessionId), playbackSource))
                                    {
                                        OnSentencePlaybackStarted?.Invoke(result.Sentence);
                                    }
                                });
                            }
                        }
                    }
                    stallWarned = false;
                    continue;
                }

                if (result.IsComplete && !result.HasPendingChunks)
                {
                    break;
                }

                var idleDuration = result.GetIdleDuration();
                if (idleDuration > TimeSpan.Zero)
                {
                    double warnAfterSeconds = GetPlaybackStallWarningSeconds(result);
                    double abortAfterSeconds = GetPlaybackStallAbortSeconds(result);
                    double idleSeconds = idleDuration.TotalSeconds;

                    if (!stallWarned && idleSeconds >= warnAfterSeconds)
                    {
                        string phase = result.HasReceivedChunks ? "after audio start" : "before first chunk";
                        LogWarning($"{LogPrefix}Playback waiting for synthesis chunks ({phase}, idle {idleSeconds:0.0}s).");
                        stallWarned = true;
                    }

                    if (idleSeconds >= abortAfterSeconds)
                    {
                        LogWarning($"{LogPrefix}Playback wait timed out after {idleSeconds:0.0}s without new synthesis chunks.");
                        break;
                    }
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static double GetPlaybackStallWarningSeconds(SpeechSynthesisResult result)
        {
            int length = result?.Sentence?.Length ?? 0;
            bool hasChunks = result != null && result.HasReceivedChunks;
            double baseSeconds = hasChunks ? 5.0 : 10.0;
            double extraSeconds = Math.Min(20.0, length * 0.02);
            double capSeconds = hasChunks ? 15.0 : 30.0;
            double total = baseSeconds + extraSeconds;
            return total > capSeconds ? capSeconds : total;
        }

        private static double GetPlaybackStallAbortSeconds(SpeechSynthesisResult result)
        {
            double warnSeconds = GetPlaybackStallWarningSeconds(result);
            return Math.Max(45.0, warnSeconds * 3.0);
        }

        private async Task WaitForBufferBudgetAsync(
            PlaybackAudioSourceBehaviour playbackSource,
            double budgetSeconds,
            CancellationToken cancellationToken,
            int requiredOutputSamples = 0)
        {
            if (playbackSource == null)
            {
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                var source = playbackSource.Source;
                if (source == null ||
                    (playbackSource.BufferedSeconds <= budgetSeconds && source.FreeSamples >= requiredOutputSamples))
                {
                    break;
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void CompletePlaybackStreamForSession(long sessionId)
        {
            if (IsDestroyed)
            {
                return;
            }

            lock (_playbackLock)
            {
                if (_playbackSessionId != sessionId)
                {
                    return;
                }


                if (_playbackSource != null)
                {
                    try
                    {
                        _playbackSource.CompleteStream();
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"{LogPrefix}Failed to complete stream: {ex.Message}");
                    }
                }
            }
        }

        private async Task WaitForPlaybackDrainAsync(long sessionId, CancellationToken cancellationToken)
        {
            const double epsilon = 0.05;
            int stagnantCount = 0;
            double lastBuffered = double.MaxValue;

            while (!cancellationToken.IsCancellationRequested)
            {
                PlaybackAudioSourceBehaviour source;
                lock (_playbackLock)
                {
                    if (_playbackSessionId != sessionId)
                    {
                        break;
                    }

                    source = _playbackSource;
                }

                if (source == null)
                {
                    break;
                }


                double buffered = source.BufferedSeconds;
                if (buffered <= epsilon)
                {
                    break;
                }


                if (Math.Abs(buffered - lastBuffered) < 0.001)
                {
                    stagnantCount++;
                    if (stagnantCount > 100) // 约3秒无变化
                    {
                        break;
                    }

                }
                else
                {
                    stagnantCount = 0;
                    lastBuffered = buffered;
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void UpdateTtsState(bool isSpeaking)
        {
            if (IsDestroyed)
            {
                Interlocked.Exchange(ref _ttsInProgress, 0);
                return;
            }

            int oldValue = isSpeaking
                ? Interlocked.Exchange(ref _ttsInProgress, 1)
                : Interlocked.Exchange(ref _ttsInProgress, 0);

            bool changed = (oldValue != 0) != isSpeaking;
            if (changed)
            {
                SafeInvokeOnMainThread(() => OnTTSStateChanged?.Invoke(isSpeaking));
            }
        }

        private void SafeInvokeOnMainThread(Action action)
        {
            PostToUnityThread(() =>
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    LogWarning($"{LogPrefix}Callback error: {ex.Message}");
                }
            });
        }

        private void CaptureUnityThreadContext()
        {
            if (_unityThreadId != 0)
            {
                return;
            }

            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            _unityContext = SynchronizationContext.Current;
        }

        private bool IsOnUnityThread =>
            _unityThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _unityThreadId;

        private bool IsDestroyed => Volatile.Read(ref _destroyState) != 0;

        private void PostToUnityThread(Action action)
        {
            if (action == null || IsDestroyed)
            {
                return;
            }

            var context = _unityContext;
            if (context == null)
            {
                if (IsOnUnityThread)
                {
                    action();
                }

                return;
            }

            if (IsOnUnityThread)
            {
                action();
                return;
            }

            context.Post(static state => ((Action)state)(), action);
        }
        #endregion
    }
}
#else
using System;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.Mono.Components;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS
{
    public readonly struct FailedFeedback
    {
        public FailedFeedback(string message)
        {
            Message = message ?? string.Empty;
        }

        public string Message { get; }
    }

    public readonly struct SuccessFeedback
    {
        public SuccessFeedback(string message)
        {
            Message = message ?? string.Empty;
        }

        public string Message { get; }
    }

    [RequireComponent(typeof(PlaybackAudioSourceBehaviour))]
    public class SpeechSynthesizer : MonoBehaviour
    {
        [Header("Initialization")]
        [SerializeField] private bool _initOnAwake = true;

        [Header("Configuration")]
        [SerializeField] private SpeechSynthesizerConfiguration _ttsConfig;

        [Header("Playback")]
        [SerializeField] private PlaybackAudioSourceBehaviour _playbackSource;
        [Range(0.02f, 0.5f)]
        [SerializeField, HideInInspector] private float _targetBufferedSeconds = 0.12f;

        [Header("Performance")]
        [Range(1, 8)]
        [SerializeField, HideInInspector] private int _maxParallelSynthesis = 2;

        [Header("Logging")]
        [SerializeField] private bool _enableLog;

        private static bool s_loggedMissingDependency;

        public bool InitOnAwake => _initOnAwake;
        public bool Initialized { get; private set; }
        public bool IsProcessingTTS => false;
        public int ActiveSynthesisJobs => 0;
        public int MaxParallelSynthesis
        {
            get => _maxParallelSynthesis;
            set => _maxParallelSynthesis = Mathf.Clamp(value, 1, 8);
        }
        public PlaybackAudioSourceBehaviour PlaybackSource => _playbackSource;
        public SpeechSynthesizerConfiguration TtsConfig => _ttsConfig ??= SpeechSynthesizerConfiguration.CreateDefault();

        public event Action<string, float> OnLoadingProgressFeedback;
        public event Action<FailedFeedback> OnLoadingFailedFeedback;
        public event Action<SuccessFeedback> OnLoadingSuccessedFeedback;
        public event Action<bool> OnSynthesizerInitialized;
        public event Action<bool> OnTTSStateChanged;
        public event Action<string> OnSentenceStarted;
        public event Action<string> OnSentenceFinished;
        public event Action<string> OnSentencePlaybackStarted;

        private void Awake()
        {
            _playbackSource ??= GetComponent<PlaybackAudioSourceBehaviour>();
            LogMissingDependencyOnce();
        }

        private void Start()
        {
            if (_initOnAwake)
            {
                Init();
            }
        }

        public void ApplyConfiguration(SpeechSynthesizerConfiguration configuration)
        {
            _ttsConfig = configuration ?? SpeechSynthesizerConfiguration.CreateDefault();
        }

        public Task ReloadConfigurationAsync()
            => ReloadConfigurationAsync(CancellationToken.None);

        public Task ReloadConfigurationAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stop();
            Init();
            return Task.CompletedTask;
        }

        public void Init()
        {
            LogMissingDependencyOnce();
            Initialized = false;
            OnLoadingProgressFeedback?.Invoke("Sherpa-ONNX dependency missing", 0f);
            OnSynthesizerInitialized?.Invoke(false);
        }

        public void Stop()
        {
            _playbackSource?.Stop();
            OnTTSStateChanged?.Invoke(false);
        }

        public Task StopAndWaitAsync()
        {
            Stop();
            return Task.CompletedTask;
        }

        public void EnqueueSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            LogMissingDependencyOnce();
            OnSentenceStarted?.Invoke(text);
            OnSentenceFinished?.Invoke(text);
            OnTTSStateChanged?.Invoke(false);
        }

        private static void LogMissingDependencyOnce()
        {
            if (s_loggedMissingDependency)
            {
                return;
            }

            s_loggedMissingDependency = true;
            Debug.LogWarning(
                "[EasyMic] SpeechSynthesizer is running in compatibility mode because com.eitan.sherpa-onnx-unity is not installed. " +
                "The component reference is preserved, but local TTS features are disabled.");
        }
    }
}
#endif
