using System;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [Serializable]
    internal sealed class AIChatRuntimeConfig
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
        public float AsrTurnDetectionDelaySeconds = -1f;
        public int AsrEnablePunctuation = -1;
        public string AsrPunctuationModelId;

        public string LocalTtsModelId;
        public int LocalTtsVoiceId = -1;
        public float LocalTtsSpeed = -1f;
        public int LocalTtsSampleRate = -1;
    }
}
