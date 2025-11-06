using System;

namespace Eitan.EasyMic.Runtime.Mono
{
    [Serializable]
    public struct MicrophoneOptions
    {
        public bool recordOnAwake;
        public bool autoFallback;

        public MicrophoneOptions(bool recordOnAwake, bool autoFallback)
        {
            this.recordOnAwake = recordOnAwake;
            this.autoFallback = autoFallback;
        }

        public static MicrophoneOptions Default => new MicrophoneOptions(true, true);
    }
}
