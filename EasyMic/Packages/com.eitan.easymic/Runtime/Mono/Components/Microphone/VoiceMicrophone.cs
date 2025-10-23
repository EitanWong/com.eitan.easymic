#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.SherpaOnnxUnity;
using Eitan.SherpaOnnxUnity;
using Eitan.SherpaOnnxUnity.Runtime;
using UnityEngine;
using UnityEngine.Serialization;

namespace Eitan.EasyMic.Runtime.Mono
{
    [AddComponentMenu("Input/Voice Microphone")]
    public class VoiceMicrophone : EasyMicrophone, ISherpaFeedbackHandler
    {

        #region Configuration
        [SerializeField] private AutomaticSpeechRecognitionConfiguration _asrConfig;

        public AutomaticSpeechRecognitionConfiguration AsrConfig
        {
            get => _asrConfig ??= AutomaticSpeechRecognitionConfiguration.CreateDefault();
            private set => _asrConfig = value;
        }
        #endregion

        #region Structure
        public enum RecognitionMode
        {
            Streaming,
            OfflineWithVad,
            Hybrid
        }

        [Serializable]
        public struct KeywordSettings
        {
            public bool Enabled;
            public string ModelId;
            public KeywordSpotting.KeywordRegistration[] CustomKeywords;
            public float KeywordsScore;
            public float KeywordsThreshold;
            public bool ContinuousConversation;
            public float ContinuousConversationTimeoutSeconds;

            public bool UseTriggerSound;
            public AudioClip TriggerSoundClip;


            public bool IsEnabled => Enabled && !string.IsNullOrWhiteSpace(ModelId);

            public KeywordSettings Clone()
            {
                return new KeywordSettings
                {
                    Enabled = Enabled,
                    ModelId = ModelId,
                    CustomKeywords = CustomKeywords != null ? (KeywordSpotting.KeywordRegistration[])CustomKeywords.Clone() : null,
                    KeywordsScore = KeywordsScore,
                    KeywordsThreshold = KeywordsThreshold,
                    ContinuousConversation = ContinuousConversation,
                    ContinuousConversationTimeoutSeconds = ContinuousConversationTimeoutSeconds,
                    UseTriggerSound = UseTriggerSound,
                    TriggerSoundClip = TriggerSoundClip
                };
            }

            public static KeywordSettings Default => new KeywordSettings
            {
                Enabled = false,
                ModelId = "sherpa-onnx-kws-zipformer-wenetspeech-3.3M-2024-01-01",
                KeywordsScore = 2.0f,
                KeywordsThreshold = 0.25f,
                ContinuousConversation = false,
                ContinuousConversationTimeoutSeconds = 8f,
            };
        }

        [Serializable]
        public sealed class AutomaticSpeechRecognitionConfiguration
        {
            [Serializable]
            public struct ASRPreset
            {
                public const string DefaultPresetId = "default";

                public string Id;
                public string DisplayName;
                public RecognitionMode RecognitionMode;
                public KeywordSettings KeywordSettings;
                public string StreamingModelId;
                public string OfflineModelId;
                public string VadModelId;
                public bool EnablePunctuation;
                public string PunctuationModelId;

                public ASRPreset Clone()
                {
                    return new ASRPreset
                    {
                        Id = Id,
                        DisplayName = DisplayName,
                        RecognitionMode = RecognitionMode,
                        KeywordSettings = KeywordSettings.Clone(),
                        StreamingModelId = StreamingModelId,
                        OfflineModelId = OfflineModelId,
                        VadModelId = VadModelId,
                        EnablePunctuation = EnablePunctuation,
                        PunctuationModelId = PunctuationModelId
                    };
                }

                public static ASRPreset Create(
                    RecognitionMode recognitionMode,
                    KeywordSettings keywordSettings,
                    string streamingModelId,
                    string offlineModelId,
                    string vadModelId,
                    bool enablePunctuation,
                    string punctuationModelId)
                {
                    return new ASRPreset
                    {
                        Id = DefaultPresetId,
                        DisplayName = "Default",
                        RecognitionMode = recognitionMode,
                        KeywordSettings = keywordSettings,
                        StreamingModelId = streamingModelId,
                        OfflineModelId = offlineModelId,
                        VadModelId = vadModelId,
                        EnablePunctuation = enablePunctuation,
                        PunctuationModelId = punctuationModelId
                    };
                }

                public static ASRPreset Default
                {
                    get
                    {
                        return Create(
                         RecognitionMode.Streaming,
                         KeywordSettings.Default,
                         "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
                         "sherpa-onnx-zipformer-zh-en-2023-11-22",
                         "silero-vad-v5",
                         true,
                         "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8");
                    }
                }
            }

            [SerializeField, FormerlySerializedAs("Presets")] private ASRPreset[] _presets = { ASRPreset.Default };
            [SerializeField, FormerlySerializedAs("ActivePresetId")] private string _activePresetId = ASRPreset.DefaultPresetId;

            public IReadOnlyList<ASRPreset> PresetConfigurations => _presets ?? Array.Empty<ASRPreset>();

            public string ActivePresetId
            {
                get => string.IsNullOrWhiteSpace(_activePresetId) ? ASRPreset.DefaultPresetId : _activePresetId;
                private set => _activePresetId = value;
            }

            public RecognitionMode RecognitionMode => GetActivePresetRaw().RecognitionMode;

            public KeywordSettings KeywordSettings => GetActivePresetRaw().KeywordSettings;

            public string StreamingModelId
            {
                get
                {
                    var preset = GetActivePresetRaw();
                    return preset.StreamingModelId;
                }
            }

            public string OfflineModelId
            {
                get
                {
                    var preset = GetActivePresetRaw();
                    return preset.OfflineModelId;
                }
            }

            public string VadModelId
            {
                get
                {
                    var preset = GetActivePresetRaw();
                    return preset.VadModelId;
                }
            }

            public bool EnablePunctuation => GetActivePresetRaw().EnablePunctuation;

            public string PunctuationModelId
            {
                get
                {
                    var preset = GetActivePresetRaw();
                    return preset.PunctuationModelId;
                }
            }

            public IReadOnlyList<ASRPreset> Presets => PresetConfigurations;

