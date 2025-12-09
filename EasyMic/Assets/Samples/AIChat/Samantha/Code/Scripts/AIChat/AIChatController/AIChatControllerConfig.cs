using System;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.ASR;
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
        public string ApiBaseUrl = "http://127.0.0.1:8000/v1/";
        public string ApiKey = string.Empty;
        public string LlmModel = "gpt-4o-mini";
        [Range(0f, 1.5f)] public float LlmTemperature = 0.7f;
        [TextArea(3, 8)]
        public string SystemPrompt = "You are Samantha, an empathetic AI companion. Keep answers concise, warm, and natural.";
        public bool LogStreamingChunks;

        [Header("Speech Output")]
        public bool UseLocalTts = true;
        public string TtsModel = "gpt-4o-mini-tts";
        public string TtsVoice = "alloy";
        public bool UseStreamingTts = true;
        [Range(0.05f, 0.4f)] public float StreamingPlaybackBufferSeconds = 0.18f;

        [Header("Experience")]
        public bool InterruptAssistantOnUserSpeech = true;

        [Header("Runtime")]
        public float MicStartupDelay = 1f;
    }
}
