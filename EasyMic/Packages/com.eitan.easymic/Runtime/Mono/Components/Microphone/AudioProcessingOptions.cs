#if EASYMIC_APM_INTEGRATION
using System;

namespace Eitan.EasyMic.Runtime.Mono
{
    /// <summary>
    /// Optional WebRTC audio preprocessing profile applied when the EasyMic APM package is installed.
    /// </summary>
    [Serializable]
    public struct AudioProcessingOptions
    {
        public bool EnableAEC;
        public bool EnableANS;
        public bool EnableAGC;

        public bool AnyEnabled => EnableAEC || EnableANS || EnableAGC;

        public AudioProcessingOptions(bool enableAEC, bool enableANS, bool enableAGC)
        {
            EnableAEC = enableAEC;
            EnableANS = enableANS;
            EnableAGC = enableAGC;
        }

        public static AudioProcessingOptions Default => new AudioProcessingOptions(true, true, true);
        public static AudioProcessingOptions Disable => new AudioProcessingOptions(false, false, false);
        public static AudioProcessingOptions AECOnly => new AudioProcessingOptions(true, false, false);
        public static AudioProcessingOptions ANSOnly => new AudioProcessingOptions(false, true, false);
        public static AudioProcessingOptions AGCOnly => new AudioProcessingOptions(false, false, true);
    }
}
#endif
