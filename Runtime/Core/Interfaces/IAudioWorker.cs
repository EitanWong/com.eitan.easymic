namespace Eitan.EasyMic.Runtime
{
    using System;

    /// <summary>
    /// The core interface for any processing unit in the audio pipeline.
    /// EasyMic invokes this from managed transport worker threads, never from the
    /// miniaudio device callback. Implementations must not call Unity APIs unless
    /// they also implement <see cref="IMainThreadAudioProcessor"/> and are routed by
    /// a main-thread dispatcher.
    /// </summary>
    public interface IAudioWorker : IDisposable
    {
        /// <summary>
        /// Initializes the worker with the recording's audio format.
        /// </summary>
        void Initialize(AudioContext state);

        /// <summary>
        /// Processes a chunk of audio data.
        /// </summary>
        void OnAudioPass(Span<float> buffer, AudioContext state);
    }

    /// <summary>
    /// Marker for processors that are safe to run on EasyMic transport workers.
    /// These processors must use preallocated state for steady-state processing and
    /// must not call UnityEngine APIs.
    /// </summary>
    public interface IAudioTransportProcessor : IAudioWorker
    {
    }

    /// <summary>
    /// Marker for processors that require Unity main-thread execution. They must not
    /// be inserted into callback or transport-worker paths.
    /// </summary>
    public interface IMainThreadAudioProcessor : IAudioWorker
    {
    }

    /// <summary>
    /// Marker for processors that are explicitly forbidden from realtime or transport
    /// execution because they may allocate, block, perform I/O, or call Unity APIs.
    /// </summary>
    public interface IRealtimeForbiddenProcessor : IAudioWorker
    {
    }
}
