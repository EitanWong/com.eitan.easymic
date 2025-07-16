namespace Eitan.EasyMic.Runtime
{
    using System;

    /// <summary>
    /// The core interface for any processing unit in the audio pipeline.
    /// It is used by MicSystem and the AudioPipeline itself.
    /// </summary>
    public interface IAudioWorker : IDisposable
    {
        /// <summary>
        /// Initializes the worker with the recording's audio format.
        /// </summary>
        void Initialize(AudioState state);

        /// <summary>
        /// Processes a chunk of audio data.
        /// </summary>
        void OnAudioPass(Span<float> buffer, AudioState state);
    }
}