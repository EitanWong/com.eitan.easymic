#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.SherpaONNXUnity;
using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Core;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using Eitan.SherpaONNXUnity.Runtime.Utilities;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
{
    /// <summary>
    /// Voice Microphone Component - Coordinates Sherpa-ONNX services and provides microphone-driven transcription events.
    /// Owns model lifecycle and ensures worker callbacks are dispatched on the Unity main thread.
    /// Changes here can affect readiness, ordering, and thread safety.
    /// 语音麦克风组件 - 协调 Sherpa-ONNX 服务并提供麦克风驱动的转录事件。
    /// </summary>
    [AddComponentMenu("Audio/Input/Voice Microphone")]
    public class VoiceMicrophone : EasyMicrophone, ISherpaFeedbackHandler
    {
        #region Constants & Enums

        private const SampleRate DEFAULT_SAMPLE_RATE = SampleRate.Hz16000; // Default model sample rate / 默认模型采样率
        private const string LOG_PREFIX = "[VoiceMicrophone]"; // Log tag for this component / 本组件日志前缀
        private const int STREAMING_PUNCTUATION_DEBOUNCE_MS = 120; // Debounce for streaming punctuation / 流式标点防抖时间
        private static readonly char[] PunctuationTerminators = { '.', '!', '?', '。', '！', '？' }; // Sentence terminators / 句末终止符
        private static readonly char[] PunctuationTrailingClosers = { '"', '”', '’', '»', ')', '）' }; // Closing punctuation after terminator / 终止符后的闭合符

        /// <summary>
        /// Component lifecycle state. / 组件生命周期状态。
        /// </summary>
        private enum LifecycleState
        {
            Uninitialized,
            Initializing,
            Ready,
            Disposing,
            Disposed
        }

        private enum PendingCallbackType
        {
            StreamingRecognition,
            OfflineRecognition,
            KeywordDetected,
            VoiceActivityChanged
        }

        private readonly struct PendingCallback
        {
            public readonly PendingCallbackType Type; // Callback category / 回调类型
            public readonly string Text; // Payload text / 文本负载
            public readonly bool Bool; // Payload boolean / 布尔负载

            public PendingCallback(PendingCallbackType type, string text)
            {
                Type = type;
                Text = text ?? string.Empty;
                Bool = false;
            }

            public PendingCallback(PendingCallbackType type, bool value)
            {
                Type = type;
                Text = string.Empty;
                Bool = value;
            }
        }

        #endregion

        #region Static Strategies

        private static readonly StreamingRecognitionStrategy StreamingStrategy = new(); // Shared streaming strategy / 共享流式策略
        private static readonly OfflineWithVadRecognitionStrategy OfflineStrategy = new(); // Shared offline+VAD strategy / 共享离线+VAD策略
        private static readonly HybridRecognitionStrategy HybridStrategy = new(); // Shared hybrid strategy / 共享混合策略
        private static readonly KeywordSpottingOnlyRecognitionStrategy KeywordSpottingOnlyStrategy = new(); // Shared keyword-only strategy / 共享关键词策略

        #endregion

        #region Serialized Fields

        [Header("ASR Configuration")]
        [SerializeField]
        [Tooltip("Automatic Speech Recognition Configuration")]
        private AutomaticSpeechRecognitionConfiguration _asrConfig; // Inspector ASR config / Inspector中的ASR配置

        #endregion

        #region Service References

        private SpeechRecognition _streamingService; // Streaming recognition service / 流式识别服务
        private SpeechRecognition _offlineService; // Offline recognition service / 离线识别服务
        private VoiceActivityDetection _vadService; // Voice activity detection service / 语音活动检测服务
        private KeywordSpotting _keywordService; // Keyword spotting service / 关键词检测服务
        private Punctuation _punctService; // Punctuation service / 标点服务

        #endregion

        #region Service Wrappers

        private SherpaVoiceFilter _voiceFilter; // Wrapper for VAD callbacks / VAD回调包装
        private SherpaRealtimeSpeechRecognizer _realtimeRecognizer; // Wrapper for streaming recognizer / 流式识别包装
        private SherpaOfflineSpeechRecognizer _offlineRecognizer; // Wrapper for offline recognizer / 离线识别包装
        private SherpaKeywordDetector _keywordDetector; // Wrapper for keyword detector / 关键词检测包装

        #endregion

        #region Infrastructure

        private SherpaONNXFeedbackReporter _feedbackReporter; // Routes Sherpa feedback / 路由Sherpa反馈
        private SherpaServiceFactory _serviceFactory; // Creates Sherpa services / 创建Sherpa服务
        private ModelProgressAggregator _progressAggregator; // Aggregates model load progress / 聚合模型加载进度

        #endregion

        #region State Management

        #region Recognition Core

        private VoiceActivityMonitor _voiceActivity; // Tracks voice activity state / 跟踪语音活动状态
        private KeywordGate _keywordGate; // Manages keyword activation window / 管理关键词激活窗口
        private RecognitionBuffer _recognitionBuffer; // Buffers partial/final transcripts / 缓冲部分与最终转录
        private TurnDetector _turnDetector; // Detects turn boundaries / 检测语音轮次边界
        private SilenceTurnRecognizer _silenceRecognizer; // Handles silence-based turn end / 处理静音结束
        private IRecognitionStrategy _recognitionStrategy; // Selected recognition strategy / 选择的识别策略
        private CancellationTokenSource _recognitionLifetimeCts; // Cancels recognition lifecycle / 取消识别生命周期
        private readonly SemaphoreSlim _punctuationSemaphore = new(1, 1); // Serializes punctuation calls / 串行化标点调用

        #endregion

        #region Recognition State

        private AutomaticSpeechRecognitionConfiguration.ASRPreset _initializingPreset; // Preset used during init / 初始化使用的预设
        private TurnDetectionOptions _turnDetectionSettings; // Active turn detection options / 当前轮次检测配置
        private bool _turnDetectionOverridden; // Whether custom settings were supplied / 是否有自定义设置
        private string _pendingStreamingResult = string.Empty; // Latest streaming text snapshot / 最新流式文本快照
        private int _expectedModelLoads; // Total model load count / 预计模型加载数
        private int _completedModelLoads; // Completed load count / 已完成加载数
        private int _failedRequiredModelLoads; // Failed required loads / 必需模型加载失败数
        private int _failedOptionalModelLoads; // Failed optional loads / 可选模型加载失败数

        #endregion

        #region Pending Device State

        // Pending device options to apply on initialization / 待在初始化时应用的设备选项
        private DeviceOptions? _pendingDeviceOptions; // Pending device settings / 待应用设备设置
        private bool _pendingRestartRecording; // Whether to restart after apply / 应用后是否重启录音

        #endregion

        #region Lifecycle State

        // Unified state management / 统一状态管理
        private readonly object _stateLock = new(); // Guards lifecycle transitions / 保护生命周期切换
        private volatile LifecycleState _lifecycleState = LifecycleState.Uninitialized; // Current lifecycle state / 当前生命周期状态

        #endregion

        #region Unity Threading State

        // Unity main-thread ownership: worker callbacks are queued and drained on Update.
        private SynchronizationContext _unityContext; // Unity main-thread context / Unity主线程上下文
        private int _unityThreadId; // Unity main thread id / Unity主线程ID
        // Incremented on reload/dispose to invalidate in-flight async work.
        private int _recognitionGeneration; // Generation for async invalidation / 异步失效代数
        private readonly ConcurrentQueue<PendingCallback> _pendingCallbacks = new(); // Worker -> main thread queue / 工作者到主线程队列

        #endregion

        #region Streaming Punctuation State

        // Streaming punctuation: a single debounced, cancelable pipeline for partial transcripts.
        private CancellationTokenSource _streamingPreviewPunctuationCts; // Cancels preview punctuation / 取消预览标点任务
        private int _streamingPreviewPunctuationRequestId; // Debounce request id / 防抖请求ID
        private int _finalPunctuationRequestId; // Latest final punctuation request / 最新最终标点请求
        private string _cachedPunctuationInput = string.Empty; // Last punctuate input / 上次标点输入
        private string _cachedPunctuationOutput = string.Empty; // Last punctuate output / 上次标点输出

        #endregion

        #endregion

        #region Properties

        public AutomaticSpeechRecognitionConfiguration AsrConfig
        {
            get => _asrConfig ??= AutomaticSpeechRecognitionConfiguration.CreateDefault();
            private set => _asrConfig = value;
        }

        public bool IsSpeaking => IsVoiceActivity;
        public bool IsVoiceActivity => _voiceActivity?.IsVoiceActive ?? false;
        public bool RequiresStreaming => CurrentStrategy.RequiresStreaming;
        public bool RequiresOffline => CurrentStrategy.RequiresOffline;
        public bool RequiresVad => CurrentStrategy.RequiresVoiceActivity;

        public bool RequiresPunctuation =>
            AsrConfig.EnablePunctuation && !string.IsNullOrWhiteSpace(AsrConfig.PunctuationModelId);

        public bool RequiresKeywordSpotting => AsrConfig.ActiveKeywordOptions.IsEnabled;

        public IReadOnlyList<AutomaticSpeechRecognitionConfiguration.ASRPreset> SupportedPresets =>
            AsrConfig.SupportedPresets;

        public string ActivePresetId => AsrConfig.ActivePresetId;

        public AutomaticSpeechRecognitionConfiguration.ASRPreset ActivePresetConfiguration =>
            AsrConfig.GetActivePreset();

        public bool AreModelsLoaded =>
            Initialized &&
            _completedModelLoads >= _expectedModelLoads &&
            _failedRequiredModelLoads == 0;
        public bool HasRequiredModelLoadFailures => _failedRequiredModelLoads > 0;
        public bool HasOptionalModelLoadFailures => _failedOptionalModelLoads > 0;
        public bool AreModelsDisposed => _lifecycleState == LifecycleState.Disposed;
        public bool IsInitializing => _lifecycleState == LifecycleState.Initializing;

        public bool IsOperational => Initialized && _lifecycleState == LifecycleState.Ready && _failedRequiredModelLoads == 0;

        private IRecognitionStrategy CurrentStrategy =>
            _recognitionStrategy ?? SelectStrategy(AsrConfig.RecognitionMode);

        #endregion

        #region Events

        public event Action<string> OnASRTranscriptionStreaming; // Streaming transcript updates / 流式转录更新
        public event Action<string> OnASRTranscriptionSubmit; // Final transcript submission / 最终转录提交
        public event Action<bool> OnVoiceActivityChanged; // Voice activity state change / 语音活动状态变化
        public event Action<bool> OnSpeakingChanged; // Alias of voice activity / 说话状态变化
        public event Action<string, bool> OnKeywordActivityChanged; // Keyword active/inactive / 关键词激活状态
        public event Action<string, float> OnLoadingProgressFeedback; // Model load progress / 模型加载进度
        public event Action<FailedFeedback> OnLoadingFailedFeedback; // Model load failed / 模型加载失败
        public event Action<SuccessFeedback> OnLoadingSucceededFeedback; // Model load success / 模型加载成功
        public event Action OnModelsDisposed; // Models disposed event / 模型释放事件
        public event Action OnModelsReloaded; // Models reloaded event / 模型重载事件

        #endregion

        #region Unity Lifecycle

        protected override void OnInitialization()
        {
            CaptureUnityThreadContext();
            ThreadingUtils.PrimeUnityInfo();
            if (_lifecycleState == LifecycleState.Initializing)
            {
                LogInfo("Initialize requested while initialization is already in progress.");
                return;
            }

            if (!TryTransitionState(LifecycleState.Uninitialized, LifecycleState.Initializing) &&
                !TryTransitionState(LifecycleState.Disposed, LifecycleState.Initializing))
            {
                LogWarning("Cannot initialize: invalid state.");
                return;
            }

            try
            {
                EnsureInfrastructure();
                InitializeServices(preserveRecordingState: false);

                // Apply pending device options if any / 应用待处理的设备选项（如果有）
                ApplyPendingDeviceOptions();
            }
            catch (Exception ex)
            {
                LogError($"Initialization failed: {ex.Message}");
                SetState(LifecycleState.Disposed);
            }
        }

        protected override void OnAudioPiplineBuild(AudioPipeline pipeline)
        {
            if (!IsReady("build audio pipeline") || pipeline == null)
            {
                return;
            }

            ConfigureAudioPipeline(pipeline);
            OnAudioPipelineReady(pipeline);
        }

        protected override void OnMicrophoneUpdate()
        {
            DrainPendingCallbacks();

            if (!IsOperational)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _keywordGate?.Update(deltaTime, IsSpeaking, IsVoiceActivity);
            if (!CurrentStrategy.RequiresOffline)
            {
                _silenceRecognizer?.Update(deltaTime, _recognitionBuffer);
            }
        }

        protected override void OnMicrophoneDispose()
        {
            DisposeAllResources();
        }

        #endregion

        #region Public API - Model Lifecycle

        /// <summary>
        /// Disposes all loaded ASR models and services.
        /// 销毁所有已加载的 ASR 模型和服务。
        /// </summary>
        public bool DisposeModels()
        {
            if (!TryTransitionState(LifecycleState.Ready, LifecycleState.Disposing))
            {
                LogWarning(_lifecycleState == LifecycleState.Disposed
                    ? "Models already disposed."
                    : "Cannot dispose: invalid state.");
                return false;
            }

            try
            {
                LogInfo("Disposing all ASR models...");
                EnsureRecordingStopped();
                DisposeServicesCore();
                SetState(LifecycleState.Disposed);
                InvokeEvent(OnModelsDisposed);
                LogInfo("All ASR models disposed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Error during model disposal: {ex.Message}");
                SetState(LifecycleState.Disposed);
                return false;
            }
        }

        /// <summary>
        /// Reloads all ASR models.
        /// 重新加载所有 ASR 模型。
        /// </summary>
        public bool ReloadModels(bool startRecording = false)
        {
            var currentState = _lifecycleState;
            if (currentState == LifecycleState.Initializing || currentState == LifecycleState.Disposing)
            {
                LogWarning($"Cannot reload: {currentState} in progress.");
                return false;
            }

            if (!TryTransitionState(currentState, LifecycleState.Initializing))
            {
                return false;
            }

            try
            {
                LogInfo("Reloading ASR models...");

                InitializeServices(preserveRecordingState: false);
                InvokeEvent(OnModelsReloaded);

                if (startRecording && Initialized)
                {
                    TryStartRecording();
                }

                LogInfo("ASR models reloaded successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to reload models: {ex.Message}");
                SetState(LifecycleState.Disposed);
                return false;
            }
        }

        /// <summary>
        /// Returns true when models can be disposed without interrupting initialization or disposal.
        /// </summary>
        public bool CanDisposeModels() => _lifecycleState == LifecycleState.Ready;

        #endregion

        #region Public API - Configuration

        /// <summary>
        /// Applies a new configuration.
        /// Can be called at any stable state (configuration will be used when ready).
        /// 应用新的配置。可以在任何稳定状态下调用（配置将在准备就绪时使用）。
        /// </summary>
        public bool ApplyConfiguration(AutomaticSpeechRecognitionConfiguration configuration)
        {
            if (configuration == null)
            {
                LogWarning("Cannot apply null configuration.");
                return false;
            }

            if (!IsStable("apply configuration"))
            {
                return false;
            }

            try
            {
                var state = _lifecycleState;
                AsrConfig = configuration.Clone();
                LogInfo($"Configuration applied (state: {state}).");

                // Only reinitialize services if in Ready state / 仅在 Ready 状态下重新初始化服务
                if (state == LifecycleState.Ready && Initialized)
                {
                    InitializeServices();
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to apply configuration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Attempts to activate a preset by identifier.
        /// 尝试通过标识符激活预设。
        /// </summary>
        public bool TrySetActivePreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
            {
                LogWarning("Preset ID cannot be null or empty.");
                return false;
            }

            if (!AsrConfig.TrySelectPreset(presetId))
            {
                LogWarning($"Preset '{presetId}' is not available.");
                return false;
            }

            // Can set preset before initialization / 可以在初始化前设置预设
            if (!Initialized || _lifecycleState != LifecycleState.Ready)
            {
                LogInfo($"Preset '{presetId}' selected, will be applied on next initialization.");
                return true;
            }

            if (!IsReady("set active preset"))
            {
                return false;
            }

            try
            {
                InitializeServices();
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to apply preset '{presetId}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Configures turn detection settings.
        /// 配置轮次检测设置。
        /// </summary>
        public void ConfigureTurnDetection(TurnDetectionOptions settings)
        {
            _turnDetectionSettings = settings.EnsureValid();
            _turnDetectionOverridden = true;

            // Can configure before initialization / 可以在初始化前配置
            if (!Initialized || _lifecycleState != LifecycleState.Ready)
            {
                LogInfo("Turn detection settings stored, will be applied on initialization.");
                return;
            }

            if (!IsReady("configure turn detection"))
            {
                return;
            }

            EnsureTurnDetector(_turnDetectionSettings);
            ResetRecognitionState();
        }

        /// <summary>
        /// Switches the capture device.
        /// Can be called at any stable state (device will be applied when ready).
        /// 切换捕获设备。可以在任何稳定状态下调用（设备将在准备就绪时应用）。
        /// </summary>
        public void SwitchCaptureDevice(
            MicDevice device,
            Channel channel,
            SampleRate sampleRate,
            bool restartRecording = true)
        {
            if (!IsStable("switch capture device"))
            {
                return;
            }

            var state = _lifecycleState;
            var options = new DeviceOptions(device, channel, sampleRate);

            // Store for later if not ready / 如果未就绪则存储
            if (state != LifecycleState.Ready || !Initialized)
            {
                _pendingDeviceOptions = options;
                _pendingRestartRecording = restartRecording;
                LogInfo($"Device options stored (state: {state}), will be applied on initialization.");
                return;
            }

            ApplyDeviceOptions(options, restartRecording);
        }

        /// <summary>
        /// Sets a GitHub proxy URL for model downloads when required by network policy.
        /// </summary>
        public void SetGithubProxy(string url) => SherpaONNXUnityAPI.SetGithubProxy(url);

        /// <summary>
        /// Resets all recognition state.
        /// 重置所有识别状态。
        /// </summary>
        public void ResetRecognitionState()
        {
            if (!IsReady("reset recognition state"))
            {
                return;
            }

            ResetRecognitionStateCore();
        }

        #endregion

        #region Protected Virtual Methods

        protected virtual void OnBeforeServicesInitializationRequested(
            AutomaticSpeechRecognitionConfiguration configuration,
            AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        { }

        protected virtual void OnServicesInitializationRequested(
            AutomaticSpeechRecognitionConfiguration configuration,
            AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        { }

        protected virtual void OnServicesInitialized(
            AutomaticSpeechRecognitionConfiguration configuration,
            AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        { }

        protected virtual void OnServiceLoadingFailed(FailedFeedback feedback) { }
        protected virtual void OnServiceLoadingSucceeded(SuccessFeedback feedback) { }
        protected virtual void OnBeforeServicesDisposed() { }
        protected virtual void OnAfterServicesDisposed() { }
        protected virtual void OnAudioPipelineReady(AudioPipeline pipeline) { }
        protected virtual void OnKeywordActivated(string keyword) { }
        protected virtual void OnKeywordDeactivated() { }
        protected virtual void OnTranscription(string result, bool isFinal) { }

        #endregion

        #region Service Lifecycle

        private void EnsureInfrastructure()
        {
            CaptureUnityThreadContext();
            _progressAggregator ??= new ModelProgressAggregator();
            // Turn detection underpins both streaming/offline commit behavior; keep it consistent across reloads.
            var effectiveTurnDetection = _turnDetectionOverridden
                ? _turnDetectionSettings
                : AsrConfig.ActiveTurnDetectionOptions;
            EnsureTurnDetector(effectiveTurnDetection);

            InitializeVoiceActivityMonitor();
            InitializeKeywordGate();
            InitializeRecognitionBuffer();
        }

        private void EnsureTurnDetector(TurnDetectionOptions settings)
        {
            settings = settings.EnsureValid();

            bool needsNewDetector = _turnDetector == null ||
                                    !AreTurnDetectionOptionsEqual(_turnDetectionSettings, settings);

            _turnDetectionSettings = settings;

            if (needsNewDetector)
            {
                _turnDetector = new AdaptiveTurnDetector(_turnDetectionSettings);
            }

            _silenceRecognizer ??= new SilenceTurnRecognizer(_turnDetector);
            _silenceRecognizer.ConfigureDetector(_turnDetector);
        }

        private static bool AreTurnDetectionOptionsEqual(TurnDetectionOptions left, TurnDetectionOptions right)
        {
            return left.MinDelaySeconds.Equals(right.MinDelaySeconds) &&
                   left.MaxDelaySeconds.Equals(right.MaxDelaySeconds);
        }

        private void InitializeVoiceActivityMonitor()
        {
            if (_voiceActivity != null)
            {
                return;
            }

            _voiceActivity = new VoiceActivityMonitor();
            _voiceActivity.VoiceActivityChanged += HandleVoiceActivityChanged;
        }

        private void InitializeKeywordGate()
        {
            var settings = AsrConfig.ActiveKeywordOptions;

            if (_keywordGate == null)
            {
                _keywordGate = new KeywordGate(
                    settings,
                    Mathf.Max(0f, settings.ContinuousConversationTimeoutSeconds),
                    0f, null);

                _keywordGate.ActivityChanged += HandleKeywordActivityChanged;
                _keywordGate.Activated += HandleKeywordActivatedInternal;
                _keywordGate.Deactivated += HandleKeywordDeactivatedInternal;
            }
            else
            {
                _keywordGate.ApplySettings(settings);
            }
        }

        private void InitializeRecognitionBuffer()
        {
            if (_recognitionBuffer != null)
            {
                return;
            }

            _recognitionBuffer = new RecognitionBuffer(
                _keywordGate,
                HandleStreamingPreview,
                synchronizationContext: null);

            _recognitionBuffer.BufferAmended += HandleBufferAmended;
            _recognitionBuffer.Finalized += HandleFinalizedTranscript;
        }

        private void HandleStreamingPreview(string streaming)
        {
            if (!IsOperational)
            {
                return;
            }

            streaming ??= string.Empty;

            if (!RequiresPunctuation || _punctService == null)
            {
                PublishStreamingPreview(streaming);
                return;
            }

            if (TryGetCachedPunctuation(streaming, out string cached))
            {
                PublishStreamingPreview(cached);
                return;
            }

            ScheduleStreamingPreviewPunctuation(streaming);
        }

        private void PublishStreamingPreview(string streaming)
        {
            if (!IsOperational)
            {
                return;
            }

            var handler = OnASRTranscriptionStreaming;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(streaming);
            }
            catch (Exception ex)
            {
                LogError($"Event invocation failed: {ex.Message}");
            }
        }

        private void InitializeServices(bool preserveRecordingState = true)
        {
            // Rebuild services in a fixed order so recognition state is reset deterministically.
            bool wasRecording = preserveRecordingState && IsRecording;
            if (wasRecording)
            {
                StopRecordingSafely();
            }

            DisposeServicesCore();
            EnsureInfrastructure();
            ResetRecognitionStateCore();

            _recognitionStrategy = SelectStrategy(AsrConfig.RecognitionMode);
            _recognitionLifetimeCts = new CancellationTokenSource();
            _recognitionGeneration++;
            ClearPendingCallbacks();

            var config = AsrConfig;
            var preset = config.GetActivePreset();
            _initializingPreset = preset;

            OnBeforeServicesInitializationRequested(config, preset);
            CreateServices(preset);
            OnServicesInitializationRequested(config, preset);

            if (_expectedModelLoads == 0)
            {
                FinalizeInitialization();
            }

            if (wasRecording && !IsRecording)
            {
                TryStartRecording();
            }
        }

        private void CreateServices(AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        {
            var defaults = AutomaticSpeechRecognitionConfiguration.ASRPreset.Default;
            _serviceFactory = new SherpaServiceFactory((int)DEFAULT_SAMPLE_RATE, EnsureFeedbackReporter());

            _expectedModelLoads = 0;
            _completedModelLoads = 0;
            _failedRequiredModelLoads = 0;
            _failedOptionalModelLoads = 0;

            CreateServiceIfNeeded(
                _recognitionStrategy.RequiresStreaming,
                required: true,
                () => _serviceFactory.CreateSpeechRecognition(
                    preset.StreamingModelId,
                    defaults.StreamingModelId,
                    "streaming",
                    options: CreateStreamingEndpointingOptions(),
                    maxPendingTranscriptions: 1,
                    dropIfBusy: true),
                result =>
                {
                    _streamingService = result.Service;
                    _realtimeRecognizer = result.Service != null
                        ? new SherpaRealtimeSpeechRecognizer(result.Service)
                        : null;
                });

            CreateServiceIfNeeded(
                _recognitionStrategy.RequiresOffline,
                required: true,
                () => _serviceFactory.CreateSpeechRecognition(preset.OfflineModelId, defaults.OfflineModelId, "offline"),
                result =>
                {
                    _offlineService = result.Service;
                    _offlineRecognizer = result.Service != null
                        ? new SherpaOfflineSpeechRecognizer(result.Service)
                        : null;
                });

            CreateServiceIfNeeded(
                _recognitionStrategy.RequiresVoiceActivity,
                required: true,
                () => _serviceFactory.CreateVoiceActivityDetection(preset.VadModelId, defaults.VadModelId),
                result =>
                {
                    _vadService = result.Service;
                    _voiceFilter = result.Service != null ? new SherpaVoiceFilter(result.Service) : null;
                });

            CreateServiceIfNeeded(
                RequiresPunctuation,
                required: false,
                () => _serviceFactory.CreatePunctuation(preset.PunctuationModelId, defaults.PunctuationModelId),
                result => _punctService = result.Service);

            CreateServiceIfNeeded(
                RequiresKeywordSpotting,
                required: _recognitionStrategy.Mode == RecognitionMode.KeywordSpottingOnly,
                () => _serviceFactory.CreateKeywordSpotting(AsrConfig.ActiveKeywordOptions, KeywordOptions.Default.ModelId),
                result =>
                {
                    _keywordService = result.Service;
                    _keywordDetector = result.Service != null
                        ? new SherpaKeywordDetector(result.Service)
                        : null;
                });

            _progressAggregator.Reset(_expectedModelLoads);
        }

        private SpeechRecognition.Options CreateStreamingEndpointingOptions()
        {
            var turn = _turnDetectionSettings.EnsureValid();
            float min = Mathf.Max(0.01f, turn.MinDelaySeconds);
            float max = Mathf.Max(min, turn.MaxDelaySeconds);

            // Avoid aggressive internal endpointing that fragments speech into tiny utterances.
            // Turn detection still controls when the final transcript is submitted.
            float rule2 = Mathf.Clamp(max + Mathf.Max(0.15f, min * 0.5f), 0.6f, 1.2f);
            float rule1 = Mathf.Clamp(rule2 + 0.2f, rule2, 1.5f);

            return new SpeechRecognition.Options
            {
                Rule1MinTrailingSilence = rule1,
                Rule2MinTrailingSilence = rule2,
                Rule3MinUtteranceLength = 60
            };
        }


        private void CreateServiceIfNeeded<T>(
            bool condition,
            bool required,
            Func<ServiceCreationResult<T>> creator,
            Action<ServiceCreationResult<T>> assigner) where T : class
        {
            if (!condition)
            {
                assigner(ServiceCreationResult<T>.NoService);
                return;
            }

            var result = creator();
            if (result.CountsTowardsInitialization)
            {
                _expectedModelLoads++;
            }

            if (!result.IsSuccess)
            {
                if (required)
                {
                    _failedRequiredModelLoads++;
                }
                else
                {
                    _failedOptionalModelLoads++;
                }
            }

            assigner(result);
        }

        private void DisposeServicesCore()
        {
            if (!Initialized &&
                _recognitionLifetimeCts == null &&
                _streamingService == null &&
                _offlineService == null &&
                _vadService == null &&
                _keywordService == null &&
                _punctService == null &&
                _serviceFactory == null &&
                _feedbackReporter == null &&
                _voiceFilter == null &&
                _realtimeRecognizer == null &&
                _offlineRecognizer == null &&
                _keywordDetector == null)
            {
                return;
            }

            OnBeforeServicesDisposed();

            _recognitionGeneration++;
            ClearPendingCallbacks();
            CancelStreamingPreviewPunctuation();

            // CRITICAL: Dispose wrappers BEFORE CTS to avoid ObjectDisposedException
            DisposeServiceWrappers();
            DisposeCancellationToken();
            DisposeServiceInstances();
            ClearReferences();

            Initialized = false;
            OnAfterServicesDisposed();
        }

        private void DisposeServiceWrappers()
        {
            UnsubscribeAndDispose(ref _voiceFilter, f => f.OnVoiceActivityChanged -= HandleVoiceActivityFromFilter);
            UnsubscribeAndDispose(ref _realtimeRecognizer, r => r.OnRecognitionResult -= HandleStreamingRecognition);
            UnsubscribeAndDispose(ref _offlineRecognizer, r => r.OnRecognitionResult -= HandleOfflineRecognition);
            UnsubscribeAndDispose(ref _keywordDetector, d => d.OnKeywordDetected -= HandleKeywordDetected);
        }

        private void DisposeCancellationToken()
        {
            if (_recognitionLifetimeCts == null)
            {
                return;
            }

            try
            {
                if (!_recognitionLifetimeCts.IsCancellationRequested)
                {
                    _recognitionLifetimeCts.Cancel();
                }
            }
            catch (ObjectDisposedException) { }

            SafeDispose(ref _recognitionLifetimeCts);
        }

        private void DisposeServiceInstances()
        {
            SafeDispose(ref _streamingService);
            SafeDispose(ref _offlineService);
            SafeDispose(ref _vadService);
            SafeDispose(ref _keywordService);
            SafeDispose(ref _punctService);
        }

        private void ClearReferences()
        {
            _feedbackReporter = null;
            _serviceFactory = null;
            _expectedModelLoads = 0;
            _completedModelLoads = 0;
            _failedRequiredModelLoads = 0;
            _failedOptionalModelLoads = 0;
            _pendingStreamingResult = string.Empty;
            _cachedPunctuationInput = string.Empty;
            _cachedPunctuationOutput = string.Empty;
            InvalidateFinalPunctuationRequests();
        }

        private void ResetRecognitionStateCore()
        {
            CancelStreamingPreviewPunctuation();
            InvalidateFinalPunctuationRequests();
            _pendingStreamingResult = string.Empty;
            _cachedPunctuationInput = string.Empty;
            _cachedPunctuationOutput = string.Empty;
            _voiceActivity?.Reset();
            _keywordGate?.Reset(clearStreamingHistory: true);
            _recognitionBuffer?.Reset();
            _silenceRecognizer?.Reset();
            SetVoiceActivity(false);
        }

        private void DisposeAllResources()
        {
            if (!TryTransitionToDisposing())
            {
                return;
            }

            try
            {
                EnsureRecordingStopped();
                DisposeServicesCore();
                DisposeInfrastructure();
            }
            finally
            {
                SetState(LifecycleState.Disposed);
            }
        }

        private void DisposeInfrastructure()
        {
            if (_voiceActivity != null)
            {
                _voiceActivity.VoiceActivityChanged -= HandleVoiceActivityChanged;
                _voiceActivity = null;
            }

            if (_keywordGate != null)
            {
                _keywordGate.ActivityChanged -= HandleKeywordActivityChanged;
                _keywordGate.Activated -= HandleKeywordActivatedInternal;
                _keywordGate.Deactivated -= HandleKeywordDeactivatedInternal;
                _keywordGate = null;
            }

            if (_recognitionBuffer != null)
            {
                _recognitionBuffer.BufferAmended -= HandleBufferAmended;
                _recognitionBuffer.Finalized -= HandleFinalizedTranscript;
                _recognitionBuffer = null;
            }

            _silenceRecognizer = null;
            _turnDetector = null;
            _progressAggregator = null;
        }

        private void ApplyPendingDeviceOptions()
        {
            if (!_pendingDeviceOptions.HasValue)
            {
                return;
            }

            var options = _pendingDeviceOptions.Value;
            _pendingDeviceOptions = null;

            LogInfo("Applying pending device options...");
            ApplyDeviceOptions(options, _pendingRestartRecording);
        }

        #endregion

        #region Audio Pipeline

        private void ConfigureAudioPipeline(AudioPipeline pipeline)
        {
            // Ensure audio matches Sherpa model requirements (mono handled by base pipeline downmixer).
            // This must run before Sherpa workers so their Initialize() sees the final sample rate.
            if (this.DeviceOpts.SampleRate != DEFAULT_SAMPLE_RATE)
            {
                pipeline.AddWorker(new Resampler((int)DEFAULT_SAMPLE_RATE));
            }

            RefreshPipelineWorkers();

            var strategy = CurrentStrategy;
            var configurator = new AudioPipelineConfigurator(pipeline);
            configurator.ConfigureKeywordDetector(_keywordDetector, HandleKeywordDetected);

            switch (strategy.Mode)
            {
                case RecognitionMode.Streaming:
                    configurator.ConfigureStreamingRecognizer(_realtimeRecognizer, HandleStreamingRecognition);
                    break;

                case RecognitionMode.OfflineWithVad:
                    configurator.ConfigureVoiceFilter(_voiceFilter, HandleVoiceActivityFromFilter);
                    configurator.ConfigureOfflineRecognizer(_offlineRecognizer, HandleOfflineRecognition);
                    break;

                case RecognitionMode.Hybrid:
                    configurator.ConfigureStreamingRecognizer(_realtimeRecognizer, HandleStreamingRecognition);
                    configurator.ConfigureVoiceFilter(_voiceFilter, HandleVoiceActivityFromFilter);
                    configurator.ConfigureOfflineRecognizer(_offlineRecognizer, HandleOfflineRecognition);
                    break;

                case RecognitionMode.KeywordSpottingOnly:
                    break;

                default:
                    configurator.ConfigureStreamingRecognizer(_realtimeRecognizer, HandleStreamingRecognition);
                    break;
            }
        }

        private void RefreshPipelineWorkers()
        {
            // The audio pipeline may dispose workers when recording stops. Re-create wrappers per pipeline build.
            UnsubscribeAndDispose(ref _keywordDetector, d => d.OnKeywordDetected -= HandleKeywordDetected);
            UnsubscribeAndDispose(ref _realtimeRecognizer, r => r.OnRecognitionResult -= HandleStreamingRecognition);
            UnsubscribeAndDispose(ref _offlineRecognizer, r => r.OnRecognitionResult -= HandleOfflineRecognition);
            UnsubscribeAndDispose(ref _voiceFilter, f => f.OnVoiceActivityChanged -= HandleVoiceActivityFromFilter);

            _keywordDetector = _keywordService != null ? new SherpaKeywordDetector(_keywordService) : null;
            _realtimeRecognizer = _streamingService != null ? new SherpaRealtimeSpeechRecognizer(_streamingService) : null;
            _offlineRecognizer = _offlineService != null ? new SherpaOfflineSpeechRecognizer(_offlineService) : null;
            _voiceFilter = _vadService != null ? new SherpaVoiceFilter(_vadService) : null;
        }

        #endregion

        #region Recognition Processing

        private void HandleStreamingRecognition(string content) =>
            EnqueueCallback(new PendingCallback(PendingCallbackType.StreamingRecognition, content));

        private void HandleOfflineRecognition(string content) =>
            EnqueueCallback(new PendingCallback(PendingCallbackType.OfflineRecognition, content));

        private void ProcessStreamingEndMarkerMainThread(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Empty streaming results signal end-of-utterance; finalize punctuation on the main thread.
            CancelStreamingPreviewPunctuation();
            _recognitionBuffer?.EmitPartial(string.Empty);

            var strategy = CurrentStrategy;
            bool applyStreamingVoiceActivity = strategy.AppliesStreamingVoiceActivity;

            string pendingSnapshot = _pendingStreamingResult ?? string.Empty;
            bool hadSpeech = IsVoiceActivity || pendingSnapshot.Length != 0;
            if (applyStreamingVoiceActivity && hadSpeech)
            {
                SetVoiceActivity(false);
            }

            if (pendingSnapshot.Length == 0)
            {
                return;
            }

            // Clear before async punctuation to avoid committing stale content after reload/dispose.
            _pendingStreamingResult = string.Empty;

            if (!strategy.AllowsStreamingFinalCommit)
            {
                return;
            }

            if (RequiresPunctuation && _punctService != null)
            {
                CommitResult(pendingSnapshot);
                int requestId = Interlocked.Increment(ref _finalPunctuationRequestId);
                _ = CommitWithOptionalPunctuationAsync(pendingSnapshot, token, _recognitionGeneration, requestId, false);
                return;
            }

            CommitResult(pendingSnapshot);
        }

        private void ProcessOfflineResultWithOptionalPunctuationMainThread(string content, CancellationToken token, int generation)
        {
            if (token.IsCancellationRequested || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            // Offline results close the turn; voice activity is cleared before final commit.
            SetVoiceActivity(false);
            _silenceRecognizer?.Reset();

            if (RequiresPunctuation && _punctService != null)
            {
                CommitResult(content);
                int requestId = Interlocked.Increment(ref _finalPunctuationRequestId);
                _ = CommitWithOptionalPunctuationAsync(content, token, generation, requestId, true);
                return;
            }

            CommitResult(content);
            _recognitionBuffer?.FinalPush();
        }

        #region Punctuation

        private void CancelStreamingPreviewPunctuation()
        {
            Interlocked.Increment(ref _streamingPreviewPunctuationRequestId);

            if (_streamingPreviewPunctuationCts == null)
            {
                return;
            }

            try
            {
                if (!_streamingPreviewPunctuationCts.IsCancellationRequested)
                {
                    _streamingPreviewPunctuationCts.Cancel();
                }
            }
            catch (ObjectDisposedException) { }

            SafeDispose(ref _streamingPreviewPunctuationCts);
        }

        private void ScheduleStreamingPreviewPunctuation(string streaming)
        {
            CancelStreamingPreviewPunctuation();
            _streamingPreviewPunctuationCts = new CancellationTokenSource();

            int requestId = Interlocked.Increment(ref _streamingPreviewPunctuationRequestId);
            int generation = _recognitionGeneration;
            CancellationToken requestToken = _streamingPreviewPunctuationCts.Token;

            _ = PunctuateAndPublishStreamingPreviewAsync(streaming, requestId, generation, requestToken);
        }

        private async Task PunctuateAndPublishStreamingPreviewAsync(
            string streaming,
            int requestId,
            int generation,
            CancellationToken token)
        {
            try
            {
                if (STREAMING_PUNCTUATION_DEBOUNCE_MS > 0)
                {
                    await Task.Delay(STREAMING_PUNCTUATION_DEBOUNCE_MS, token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested || generation != _recognitionGeneration ||
                    requestId != _streamingPreviewPunctuationRequestId)
                {
                    return;
                }

                string punctuated = await ApplyPunctuationAsync(streaming, token).ConfigureAwait(false);

                if (token.IsCancellationRequested || generation != _recognitionGeneration ||
                    requestId != _streamingPreviewPunctuationRequestId)
                {
                    return;
                }

                PostToUnityThread(() => PublishStreamingPreview(punctuated));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogWarning($"Streaming punctuation pipeline failed: {ex.Message}");
                if (token.IsCancellationRequested || generation != _recognitionGeneration ||
                    requestId != _streamingPreviewPunctuationRequestId)
                {
                    return;
                }

                PostToUnityThread(() => PublishStreamingPreview(streaming));
            }
        }

        private async Task CommitWithOptionalPunctuationAsync(
            string content,
            CancellationToken token,
            int generation,
            int requestId,
            bool finalizeAfterCommit)
        {
            try
            {
                string text = await ApplyPunctuationAsync(content, token).ConfigureAwait(false);
                if (token.IsCancellationRequested || generation != _recognitionGeneration ||
                    requestId != _finalPunctuationRequestId)
                {
                    return;
                }

                PostToUnityThread(() =>
                {
                    if (requestId != _finalPunctuationRequestId)
                    {
                        return;
                    }

                    CommitResult(text);
                    if (finalizeAfterCommit)
                    {
                        _recognitionBuffer?.FinalPush();
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogWarning($"Punctuation pipeline failed: {ex.Message}");
                if (!finalizeAfterCommit)
                {
                    return;
                }

                PostToUnityThread(() =>
                {
                    if (token.IsCancellationRequested || generation != _recognitionGeneration ||
                        requestId != _finalPunctuationRequestId)
                    {
                        return;
                    }

                    _recognitionBuffer?.FinalPush();
                });
            }
        }

        private async Task<string> ApplyPunctuationAsync(string content, CancellationToken token)
        {
            content ??= string.Empty;

            if (content.Length == 0 || _punctService == null || token.IsCancellationRequested)
            {
                return content;
            }

            try
            {
                if (TryGetCachedPunctuation(content, out string cached))
                {
                    return cached;
                }

                await _punctuationSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (TryGetCachedPunctuation(content, out cached))
                    {
                        return cached;
                    }

                    string punctuated = await _punctService.AddPunctuationAsync(content, token).ConfigureAwait(false) ?? string.Empty;
                    _cachedPunctuationInput = content;
                    _cachedPunctuationOutput = punctuated;
                    return punctuated;
                }
                finally
                {
                    _punctuationSemaphore.Release();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogWarning($"Punctuation failed: {ex.Message}");
                return content;
            }
        }

        private bool TryGetCachedPunctuation(string content, out string cached)
        {
            if (string.IsNullOrEmpty(content))
            {
                cached = string.Empty;
                return true;
            }

            if (string.Equals(content, _cachedPunctuationOutput, StringComparison.Ordinal))
            {
                cached = content;
                return true;
            }

            if (string.Equals(content, _cachedPunctuationInput, StringComparison.Ordinal))
            {
                cached = _cachedPunctuationOutput;
                return true;
            }

            if (EndsWithPunctuation(content))
            {
                cached = content;
                return true;
            }

            cached = null;
            return false;
        }

        private static bool EndsWithPunctuation(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            int index = content.Length - 1;
            while (index >= 0 && char.IsWhiteSpace(content[index]))
            {
                index--;
            }

            if (index < 0)
            {
                return false;
            }

            char last = content[index];
            if (IsPunctuationTerminator(last))
            {
                return true;
            }

            if (IsTrailingCloser(last))
            {
                index--;
                while (index >= 0 && char.IsWhiteSpace(content[index]))
                {
                    index--;
                }

                return index >= 0 && IsPunctuationTerminator(content[index]);
            }

            return false;
        }

        private static bool IsPunctuationTerminator(char ch) => Array.IndexOf(PunctuationTerminators, ch) >= 0;

        private static bool IsTrailingCloser(char ch) => Array.IndexOf(PunctuationTrailingClosers, ch) >= 0;

        #endregion

        private void InvalidateFinalPunctuationRequests() =>
            Interlocked.Increment(ref _finalPunctuationRequestId);

        private void ProcessStreamingResult(string content, CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            content ??= string.Empty;
            _recognitionBuffer?.EmitPartial(content);

            var strategy = CurrentStrategy;
            bool applyStreamingVoiceActivity = strategy.AppliesStreamingVoiceActivity;
            bool trackPendingStreamingResult = strategy.AllowsStreamingFinalCommit || strategy.SubmitWhenSpeakingEndsWithoutOffline;

            if (content.Length != 0)
            {
                if (trackPendingStreamingResult)
                {
                    _pendingStreamingResult = content;
                }

                if (applyStreamingVoiceActivity)
                {
                    SetVoiceActivity(true);
                }

                return;
            }

            // Empty results are handled by ProcessStreamingEndMarkerAsync to keep ordering predictable (e.g., punctuation).
        }

        private void CommitResult(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !IsOperational)
            {
                return;
            }

            if (_recognitionLifetimeCts?.IsCancellationRequested == true)
            {
                return;
            }

            _recognitionBuffer?.Commit(text);
        }

        #endregion

        #region Event Handlers

        private void HandleBufferAmended(string buffer)
        {
            if (!IsOperational)
            {
                return;
            }

            OnTranscription(buffer, false);
        }

        private void HandleFinalizedTranscript(string text)
        {
            if (!IsOperational)
            {
                return;
            }

            OnTranscription(text, true);
            InvokeEvent(() => OnASRTranscriptionSubmit?.Invoke(text));
            _keywordGate?.OnUtteranceEnded();


        }

        private void HandleVoiceActivityChanged(bool isActive)
        {
            if (!IsOperational)
            {
                return;
            }

            _silenceRecognizer?.OnVoiceActivityChanged(isActive);
            InvokeEvent(() => OnVoiceActivityChanged?.Invoke(isActive));
            InvokeEvent(() => OnSpeakingChanged?.Invoke(isActive));
        }

        private void HandleKeywordActivityChanged(string keyword, bool isActive)
        {
            if (!IsOperational)
            {
                return;
            }

            InvokeEvent(() => OnKeywordActivityChanged?.Invoke(keyword, isActive));
        }

        private void HandleKeywordActivatedInternal(string keyword)
        {
            if (!IsOperational)
            {
                return;
            }

            OnKeywordActivated(keyword);
        }

        private void HandleKeywordDeactivatedInternal(string keyword)
        {
            if (!IsOperational)
            {
                return;
            }

            OnKeywordDeactivated();
        }

        private void HandleKeywordDetected(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            EnqueueCallback(new PendingCallback(PendingCallbackType.KeywordDetected, keyword));
        }

        private void HandleVoiceActivityFromFilter(bool isActive)
        {
            EnqueueCallback(new PendingCallback(PendingCallbackType.VoiceActivityChanged, isActive));
        }

        private void SetVoiceActivity(bool isActive) => _voiceActivity?.SetVoiceActivity(isActive);

        #endregion

        #region Initialization Finalization

        private void FinalizeInitialization()
        {
            if (Initialized)
            {
                return;
            }

            Initialized = true;
            SetState(LifecycleState.Ready);
            OnServicesInitialized(AsrConfig, _initializingPreset);
        }

        private void FinalizeInitializationIfReady()
        {
            if (Initialized)
            {
                return;
            }

            if (_expectedModelLoads == 0 || _completedModelLoads >= _expectedModelLoads)
            {
                FinalizeInitialization();
            }
        }

        #endregion

        #region Progress & Feedback

        private void PostProgress(Action<ModelProgressAggregator> register, string message)
        {
            PostToUnityThread(() =>
            {
                var aggregator = _progressAggregator;
                if (aggregator != null)
                {
                    register?.Invoke(aggregator);
                }

                PublishProgress(message);
            });
        }

        private void PublishProgress(string message)
        {
            if (_progressAggregator == null)
            {
                return;
            }

            float progress = _progressAggregator.CalculateProgress();
            InvokeEvent(() => OnLoadingProgressFeedback?.Invoke(message, progress));
        }

        #endregion

        #region ISherpaFeedbackHandler

        public void OnFeedback(PrepareFeedback feedback)
        {
            PostProgress(
                aggregator => aggregator.RegisterPrepare(feedback?.Metadata, feedback?.Message),
                feedback?.Message);
        }

        public void OnFeedback(DownloadFeedback feedback)
        {
            PostProgress(
                aggregator => aggregator.RegisterDownload(feedback?.Metadata, feedback?.Progress ?? 0, feedback?.Message),
                feedback?.Message);
        }

        public void OnFeedback(DecompressFeedback feedback)
        {
            PostProgress(
                aggregator => aggregator.RegisterDecompress(feedback?.Metadata, feedback?.Progress ?? 0, feedback?.Message),
                feedback?.Message);
        }

        public void OnFeedback(VerifyFeedback feedback)
        {
            PostProgress(
                aggregator => aggregator.RegisterVerify(feedback?.Metadata, feedback?.Progress ?? 0, feedback?.Message),
                feedback?.Message);
        }

        public void OnFeedback(LoadFeedback feedback)
        {
            PostProgress(
                aggregator => aggregator.RegisterLoad(feedback?.Metadata, feedback?.Message),
                feedback?.Message);
        }

        public void OnFeedback(CancelFeedback feedback) => PostProgress(null, feedback?.Message);
        public void OnFeedback(CleanFeedback feedback) => PostProgress(null, feedback?.Message);

        public void OnFeedback(SuccessFeedback feedback)
        {
            PostToUnityThread(() =>
            {
                _progressAggregator?.RegisterSuccess(feedback?.Metadata, feedback?.Message);
                _completedModelLoads++;
                PublishProgress(feedback?.Message);
                OnServiceLoadingSucceeded(feedback);
                InvokeEvent(() => OnLoadingSucceededFeedback?.Invoke(feedback));
                FinalizeInitializationIfReady();
            });
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            PostToUnityThread(() =>
            {
                _completedModelLoads++;
                if (IsLoadFailureRequired(feedback?.Metadata))
                {
                    _failedRequiredModelLoads++;
                }
                else
                {
                    _failedOptionalModelLoads++;
                }
                string modelId = feedback?.Metadata?.modelId;
                string moduleType = feedback?.Metadata?.moduleType.ToString();
                string detail = !string.IsNullOrWhiteSpace(modelId)
                    ? $" (modelId={modelId}{(string.IsNullOrWhiteSpace(moduleType) ? string.Empty : $", moduleType={moduleType}")})"
                    : string.Empty;
                LogError($"Model load failed{detail}: {feedback?.Message ?? "unknown error"}");
                PublishProgress(feedback?.Message);
                OnServiceLoadingFailed(feedback);
                InvokeEvent(() => OnLoadingFailedFeedback?.Invoke(feedback));
                FinalizeInitializationIfReady();
            });
        }

        #endregion

        #region Helper Methods

        private IRecognitionStrategy SelectStrategy(RecognitionMode mode) => mode switch
        {
            RecognitionMode.Streaming => StreamingStrategy,
            RecognitionMode.OfflineWithVad => OfflineStrategy,
            RecognitionMode.Hybrid => HybridStrategy,
            RecognitionMode.KeywordSpottingOnly => KeywordSpottingOnlyStrategy,
            _ => StreamingStrategy
        };

        private SherpaONNXFeedbackReporter EnsureFeedbackReporter() =>
            _feedbackReporter ??= new SherpaONNXFeedbackReporter(null, this);

        private bool IsLoadFailureRequired(SherpaONNXModelMetadata metadata)
        {
            if (metadata == null)
            {
                return true;
            }

            string moduleType = metadata.moduleType.ToString();
            if (string.IsNullOrWhiteSpace(moduleType))
            {
                return true;
            }

            string normalized = moduleType.Trim().ToLowerInvariant();
            if (normalized.Contains("punct"))
            {
                return false;
            }

            if (normalized.Contains("keyword"))
            {
                // Keyword spotting is only fatal when it's the only configured recognition mode.
                return CurrentStrategy.Mode == RecognitionMode.KeywordSpottingOnly;
            }

            if (normalized.Contains("vad") || normalized.Contains("voice"))
            {
                return CurrentStrategy.RequiresVoiceActivity;
            }

            // Speech recognition models are required when they are part of the selected strategy.
            return true;
        }

        private void EnsureRecordingStopped()
        {
            if (!IsRecording)
            {
                return;
            }

            LogInfo("Ensuring recording is stopped...");
            StopRecordingSafely();
            if (IsRecording)
            {
                LogWarning("Recording did not stop immediately; continuing without blocking the Unity main thread.");
            }
        }

        private void StopRecordingSafely()
        {
            try
            {
                if (IsRecording)
                {
                    StopRecording();
                }
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to stop recording: {ex.Message}");
            }
        }

        private void TryStartRecording()
        {
            if (!IsOperational)
            {
                return;
            }

            try
            {
                StartRecording();
            }
            catch (Exception ex)
            {
                LogError($"Failed to start recording: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates that the component is in Ready state.
        /// 验证组件是否处于 Ready 状态。
        /// </summary>
        private bool IsReady(string operation)
        {
            var state = _lifecycleState;

            if (state == LifecycleState.Initializing)
            {
                LogWarning($"Cannot {operation}: initialization in progress.");
                return false;
            }

            if (state == LifecycleState.Disposing)
            {
                LogWarning($"Cannot {operation}: disposal in progress.");
                return false;
            }

            if (state != LifecycleState.Ready)
            {
                LogWarning($"Cannot {operation}: component not ready (current state: {state}).");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that the component is not in a transitioning state.
        /// 验证组件不在过渡状态。
        /// </summary>
        private bool IsStable(string operation)
        {
            var state = _lifecycleState;

            if (state == LifecycleState.Initializing)
            {
                LogWarning($"Cannot {operation}: initialization in progress. Please wait or call after initialization completes.");
                return false;
            }

            if (state == LifecycleState.Disposing)
            {
                LogWarning($"Cannot {operation}: disposal in progress.");
                return false;
            }

            return true;
        }

        #endregion

        #region State Management Helpers

        private bool TryTransitionState(LifecycleState expected, LifecycleState newState)
        {
            lock (_stateLock)
            {
                if (_lifecycleState != expected)
                {
                    return false;
                }

                _lifecycleState = newState;
                return true;
            }
        }

        private void SetState(LifecycleState newState)
        {
            lock (_stateLock)
            {
                _lifecycleState = newState;
            }
        }

        private bool TryTransitionToDisposing()
        {
            lock (_stateLock)
            {
                if (_lifecycleState == LifecycleState.Disposing || _lifecycleState == LifecycleState.Disposed)
                {
                    return false;
                }

                _lifecycleState = LifecycleState.Disposing;
                return true;
            }
        }

        #endregion

        #region Threading Helpers

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

        private void PostToUnityThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            var context = _unityContext;
            if (context == null)
            {
                // Unity context is expected to be captured during initialization. Avoid invoking Unity-facing
                // work on a worker thread if feedback arrives unexpectedly early.
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

        private void EnqueueCallback(PendingCallback callback)
        {
            var cts = _recognitionLifetimeCts;
            if (cts == null)
            {
                return;
            }

            try
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _pendingCallbacks.Enqueue(callback);
        }

        private void ClearPendingCallbacks()
        {
            while (_pendingCallbacks.TryDequeue(out _))
            {
            }
        }

        private void DrainPendingCallbacks()
        {
            // Drain on main thread to preserve ordering and avoid Unity API calls from worker threads.
            while (_pendingCallbacks.TryDequeue(out var callback))
            {
                if (!IsOperational)
                {
                    continue;
                }

                CancellationToken token;
                var cts = _recognitionLifetimeCts;
                if (cts == null)
                {
                    return;
                }

                try
                {
                    token = cts.Token;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                switch (callback.Type)
                {
                    case PendingCallbackType.StreamingRecognition:
                        if (callback.Text.Length != 0)
                        {
                            ProcessStreamingResult(callback.Text, token);
                        }
                        else
                        {
                            ProcessStreamingEndMarkerMainThread(token);
                        }
                        break;

                    case PendingCallbackType.OfflineRecognition:
                        ProcessOfflineResultWithOptionalPunctuationMainThread(callback.Text, token, _recognitionGeneration);
                        break;

                    case PendingCallbackType.KeywordDetected:
                        _keywordGate?.Activate(callback.Text);
                        break;

                    case PendingCallbackType.VoiceActivityChanged:
                        SetVoiceActivity(callback.Bool);
                        break;
                }
            }
        }

        #endregion

        #region Dispose Helpers

        private static void SafeDispose<T>(ref T disposable) where T : class, IDisposable
        {
            if (disposable == null)
            {
                return;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_PREFIX} Failed to dispose {typeof(T).Name}: {ex.Message}");
            }
            finally
            {
                disposable = null;
            }
        }

        private static void UnsubscribeAndDispose<T>(ref T disposable, Action<T> unsubscribe)
            where T : class, IDisposable
        {
            if (disposable == null)
            {
                return;
            }

            try
            {
                unsubscribe?.Invoke(disposable);
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LOG_PREFIX} Failed to dispose {typeof(T).Name}: {ex.Message}");
            }
            finally
            {
                disposable = null;
            }
        }

        private void InvokeEvent(Action action)
        {
            try
            {
                if (action == null)
                {
                    return;
                }

                if (!IsOnUnityThread && _unityContext != null)
                {
                    _unityContext.Post(static state => ((Action)state)(), action);
                    return;
                }

                if (!IsOnUnityThread && _unityContext == null)
                {
                    return;
                }

                action.Invoke();
            }
            catch (Exception ex)
            {
                LogError($"Event invocation failed: {ex.Message}");
            }
        }

        #endregion

        #region Logging

        private static void LogInfo(string message) => Debug.Log($"{LOG_PREFIX} {message}");
        private static void LogWarning(string message) => Debug.LogWarning($"{LOG_PREFIX} {message}");
        private static void LogError(string message) => Debug.LogError($"{LOG_PREFIX} {message}");

        #endregion
    }
}
#endif
