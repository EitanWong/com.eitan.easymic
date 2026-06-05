#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using Eitan.EasyMic.Runtime.Mono.Components;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Configuration for TTS pipeline.
    /// </summary>
    internal struct TtsPipelineConfig
    {
        public bool UseLocalTts;
        public SpeechSynthesizer LocalSynthesizer;
        public PlaybackAudioSourceBehaviour PlaybackSource;
        public Func<OpenAICompatibleClient> ClientProvider;
        public string RemoteModel;
        public string RemoteVoice;
        public Func<string, string, string, string> RemoteInputFormatter;
        public bool EnableStreamingTts;
        public bool LogSentences;
        public bool EnableDiagnostics;
        public int MaxParallelGenerations;
        public float PlaybackVolume;
        public Action<Action> MainThreadDispatcher;

        public static TtsPipelineConfig Default => new TtsPipelineConfig
        {
            UseLocalTts = false,
            EnableStreamingTts = true,
            MaxParallelGenerations = 0,
            PlaybackVolume = 1f,
            MainThreadDispatcher = null
        };
    }
}
#endif
