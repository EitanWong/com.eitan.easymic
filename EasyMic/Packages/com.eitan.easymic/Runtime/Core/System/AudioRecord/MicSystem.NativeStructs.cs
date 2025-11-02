using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Sf_DeviceSubConfig
        {
            public int format;
            public uint channels;
            public IntPtr pDeviceID;
            public int shareMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Sf_DeviceConfig
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
    }
}
