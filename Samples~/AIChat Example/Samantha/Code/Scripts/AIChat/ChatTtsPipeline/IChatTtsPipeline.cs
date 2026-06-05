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
}
#endif