            public AutomaticSpeechRecognitionConfiguration Clone()
            {
                return new AutomaticSpeechRecognitionConfiguration
                {
                    _activePresetId = ActivePresetId,
                    _presets = _presets != null && _presets.Length > 0
                        ? ClonePresetsArray(_presets)
                        : Array.Empty<ASRPreset>()
                };
            }

            public ASRPreset GetActivePreset() => GetActivePresetInternal(true);

            public bool TrySelectPreset(string presetId)
            {
                var configurations = PresetConfigurations;
                if (configurations.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(presetId) || string.Equals(presetId, ASRPreset.DefaultPresetId, StringComparison.OrdinalIgnoreCase))
                    {
                        ActivePresetId = ASRPreset.DefaultPresetId;
                        return true;
                    }

                    return false;
                }

                if (string.IsNullOrWhiteSpace(presetId))
                {
                    var first = configurations[0];
                    ActivePresetId = !string.IsNullOrWhiteSpace(first.Id)
                        ? first.Id
                        : ASRPreset.DefaultPresetId;
                    return true;
                }

                for (int i = 0; i < configurations.Count; i++)
                {
                    var preset = configurations[i];
                    if (!string.IsNullOrWhiteSpace(preset.Id) && string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase))
                    {
                        ActivePresetId = preset.Id;
                        return true;
                    }
                }

                return false;
            }

            public bool TryGetPreset(string presetId, out ASRPreset preset)
            {
                var configurations = PresetConfigurations;
                if (!string.IsNullOrWhiteSpace(presetId))
                {
                    for (int i = 0; i < configurations.Count; i++)
                    {
                        var candidate = configurations[i];
                        if (!string.IsNullOrWhiteSpace(candidate.Id) && string.Equals(candidate.Id, presetId, StringComparison.OrdinalIgnoreCase))
                        {
                            preset = candidate.Clone();
                            return true;
                        }
                    }
                }

                preset = default;
                return false;
            }

            public bool AddPreset(ASRPreset preset, bool overwrite = false)
            {
                if (_presets == null)
                {
                    _presets = Array.Empty<ASRPreset>();
                }

                if (!overwrite && TryGetPreset(preset.Id, out _))
                {
                    return false;
                }

                var presets = new List<ASRPreset>(_presets.Length + 1);
                var replaced = false;

                for (int i = 0; i < _presets.Length; i++)
                {
                    var existing = _presets[i];
                    if (!string.IsNullOrWhiteSpace(existing.Id) && string.Equals(existing.Id, preset.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!overwrite)
                        {
                            presets.Add(existing);
                            continue;
                        }

                        presets.Add(preset);
                        replaced = true;
                    }
                    else
                    {
                        presets.Add(existing);
                    }
                }

                if (!replaced)
                {
                    presets.Add(preset);
                }

                _presets = ClonePresetsArray(presets.ToArray());
                return true;
            }

