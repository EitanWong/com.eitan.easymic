using System;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime.Mono.Components;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Interface for TTS pipeline implementations supporting both local and remote synthesis.
    /// </summary>
    internal interface IChatTtsPipeline : IDisposable
    {
        event Action<bool> OnSpeakingStateChanged;
        event Action<string> OnSentenceStarted;
        event Action<string> OnSentenceCompleted;
        event Action<float> OnBufferProgress;

        bool IsSpeaking { get; }
        int QueuedSentenceCount { get; }

        void Configure(TtsPipelineConfig config);
        void Enqueue(string sentence);
        void Stop();
        Task StopAndWaitAsync();
        Task WaitForIdleAsync();
    }

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
