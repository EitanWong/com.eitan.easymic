
namespace Eitan.EasyMic.Runtime
{
    using System;
    /// <summary>
    /// An abstract base class for audio workers that can ONLY READ audio data.
    /// This is ideal for analysis tasks like volume metering, silence detection, or logging.
    /// The design ensures compile-time safety against accidental modification of the audio buffer.
    /// </summary>
    public abstract class AudioReader : AudioWorkerBase
    {
        /// <summary>
        /// This 'sealed override' is the key to the read-only safety.
        /// It intercepts the call from the pipeline which has a mutable Span<float>.
        /// It then calls the abstract `Read` method, but passes a `ReadOnlySpan<float>`,
        /// enforcing that any inheriting class cannot modify the original buffer.
        /// </summary>
        public sealed override void OnAudioPass(Span<float> audiobuffer, AudioState state)
        {
            if (!IsInitialized) return;

            OnAudioRead(audiobuffer, state);
        }

        /// <summary>
        /// Implement this method to analyze audio data in a read-only fashion.
        /// </summary>
        /// <param name="audiobuffer">A read-only view of the audio buffer.</param>
        public abstract void OnAudioRead(ReadOnlySpan<float> audiobuffer, AudioState state);
    }

}