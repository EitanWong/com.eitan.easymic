namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public struct AIChatResolvedConfiguration
    {
        public string ApiKey;
        public string ApiBaseUrl;
        public string LlmModel;
        public float LlmTemperature;
        public int MaxHistoryTurns;
        public bool UseLocalTts;
        public string TtsModel;
        public string TtsVoice;
        public bool UseStreamingTts;
        public bool EnableTtsDiagnostics;
        public float AsrTurnDetectionDelaySeconds;
        public bool InterruptAssistantOnUserSpeech;
        public float MicStartupDelay;
        public int AsrRecognitionModeIndex;
        public string AsrStreamingModelId;
        public string AsrOfflineModelId;
        public string AsrVadModelId;
        public bool AsrEnablePunctuation;
        public string AsrPunctuationModelId;
        public string LocalTtsModelId;
        public int LocalTtsVoiceId;
        public float LocalTtsSpeed;
        public int LocalTtsSampleRate;
    }
}
