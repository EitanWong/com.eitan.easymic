using System;

namespace Eitan.EasyMic.Runtime
{
    internal static class AndroidLegacyDeviceConfig
    {
        internal const uint VoiceCommunicationPeriodMs = 10;
        internal const uint VoiceCommunicationPeriods = 3;

        private enum OpenSlStreamType
        {
            Default = 0,
            Voice = 1,
        }

        private enum OpenSlRecordingPreset
        {
            Default = 0,
            VoiceCommunication = 4,
        }

        private enum AAudioUsage
        {
            VoiceCommunication = 2,
        }

        private enum AAudioContentType
        {
            Default = 0,
            Speech = 1,
        }

        private enum AAudioInputPreset
        {
            Default = 0,
            VoiceCommunication = 4,
        }

        private enum AAudioAllowedCapturePolicy
        {
            ByAll = 1,
        }

        internal static int EstimateOutputDelayMs(uint sampleRate)
        {
            uint frames = GetPeriodSizeInFrames(sampleRate);
            long totalFrames = (long)frames * VoiceCommunicationPeriods;
            if (sampleRate == 0)
            {
                return (int)(VoiceCommunicationPeriodMs * VoiceCommunicationPeriods);
            }

            return (int)Math.Max(
                VoiceCommunicationPeriodMs * VoiceCommunicationPeriods,
                Math.Round(totalFrames * 1000.0 / sampleRate));
        }

        internal static void ApplyLowLatencyAndroidConfig(ref Native.DeviceConfig config, uint sampleRate, bool configurePlayback)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            config.PeriodSizeInFrames = GetPeriodSizeInFrames(sampleRate);
            config.PeriodSizeInMilliseconds = VoiceCommunicationPeriodMs;
            config.Periods = VoiceCommunicationPeriods;
            config.OpenSl.StreamType = configurePlayback ? (int)OpenSlStreamType.Voice : (int)OpenSlStreamType.Default;
            config.OpenSl.RecordingPreset = configurePlayback ? (int)OpenSlRecordingPreset.Default : (int)OpenSlRecordingPreset.VoiceCommunication;
            config.OpenSl.EnableCompatibilityWorkarounds = 1;
            config.AAudio.Usage = (int)AAudioUsage.VoiceCommunication;
            config.AAudio.ContentType = configurePlayback ? (int)AAudioContentType.Speech : (int)AAudioContentType.Default;
            config.AAudio.InputPreset = configurePlayback ? (int)AAudioInputPreset.Default : (int)AAudioInputPreset.VoiceCommunication;
            config.AAudio.AllowedCapturePolicy = (int)AAudioAllowedCapturePolicy.ByAll;
            config.AAudio.EnableCompatibilityWorkarounds = 1;
            config.AAudio.AllowSetBufferCapacity = 1;
#endif
        }

        private static uint GetPeriodSizeInFrames(uint sampleRate)
        {
            uint fallback = sampleRate > 0 ? sampleRate / 100u : 0u;
            return Math.Max(80u, fallback);
        }
    }
}
