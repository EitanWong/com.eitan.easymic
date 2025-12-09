using System;
using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void InitializeComponents()
        {
            _serviceLoadingRecord = new Dictionary<string, float>();
            _sentenceAssembler = new StreamingSentenceAssembler();
            _networkHandler = new NetworkAdaptiveHandler();
        }

        private void InitializeOpenAiClient()
        {
            if (string.IsNullOrWhiteSpace(Config.ApiBaseUrl))
            {
                Debug.LogWarning("[AIChat] API base URL is empty.");
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
                _openAiClient = new OpenAICompatibleClient(normalized, Config.ApiKey);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIChat] Failed to initialize OpenAI client: {ex.Message}");
                _openAiClient = null;
            }
        }

        private void InitializeMicrophone()
        {
            var mic = Microphone;
            if (mic == null)
            {
                Debug.LogError("[AIChat] VoiceMicrophone reference is missing.");
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
            if (_ttsPipeline == null)
            {
                _ttsPipeline = new ChatTtsPipeline(GetOrCreateOpenAiClient);
                _ttsPipeline.OnSpeakingStateChanged += OnPipelineSpeakingStateChanged;
                _ttsPipeline.OnSentenceStarted += OnTtsSentenceStarted;
                _ttsPipeline.OnSentenceCompleted += OnTtsSentenceCompleted;
            }

            var pipelineConfig = new TtsPipelineConfig
            {
                UseLocalTts = Config.UseLocalTts && SpeechSynthesizer != null,
                LocalSynthesizer = SpeechSynthesizer,
                ClientProvider = GetOrCreateOpenAiClient,
                RemoteModel = Config.TtsModel,
                RemoteVoice = Config.TtsVoice,
                EnableStreamingTts = Config.UseStreamingTts,
                StreamingBufferSeconds = Mathf.Clamp(Config.StreamingPlaybackBufferSeconds, 0.05f, 0.4f),
                LogSentences = Config.LogStreamingChunks
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
