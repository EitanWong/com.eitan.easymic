using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static class AndroidLegacyDeviceConfig
    {
        internal const uint VoiceCommunicationPeriodMs = 10;
        internal const uint VoiceCommunicationPeriods = 3;

        private enum OpenSlStreamType : int
        {
            Default = 0,
            Voice = 1,
        }

        private enum OpenSlRecordingPreset : int
        {
            Default = 0,
            Generic = 1,
            Camcorder = 2,
            VoiceRecognition = 3,
            VoiceCommunication = 4,
            VoiceUnprocessed = 5,
        }

        private enum AAudioUsage : int
        {
            Default = 0,
            Media = 1,
            VoiceCommunication = 2,
        }

        private enum AAudioContentType : int
        {
            Default = 0,
            Speech = 1,
            Music = 2,
        }

        private enum AAudioInputPreset : int
        {
            Default = 0,
            Generic = 1,
            Camcorder = 2,
            VoiceRecognition = 3,
            VoiceCommunication = 4,
            Unprocessed = 5,
            VoicePerformance = 6,
        }

        private enum AAudioAllowedCapturePolicy : int
        {
            Default = 0,
            ByAll = 1,
            BySystem = 2,
            ByNone = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceSubConfig
        {
            public int format;
            public uint channels;
            public IntPtr pDeviceID;
            public int shareMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceConfig
        {
            public uint periodSizeInFrames;
            public uint periodSizeInMilliseconds;
            public uint periods;
            public byte noPreSilencedOutputBuffer;
            public byte noClip;
            public byte noDisableDenormals;
            public byte noFixedSizedCallback;
            public IntPtr playback;
            public IntPtr capture;
            public IntPtr wasapi;
            public IntPtr coreaudio;
            public IntPtr alsa;
            public IntPtr pulse;
            public IntPtr opensl;
            public IntPtr aaudio;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OpenSlConfig
        {
            public int streamType;
            public int recordingPreset;
            public uint enableCompatibilityWorkarounds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AAudioConfig
        {
            public int usage;
            public int contentType;
            public int inputPreset;
            public int allowedCapturePolicy;
            public uint noAutoStartAfterReroute;
            public uint enableCompatibilityWorkarounds;
            public uint allowSetBufferCapacity;
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

        internal static IntPtr CreateCaptureLegacyConfig(
            Native.SampleFormat format,
            uint channels,
            uint sampleRate,
            IntPtr deviceIdHandle,
            out GCHandle subHandle,
            out GCHandle dtoHandle,
            out GCHandle openSlHandle,
            out GCHandle aaudioHandle)
        {
            var capture = new DeviceSubConfig
            {
                format = (int)format,
                channels = channels,
                pDeviceID = deviceIdHandle,
                shareMode = 0
            };

            subHandle = GCHandle.Alloc(capture, GCHandleType.Pinned);
            return CreateLegacyConfigCore(
                sampleRate,
                IntPtr.Zero,
                subHandle.AddrOfPinnedObject(),
                out dtoHandle,
                out openSlHandle,
                out aaudioHandle,
                configurePlayback: false);
        }

        internal static IntPtr CreatePlaybackLegacyConfig(
            Native.SampleFormat format,
            uint channels,
            uint sampleRate,
            out GCHandle subHandle,
            out GCHandle dtoHandle,
            out GCHandle openSlHandle,
            out GCHandle aaudioHandle)
        {
            var playback = new DeviceSubConfig
            {
                format = (int)format,
                channels = channels,
                pDeviceID = IntPtr.Zero,
                shareMode = 0
            };

            subHandle = GCHandle.Alloc(playback, GCHandleType.Pinned);
            return CreateLegacyConfigCore(
                sampleRate,
                subHandle.AddrOfPinnedObject(),
                IntPtr.Zero,
                out dtoHandle,
                out openSlHandle,
                out aaudioHandle,
                configurePlayback: true);
        }

        private static IntPtr CreateLegacyConfigCore(
            uint sampleRate,
            IntPtr playbackConfig,
            IntPtr captureConfig,
            out GCHandle dtoHandle,
            out GCHandle openSlHandle,
            out GCHandle aaudioHandle,
            bool configurePlayback)
        {
            var openSl = new OpenSlConfig
            {
                streamType = configurePlayback ? (int)OpenSlStreamType.Voice : (int)OpenSlStreamType.Default,
                recordingPreset = configurePlayback ? (int)OpenSlRecordingPreset.Default : (int)OpenSlRecordingPreset.VoiceCommunication,
                enableCompatibilityWorkarounds = 1
            };
            openSlHandle = GCHandle.Alloc(openSl, GCHandleType.Pinned);

            var aaudio = new AAudioConfig
            {
                usage = (int)AAudioUsage.VoiceCommunication,
                contentType = (int)(configurePlayback ? AAudioContentType.Speech : AAudioContentType.Default),
                inputPreset = (int)(configurePlayback ? AAudioInputPreset.Default : AAudioInputPreset.VoiceCommunication),
                allowedCapturePolicy = (int)AAudioAllowedCapturePolicy.ByAll,
                noAutoStartAfterReroute = 0,
                enableCompatibilityWorkarounds = 1,
                allowSetBufferCapacity = 1
            };
            aaudioHandle = GCHandle.Alloc(aaudio, GCHandleType.Pinned);

            var dto = new DeviceConfig
            {
                periodSizeInFrames = GetPeriodSizeInFrames(sampleRate),
                periodSizeInMilliseconds = VoiceCommunicationPeriodMs,
                periods = VoiceCommunicationPeriods,
                noPreSilencedOutputBuffer = 0,
                noClip = 0,
                noDisableDenormals = 0,
                noFixedSizedCallback = 0,
                playback = playbackConfig,
                capture = captureConfig,
                wasapi = IntPtr.Zero,
                coreaudio = IntPtr.Zero,
                alsa = IntPtr.Zero,
                pulse = IntPtr.Zero,
                opensl = openSlHandle.AddrOfPinnedObject(),
                aaudio = aaudioHandle.AddrOfPinnedObject()
            };

            dtoHandle = GCHandle.Alloc(dto, GCHandleType.Pinned);
            return dtoHandle.AddrOfPinnedObject();
        }

        private static uint GetPeriodSizeInFrames(uint sampleRate)
        {
            uint fallback = sampleRate > 0 ? sampleRate / 100u : 0u;
            return Math.Max(80u, fallback);
        }
    }
}
