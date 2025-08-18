namespace Eitan.EasyMic.Runtime {
    using System;

    /// <summary>
    /// An abstract base class for audio workers that can READ FROM and WRITE TO the audio buffer.
    /// This is designed for processing tasks that modify the audio, such as filters (e.g., low-pass),
    /// effects (e.g., echo), or format conversion.
    /// </summary>
    public abstract class AudioWriter : AudioWorkerBase
    {
        /// <summary>
        /// This sealed override simplifies the interface for developers. Instead of overriding
        /// ProcessAudioBuffer, they implement the more descriptively named `Write` method.
        /// </summary>
        public sealed override void OnAudioPass(Span<float> audiobuffer, AudioState state)
        {
            if (!IsInitialized) { return; }
            OnAudioWrite(audiobuffer, state);
        }

        /// <summary>
        /// Implement this method to process or modify the audio data.
        /// </summary>
        /// <param name="audiobuffer">The audio buffer, which can be directly modified.</param>
        protected abstract void OnAudioWrite(Span<float> audiobuffer, AudioState state);
    }

}
