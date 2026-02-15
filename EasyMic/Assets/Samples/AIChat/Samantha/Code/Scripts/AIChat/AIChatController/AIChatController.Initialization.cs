using System;
using System.Collections.Generic;
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void InitializeComponents()
        {
            _serviceLoadingRecord = new Dictionary<string, float>
            {
                [SERVICE_ASR_INIT_KEY] = 0f,
                [SERVICE_TTS_INIT_KEY] = 0f
            };

            _runtimeConfigStore = new JsonAIChatRuntimeConfigStore();
            _requestOrchestrator = new AIChatRequestOrchestrator(
                historyTurnProvider: () => Config.MaxHistoryTurns,
                systemPromptProvider: GetSystemPrompt,
                cleanText: CleanText,
                maxResponseBufferSize: MaxResponseBufferSize);

            _initialized = false;
            _lastLoadingProgress = 0f;
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

            try
            {
                string path = RuntimeConfigPath;
                var runtimeConfig = _runtimeConfigStore.LoadOrCreate(path, Config, out _);
                if (runtimeConfig == null)
                {
                    return;
                }

                _runtimeConfigStore.Apply(runtimeConfig, Config);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to load runtime config: {ex.Message}");
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

            ApplyTurnDetectionDelayOverride(mic);

            mic.OnMicrophoneInitialized += OnMicrophoneInitializedHandler;
            mic.OnASRTranscriptionStreaming += OnAsrStreamingHandler;
            mic.OnASRTranscriptionSubmit += OnAsrSubmitHandler;
            mic.OnSpeakingChanged += OnSpeakingChangedHandler;
            mic.OnLoadingProgressFeedback += OnMicrophoneLoadingProgressFeedbackHandler;

            if (!mic.MicrophoneOpts.recordOnAwake)
            {
                mic.Init();
            }
        }

        private void ApplyTurnDetectionDelayOverride(VoiceMicrophone microphone)
        {
            if (microphone == null)
            {
                return;
            }

            float delay = Mathf.Max(0.1f, Config.AsrTurnDetectionDelaySeconds);
            microphone.ConfigureTurnDetection(new TurnDetectionOptions(delay, delay));
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
            StopPendingMicStartup();

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
