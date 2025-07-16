namespace Eitan.EasyMic.Runtime
{
    using System;
    /// <summary>
    /// A base implementation of IAudioWorker providing common functionality.
    /// You provided a version of this; this one is slightly adjusted to align with the new design.
    /// </summary>
    public abstract class AudioWorkerBase : IAudioWorker
    {
        protected bool IsInitialized { get; private set; }
        protected bool IsDisposed{ get; private set; }
        public virtual void Initialize(AudioState state)
        {
            IsInitialized = true;;
        }

        public abstract void OnAudioPass(Span<float> audiobuffer, AudioState state);

        public virtual void Dispose()
        {
            if (IsDisposed) return;
                IsDisposed = true;
            IsInitialized = false;
        }
    }

}