            public bool UpdatePreset(ASRPreset preset)
            {
                if (_presets == null || _presets.Length == 0 || string.IsNullOrWhiteSpace(preset.Id))
                {
                    return false;
                }

                var updated = false;
                var presets = new ASRPreset[_presets.Length];

                for (int i = 0; i < _presets.Length; i++)
                {
                    var existing = _presets[i];
                    if (!string.IsNullOrWhiteSpace(existing.Id) && string.Equals(existing.Id, preset.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        presets[i] = preset;
                        updated = true;
                    }
                    else
                    {
                        presets[i] = existing;
                    }
                }

                if (updated)
                {
                    _presets = ClonePresetsArray(presets);
                }

                return updated;
            }

            public bool RemovePreset(string presetId)
            {
                if (_presets == null || _presets.Length == 0 || string.IsNullOrWhiteSpace(presetId))
                {
                    return false;
                }

                var presets = new List<ASRPreset>(_presets.Length);
                var removed = false;

                for (int i = 0; i < _presets.Length; i++)
                {
                    var existing = _presets[i];
                    if (!string.IsNullOrWhiteSpace(existing.Id) && string.Equals(existing.Id, presetId, StringComparison.OrdinalIgnoreCase))
                    {
                        removed = true;
                        continue;
                    }

                    presets.Add(existing);
                }

                if (!removed)
                {
                    return false;
                }

                _presets = ClonePresetsArray(presets.ToArray());

                if (string.Equals(ActivePresetId, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    TrySelectPreset(null);
                }

                return true;
            }

            public bool SetActivePreset(string presetId)
            {
                return TrySelectPreset(presetId);
            }

            public static AutomaticSpeechRecognitionConfiguration CreateDefault()
            {
                return new AutomaticSpeechRecognitionConfiguration();
            }

            private ASRPreset GetActivePresetRaw() => GetActivePresetInternal(false);

            private ASRPreset GetActivePresetInternal(bool clone)
            {
                var configurations = PresetConfigurations;
                ASRPreset preset;

                if (configurations.Count == 0)
                {
                    preset = ASRPreset.Default;
                }
                else
                {
                    preset = configurations[0];
                    var presetId = ActivePresetId;
                    if (!string.IsNullOrWhiteSpace(presetId))
                    {
                        for (int i = 0; i < configurations.Count; i++)
                        {
                            var cfg = configurations[i];
                            if (!string.IsNullOrWhiteSpace(cfg.Id) && string.Equals(cfg.Id, presetId, StringComparison.OrdinalIgnoreCase))
                            {
                                preset = cfg;
                                break;
                            }
                        }
                    }
                }

                return clone ? preset.Clone() : preset;
            }

            private static ASRPreset[] ClonePresetsArray(ASRPreset[] source)
            {
                if (source == null || source.Length == 0)
                {
                    return Array.Empty<ASRPreset>();
                }

                var clone = new ASRPreset[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    clone[i] = source[i].Clone();
                }

                return clone;
            }
        }

        #endregion

        #region  Internal Fields
        #region  Constant

        private const SampleRate DEFAULT_SAMPLERATE = SampleRate.Hz16000;
        private const float MIN_SILENCE_AFTER_SPEECH = 0.16f;
        private const float MAX_SILENCE_AFTER_SPEECH = 1.5f;
        private const float SILENCE_DURATION_SCALE = 0.3f;
        private const float MIN_SPEECH_SEGMENT_DURATION = 0.1f;
        private const float MIN_CONVERSATION_TIMEOUT_SECONDS = 0.5f;
        #endregion

        private struct SilenceTracker
        {
            public float Elapsed;
            public float Hold;

            public void ResetElapsed()
            {
                Elapsed = 0f;
            }

            public void OnSilence(float deltaTime)
            {
                Elapsed += deltaTime;
                if (Hold > 0f)
                {
                    Hold = Mathf.Max(0f, Hold - deltaTime);
                }
            }

            public void Extend(float seconds)
            {
                Hold = Mathf.Max(Hold, seconds);
            }

            public void ClearHold()
            {
                Hold = 0f;
            }

            public void Reset()
            {
                ResetElapsed();
                Hold = 0f;
            }

            public float Required(float baseRequirement)
            {
                return Mathf.Max(baseRequirement, Hold);
            }
        }

        #region  Private Fields
        #region SherpaService
        private SpeechRecognition _streamingService;
        private SpeechRecognition _offlineService;
        private VoiceActivityDetection _vadService;
        private KeywordSpotting _keywordService;
        private Punctuation _punctService;
        #endregion

        #region EasyMicWorker
        private SherpaVoiceFilter _activeVoiceFilter;
        private SherpaRealtimeSpeechRecognizer _activeRealtimeRecognizer;
        private SherpaOfflineSpeechRecognizer _activeOfflineRecognizer;
        private SherpaKeywordDetector _keywordDetector;
        #endregion

        private SherpaOnnxFeedbackReporter _feedbackReporter;

        private readonly StringBuilder _asrBuffer = new StringBuilder();
        private string _lastStreamingPartial;
        private string _lastStreamingCommittedResult = string.Empty;
        private string _lastSubmittedTranscription = string.Empty;
        private CancellationTokenSource _recognitionLifetimeCts;

        private int _pendingModelLoads;
        private int _counterModelLoads;
        private string _lastKeyword;
        private bool _keywordActive;
        private bool _hasStreamingEmission;
        private SilenceTracker _silence;
        private float _currentSpeechSegmentDuration;
        private float _lastSpeechSegmentDuration = MIN_SPEECH_SEGMENT_DURATION;
        private bool _lastVoiceActivityState;


        #endregion

        #endregion

        #region  Properties

        public bool IsSpeaking { get; private set; }
        public bool IsVoiceActivity { get; private set; }


        public bool RequiresStreaming => AsrConfig.RecognitionMode == RecognitionMode.Streaming || AsrConfig.RecognitionMode == RecognitionMode.Hybrid;

        public bool RequiresOffline => AsrConfig.RecognitionMode == RecognitionMode.OfflineWithVad || AsrConfig.RecognitionMode == RecognitionMode.Hybrid;
        public bool RequiresVad => AsrConfig.RecognitionMode == RecognitionMode.OfflineWithVad || AsrConfig.RecognitionMode == RecognitionMode.Hybrid;

        public bool RequiresPunctuation => AsrConfig.EnablePunctuation && !string.IsNullOrEmpty(AsrConfig.PunctuationModelId) && !string.IsNullOrWhiteSpace(AsrConfig.PunctuationModelId);

        public bool RequiresKeywordSpotting => AsrConfig.KeywordSettings.IsEnabled && !string.IsNullOrEmpty(AsrConfig.KeywordSettings.ModelId) && !string.IsNullOrWhiteSpace(AsrConfig.KeywordSettings.ModelId);

        public IReadOnlyList<AutomaticSpeechRecognitionConfiguration.ASRPreset> Supportedpresets =>
            AsrConfig.PresetConfigurations;

        public string ActivepresetId => AsrConfig.ActivePresetId ?? AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId;

        public AutomaticSpeechRecognitionConfiguration.ASRPreset ActivepresetConfiguration => AsrConfig.GetActivePreset();

        #endregion


        #region Event

        public event Action<string> OnASRTranscriptionStreaming;
        public event Action<string> OnASRTranscriptionSubmit;
        public event Action<bool> OnVoiceActivityChanged;
        public event Action<bool> OnSpeakingChanged;
        public event Action<string, bool> OnKeywordActivityChanged;

        public event Action<string, float> OnLoadingProgressFeedback;
        public event Action<FailedFeedback> OnLoadingFailedFeedback;
        public event Action<SuccessFeedback> OnLoadingSuccessedFeedback;

        #endregion
        #region Voice Microphone Behaviour

        #region Overrides

        protected override void OnInitialization()
        {
            _ = AsrConfig;
            InitializeServices(preserveRecordingState: false);
        }

        protected override void OnAudioPiplineBuild(AudioPipeline pipeline)
        {
            if (pipeline == null)
            {
                return;
            }

            ConfigureKeywordSpotting(pipeline);
            ConfigureStreamingRecognition(pipeline);
            ConfigureVoiceActivityDetection(pipeline);
            ConfigureOfflineRecognition(pipeline);
            OnAudioPipelineReady(pipeline);
        }

        protected override void OnMicrophoneUpdate()
        {
            UpdateKeywordTimers();
            UpdateSpeakingState();


            DebugSimulate();
        }

        protected override void OnMicrophoneDispose()
        {
            DisposeServices();
        }

        #endregion

        #region Pipeline Configuration

        protected virtual void ConfigureKeywordSpotting(AudioPipeline pipeline)
        {
            if (!RequiresKeywordSpotting || pipeline == null)
            {
                return;
            }

            if (_keywordDetector == null && _keywordService != null)
            {
                _keywordDetector = new SherpaKeywordDetector(_keywordService);
            }

            if (_keywordDetector == null)
            {
                return;
            }

            _keywordDetector.OnKeywordDetected -= HandleKeywordDetected;
            _keywordDetector.OnKeywordDetected += HandleKeywordDetected;
            pipeline.AddWorker(_keywordDetector);
        }

        protected virtual void ConfigureStreamingRecognition(AudioPipeline pipeline)
        {
            if (!RequiresStreaming || _streamingService == null || pipeline == null)
            {
                return;
            }

            if (_activeRealtimeRecognizer == null)
            {
                _activeRealtimeRecognizer = new SherpaRealtimeSpeechRecognizer(_streamingService);
            }

            _activeRealtimeRecognizer.OnRecognitionResult -= HandleStreamingRecognition;
            _activeRealtimeRecognizer.OnRecognitionResult += HandleStreamingRecognition;
            pipeline.AddWorker(_activeRealtimeRecognizer);
        }

        protected virtual void ConfigureVoiceActivityDetection(AudioPipeline pipeline)
        {
            if (!RequiresVad || _vadService == null || pipeline == null)
            {
                return;
            }

            if (_activeVoiceFilter == null)
            {
                _activeVoiceFilter = new SherpaVoiceFilter(_vadService);
            }

            _activeVoiceFilter.OnVoiceActivityChanged -= OnInternalVoiceActivityChangeHandler;
            _activeVoiceFilter.OnVoiceActivityChanged += OnInternalVoiceActivityChangeHandler;

            pipeline.AddWorker(_activeVoiceFilter);
        }

        protected virtual void ConfigureOfflineRecognition(AudioPipeline pipeline)
        {
            if (!RequiresOffline || _offlineService == null || pipeline == null)
            {
                return;
            }

            if (_activeOfflineRecognizer == null)
            {
                _activeOfflineRecognizer = new SherpaOfflineSpeechRecognizer(_offlineService);
            }

            _activeOfflineRecognizer.OnRecognitionResult -= HandleOfflineRecognition;
            _activeOfflineRecognizer.OnRecognitionResult += HandleOfflineRecognition;
            pipeline.AddWorker(_activeOfflineRecognizer);
        }

        #endregion

        #endregion

        #region Public Methods

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

        #endregion


        #region Protected Hooks

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
        protected virtual void OnServiceLoadingSuccessed(SuccessFeedback feedback)
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

        #endregion


        #region Private Methods

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

        private void DisposeServices()
        {
            OnBeforeServicesDisposed();

            _recognitionLifetimeCts?.Cancel();
            _recognitionLifetimeCts?.Dispose();
            _recognitionLifetimeCts = null;

            if (_activeVoiceFilter != null)
            {
                SafeDispose(ref _activeVoiceFilter);
            }

            if (_activeRealtimeRecognizer != null)
            {
                _activeRealtimeRecognizer.OnRecognitionResult -= HandleStreamingRecognition;
                SafeDispose(ref _activeRealtimeRecognizer);
            }

            if (_activeOfflineRecognizer != null)
            {
                _activeOfflineRecognizer.OnRecognitionResult -= HandleOfflineRecognition;
                SafeDispose(ref _activeOfflineRecognizer);
            }

            if (_vadService != null)
            {
                _vadService.OnSpeakingStateChanged -= OnInternalVoiceActivityChangeHandler;
                SafeDispose(ref _vadService);
            }

            if (_streamingService != null)
            {
                SafeDispose(ref _streamingService);
            }

            if (_offlineService != null)
            {
                SafeDispose(ref _offlineService);
            }

            if (_keywordDetector != null)
            {
                _keywordDetector.OnKeywordDetected -= HandleKeywordDetected;
                SafeDispose(ref _keywordDetector);
            }

            if (_keywordService != null)
            {
                SafeDispose(ref _keywordService);
            }

            if (_punctService != null)
            {
                SafeDispose(ref _punctService);
            }

            _feedbackReporter = null;
            _pendingModelLoads = 0;
            _lastStreamingCommittedResult = string.Empty;
            _counterModelLoads = 0;
            _modelProgressStates.Clear();
            ResetKeywordGate(true);
            _lastSubmittedTranscription = string.Empty;
            _asrBuffer.Clear();
            if (IsSpeaking)
            {
                IsSpeaking = false;
                OnSpeakingChanged?.Invoke(false);
            }
            SetVoiceActivity(false);
            Initialized = false;

            OnAfterServicesDisposed();
        }


        private void InitializeServices(bool preserveRecordingState = true)
        {
            bool wasRecording = preserveRecordingState && base.IsRecording;
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

            _lastSubmittedTranscription = string.Empty;
            _lastStreamingPartial = string.Empty;
            _lastStreamingCommittedResult = string.Empty;
            _recognitionLifetimeCts = new CancellationTokenSource();
            Volatile.Write(ref _pendingModelLoads, 0);
            Volatile.Write(ref _counterModelLoads, 0);
            _modelProgressStates.Clear();

            var config = AsrConfig ?? AutomaticSpeechRecognitionConfiguration.CreateDefault();
            var preset = config.GetActivePreset();

            OnBeforeServicesInitializationRequested(config, preset);
            string streamingModelId = preset.StreamingModelId;
            string offlineModelId = preset.OfflineModelId;
            string vadModelId = preset.VadModelId;
            var punctuationModelId = preset.PunctuationModelId;
            var defaultPreset = AutomaticSpeechRecognitionConfiguration.ASRPreset.Default;

            if (RequiresStreaming)
            {
                if (!TryCreateSpeechRecognitionService(streamingModelId, defaultPreset.StreamingModelId, "streaming", out _streamingService))
                {
                    _streamingService = null;
                }
            }

            if (RequiresOffline)
            {
                if (!TryCreateSpeechRecognitionService(offlineModelId, defaultPreset.OfflineModelId, "offline", out _offlineService))
                {
                    _offlineService = null;
                }
            }

            if (RequiresVad)
            {
                if (!TryCreateVoiceActivityService(vadModelId, defaultPreset.VadModelId, out _vadService))
                {
                    _vadService = null;
                }
            }

            if (RequiresPunctuation)
            {
                if (!TryCreatePunctuationService(punctuationModelId, defaultPreset.PunctuationModelId, out _punctService))
                {
                    _punctService = null;
                }
            }

            if (RequiresKeywordSpotting)
            {
                if (!TryCreateKeywordSpottingService(config.KeywordSettings, out _keywordService))
                {
                    _keywordService = null;
                    ResetKeywordGate(clearStreaming: true);
                }
            }

            OnServicesInitializationRequested(config, preset);

            if (_pendingModelLoads == 0)
            {
                Initialized = true;
                OnServicesInitialized(config, preset);
            }

            if (wasRecording && !base.IsRecording)
            {
                TryStartRecording();
            }
        }

        private static string NormalizeModelId(string modelId)
        {
            return string.IsNullOrWhiteSpace(modelId) ? string.Empty : modelId.Trim();
        }

        private SherpaOnnxFeedbackReporter EnsureFeedbackReporter()
        {
            return _feedbackReporter ??= new SherpaOnnxFeedbackReporter(null, this);
        }

        private bool TryCreateSpeechRecognitionService(string requestedModelId, string fallbackModelId, string descriptor, out SpeechRecognition service)
        {
            service = null;

            string primary = NormalizeModelId(requestedModelId);
            string fallback = NormalizeModelId(fallbackModelId);
            var reporter = EnsureFeedbackReporter();

            string[] candidates = new string[2];
            int candidateCount = 0;

            if (primary.Length > 0)
            {
                candidates[candidateCount++] = primary;
            }

            if (fallback.Length > 0 && !string.Equals(fallback, primary, StringComparison.Ordinal))
            {
                candidates[candidateCount++] = fallback;
            }

            if (candidateCount == 0)
            {
                Debug.LogWarning($"VoiceMicrophone: missing {descriptor} speech recognition model identifier; {descriptor} features will be disabled.");
                return false;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                string candidate = candidates[i];
                try
                {
                    service = new SpeechRecognition(candidate, (int)DEFAULT_SAMPLERATE, reporter);
                    Interlocked.Increment(ref _pendingModelLoads);
                    if (!string.Equals(candidate, primary, StringComparison.Ordinal) && primary.Length > 0)
                    {
                        Debug.LogWarning($"VoiceMicrophone: {descriptor} speech recognition fell back to model '{candidate}'.");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"VoiceMicrophone: failed to initialize {descriptor} speech recognition model '{candidate}': {ex.Message}");
                }
            }

            Debug.LogWarning($"VoiceMicrophone: {descriptor} speech recognition unavailable; feature disabled.");
            return false;
        }

        private bool TryCreateVoiceActivityService(string requestedModelId, string fallbackModelId, out VoiceActivityDetection service)
        {
            service = null;
            string primary = NormalizeModelId(requestedModelId);
            string fallback = NormalizeModelId(fallbackModelId);
            var reporter = EnsureFeedbackReporter();

            string[] candidates = new string[2];
            int candidateCount = 0;

            if (primary.Length > 0)
            {
                candidates[candidateCount++] = primary;
            }

            if (fallback.Length > 0 && !string.Equals(fallback, primary, StringComparison.Ordinal))
            {
                candidates[candidateCount++] = fallback;
            }

            if (candidateCount == 0)
            {
                Debug.LogWarning("VoiceMicrophone: missing voice activity detection model identifier; VAD features will be disabled.");
                return false;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                string candidate = candidates[i];
                try
                {
                    service = new VoiceActivityDetection(candidate, (int)DEFAULT_SAMPLERATE, reporter);
                    Interlocked.Increment(ref _pendingModelLoads);
                    if (!string.Equals(candidate, primary, StringComparison.Ordinal) && primary.Length > 0)
                    {
                        Debug.LogWarning($"VoiceMicrophone: voice activity detection fell back to model '{candidate}'.");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"VoiceMicrophone: failed to initialize voice activity detector '{candidate}': {ex.Message}");
                }
            }

            Debug.LogWarning("VoiceMicrophone: voice activity detection unavailable; feature disabled.");
            return false;
        }

        private bool TryCreatePunctuationService(string requestedModelId, string fallbackModelId, out Punctuation service)
        {
            service = null;
            string primary = NormalizeModelId(requestedModelId);
            string fallback = NormalizeModelId(fallbackModelId);
            var reporter = EnsureFeedbackReporter();

            string[] candidates = new string[2];
            int candidateCount = 0;

            if (primary.Length > 0)
            {
                candidates[candidateCount++] = primary;
            }

            if (fallback.Length > 0 && !string.Equals(fallback, primary, StringComparison.Ordinal))
            {
                candidates[candidateCount++] = fallback;
            }

            if (candidateCount == 0)
            {
                Debug.LogWarning("VoiceMicrophone: missing punctuation model identifier; punctuation will be skipped.");
                return false;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                string candidate = candidates[i];
                try
                {
                    service = new Punctuation(candidate, (int)DEFAULT_SAMPLERATE, reporter);
                    Interlocked.Increment(ref _pendingModelLoads);
                    if (!string.Equals(candidate, primary, StringComparison.Ordinal) && primary.Length > 0)
                    {
                        Debug.LogWarning($"VoiceMicrophone: punctuation service fell back to model '{candidate}'.");
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"VoiceMicrophone: punctuation service load failed for model '{candidate}' - {ex.Message}");
                }
            }

            Debug.LogWarning("VoiceMicrophone: punctuation service unavailable; punctuation will be skipped.");
            return false;
        }

        private bool TryCreateKeywordSpottingService(KeywordSettings keywordSettings, out KeywordSpotting service)
        {
            service = null;

            if (!keywordSettings.IsEnabled)
            {
                return false;
            }

            try
            {
                float score = Mathf.Max(0f, keywordSettings.KeywordsScore);
                float threshold = Mathf.Clamp01(keywordSettings.KeywordsThreshold);
                var customKeywords = keywordSettings.CustomKeywords != null
                    ? (KeywordSpotting.KeywordRegistration[])keywordSettings.CustomKeywords.Clone()
                    : null;

                string primary = NormalizeModelId(keywordSettings.ModelId);
                string fallback = NormalizeModelId(KeywordSettings.Default.ModelId);
                var reporter = EnsureFeedbackReporter();

                string[] candidates = new string[2];
                int candidateCount = 0;

                if (primary.Length > 0)
                {
                    candidates[candidateCount++] = primary;
                }

                if (fallback.Length > 0 && !string.Equals(fallback, primary, StringComparison.Ordinal))
                {
                    candidates[candidateCount++] = fallback;
                }

                if (candidateCount == 0)
                {
                    Debug.LogWarning("VoiceMicrophone: missing keyword spotting model identifier; keyword detection disabled.");
                    return false;
                }

                for (int i = 0; i < candidateCount; i++)
                {
                    string candidate = candidates[i];
                    try
                    {
                        service = new KeywordSpotting(
                            candidate,
                            (int)DEFAULT_SAMPLERATE,
                            score,
                            threshold,
                            customKeywords,
                            reporter);

                        Interlocked.Increment(ref _pendingModelLoads);
                        if (!string.Equals(candidate, primary, StringComparison.Ordinal) && primary.Length > 0)
                        {
                            Debug.LogWarning($"VoiceMicrophone: keyword spotting fell back to model '{candidate}'.");
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"VoiceMicrophone: failed to initialize keyword spotting model '{candidate}': {ex.Message}");
                    }
                }

                Debug.LogWarning("VoiceMicrophone: keyword spotting unavailable; keyword detection disabled.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"VoiceMicrophone: unexpected keyword spotting initialization failure - {ex.Message}");
                return false;
            }
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





        #region Private ASR Handler Methods

        private void HandleStreamingRecognition(string content) => _ = ProcessRecognitionAsync(content, true);

        private void HandleOfflineRecognition(string content) => _ = ProcessRecognitionAsync(content, false);

        private async Task ProcessRecognitionAsync(string content, bool isStreaming)
        {
            var token = _recognitionLifetimeCts?.Token ?? CancellationToken.None;
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                string text = content ?? string.Empty;

                if (IsPunctuationActive() && !string.IsNullOrEmpty(text))
                {
                    var punctuationService = _punctService;
                    if (punctuationService != null)
                    {
                        try
                        {
                            text = await punctuationService.AddPunctuationAsync(text);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"VoiceMicrophone: punctuation failed - {ex.Message}");
                        }
                    }
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (isStreaming)
                {
                    HandleStreamingText(text);
                }
                else
                {
                    HandleOfflineText(text);
                }
            }
            catch (OperationCanceledException)
            {
                // Intentionally ignored.
            }
            catch (Exception ex)
            {
                Debug.LogError($"VoiceMicrophone: recognition pipeline failed - {ex.Message}");
            }
        }

        private void HandleStreamingText(string content)
        {
            content = content?.Trim();
            bool hasContent = !string.IsNullOrEmpty(content);

            ApplyStreamingVoiceActivity(hasContent);

            if (!hasContent)
            {
                EmitStreaming(string.Empty);

                if (!string.IsNullOrEmpty(_lastStreamingPartial))
                {
                    CommitRecognition(_lastStreamingPartial, fromStreaming: true);
                    _lastStreamingPartial = string.Empty;
                }

                return;
            }

            if (_lastStreamingPartial != content)
            {
                EmitStreaming(content);
            }

            if (AllowResultStorage())
            {
                _lastStreamingPartial = content;
            }
            else
            {
                _lastStreamingPartial = string.Empty;
            }
        }

        private void HandleOfflineText(string content)
        {
            content = content?.Trim();
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            CommitRecognition(content, fromStreaming: false);
        }

        private void CommitRecognition(string text, bool fromStreaming)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (fromStreaming && AsrConfig.RecognitionMode == RecognitionMode.Hybrid)
            {
                return;
            }

            if (!AllowResultStorage())
            {
                return;
            }

            string textToAppend = fromStreaming ? ExtractStreamingDelta(text) : text;

            if (string.IsNullOrEmpty(textToAppend))
            {
                return;
            }

            if (!fromStreaming)
            {
                _lastStreamingCommittedResult = string.Empty;
            }

            _asrBuffer.Append(textToAppend);
            OnTranscription(_asrBuffer.ToString(), false);

            if (!fromStreaming)
            {
                CompleteOfflineSubmissionIfReady();
            }
        }

        private void CompleteOfflineSubmissionIfReady()
        {
            if (!IsOfflinePipelineActive())
            {
                SubmitAsrBuffer();
                return;
            }

            if (IsSpeaking)
            {
                return;
            }

            if (_asrBuffer.Length == 0)
            {
                return;
            }

            SubmitAsrBuffer();
        }

        private string ExtractStreamingDelta(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(_lastStreamingCommittedResult))
            {
                _lastStreamingCommittedResult = text;
                return text;
            }

            if (string.Equals(text, _lastStreamingCommittedResult, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (text.StartsWith(_lastStreamingCommittedResult, StringComparison.Ordinal))
            {
                string delta = text.Substring(_lastStreamingCommittedResult.Length);
                _lastStreamingCommittedResult = text;
                return delta;
            }

            if (_lastStreamingCommittedResult.StartsWith(text, StringComparison.Ordinal))
            {
                _lastStreamingCommittedResult = text;
                return string.Empty;
            }

            int prefixLength = GetCommonPrefixLength(_lastStreamingCommittedResult, text);
            if (prefixLength > 0)
            {
                string delta = text.Substring(prefixLength);
                _lastStreamingCommittedResult = text;
                return delta;
            }

            _lastStreamingCommittedResult = text;
            return text;
        }

        private static int GetCommonPrefixLength(string first, string second)
        {
            int length = Math.Min(first.Length, second.Length);
            int index = 0;
            while (index < length && first[index] == second[index])
            {
                index++;
            }

            return index;
        }

        private void EmitStreaming(string content)
        {
            if (!IsStreamingPipelineActive())
            {
                return;
            }

            if (!AllowStreamingEmission())
            {
                return;
            }
            if (!string.IsNullOrEmpty(content) && !string.IsNullOrWhiteSpace(content))
            {
                OnASRTranscriptionStreaming?.Invoke(content);
            }
            _hasStreamingEmission = !string.IsNullOrEmpty(content);
        }

        private void ApplyStreamingVoiceActivity(bool isActive)
        {
            if (AsrConfig.RecognitionMode == RecognitionMode.Streaming)
            {
                SetVoiceActivity(isActive);
            }
        }

        private void SetVoiceActivity(bool isVoiceActivity)
        {
            if (IsVoiceActivity == isVoiceActivity)
            {
                return;
            }

            IsVoiceActivity = isVoiceActivity;
            OnVoiceActivityChanged?.Invoke(IsVoiceActivity);
            if (IsVoiceActivity)
            {

                _silence.ResetElapsed();

            }
        }

        private void OnInternalVoiceActivityChangeHandler(bool isVoiceActivity) => SetVoiceActivity(isVoiceActivity);

        private void OnInternalTranscriptionSubmitHandler(string content)
        {
            if (!string.IsNullOrEmpty(content))
            {
                OnTranscription(content, true);
                OnASRTranscriptionSubmit?.Invoke(content);
            }
        }
        #endregion


        #region Private Update Methodse
        private void UpdateKeywordTimers()
        {
            if (!IsKeywordSpottingOperational() || !_keywordActive)
            {
                return;
            }

            var keywordSettings = AsrConfig.KeywordSettings;
            if (keywordSettings.ContinuousConversation)
            {
                float configuredTimeout = Mathf.Max(0f, keywordSettings.ContinuousConversationTimeoutSeconds);
                float dialogTimeout = Mathf.Max(MIN_CONVERSATION_TIMEOUT_SECONDS, configuredTimeout);
                if (!IsSpeaking && !IsVoiceActivity)
                {
                    if (_keywordActive && _silence.Elapsed > dialogTimeout)
                    {
                        ResetKeywordGate(clearStreaming: true);
                        _keywordActive = false;
                    }

                }

            }
        }

        private void UpdateSpeakingState()
        {
            float deltaTime = Time.deltaTime;
            bool voiceActive = IsVoiceActivity;

            if (voiceActive)
            {
                if (!_lastVoiceActivityState)
                {
                    _currentSpeechSegmentDuration = 0f;
                }

                _currentSpeechSegmentDuration += deltaTime;
                _silence.ResetElapsed();

                if (!IsSpeaking)
                {
                    EnterSpeaking();
                }
            }
            else
            {
                if (_lastVoiceActivityState)
                {
                    FinalizeSpeechSegment();
                }

                _silence.OnSilence(deltaTime);

                if (IsSpeaking)
                {
                    float requiredSilence = CalculateSilenceThreshold();
                    if (_silence.Elapsed >= requiredSilence)
                    {
                        ExitSpeaking();
                        _silence.ClearHold();
                    }
                }
            }

            _lastVoiceActivityState = voiceActive;
        }

        private void EnterSpeaking()
        {
            if (IsSpeaking)
            {
                return;
            }
            IsSpeaking = true;
            _lastSubmittedTranscription = string.Empty;
            OnSpeakingChanged?.Invoke(true);
        }

        private void ExitSpeaking()
        {
            if (!IsSpeaking)
            {
                return;
            }

            IsSpeaking = false;
            OnSpeakingChanged?.Invoke(false);

            if (IsOfflinePipelineActive())
            {
                CompleteOfflineSubmissionIfReady();
            }
            else
            {
                SubmitAsrBuffer();
            }
        }

        private void FinalizeSpeechSegment()
        {
            _lastSpeechSegmentDuration = Mathf.Max(_currentSpeechSegmentDuration, MIN_SPEECH_SEGMENT_DURATION);
            _currentSpeechSegmentDuration = 0f;
            _silence.ResetElapsed();
        }

        private float CalculateSilenceThreshold()
        {
            float speechDuration = Mathf.Max(_lastSpeechSegmentDuration, MIN_SPEECH_SEGMENT_DURATION);
            float dynamicThreshold = Mathf.Clamp(
                speechDuration * SILENCE_DURATION_SCALE,
                MIN_SILENCE_AFTER_SPEECH,
                MAX_SILENCE_AFTER_SPEECH);
            return _silence.Required(dynamicThreshold);
        }


        #endregion


        private bool IsStreamingPipelineActive()
        {
            return RequiresStreaming && _streamingService != null;
        }

        private bool IsOfflinePipelineActive()
        {
            return RequiresOffline && _offlineService != null;
        }

        private bool IsKeywordSpottingOperational()
        {
            return RequiresKeywordSpotting && _keywordService != null;
        }

        private bool IsPunctuationActive()
        {
            return RequiresPunctuation && _punctService != null;
        }

        private bool AllowResultStorage()
        {
            return !IsKeywordSpottingOperational() || _keywordActive;
        }

        private bool AllowStreamingEmission()
        {
            if (!IsKeywordSpottingOperational())
            {
                return true;
            }

            if (_keywordActive)
            {
                return true;
            }

            if (_hasStreamingEmission)
            {
                OnASRTranscriptionStreaming?.Invoke(string.Empty);
                _hasStreamingEmission = false;
            }

            return false;
        }

        private void HandleConversationClosed()
        {
            if (!IsKeywordSpottingOperational())
            {
                return;
            }

            if (AsrConfig.KeywordSettings.ContinuousConversation)
            {
                // Keep the gate open; timers handle expiry.
                return;
            }

            ResetKeywordGate(clearStreaming: true);
        }

        private void ResetKeywordGate(bool clearStreaming)
        {
            bool wasActive = _keywordActive;
            var previousKeyword = _lastKeyword;
            _keywordActive = false;
            _lastStreamingPartial = string.Empty;
            _lastStreamingCommittedResult = string.Empty;
            _lastKeyword = string.Empty;
            _silence.Reset();

            if (clearStreaming && _hasStreamingEmission)
            {
                OnASRTranscriptionStreaming?.Invoke(string.Empty);
            }

            _hasStreamingEmission = false;

            if (wasActive)
            {
                OnKeywordDeactivated();
                OnKeywordActivityChanged?.Invoke(previousKeyword, false);
            }
        }


        private void SubmitAsrBuffer()
        {
            if (_asrBuffer.Length == 0)
            {
                return;
            }

            var transcriptionResult = _asrBuffer.ToString().Trim();
            _asrBuffer.Clear();
            if (string.IsNullOrEmpty(transcriptionResult))
            {
                HandleConversationClosed();
                return;
            }

            if (string.Equals(transcriptionResult, _lastSubmittedTranscription, StringComparison.Ordinal))
            {
                HandleConversationClosed();
                return;
            }

            _lastSubmittedTranscription = transcriptionResult;
            OnInternalTranscriptionSubmitHandler(transcriptionResult);
            HandleConversationClosed();
        }

        private void HandleKeywordDetected(string keyword)
        {
            if (!IsKeywordSpottingOperational())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return;
            }

            _lastKeyword = keyword;
            _keywordActive = true;


            OnKeywordActivated(keyword);
            OnKeywordActivityChanged?.Invoke(keyword, true);
            _silence.Extend(MAX_SILENCE_AFTER_SPEECH * 2);
            var keywordSettings = AsrConfig.KeywordSettings;

            if (!keywordSettings.UseTriggerSound)
            {
                return;
            }

            var triggerSound = keywordSettings.TriggerSoundClip;
            if (triggerSound != null)
            {
                AudioPlayback.PlayClip(triggerSound);
                _silence.Extend(triggerSound.length * 2);
            }
        }


        #endregion

        #region Debug

        private Coroutine simulateCor;
        private void DebugSimulate()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (simulateCor != null)
                {
                    StopCoroutine(simulateCor);
                }
                simulateCor = StartCoroutine(ASRSimulate());
            }
        }

        private System.Collections.IEnumerator ASRSimulate()
        {

            OnInternalVoiceActivityChangeHandler(true);
            HandleKeywordDetected("小智同学");
            yield return new WaitForSecondsRealtime(4 * .15f);
            OnInternalVoiceActivityChangeHandler(false);
            yield return new WaitForSecondsRealtime(4 * .15f);
            OnInternalVoiceActivityChangeHandler(true);
            var content = "测试语音转文字";
            HandleOfflineRecognition(content);
            yield return new WaitForSecondsRealtime(content.Length * .15f);
            OnInternalVoiceActivityChangeHandler(false);
            yield return null;
        }
        #endregion

        #region ISherpaFeedbackHandler

        private const float DOWNLOAD_WEIGHT = 0.4f;
        private const float UNCOMPRESS_WEIGHT = 0.2f;
        private const float VERIFY_WEIGHT = 0.25f;
        private const float LOAD_WEIGHT = 0.15f;

        private readonly Dictionary<string, ModelProgress> _modelProgressStates = new Dictionary<string, ModelProgress>();

        private sealed class ModelProgress
        {
            public bool HasStarted;
            public float Download;
            public float Uncompress;

            public float Verify;
            public float Load;

            public void StartNew()
            {
                HasStarted = true;
                Download = 0f;
                Uncompress = 0f;
                Verify = 0f;
                Load = 0f;
            }

            public float GetProgress()
            {
                float total =
                    DOWNLOAD_WEIGHT * Download +
                    UNCOMPRESS_WEIGHT * Uncompress +
                    VERIFY_WEIGHT * Verify +
                    LOAD_WEIGHT * Load;

                return Mathf.Clamp01(total);
            }
        }

        private ModelProgress GetOrCreateState(SherpaOnnxModelMetadata metadata, bool startIfNew = true)
        {
            var key = GetModelKey(metadata);
            if (!_modelProgressStates.TryGetValue(key, out var state))
            {
                state = new ModelProgress();
                _modelProgressStates[key] = state;
            }

            if (startIfNew && !state.HasStarted)
            {
                state.StartNew();
            }

            return state;
        }

        private static string GetModelKey(SherpaOnnxModelMetadata metadata)
        {
            return string.IsNullOrWhiteSpace(metadata?.modelId) ? "unknown" : metadata.modelId;
        }

        private static bool IsInitialPrepareMessage(string message)
        {
            return !string.IsNullOrEmpty(message) && message.IndexOf("preparing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PublishProgress(string message)
        {
            var progress = CalculateTotalProgress();
            OnLoadingProgressFeedback?.Invoke(message, progress);
        }

        private void PublishFailed(FailedFeedback failedFeedback)
        {
            PublishProgress(failedFeedback?.Message);
            OnServiceLoadingFailed(failedFeedback);
            OnLoadingFailedFeedback?.Invoke(failedFeedback);
        }

        private void PublishSuccess(SuccessFeedback successFeedback)
        {
            PublishProgress(successFeedback?.Message);
            OnServiceLoadingSuccessed(successFeedback);
            OnLoadingSuccessedFeedback?.Invoke(successFeedback);
        }

        private float CalculateTotalProgress()
        {
            if (_pendingModelLoads <= 0)
            {
                return 0f;
            }

            float sum = 0f;
            foreach (var state in _modelProgressStates.Values)
            {
                sum += state.GetProgress();
            }

            return Mathf.Clamp01(sum / Mathf.Max(1, _pendingModelLoads));
        }

        private static float NormalizeProgress(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            if (value > 1f && value <= 100f)
            {
                value /= 100f;
            }

            return Mathf.Clamp01(value);
        }

        private static float DetermineLoadStageProgress(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return 0f;
            }

            if (message.IndexOf("loaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1f;
            }

            return 0f;
        }

        public void OnFeedback(PrepareFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            var state = GetOrCreateState(feedback.Metadata, startIfNew: false);

            if (!state.HasStarted || IsInitialPrepareMessage(feedback.Message))
            {
                state.StartNew();
            }

            PublishProgress(feedback.Message);
        }

        public void OnFeedback(DownloadFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            var state = GetOrCreateState(feedback.Metadata);
            var progress = NormalizeProgress(feedback.Progress);
            state.Download = Mathf.Max(state.Download, progress);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(UncompressFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            var state = GetOrCreateState(feedback.Metadata);
            var progress = NormalizeProgress(feedback.Progress);
            state.Uncompress = Mathf.Max(state.Uncompress, progress);
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(VerifyFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            var state = GetOrCreateState(feedback.Metadata);

            if (Mathf.Approximately(state.Download, 0f))
            {
                state.Download = 1f;
            }

            if (Mathf.Approximately(state.Uncompress, 0f))
            {
                state.Uncompress = 1f;
            }

            state.Verify = Mathf.Max(state.Verify, NormalizeProgress(feedback.Progress));
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(LoadFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            var state = GetOrCreateState(feedback.Metadata);

            if (Mathf.Approximately(state.Download, 0f))
            {
                state.Download = 1f;
            }

            if (Mathf.Approximately(state.Uncompress, 0f))
            {
                state.Uncompress = 1f;
            }

            if (Mathf.Approximately(state.Verify, 0f))
            {
                state.Verify = 1f;
            }

            state.Load = Mathf.Max(state.Load, DetermineLoadStageProgress(feedback.Message));
            PublishProgress(feedback.Message);
        }

        public void OnFeedback(CancelFeedback feedback)
        {
            PublishProgress(feedback?.Message);
        }

        public void OnFeedback(SuccessFeedback feedback)
        {

            PublishSuccess(feedback);
            Interlocked.Increment(ref _counterModelLoads);

            if (feedback != null)
            {
                var state = GetOrCreateState(feedback.Metadata);

                state.Download = 1f;
                state.Uncompress = 1f;

                state.Verify = 1f;
                state.Load = 1f;
            }

            PublishProgress(feedback?.Message);
            if (_counterModelLoads >= _pendingModelLoads)
            {

                Initialized = true;

                var config = AsrConfig;
                var preset = config.GetActivePreset();
                OnServicesInitialized(config, preset);
            }
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            Debug.LogError($"VoiceMicrophone: model load failed - {feedback?.Message ?? "unknown error"}");
            PublishFailed(feedback);
        }

        public void OnFeedback(CleanFeedback feedback)
        {
            PublishProgress(feedback?.Message);
        }
        #endregion

    }
}

#endif
