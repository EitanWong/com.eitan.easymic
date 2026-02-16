#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System;
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Encapsulates all tweakable AI chat configuration so it can be reused and edited cleanly.
    /// </summary>
    [Serializable]
    public class AIChatControllerConfig
    {
        [Header("Components")]
        public VoiceMicrophone Microphone;
        public SpeechSynthesizer SpeechSynthesizer;

        [Header("LLM Settings")]
        public string ApiBaseUrl = "https://api.openai.com/v1/";
        public string LlmModel = "gpt-5.2";
        [Range(0f, 1.5f)] public float LlmTemperature = 0.7f;
        public PromptProfile SystemPromptProfile;
        public bool LogStreamingChunks;

        [Header("Conversation")]
        [Min(0)]
        public int MaxHistoryTurns = 6;
        [Header("Speech Input")]
        [Min(0.1f)] public float AsrTurnDetectionDelaySeconds = AIChatRuntimeDefaults.DefaultAsrTurnDetectionDelaySeconds;

        [Header("Speech Output")]
        public bool UseLocalTts = false;
        public string TtsModel = "tts-1";
        public string TtsVoice = "alloy";
        public bool UseStreamingTts = true;
        public bool EnableTtsDiagnostics = false;

        [Header("Experience")]
        public bool InterruptAssistantOnUserSpeech = true;
        public bool AutoHideMouseCursorWhenIdle = true;
        [Min(0f)] public float MouseCursorHideDelaySeconds = 1.5f;
        [Min(0f)] public float CursorMoveThresholdPixels = 1f;

        [Header("Runtime")]
        public float MicStartupDelay = 1f;
        public bool LoadRuntimeConfigOnAwake = true;
        public string RuntimeConfigFileName = "ai_chat_config.json";

        [NonSerialized]
        private string _apiKeyOverride = string.Empty;

        public void SetApiKeyOverride(string apiKey)
        {
            _apiKeyOverride = apiKey ?? string.Empty;
        }

        public string ResolveApiKey()
        {
            if (!string.IsNullOrWhiteSpace(_apiKeyOverride))
            {
                return _apiKeyOverride;
            }

            return string.Empty;
        }
    }
}

#endif
