#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading.Tasks;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Interface for TTS pipeline implementations supporting both local and remote synthesis.
    /// </summary>
    internal interface IChatTtsPipeline : IDisposable
    {
        event Action<long, bool> OnSpeakingStateChanged;
        event Action<long, string> OnSentenceStarted;
        event Action<long, string> OnSentenceCompleted;
        event Action<long, float> OnBufferProgress;
        event Action<float[], int, int, int> OnPlaybackAudioQueued;

        bool IsSpeaking { get; }
        int QueuedSentenceCount { get; }

        void Configure(TtsPipelineConfig config);
        bool BeginTurn(long turnId);
        bool Enqueue(long turnId, string sentence);
        void CompleteTurn(long turnId);
        void Stop();
        Task StopAndWaitAsync();
        Task WaitForIdleAsync();
    }
}
#endif
