#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.SherpaOnnxUnity;
using Eitan.SherpaOnnxUnity;
using Eitan.SherpaOnnxUnity.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Coordinates Sherpa-ONNX services and exposes microphone-driven transcription events.
    /// </summary>
    [AddComponentMenu("Input/Voice Microphone")]
    public class VoiceMicrophone : EasyMicrophone, ISherpaFeedbackHandler
    {
        private const SampleRate DEFAULT_SAMPLERATE = SampleRate.Hz16000;
        private const float MIN_SILENCE_AFTER_SPEECH = 0.16f;
        private const float MAX_SILENCE_AFTER_SPEECH = 1.5f;
        private const float SILENCE_DURATION_SCALE = 0.3f;
        private const float MIN_SPEECH_SEGMENT_DURATION = 0.1f;
        private const float MIN_CONVERSATION_TIMEOUT_SECONDS = 0.5f;

        private static readonly StreamingRecognitionStrategy StreamingStrategy = new StreamingRecognitionStrategy();
        private static readonly OfflineWithVadRecognitionStrategy OfflineStrategy = new OfflineWithVadRecognitionStrategy();
        private static readonly HybridRecognitionStrategy HybridStrategy = new HybridRecognitionStrategy();

        [SerializeField]
        private AutomaticSpeechRecognitionConfiguration _asrConfig;

        private SpeechRecognition _streamingService;
        private SpeechRecognition _offlineService;
        private VoiceActivityDetection _vadService;
        private KeywordSpotting _keywordService;
        private Punctuation _punctService;

        private SherpaVoiceFilter _voiceFilter;
        private SherpaRealtimeSpeechRecognizer _realtimeRecognizer;
        private SherpaOfflineSpeechRecognizer _offlineRecognizer;
        private SherpaKeywordDetector _keywordDetector;

        private SherpaOnnxFeedbackReporter _feedbackReporter;
        private SherpaServiceFactory _serviceFactory;
        private SpeechStateMachine _speechStateMachine;
        private KeywordGate _keywordGate;
        private TextAccumulator _textAccumulator;
        private ModelProgressAggregator _progressAggregator;
        private readonly IPunctuationPolicy _punctuationPolicy = new OfflineOnlyPunctuationPolicy();
        private IRecognitionStrategy _recognitionStrategy;
        private CancellationTokenSource _recognitionLifetimeCts;
        private AutomaticSpeechRecognitionConfiguration.ASRPreset _initializingPreset;

        private string _pendingStreamingCommit = string.Empty;
        private string _pendingStreamingFinal = string.Empty;
        private string _pendingOfflineFinal = string.Empty;
        private int _expectedModelLoads;
        private int _completedModelLoads;

        private Action<SuccessFeedback> _legacySuccessHandlers;

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
        public bool IsSpeaking => _speechStateMachine?.IsSpeaking ?? false;

        /// <summary>
        /// Gets whether voice activity is currently detected.
        /// </summary>
        public bool IsVoiceActivity => _speechStateMachine?.IsVoiceActive ?? false;

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
        public bool RequiresKeywordSpotting => AsrConfig.ActiveKeywordSettings.IsEnabled;

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

        public event Action<string> OnASRTranscriptionStreaming;
        public event Action<string> OnASRTranscriptionSubmit;
        public event Action<bool> OnVoiceActivityChanged;
        public event Action<bool> OnSpeakingChanged;
        public event Action<string, bool> OnKeywordActivityChanged;
        public event Action<string, float> OnLoadingProgressFeedback;
        public event Action<FailedFeedback> OnLoadingFailedFeedback;
        public event Action<SuccessFeedback> OnLoadingSucceededFeedback;

        [Obsolete("Use OnLoadingSucceededFeedback instead.")]
        public event Action<SuccessFeedback> OnLoadingSuccessedFeedback
        {
            add => _legacySuccessHandlers += value;
            remove => _legacySuccessHandlers -= value;
        }

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
            _speechStateMachine?.Update(deltaTime);
            _keywordGate?.Update(deltaTime, IsSpeaking, IsVoiceActivity);
        }

        protected override void OnMicrophoneDispose()
        {
            DisposeServices();
        }

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

        public void SetGithubProxy(string url)
        {
            SherpaOnnxUnityAPI.SetGithubProxy(url);
        }

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

        private void EnsureInfrastructure()
        {
            _progressAggregator ??= new ModelProgressAggregator();

            if (_speechStateMachine == null)
            {
                _speechStateMachine = new SpeechStateMachine(
                    MIN_SILENCE_AFTER_SPEECH,
                    MAX_SILENCE_AFTER_SPEECH,
                    SILENCE_DURATION_SCALE,
                    MIN_SPEECH_SEGMENT_DURATION);

                _speechStateMachine.VoiceActivityChanged += HandleVoiceActivityChanged;
                _speechStateMachine.SpeakingChanged += HandleSpeakingChanged;
                _speechStateMachine.UtteranceEnded += HandleUtteranceEnded;
            }

            if (_keywordGate == null)
            {
                _keywordGate = new KeywordGate(
                    AsrConfig.ActiveKeywordSettings,
                    MIN_CONVERSATION_TIMEOUT_SECONDS,
                    MAX_SILENCE_AFTER_SPEECH * 2f,
                    seconds => _speechStateMachine.ExtendSilenceHold(seconds));

                _keywordGate.ActivityChanged += HandleKeywordActivityChanged;
                _keywordGate.Activated += HandleKeywordActivatedInternal;
                _keywordGate.Deactivated += HandleKeywordDeactivatedInternal;
            }
            else
            {
                _keywordGate.ApplySettings(AsrConfig.ActiveKeywordSettings);
            }

            if (_textAccumulator == null)
            {
                _textAccumulator = new TextAccumulator(
                    _keywordGate,
                    streaming => OnASRTranscriptionStreaming?.Invoke(streaming));

                _textAccumulator.BufferAmended += HandleBufferAmended;
                _textAccumulator.Finalized += HandleFinalizedTranscript;
            }
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

            _speechStateMachine.Reset();
            _keywordGate.Reset(clearStreamingHistory: true);
            _textAccumulator.Reset();

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
            _pendingStreamingCommit = string.Empty;
            _pendingStreamingFinal = string.Empty;
            _pendingOfflineFinal = string.Empty;

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
                ? _serviceFactory.CreateKeywordSpotting(config.ActiveKeywordSettings, KeywordSettings.Default.ModelId)
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
            _pendingStreamingCommit = string.Empty;
            _pendingStreamingFinal = string.Empty;
            _pendingOfflineFinal = string.Empty;

            _speechStateMachine?.Reset();
            _keywordGate?.Reset(clearStreamingHistory: true);
            _textAccumulator?.Reset();
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

        private void HandleStreamingRecognition(string content) => _ = ProcessRecognitionAsync(content, isStreaming: true);

        private void HandleOfflineRecognition(string content) => _ = ProcessRecognitionAsync(content, isStreaming: false);

        private async Task ProcessRecognitionAsync(string content, bool isStreaming)
        {
            var token = _recognitionLifetimeCts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (isStreaming)
                {
                    await HandleStreamingAsync(content ?? string.Empty);
                }
                else
                {
                    HandleOffline(content ?? string.Empty);
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

        private Task HandleStreamingAsync(string content)
        {
            bool hasContent = !string.IsNullOrEmpty(content);

            if (hasContent)
            {
                if (_recognitionStrategy.AppliesStreamingVoiceActivity)
                {
                    _speechStateMachine.SetVoiceActivity(true);
                }

                _textAccumulator.EmitPartial(content);

                if (AllowsRecognitionOutput())
                {
                    _pendingStreamingCommit = content;
                }
            }
            else
            {
                _textAccumulator.EmitPartial(string.Empty);

                if (_recognitionStrategy.AppliesStreamingVoiceActivity)
                {
                    _speechStateMachine.SetVoiceActivity(false);
                }

                if (_recognitionStrategy.AllowsStreamingFinalCommit && AllowsRecognitionOutput())
                {
                    _pendingStreamingFinal = _pendingStreamingCommit;
                }

                _pendingStreamingCommit = string.Empty;
            }

            return Task.CompletedTask;
        }

        private void HandleOffline(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            if (!AllowsRecognitionOutput())
            {
                _pendingOfflineFinal = string.Empty;
                return;
            }

            _pendingOfflineFinal = content;

            if (!_speechStateMachine.IsSpeaking)
            {
                QueueOfflineFinalSubmission();
            }
        }

        private async Task CommitFinalAsync(string text, bool isStreaming)
        {
            if (string.IsNullOrEmpty(text) || !AllowsRecognitionOutput())
            {
                return;
            }

            var token = _recognitionLifetimeCts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
            {
                return;
            }

            string result = text;
            if (ShouldApplyPunctuation(new PunctuationRequestContext(isFinal: true, isStreaming: isStreaming)) && _punctService != null)
            {
                try
                {
                    result = await _punctService.AddPunctuationAsync(result);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"VoiceMicrophone: punctuation failed - {ex.Message}");
                }
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            _textAccumulator.CommitFinal(result);
        }

        private bool ShouldApplyPunctuation(PunctuationRequestContext context)
        {
            return _punctService != null && _punctuationPolicy.ShouldApplyPunctuation(context);
        }

        private void QueueOfflineFinalSubmission()
        {
            if (string.IsNullOrEmpty(_pendingOfflineFinal))
            {
                return;
            }

            var text = _pendingOfflineFinal;
            _pendingOfflineFinal = string.Empty;
            RunCommitAsync(text, isStreaming: false);
        }

        private void QueueStreamingFinalSubmission()
        {
            if (string.IsNullOrEmpty(_pendingStreamingFinal))
            {
                return;
            }

            var text = _pendingStreamingFinal;
            _pendingStreamingFinal = string.Empty;
            RunCommitAsync(text, isStreaming: true);
        }

        private void RunCommitAsync(string text, bool isStreaming)
        {
            var task = CommitFinalAsync(text, isStreaming);
            if (task.IsCompleted)
            {
                return;
            }

            task.ContinueWith(
                t =>
                {
                    if (t.Exception != null)
                    {
                        Debug.LogException(t.Exception.GetBaseException());
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void HandleBufferAmended(string buffer)
        {
            OnTranscription(buffer, false);
        }

        private void HandleFinalizedTranscript(string text)
        {
            OnTranscription(text, true);
            OnASRTranscriptionSubmit?.Invoke(text);
            _textAccumulator?.CompleteConversation();
        }

        private void HandleVoiceActivityChanged(bool isActive)
        {
            OnVoiceActivityChanged?.Invoke(isActive);
        }

        private void HandleSpeakingChanged(bool isSpeaking)
        {
            OnSpeakingChanged?.Invoke(isSpeaking);
        }

        private void HandleUtteranceEnded()
        {
            QueueOfflineFinalSubmission();

            if (_recognitionStrategy.SubmitWhenSpeakingEndsWithoutOffline)
            {
                QueueStreamingFinalSubmission();
            }

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

        private void HandleVoiceActivityFromFilter(bool isVoiceActivity)
        {
            _speechStateMachine.SetVoiceActivity(isVoiceActivity);
        }

        private bool AllowsRecognitionOutput()
        {
            return _keywordGate?.AllowsRecognition ?? true;
        }

        private void SetVoiceActivity(bool isVoiceActivity)
        {
            _speechStateMachine?.SetVoiceActivity(isVoiceActivity);
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

        private SherpaOnnxFeedbackReporter EnsureFeedbackReporter()
        {
            return _feedbackReporter ??= new SherpaOnnxFeedbackReporter(null, this);
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
            _legacySuccessHandlers?.Invoke(feedback);
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
    }
}
#endif
