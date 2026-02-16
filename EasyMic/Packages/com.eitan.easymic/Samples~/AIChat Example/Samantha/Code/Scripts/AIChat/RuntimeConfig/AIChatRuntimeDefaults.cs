namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal static class AIChatRuntimeDefaults
    {
        public const string DefaultAsrStreamingModelId = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
        public const string DefaultAsrOfflineModelId = "sherpa-onnx-zipformer-zh-en-2023-11-22";
        public const string DefaultAsrVadModelId = "silero-vad-v5";
        public const string DefaultAsrPunctuationModelId = "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8";
        public const float DefaultAsrTurnDetectionDelaySeconds = 0.8f;

        public const string DefaultLocalTtsModelId = "vits-melo-tts-zh_en";
        public const int DefaultLocalTtsVoiceId = 1;
        public const float DefaultLocalTtsSpeed = 1f;
        public const int DefaultLocalTtsSampleRate = 44100;
    }
}
