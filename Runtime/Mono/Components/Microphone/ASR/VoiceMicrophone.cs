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
    /// Coordinates Sherpa-ONNX services and exposes microphone-driven transcription events.
    /// </summary>
    [AddComponentMenu("Audio/Input/Voice Microphone")]
    public class VoiceMicrophone : EasyMicrophone, ISherpaFeedbackHandler
    {
        private const SampleRate DEFAULT_SAMPLERATE = SampleRate.Hz16000;

        private static readonly StreamingRecognitionStrategy StreamingStrategy = new StreamingRecognitionStrategy();
        private static readonly OfflineWithVadRecognitionStrategy OfflineStrategy = new OfflineWithVadRecognitionStrategy();
        private static readonly HybridRecognitionStrategy HybridStrategy = new HybridRecognitionStrategy();

        #region Serialized Fields

        [SerializeField]
        private AutomaticSpeechRecognitionConfiguration _asrConfig;

        #endregion

        #region Services

        private SpeechRecognition _streamingService;
        private SpeechRecognition _offlineService;
        private VoiceActivityDetection _vadService;
        private KeywordSpotting _keywordService;
        private Punctuation _punctService;

        private SherpaVoiceFilter _voiceFilter;
        private SherpaRealtimeSpeechRecognizer _realtimeRecognizer;
        private SherpaOfflineSpeechRecognizer _offlineRecognizer;
        private SherpaKeywordDetector _keywordDetector;


        private SherpaONNXFeedbackReporter _feedbackReporter;
        private SherpaServiceFactory _serviceFactory;

        #endregion

        #region State

        private VoiceActivityMonitor _voiceActivity;
        private KeywordGate _keywordGate;
        private RecognitionBuffer _recognitionBuffer;
        private TurnDetector _turnDetector;
        private SilenceTurnRecognizer _silenceRecognizer;
        private AutomaticSpeechRecognitionConfiguration.ASRPreset _initializingPreset;
        private TurnDetectionOptions _turnDetectionSettings;
        private ModelProgressAggregator _progressAggregator;
        private IRecognitionStrategy _recognitionStrategy;
        private CancellationTokenSource _recognitionLifetimeCts;

        private string _pendingStreamingResult = string.Empty;
        private int _expectedModelLoads;
        private int _completedModelLoads;


        #endregion

        #region Properties

        /// <summary>
        /// Gets the active ASR configuration (clone created on first access).
        /// </summary>
        public AutomaticSpeechRecognitionConfiguration AsrConfig
        {
            get => _asrConfig ??= AutomaticSpeechRecognitionConfiguration.CreateDefault();
            private set => _asrConfig = value;
        }

        /// <summary>
        /// Gets whether the speech state machine currently considers the user to be speaking.
        /// </summary>
        public bool IsSpeaking => IsVoiceActivity;

        /// <summary>
        /// Gets whether voice activity is currently detected.
        /// </summary>
        public bool IsVoiceActivity => _voiceActivity?.IsVoiceActive ?? false;

        /// <summary>
        /// Gets a value indicating whether the current strategy requires streaming recognition.
        /// </summary>
        public bool RequiresStreaming => SelectStrategy(AsrConfig.RecognitionMode).RequiresStreaming;

        /// <summary>
        /// Gets a value indicating whether the current strategy requires offline recognition.
        /// </summary>
        public bool RequiresOffline => SelectStrategy(AsrConfig.RecognitionMode).RequiresOffline;

        /// <summary>
        /// Gets a value indicating whether the current strategy requires voice activity detection.
        /// </summary>
        public bool RequiresVad => SelectStrategy(AsrConfig.RecognitionMode).RequiresVoiceActivity;

        /// <summary>
        /// Gets a value indicating whether punctuation services are enabled for the active preset.
        /// </summary>
        public bool RequiresPunctuation =>
            AsrConfig.EnablePunctuation &&
            !string.IsNullOrWhiteSpace(AsrConfig.PunctuationModelId);

        /// <summary>
        /// Gets a value indicating whether keyword spotting is enabled.
        /// </summary>
        public bool RequiresKeywordSpotting => AsrConfig.ActiveKeywordOptions.IsEnabled;

        /// <summary>
        /// Gets the presets supported by the current configuration.
        /// </summary>
        public IReadOnlyList<AutomaticSpeechRecognitionConfiguration.ASRPreset> SupportedPresets =>
            AsrConfig.SupportedPresets;

        /// <summary>
        /// Gets the identifier of the active preset.
        /// </summary>
        public string ActivePresetId => AsrConfig.ActivePresetId;

        /// <summary>
        /// Gets a copy of the active preset.
        /// </summary>
        public AutomaticSpeechRecognitionConfiguration.ASRPreset ActivePresetConfiguration =>
            AsrConfig.GetActivePreset();

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
        #endregion

        #region Unity Lifecycle

        protected override void OnInitialization()
        {
            EnsureInfrastructure();
            InitializeServices(preserveRecordingState: false);
        }

        protected override void OnAudioPiplineBuild(AudioPipeline pipeline)
        {
            if (pipeline == null)
            {
                return;
            }

            var configurator = new AudioPipelineConfigurator(pipeline);
            configurator.ConfigureKeywordDetector(_keywordDetector, HandleKeywordDetected);
            configurator.ConfigureStreamingRecognizer(_realtimeRecognizer, HandleStreamingRecognition);
            configurator.ConfigureVoiceFilter(_voiceFilter, HandleVoiceActivityFromFilter);
            configurator.ConfigureOfflineRecognizer(_offlineRecognizer, HandleOfflineRecognition);
            OnAudioPipelineReady(pipeline);
        }

        protected override void OnMicrophoneUpdate()
        {
            float deltaTime = Time.deltaTime;
            _keywordGate?.Update(deltaTime, IsSpeaking, IsVoiceActivity);
            _silenceRecognizer?.Update(deltaTime, _recognitionBuffer);
        }

        protected override void OnMicrophoneDispose()
        {
            DisposeServices();
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Applies a new configuration and rebuilds services.
        /// </summary>
        public void ApplyConfiguration(AutomaticSpeechRecognitionConfiguration configuration)
        {
            if (configuration == null)
            {
                return;
            }

            AsrConfig = configuration.Clone();
            if (Initialized)
            {
                InitializeServices();
            }
        }

        /// <summary>
        /// Attempts to activate a preset by identifier.
        /// </summary>
        public bool TrySetActivePreset(string presetId)
        {
            if (!AsrConfig.TrySelectPreset(presetId))
            {
                Debug.LogWarning($"VoiceMicrophone: preset '{presetId}' is not available in the current configuration.");
                return false;
            }

            if (Initialized)
            {
                try
                {
                    InitializeServices();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"VoiceMicrophone: failed to apply preset '{presetId}': {ex.Message}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Applies new turn detection settings for adaptive silence handling.
        /// </summary>
        public void ConfigureTurnDetection(TurnDetectionOptions settings)
        {
            _turnDetectionSettings = settings.EnsureValid();
            _turnDetector = CreateTurnDetector(_turnDetectionSettings);
            _silenceRecognizer?.ConfigureDetector(_turnDetector);
            _silenceRecognizer?.Reset();
            _recognitionBuffer?.Reset();
        }

        /// <summary>
        /// Switches the capture device used by this microphone.
        /// </summary>
        public void SwitchCaptureDevice(MicDevice device, Channel channel, SampleRate sampleRate, bool restartRecording = true)
        {
            var options = new DeviceOptions(device, channel, sampleRate);
            ApplyDeviceOptions(options, restartRecording);
        }

        public void SetGithubProxy(string url)
        {
            SherpaONNXUnityAPI.SetGithubProxy(url);
        }

        #endregion

        protected virtual void OnBeforeServicesInitializationRequested(
            AutomaticSpeechRecognitionConfiguration configuration,
            AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        {
        }

        protected virtual void OnServicesInitializationRequested(
            AutomaticSpeechRecognitionConfiguration configuration,
            AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        {
        }

        protected virtual void OnServicesInitialized(
            AutomaticSpeechRecognitionConfiguration configuration,
            AutomaticSpeechRecognitionConfiguration.ASRPreset preset)
        {
        }

        protected virtual void OnServiceLoadingFailed(FailedFeedback feedback)
        {
        }
        protected virtual void OnServiceLoadingSucceeded(SuccessFeedback feedback)
        {
        }

        protected virtual void OnBeforeServicesDisposed()
        {
        }

        protected virtual void OnAfterServicesDisposed()
        {
        }

        protected virtual void OnAudioPipelineReady(AudioPipeline pipeline)
        {
        }

        protected virtual void OnKeywordActivated(string keyword)
        {
        }

        protected virtual void OnKeywordDeactivated()
        {
        }

        protected virtual void OnTranscription(string result, bool end)
        {
        }

        #region Service Lifecycle

        private void EnsureInfrastructure()
        {
            _progressAggregator ??= new ModelProgressAggregator();
            _turnDetectionSettings = AsrConfig.ActiveTurnDetectionOptions;
            _turnDetector = CreateTurnDetector(_turnDetectionSettings);

            if (_voiceActivity == null)
            {
                _voiceActivity = new VoiceActivityMonitor();
                _voiceActivity.VoiceActivityChanged += HandleVoiceActivityChanged;
            }

            var keywordSettings = AsrConfig.ActiveKeywordOptions;
            if (_keywordGate == null)
            {
                _keywordGate = new KeywordGate(
                    keywordSettings,
                    Mathf.Max(0f, keywordSettings.ContinuousConversationTimeoutSeconds),
                    0f,
                    null);

                _keywordGate.ActivityChanged += HandleKeywordActivityChanged;
                _keywordGate.Activated += HandleKeywordActivatedInternal;
                _keywordGate.Deactivated += HandleKeywordDeactivatedInternal;
            }
            else
            {
                _keywordGate.ApplySettings(keywordSettings);
            }

            if (_recognitionBuffer == null)
            {
                _recognitionBuffer = new RecognitionBuffer(
                    _keywordGate,
                    streaming => OnASRTranscriptionStreaming?.Invoke(streaming));

                _recognitionBuffer.BufferAmended += HandleBufferAmended;
                _recognitionBuffer.Finalized += HandleFinalizedTranscript;
            }

            _silenceRecognizer ??= new SilenceTurnRecognizer(_turnDetector);
            _silenceRecognizer.ConfigureDetector(_turnDetector);
            _silenceRecognizer.Reset();
            _recognitionBuffer.Reset();
        }

        private void InitializeServices(bool preserveRecordingState = true)
        {
            bool wasRecording = preserveRecordingState && IsRecording;

            if (wasRecording)
            {
                try
                {
                    StopRecording();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"VoiceMicrophone: failed to stop recording before reconfiguration - {ex.Message}");
                    wasRecording = false;
                }
            }

            DisposeServices();
            EnsureInfrastructure();

            _voiceActivity.Reset();
            _keywordGate.Reset(clearStreamingHistory: true);
            _recognitionBuffer.Reset();
            _silenceRecognizer.Reset();
            _pendingStreamingResult = string.Empty;

            _recognitionStrategy = SelectStrategy(AsrConfig.RecognitionMode);
            _recognitionLifetimeCts = new CancellationTokenSource();

            var config = AsrConfig;
            var preset = config.GetActivePreset();
            _initializingPreset = preset;

            OnBeforeServicesInitializationRequested(config, preset);

            var defaults = AutomaticSpeechRecognitionConfiguration.ASRPreset.Default;
            _serviceFactory = new SherpaServiceFactory((int)DEFAULT_SAMPLERATE, EnsureFeedbackReporter());

            _expectedModelLoads = 0;
            _completedModelLoads = 0;

            var streamingResult = _recognitionStrategy.RequiresStreaming
                ? _serviceFactory.CreateSpeechRecognition(preset.StreamingModelId, defaults.StreamingModelId, "streaming recognition")
                : ServiceCreationResult<SpeechRecognition>.NoService;

            var offlineResult = _recognitionStrategy.RequiresOffline
                ? _serviceFactory.CreateSpeechRecognition(preset.OfflineModelId, defaults.OfflineModelId, "offline recognition")
                : ServiceCreationResult<SpeechRecognition>.NoService;

            var vadResult = _recognitionStrategy.RequiresVoiceActivity
                ? _serviceFactory.CreateVoiceActivityDetection(preset.VadModelId, defaults.VadModelId)
                : ServiceCreationResult<VoiceActivityDetection>.NoService;

            var punctuationResult = RequiresPunctuation
                ? _serviceFactory.CreatePunctuation(preset.PunctuationModelId, defaults.PunctuationModelId)
                : ServiceCreationResult<Punctuation>.NoService;

            var keywordResult = RequiresKeywordSpotting
                ? _serviceFactory.CreateKeywordSpotting(config.ActiveKeywordOptions, KeywordOptions.Default.ModelId)
                : ServiceCreationResult<KeywordSpotting>.NoService;

            if (streamingResult.CountsTowardsInitialization)
            {
                _expectedModelLoads++;
            }


            if (offlineResult.CountsTowardsInitialization)
            {
                _expectedModelLoads++;
            }

            if (vadResult.CountsTowardsInitialization)
            {
                _expectedModelLoads++;
            }


            if (punctuationResult.CountsTowardsInitialization)
            {
                _expectedModelLoads++;
            }


            if (keywordResult.CountsTowardsInitialization)
            {
                _expectedModelLoads++;
            }


            AssignServices(streamingResult, offlineResult, vadResult, punctuationResult, keywordResult);

            _progressAggregator.Reset(_expectedModelLoads);
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

        private void AssignServices(
            ServiceCreationResult<SpeechRecognition> streamingResult,
            ServiceCreationResult<SpeechRecognition> offlineResult,
            ServiceCreationResult<VoiceActivityDetection> vadResult,
            ServiceCreationResult<Punctuation> punctuationResult,
            ServiceCreationResult<KeywordSpotting> keywordResult)
        {
            _streamingService = streamingResult.Service;
            _realtimeRecognizer = _streamingService != null ? new SherpaRealtimeSpeechRecognizer(_streamingService) : null;

            _offlineService = offlineResult.Service;
            _offlineRecognizer = _offlineService != null ? new SherpaOfflineSpeechRecognizer(_offlineService) : null;

            _vadService = vadResult.Service;
            _voiceFilter = _vadService != null ? new SherpaVoiceFilter(_vadService) : null;

            _punctService = punctuationResult.Service;

            _keywordService = keywordResult.Service;
            _keywordDetector = _keywordService != null ? new SherpaKeywordDetector(_keywordService) : null;
        }

        private void DisposeServices()
        {
            OnBeforeServicesDisposed();

            _recognitionLifetimeCts?.Cancel();
            _recognitionLifetimeCts?.Dispose();
            _recognitionLifetimeCts = null;

            if (_voiceFilter != null)
            {
                _voiceFilter.OnVoiceActivityChanged -= HandleVoiceActivityFromFilter;
                SafeDispose(ref _voiceFilter);
            }

            if (_realtimeRecognizer != null)
            {
                _realtimeRecognizer.OnRecognitionResult -= HandleStreamingRecognition;
                SafeDispose(ref _realtimeRecognizer);
            }

            if (_offlineRecognizer != null)
            {
                _offlineRecognizer.OnRecognitionResult -= HandleOfflineRecognition;
                SafeDispose(ref _offlineRecognizer);
            }

            if (_keywordDetector != null)
            {
                _keywordDetector.OnKeywordDetected -= HandleKeywordDetected;
                SafeDispose(ref _keywordDetector);
            }

            SafeDispose(ref _streamingService);
            SafeDispose(ref _offlineService);
            SafeDispose(ref _vadService);
            SafeDispose(ref _keywordService);
            SafeDispose(ref _punctService);

            _feedbackReporter = null;
            _serviceFactory = null;
            _expectedModelLoads = 0;
            _completedModelLoads = 0;
            _pendingStreamingResult = string.Empty;

            _voiceActivity?.Reset();
            _keywordGate?.Reset(clearStreamingHistory: true);
            _recognitionBuffer?.Reset();
            _silenceRecognizer?.Reset();
            SetVoiceActivity(false);
            Initialized = false;

            OnAfterServicesDisposed();
        }

        private void TryStartRecording()
        {
            try
            {
                StartRecording();
            }
            catch (Exception ex)
            {
                Debug.LogError($"VoiceMicrophone: failed to restart recording after configuration update - {ex.Message}");
            }
        }

        #endregion

        private void HandleStreamingRecognition(string content) => _ = ProcessRecognitionAsync(content, isStreaming: true);

        private void HandleOfflineRecognition(string content) => _ = ProcessRecognitionAsync(content, isStreaming: false);

        private async Task ProcessRecognitionAsync(string content, bool isStreaming)
        {
            var token = _recognitionLifetimeCts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
            {
                return;
            }

            content ??= string.Empty;

            try
            {
                if (_punctService != null)
                {
                    try
                    {
                        content = await _punctService.AddPunctuationAsync(content);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"VoiceMicrophone: punctuation failed - {ex.Message}");
                    }
                }

                content ??= string.Empty;

                if (isStreaming)
                {
                    await HandleStreamingAsync(content, token);
                }
                else
                {
                    await HandleOfflineAsync(content, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Intended cancellation path.
            }
            catch (Exception ex)
            {
                Debug.LogError($"VoiceMicrophone: recognition pipeline failed - {ex.Message}");
            }
        }

        #region Recognition Flow

        private Task HandleStreamingAsync(string content, CancellationToken token)
        {
            string payload = content ?? string.Empty;
            bool hasContent = payload.Length > 0;

            _recognitionBuffer.EmitPartial(payload);

            if (hasContent)
            {
                SetVoiceActivity(true);
                _pendingStreamingResult = payload;
                return Task.CompletedTask;
            }

            SetVoiceActivity(false);

            if (string.IsNullOrEmpty(_pendingStreamingResult))
            {
                return Task.CompletedTask;
            }

            if (_recognitionStrategy == null || !_recognitionStrategy.AllowsStreamingFinalCommit)
            {
                _pendingStreamingResult = string.Empty;
                return Task.CompletedTask;
            }

            if (!token.IsCancellationRequested)
            {
                AppendToRecognitionBuffer(_pendingStreamingResult);
            }

            _pendingStreamingResult = string.Empty;
            return Task.CompletedTask;
        }

        private Task HandleOfflineAsync(string content, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.CompletedTask;
            }

            SetVoiceActivity(false);

            if (!token.IsCancellationRequested)
            {
                AppendToRecognitionBuffer(content);
            }

            return Task.CompletedTask;
        }

        private void AppendToRecognitionBuffer(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (_recognitionLifetimeCts?.IsCancellationRequested == true)
            {
                return;
            }

            _recognitionBuffer.Commit(text);
        }

        #endregion

        #region Activity & Keyword Handlers

        private void HandleBufferAmended(string buffer)
        {
            OnTranscription(buffer, false);
        }

        private void HandleFinalizedTranscript(string text)
        {
            OnTranscription(text, true);
            OnASRTranscriptionSubmit?.Invoke(text);
        }

        private void HandleVoiceActivityChanged(bool isActive)
        {
            _silenceRecognizer?.OnVoiceActivityChanged(isActive);
            OnVoiceActivityChanged?.Invoke(isActive);
            OnSpeakingChanged?.Invoke(isActive);

            if (!isActive &&
                _recognitionStrategy != null &&
                _recognitionStrategy.SubmitWhenSpeakingEndsWithoutOffline &&
                !string.IsNullOrEmpty(_pendingStreamingResult))
            {
                AppendToRecognitionBuffer(_pendingStreamingResult);
                _pendingStreamingResult = string.Empty;
            }
        }

        private void HandleUtteranceEnded()
        {
            _keywordGate?.OnUtteranceEnded();
        }

        private void HandleKeywordActivityChanged(string keyword, bool isActive)
        {
            OnKeywordActivityChanged?.Invoke(keyword, isActive);
        }

        private void HandleKeywordActivatedInternal(string keyword)
        {
            OnKeywordActivated(keyword);
        }

        private void HandleKeywordDeactivatedInternal(string keyword)
        {
            OnKeywordDeactivated();
        }

        private void HandleKeywordDetected(string keyword)
        {
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                _keywordGate?.Activate(keyword);
            }
        }

        private void HandleVoiceActivityFromFilter(bool isVoiceActivity) => SetVoiceActivity(isVoiceActivity);

        private void SetVoiceActivity(bool isVoiceActivity) => _voiceActivity?.SetVoiceActivity(isVoiceActivity);

        #endregion

        #region Strategy & Feedback

        private TurnDetector CreateTurnDetector(TurnDetectionOptions settings)
        {
            return new AdaptiveTurnDetector(settings);
        }

        private IRecognitionStrategy SelectStrategy(RecognitionMode mode)
        {
            return mode switch
            {
                RecognitionMode.Streaming => StreamingStrategy,
                RecognitionMode.OfflineWithVad => OfflineStrategy,
                RecognitionMode.Hybrid => HybridStrategy,
                _ => StreamingStrategy
            };
        }

        private SherpaONNXFeedbackReporter EnsureFeedbackReporter()
        {
            return _feedbackReporter ??= new SherpaONNXFeedbackReporter(null, this);
        }

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
                Debug.LogWarning($"VoiceMicrophone: failed to dispose {typeof(T).Name} - {ex.Message}");
            }
            finally
            {
                disposable = null;
            }
        }

        private void FinalizeInitialization()
        {
            if (Initialized)
            {
                return;
            }

            Initialized = true;
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

        private void PublishProgress(string message)
        {
            float progress = _progressAggregator.CalculateProgress();
            OnLoadingProgressFeedback?.Invoke(message, progress);
        }

        private void PublishFailed(FailedFeedback feedback)
        {
            PublishProgress(feedback?.Message);
            OnServiceLoadingFailed(feedback);
            OnLoadingFailedFeedback?.Invoke(feedback);
        }

        private void PublishSuccess(SuccessFeedback feedback)
        {
            PublishProgress(feedback?.Message);
            OnServiceLoadingSucceeded(feedback);
            OnLoadingSucceededFeedback?.Invoke(feedback);
        }

        public void OnFeedback(PrepareFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            _progressAggregator.RegisterPrepare(feedback.Metadata, feedback.Message);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(DownloadFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            _progressAggregator.RegisterDownload(feedback.Metadata, feedback.Progress, feedback.Message);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(DecompressFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            _progressAggregator.RegisterDecompress(feedback.Metadata, feedback.Progress, feedback.Message);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(VerifyFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            _progressAggregator.RegisterVerify(feedback.Metadata, feedback.Progress, feedback.Message);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(LoadFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            _progressAggregator.RegisterLoad(feedback.Metadata, feedback.Message);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(CancelFeedback feedback)
        {
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(SuccessFeedback feedback)
        {
            if (feedback != null)
            {
                _progressAggregator.RegisterSuccess(feedback.Metadata, feedback.Message);
            }

            _completedModelLoads++;
            PublishSuccess(feedback);
            FinalizeInitializationIfReady();
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            _completedModelLoads++;
            Debug.LogError($"VoiceMicrophone: model load failed - {feedback?.Message ?? "unknown error"}");
            PublishFailed(feedback);
            FinalizeInitializationIfReady();
        }

        public void OnFeedback(CleanFeedback feedback)
        {
            PublishProgress(feedback?.Message);
        }

        #endregion
    }
}
#endif
