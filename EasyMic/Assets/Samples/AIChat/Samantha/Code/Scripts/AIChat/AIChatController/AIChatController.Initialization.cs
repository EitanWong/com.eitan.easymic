using System;
using System.Collections.Generic;
using System.IO;
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private const string DefaultAsrStreamingModelId = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
        private const string DefaultAsrOfflineModelId = "sherpa-onnx-zipformer-zh-en-2023-11-22";
        private const string DefaultAsrVadModelId = "silero_vad_v5";
        private const string DefaultAsrPunctuationModelId = "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8";

        private const string DefaultLocalTtsModelId = "vits-melo-tts-zh_en";
        private const int DefaultLocalTtsVoiceId = 1;
        private const float DefaultLocalTtsSpeed = 1f;
        private const int DefaultLocalTtsSampleRate = 44100;

        [Serializable]
        private class RuntimeConfig
        {
            public string ApiKey;
            public string ApiBaseUrl;
            public string LlmModel;
            public float LlmTemperature = -1f;
            public string TtsModel;
            public string TtsVoice;
            public int UseLocalTts = -1;
            public int AsrRecognitionModeIndex = -1;
            public string AsrStreamingModelId;
            public string AsrOfflineModelId;
            public string AsrVadModelId;
            public int AsrEnablePunctuation = -1;
            public string AsrPunctuationModelId;
            public string LocalTtsModelId;
            public int LocalTtsVoiceId = -1;
            public float LocalTtsSpeed = -1f;
            public int LocalTtsSampleRate = -1;
        }

        private void InitializeComponents()
        {
            _serviceLoadingRecord = new Dictionary<string, float>();
            _sentenceAssembler = new StreamingSentenceAssembler();
            _networkHandler = new NetworkAdaptiveHandler();
        }

        private void LoadRuntimeConfigIfNeeded()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (!Config.LoadRuntimeConfigOnAwake)
            {
                return;
            }

            var fileName = string.IsNullOrWhiteSpace(Config.RuntimeConfigFileName)
                ? "ai_chat_config.json"
                : Config.RuntimeConfigFileName;
            var path = Path.Combine(Application.persistentDataPath, fileName);
            if (!File.Exists(path))
            {
                var runtimeConfig = CreateDefaultRuntimeConfig();
                SaveDefaultRuntimeConfig(path);
                ApplyRuntimeAsrConfig(runtimeConfig);
                ApplyRuntimeLocalTtsConfig(runtimeConfig);
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var runtimeConfig = JsonUtility.FromJson<RuntimeConfig>(json);
                if (runtimeConfig == null)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(runtimeConfig.ApiKey))
                {
                    Config.SetApiKeyOverride(runtimeConfig.ApiKey);
                }

                if (!string.IsNullOrWhiteSpace(runtimeConfig.ApiBaseUrl))
                {
                    Config.ApiBaseUrl = runtimeConfig.ApiBaseUrl;
                }

                if (!string.IsNullOrWhiteSpace(runtimeConfig.LlmModel))
                {
                    Config.LlmModel = runtimeConfig.LlmModel;
                }

                if (runtimeConfig.LlmTemperature >= 0f)
                {
                    Config.LlmTemperature = runtimeConfig.LlmTemperature;
                }

                if (!string.IsNullOrWhiteSpace(runtimeConfig.TtsModel))
                {
                    Config.TtsModel = runtimeConfig.TtsModel;
                }

                if (!string.IsNullOrWhiteSpace(runtimeConfig.TtsVoice))
                {
                    Config.TtsVoice = runtimeConfig.TtsVoice;
                }

                if (runtimeConfig.UseLocalTts >= 0)
                {
                    Config.UseLocalTts = runtimeConfig.UseLocalTts > 0;
                }

                ApplyRuntimeAsrConfig(runtimeConfig);
                ApplyRuntimeLocalTtsConfig(runtimeConfig);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to load runtime config: {ex.Message}");
            }
        }

        private void SaveDefaultRuntimeConfig(string path)
        {
            var runtimeConfig = CreateDefaultRuntimeConfig();
            try
            {
                var json = JsonUtility.ToJson(runtimeConfig, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to save default runtime config: {ex.Message}");
            }
        }

        private RuntimeConfig CreateDefaultRuntimeConfig()
        {
            var config = new RuntimeConfig
            {
                ApiBaseUrl = Config.ApiBaseUrl,
                LlmModel = Config.LlmModel,
                LlmTemperature = Config.LlmTemperature,
                TtsModel = Config.TtsModel,
                TtsVoice = Config.TtsVoice,
                UseLocalTts = Config.UseLocalTts ? 1 : 0,
                AsrRecognitionModeIndex = MapRecognitionModeToIndex(RecognitionMode.OfflineWithVad),
                AsrEnablePunctuation = 0
            };

            config.AsrStreamingModelId = DefaultAsrStreamingModelId;
            config.AsrOfflineModelId = DefaultAsrOfflineModelId;
            config.AsrVadModelId = DefaultAsrVadModelId;
            config.AsrPunctuationModelId = DefaultAsrPunctuationModelId;

            config.LocalTtsModelId = DefaultLocalTtsModelId;
            config.LocalTtsVoiceId = DefaultLocalTtsVoiceId;
            config.LocalTtsSpeed = DefaultLocalTtsSpeed;
            config.LocalTtsSampleRate = DefaultLocalTtsSampleRate;

            return config;
        }

        private void ApplyRuntimeAsrConfig(RuntimeConfig runtimeConfig)
        {
            var mic = Microphone;
            if (mic == null)
            {
                return;
            }

            var preset = mic.AsrConfig.ActivePresetConfiguration;
            if (TryMapRecognitionMode(runtimeConfig.AsrRecognitionModeIndex, out var recognitionMode))
            {
                preset.RecognitionMode = recognitionMode;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrStreamingModelId))
            {
                preset.StreamingModelId = runtimeConfig.AsrStreamingModelId;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrOfflineModelId))
            {
                preset.OfflineModelId = runtimeConfig.AsrOfflineModelId;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrVadModelId))
            {
                preset.VadModelId = runtimeConfig.AsrVadModelId;
            }

            if (runtimeConfig.AsrEnablePunctuation >= 0)
            {
                preset.EnablePunctuation = runtimeConfig.AsrEnablePunctuation > 0;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrPunctuationModelId))
            {
                preset.PunctuationModelId = runtimeConfig.AsrPunctuationModelId;
            }

            preset.Id = AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId;

            var asrConfig = AutomaticSpeechRecognitionConfiguration.CreateDefault();
            asrConfig.AddPreset(preset, true);
            asrConfig.SetActivePreset(AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId);
            mic.ApplyConfiguration(asrConfig);
        }

        private void ApplyRuntimeLocalTtsConfig(RuntimeConfig runtimeConfig)
        {
            var synthesizer = SpeechSynthesizer;
            if (synthesizer == null)
            {
                return;
            }

            var preset = synthesizer.TtsConfig.GetActivePreset();

            if (!string.IsNullOrWhiteSpace(runtimeConfig.LocalTtsModelId))
            {
                preset.modelId = runtimeConfig.LocalTtsModelId;
            }

            if (runtimeConfig.LocalTtsVoiceId >= 0)
            {
                preset.voiceId = runtimeConfig.LocalTtsVoiceId;
            }

            if (runtimeConfig.LocalTtsSpeed > 0f)
            {
                preset.speed = runtimeConfig.LocalTtsSpeed;
            }

            if (runtimeConfig.LocalTtsSampleRate > 0)
            {
                preset.sampleRates = runtimeConfig.LocalTtsSampleRate;
            }

            preset.Id = SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId;

            var ttsConfig = SpeechSynthesizerConfiguration.CreateDefault();
            ttsConfig.AddPreset(preset, true);
            ttsConfig.SetActivePreset(SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId);
            synthesizer.ApplyConfiguration(ttsConfig);
        }

        private static bool TryMapRecognitionMode(int index, out RecognitionMode mode)
        {
            switch (index)
            {
                case 0:
                    mode = RecognitionMode.Streaming;
                    return true;
                case 1:
                    mode = RecognitionMode.OfflineWithVad;
                    return true;
                case 2:
                    mode = RecognitionMode.Hybrid;
                    return true;
                default:
                    mode = RecognitionMode.Streaming;
                    return false;
            }
        }

        private static int MapRecognitionModeToIndex(RecognitionMode mode)
        {
            switch (mode)
            {
                case RecognitionMode.Streaming:
                    return 0;
                case RecognitionMode.OfflineWithVad:
                    return 1;
                case RecognitionMode.Hybrid:
                    return 2;
                default:
                    return -1;
            }
        }

        private void InitializeOpenAiClient()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Config.ApiBaseUrl))
            {
                Debug.LogWarning("[AIChat] API base URL is empty.");
                ReportError("API base URL is empty.");
                return;
            }

            var apiKey = Config.ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Debug.LogWarning("[AIChat] API key is missing. Set it via runtime config or SetApiKey.");
                ReportError("API key is missing. Set it via runtime config or SetApiKey.");
                return;
            }

            string normalized = Config.ApiBaseUrl.Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            try
            {
                _openAiClient?.Dispose();
                _openAiClient = new OpenAICompatibleClient(normalized, apiKey);
                _openAiClient.EnableTtsDiagnostics = Config.EnableTtsDiagnostics;
                _lastErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIChat] Failed to initialize OpenAI client: {ex.Message}");
                ReportError($"Failed to initialize OpenAI client: {ex.Message}");
                _openAiClient = null;
            }
        }

        private void InitializeMicrophone()
        {
            if (_initializationFailed)
            {
                return;
            }

            var mic = Microphone;
            if (mic == null)
            {
                Debug.LogError("[AIChat] VoiceMicrophone reference is missing.");
                ReportError("VoiceMicrophone reference is missing.");
                return;
            }

            mic.OnMicrophoneInitialized += OnMicrophoneInitializedHandler;
            mic.OnASRTranscriptionStreaming += OnAsrStreamingHandler;
            mic.OnASRTranscriptionSubmit += OnAsrSubmitHandler;
            mic.OnSpeakingChanged += OnSpeakingChangedHandler;
            mic.OnLoadingProgressFeedback += OnMicrophoneLoadingProgressFeedbackHandler;

            if (!mic.MicrophoneOpts.recordOnAwake)
            {
                mic.Init();
            }
            else if (!mic.IsRecording)
            {
                mic.StartRecording();
            }
        }

        private void InitializeSpeechSynthesizer()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (!Config.UseLocalTts)
            {
                if (SpeechSynthesizer != null)
                {
                    SpeechSynthesizer.enabled = false;
                }
                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 1f);
                EnsureTtsPipelineConfigured();
                return;
            }

            if (SpeechSynthesizer == null)
            {
                Debug.LogWarning("[AIChat] SpeechSynthesizer reference is required when local TTS is enabled.");
                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 0f);
                ReportError("SpeechSynthesizer reference is required when local TTS is enabled.");
                return;
            }

            SpeechSynthesizer.OnLoadingProgressFeedback += OnSpeechSynthesizerProgressFeedbackHandler;
            SpeechSynthesizer.OnTTSStateChanged += OnLocalTtsStateChanged;
            _localTtsCallbacksRegistered = true;
            EnsureTtsPipelineConfigured();
        }

        private void TeardownMicrophone()
        {
            var mic = Microphone;
            if (mic == null)
            {
                return;
            }

            mic.OnMicrophoneInitialized -= OnMicrophoneInitializedHandler;
            mic.OnASRTranscriptionStreaming -= OnAsrStreamingHandler;
            mic.OnASRTranscriptionSubmit -= OnAsrSubmitHandler;
            mic.OnSpeakingChanged -= OnSpeakingChangedHandler;
            mic.OnLoadingProgressFeedback -= OnMicrophoneLoadingProgressFeedbackHandler;

            if (mic.IsRecording)
            {
                mic.StopRecording();
            }
        }

        private void TeardownSpeechSynthesizer()
        {
            if (!_localTtsCallbacksRegistered || SpeechSynthesizer == null)
            {
                return;
            }

            SpeechSynthesizer.OnLoadingProgressFeedback -= OnSpeechSynthesizerProgressFeedbackHandler;
            SpeechSynthesizer.OnTTSStateChanged -= OnLocalTtsStateChanged;
            _localTtsCallbacksRegistered = false;
        }

        private void TeardownTtsPipeline()
        {
            if (_ttsPipeline == null)
            {
                return;
            }

            _ttsPipeline.OnSpeakingStateChanged -= OnPipelineSpeakingStateChanged;
            _ttsPipeline.Dispose();
            _ttsPipeline = null;
        }

        private OpenAICompatibleClient GetOrCreateOpenAiClient()
        {
            if (_openAiClient == null)
            {
                InitializeOpenAiClient();
            }

            return _openAiClient;
        }

        private void EnsureTtsPipelineConfigured()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (_ttsPipeline == null)
            {
                _ttsPipeline = new ChatTtsPipeline(GetOrCreateOpenAiClient);
                _ttsPipeline.OnSpeakingStateChanged += OnPipelineSpeakingStateChanged;
                _ttsPipeline.OnSentenceStarted += OnTtsSentenceStarted;
                _ttsPipeline.OnSentenceCompleted += OnTtsSentenceCompleted;
            }

            if (_openAiClient != null)
            {
                _openAiClient.EnableTtsDiagnostics = Config.EnableTtsDiagnostics;
            }

            var pipelineConfig = new TtsPipelineConfig
            {
                UseLocalTts = Config.UseLocalTts && SpeechSynthesizer != null,
                LocalSynthesizer = SpeechSynthesizer,
                PlaybackSource = SpeechSynthesizer != null ? SpeechSynthesizer.PlaybackSource : null,
                ClientProvider = GetOrCreateOpenAiClient,
                RemoteModel = Config.TtsModel,
                RemoteVoice = Config.TtsVoice,
                EnableStreamingTts = Config.UseStreamingTts,
                StreamingBufferSeconds = Mathf.Clamp(Config.StreamingPlaybackBufferSeconds, 0.05f, 0.4f),
                LogSentences = Config.LogStreamingChunks,
                EnableDiagnostics = Config.EnableTtsDiagnostics,
                MainThreadDispatcher = PostToUnityThread
            };

            _ttsPipeline.Configure(pipelineConfig);
        }

        private void OnPipelineSpeakingStateChanged(bool isSpeaking)
        {
            SetAssistantSpeakingState(isSpeaking);
        }

        private void OnTtsSentenceStarted(string sentence)
        {
            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][TTS] Speaking: {sentence}");
            }
        }

        private void OnTtsSentenceCompleted(string sentence)
        {
            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][TTS] Completed: {sentence}");
            }
        }
    }
}
