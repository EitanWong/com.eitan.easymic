using System;
using System.Threading.Tasks;
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
        public Func<OpenAICompatibleClient> ClientProvider;
        public string RemoteModel;
        public string RemoteVoice;
        public bool EnableStreamingTts;
        public float StreamingBufferSeconds;
        public bool LogSentences;
        public int MaxParallelGenerations;
        public float PlaybackVolume;

        public static TtsPipelineConfig Default => new TtsPipelineConfig
        {
            UseLocalTts = false,
            EnableStreamingTts = true,
            StreamingBufferSeconds = 0.18f,
            MaxParallelGenerations = 0,
            PlaybackVolume = 1f
        };
    }
}
