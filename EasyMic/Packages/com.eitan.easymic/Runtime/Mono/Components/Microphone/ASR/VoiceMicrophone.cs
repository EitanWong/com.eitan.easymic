#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.SherpaONNXUnity;
using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Core;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Voice Microphone Component - Coordinates Sherpa-ONNX services and provides microphone-driven transcription events.
    /// 语音麦克风组件 - 协调 Sherpa-ONNX 服务并提供麦克风驱动的转录事件。
    /// </summary>
    [AddComponentMenu("Audio/Input/Voice Microphone")]
    public class VoiceMicrophone : EasyMicrophone, ISherpaFeedbackHandler
    {
        #region Constants & Enums

        private const SampleRate DEFAULT_SAMPLE_RATE = SampleRate.Hz16000;
        private const string LOG_PREFIX = "[VoiceMicrophone]";

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

        #endregion

        #region Static Strategies

        private static readonly StreamingRecognitionStrategy StreamingStrategy = new();
        private static readonly OfflineWithVadRecognitionStrategy OfflineStrategy = new();
        private static readonly HybridRecognitionStrategy HybridStrategy = new();
        private static readonly KeywordSpottingOnlyRecognitionStrategy KeywordSpottingOnlyStrategy = new();

        #endregion

        #region Serialized Fields

        [Header("ASR Configuration")]
        [SerializeField]
        [Tooltip("Automatic Speech Recognition Configuration")]
        private AutomaticSpeechRecognitionConfiguration _asrConfig;

        #endregion

        #region Service References

        private SpeechRecognition _streamingService;
        private SpeechRecognition _offlineService;
        private VoiceActivityDetection _vadService;
        private KeywordSpotting _keywordService;
        private Punctuation _punctService;

        #endregion

        #region Service Wrappers

        private SherpaVoiceFilter _voiceFilter;
        private SherpaRealtimeSpeechRecognizer _realtimeRecognizer;
        private SherpaOfflineSpeechRecognizer _offlineRecognizer;
        private SherpaKeywordDetector _keywordDetector;

        #endregion

        #region Infrastructure

        private SherpaONNXFeedbackReporter _feedbackReporter;
        private SherpaServiceFactory _serviceFactory;
        private ModelProgressAggregator _progressAggregator;

        #endregion

        #region State Management

        private VoiceActivityMonitor _voiceActivity;
        private KeywordGate _keywordGate;
        private RecognitionBuffer _recognitionBuffer;
        private TurnDetector _turnDetector;
        private SilenceTurnRecognizer _silenceRecognizer;
        private IRecognitionStrategy _recognitionStrategy;
        private CancellationTokenSource _recognitionLifetimeCts;

        private AutomaticSpeechRecognitionConfiguration.ASRPreset _initializingPreset;
        private TurnDetectionOptions _turnDetectionSettings;
        private string _pendingStreamingResult = string.Empty;
        private int _expectedModelLoads;
        private int _completedModelLoads;

        // Pending device options to apply on initialization / 待在初始化时应用的设备选项
        private DeviceOptions? _pendingDeviceOptions;
        private bool _pendingRestartRecording;

        // Unified state management / 统一状态管理
        private readonly object _stateLock = new();
        private volatile LifecycleState _lifecycleState = LifecycleState.Uninitialized;

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

        public bool AreModelsLoaded => Initialized && _completedModelLoads >= _expectedModelLoads;
        public bool AreModelsDisposed => _lifecycleState == LifecycleState.Disposed;
        public bool IsInitializing => _lifecycleState == LifecycleState.Initializing;

        public bool IsOperational => Initialized && _lifecycleState == LifecycleState.Ready;

        private IRecognitionStrategy CurrentStrategy =>
            _recognitionStrategy ?? SelectStrategy(AsrConfig.RecognitionMode);

        #endregion

        #region Events

        public event Action<string> OnASRTranscriptionStreaming;
        public event Action<string> OnASRTranscriptionSubmit;
        public event Action<bool> OnVoiceActivityChanged;
        public event Action<bool> OnSpeakingChanged;
        public event Action<string, bool> OnKeywordActivityChanged;
        public event Action<string, float> OnLoadingProgressFeedback;
        public event Action<FailedFeedback> OnLoadingFailedFeedback;
        public event Action<SuccessFeedback> OnLoadingSucceededFeedback;
        public event Action OnModelsDisposed;
        public event Action OnModelsReloaded;

        #endregion

        #region Unity Lifecycle

        protected override void OnInitialization()
        {
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
            if (!IsOperational)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            _keywordGate?.Update(deltaTime, IsSpeaking, IsVoiceActivity);
            _silenceRecognizer?.Update(deltaTime, _recognitionBuffer);
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

                if (currentState != LifecycleState.Disposed)
                {
                    DisposeServicesCore();
                }

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

            _turnDetector = new AdaptiveTurnDetector(_turnDetectionSettings);
            _silenceRecognizer?.ConfigureDetector(_turnDetector);
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
            _progressAggregator ??= new ModelProgressAggregator();
            _turnDetectionSettings = AsrConfig.ActiveTurnDetectionOptions;
            _turnDetector ??= new AdaptiveTurnDetector(_turnDetectionSettings);

            InitializeVoiceActivityMonitor();
            InitializeKeywordGate();
            InitializeRecognitionBuffer();
            _silenceRecognizer ??= new SilenceTurnRecognizer(_turnDetector);
            _silenceRecognizer.ConfigureDetector(_turnDetector);
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
                streaming => InvokeEvent(() => OnASRTranscriptionStreaming?.Invoke(streaming)));

            _recognitionBuffer.BufferAmended += HandleBufferAmended;
            _recognitionBuffer.Finalized += HandleFinalizedTranscript;
        }

        private void InitializeServices(bool preserveRecordingState = true)
        {
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

            CreateServiceIfNeeded(
                _recognitionStrategy.RequiresStreaming,
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
                () => _serviceFactory.CreateVoiceActivityDetection(preset.VadModelId, defaults.VadModelId),
                result =>
                {
                    _vadService = result.Service;
                    _voiceFilter = result.Service != null ? new SherpaVoiceFilter(result.Service) : null;
                });

            CreateServiceIfNeeded(
                RequiresPunctuation,
                () => _serviceFactory.CreatePunctuation(preset.PunctuationModelId, defaults.PunctuationModelId),
                result => _punctService = result.Service);

            CreateServiceIfNeeded(
                RequiresKeywordSpotting,
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

            assigner(result);
        }

        private void DisposeServicesCore()
        {
            OnBeforeServicesDisposed();

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
            _pendingStreamingResult = string.Empty;
        }

        private void ResetRecognitionStateCore()
        {
            _pendingStreamingResult = string.Empty;
            _voiceActivity?.Reset();
            _keywordGate?.Reset(clearStreamingHistory: true);
            _recognitionBuffer?.Reset();
            _silenceRecognizer?.Reset();
            SetVoiceActivity(false);
        }

        private void DisposeAllResources()
        {
            if (!TryTransitionState(_lifecycleState, LifecycleState.Disposing))
            {
                // Force dispose if already disposing or in bad state
                if (_lifecycleState == LifecycleState.Disposing)
                {
                    return;
                }
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
            ProcessRecognition(content, isStreaming: true);

        private void HandleOfflineRecognition(string content) =>
            ProcessRecognition(content, isStreaming: false);

        private async void ProcessRecognition(string content, bool isStreaming)
        {
            if (!IsOperational)
            {
                return;
            }

            var token = _recognitionLifetimeCts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
            {
                return;
            }

            content ??= string.Empty;

            try
            {
                if (isStreaming)
                {
                    content ??= string.Empty;
                    if (content.Length != 0)
                    {
                        // Keep streaming path as low-latency as possible; punctuation is applied only when committing.
                        ProcessStreamingResult(content, token);
                        return;
                    }

                    await ProcessStreamingEndMarkerAsync(token);
                }
                else
                {
                    content = await ApplyPunctuationAsync(content, token);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    ProcessOfflineResult(content, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogError($"Recognition pipeline failed: {ex.Message}");
            }
        }

        private async Task ProcessStreamingEndMarkerAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

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

            string pending = pendingSnapshot;
            if (strategy.AllowsStreamingFinalCommit && _punctService != null)
            {
                string punctuated = await ApplyPunctuationAsync(pending, token);
                if (!token.IsCancellationRequested && !string.IsNullOrWhiteSpace(punctuated))
                {
                    pending = punctuated;
                }
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (strategy.AllowsStreamingFinalCommit)
            {
                CommitResult(pending);
            }

            if (hadSpeech &&
                string.Equals(_pendingStreamingResult, pendingSnapshot, StringComparison.Ordinal))
            {
                _pendingStreamingResult = string.Empty;
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
                return await _punctService.AddPunctuationAsync(content) ?? string.Empty;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogWarning($"Punctuation failed: {ex.Message}");
                return content ?? string.Empty;
            }
        }

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

        private void ProcessOfflineResult(string content, CancellationToken token)
        {
            if (token.IsCancellationRequested || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            SetVoiceActivity(false);
            CommitResult(content);
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
            if (!IsOperational || string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            _keywordGate?.Activate(keyword);
        }

        private void HandleVoiceActivityFromFilter(bool isActive)
        {
            if (!IsOperational)
            {
                return;
            }

            SetVoiceActivity(isActive);
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
            _progressAggregator?.RegisterPrepare(feedback?.Metadata, feedback?.Message);
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(DownloadFeedback feedback)
        {
            _progressAggregator?.RegisterDownload(feedback?.Metadata, feedback?.Progress ?? 0, feedback?.Message);
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(DecompressFeedback feedback)
        {
            _progressAggregator?.RegisterDecompress(feedback?.Metadata, feedback?.Progress ?? 0, feedback?.Message);
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(VerifyFeedback feedback)
        {
            _progressAggregator?.RegisterVerify(feedback?.Metadata, feedback?.Progress ?? 0, feedback?.Message);
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(LoadFeedback feedback)
        {
            _progressAggregator?.RegisterLoad(feedback?.Metadata, feedback?.Message);
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(CancelFeedback feedback) => PublishProgress(feedback?.Message);
        public void OnFeedback(CleanFeedback feedback) => PublishProgress(feedback?.Message);

        public void OnFeedback(SuccessFeedback feedback)
        {
            _progressAggregator?.RegisterSuccess(feedback?.Metadata, feedback?.Message);
            _completedModelLoads++;
            PublishProgress(feedback?.Message);
            OnServiceLoadingSucceeded(feedback);
            InvokeEvent(() => OnLoadingSucceededFeedback?.Invoke(feedback));
            FinalizeInitializationIfReady();
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            _completedModelLoads++;
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

        private void EnsureRecordingStopped()
        {
            if (!IsRecording)
            {
                return;
            }

            LogInfo("Ensuring recording is stopped...");
            for (int i = 0; i < 3 && IsRecording; i++)
            {
                StopRecordingSafely();
                if (IsRecording)
                {
                    Thread.Sleep(50);
                }
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
                action?.Invoke();
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
