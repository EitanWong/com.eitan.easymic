using System;

namespace Eitan.EasyMic.Runtime
{
    internal static class MiniaudioDeviceConfigPolicy
    {
        private const int WasapiUsageProAudio = 2;
        private const int OpenSlStreamVoice = 1;
        private const int OpenSlRecordingVoiceCommunication = 4;
        private const int AAudioUsageVoiceCommunication = 2;
        private const int AAudioContentSpeech = 1;
        private const int AAudioInputVoiceCommunication = 4;
        private const int AAudioInputVoicePerformance = 6;
        private const int AAudioAllowCaptureByAll = 1;

        public static void Apply(
            ref Native.DeviceConfig config,
            uint sampleRate,
            Native.DeviceType deviceType,
            EasyMicLatencyProfile profile)
        {
            uint periodFrames = CalculatePeriodFrames(sampleRate, profile);
            config.PeriodSizeInFrames = periodFrames;
            config.PeriodSizeInMilliseconds = CalculatePeriodMilliseconds(sampleRate, periodFrames);
            config.Periods = CalculatePeriods(profile);
            config.PerformanceProfile = profile == EasyMicLatencyProfile.SafeStreaming ? 1 : 0;
            config.NoClip = 1;
            config.NoDisableDenormals = 0;
            config.NoFixedSizedCallback = 0;

            bool playback = (deviceType & Native.DeviceType.Playback) != 0;
            bool capture = (deviceType & Native.DeviceType.Record) != 0;

            ApplyWasapi(ref config, profile);
            ApplyAlsa(ref config, profile);
            ApplyCoreAudio(ref config, profile);
            ApplyAndroid(ref config, playback, capture, profile);
        }

        private static uint CalculatePeriodFrames(uint sampleRate, EasyMicLatencyProfile profile)
        {
            uint sr = Math.Max(8000u, sampleRate);
            uint frames;
            switch (profile)
            {
                case EasyMicLatencyProfile.UltraLowLatency:
                    frames = sr / 200u; // 5 ms
                    break;
                case EasyMicLatencyProfile.LowLatency:
                    frames = sr / 100u; // 10 ms
                    break;
                case EasyMicLatencyProfile.SafeStreaming:
                    frames = sr / 40u; // 25 ms
                    break;
                default:
                    frames = sr / 67u; // about 15 ms
                    break;
            }

            return Math.Max(64u, frames);
        }

        private static uint CalculatePeriodMilliseconds(uint sampleRate, uint periodFrames)
        {
            if (sampleRate == 0)
            {
                return 0;
            }

            return Math.Max(1u, (uint)Math.Round(periodFrames * 1000.0 / sampleRate));
        }

        private static uint CalculatePeriods(EasyMicLatencyProfile profile)
        {
            switch (profile)
            {
                case EasyMicLatencyProfile.UltraLowLatency:
                    return 2;
                case EasyMicLatencyProfile.LowLatency:
                    return 3;
                case EasyMicLatencyProfile.SafeStreaming:
                    return 4;
                default:
                    return 3;
            }
        }

        private static void ApplyWasapi(ref Native.DeviceConfig config, EasyMicLatencyProfile profile)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            config.Wasapi.Usage = WasapiUsageProAudio;
            config.Wasapi.NoAutoConvertSrc = profile == EasyMicLatencyProfile.SafeStreaming ? (byte)0 : (byte)1;
            config.Wasapi.NoDefaultQualitySrc = profile == EasyMicLatencyProfile.SafeStreaming ? (byte)0 : (byte)1;
            config.Wasapi.NoHardwareOffloading = 1;
#endif
        }

        private static void ApplyAlsa(ref Native.DeviceConfig config, EasyMicLatencyProfile profile)
        {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            config.Alsa.NoAutoFormat = profile == EasyMicLatencyProfile.SafeStreaming ? 0u : 1u;
            config.Alsa.NoAutoChannels = profile == EasyMicLatencyProfile.SafeStreaming ? 0u : 1u;
            config.Alsa.NoAutoResample = profile == EasyMicLatencyProfile.SafeStreaming ? 0u : 1u;
#endif
        }

        private static void ApplyCoreAudio(ref Native.DeviceConfig config, EasyMicLatencyProfile profile)
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_IOS
            config.CoreAudio.AllowNominalSampleRateChange = profile == EasyMicLatencyProfile.SafeStreaming ? 0u : 1u;
#endif
        }

        private static void ApplyAndroid(
            ref Native.DeviceConfig config,
            bool playback,
            bool capture,
            EasyMicLatencyProfile profile)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            config.OpenSl.StreamType = playback ? OpenSlStreamVoice : 0;
            config.OpenSl.RecordingPreset = capture ? OpenSlRecordingVoiceCommunication : 0;
            config.OpenSl.EnableCompatibilityWorkarounds = 1;
            config.AAudio.Usage = AAudioUsageVoiceCommunication;
            config.AAudio.ContentType = playback ? AAudioContentSpeech : 0;
            config.AAudio.InputPreset = capture && profile == EasyMicLatencyProfile.UltraLowLatency
                ? AAudioInputVoicePerformance
                : (capture ? AAudioInputVoiceCommunication : 0);
            config.AAudio.AllowedCapturePolicy = AAudioAllowCaptureByAll;
            config.AAudio.EnableCompatibilityWorkarounds = 1;
            config.AAudio.AllowSetBufferCapacity = 1;
#endif
        }
    }
}
