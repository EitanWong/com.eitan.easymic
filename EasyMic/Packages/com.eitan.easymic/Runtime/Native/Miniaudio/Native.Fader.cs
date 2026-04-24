using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FaderConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FaderState
        {
            public FaderConfig Config;
            public float VolumeBeg;
            public float VolumeEnd;
            public ulong LengthInFrames;
            public long CursorInFrames;
        }

        [DllImport(LibraryName, EntryPoint = "ma_fader_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern FaderConfig FaderConfigInit(SampleFormat format, uint channels, uint sampleRate);

        [DllImport(LibraryName, EntryPoint = "ma_fader_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result FaderInit(ref FaderConfig config, IntPtr fader);

        [DllImport(LibraryName, EntryPoint = "ma_fader_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result FaderProcessPcmFrames(IntPtr fader, IntPtr pFramesOut, IntPtr pFramesIn, ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_fader_set_fade", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FaderSetFadeNative(IntPtr fader, float volumeBeg, float volumeEnd, ulong lengthInFrames);

        [DllImport(LibraryName, EntryPoint = "ma_fader_set_fade_ex", CallingConvention = CallingConvention.Cdecl)]
        private static extern void FaderSetFadeExNative(IntPtr fader, float volumeBeg, float volumeEnd, ulong lengthInFrames, long startOffsetInFrames);

        [DllImport(LibraryName, EntryPoint = "ma_fader_get_current_volume", CallingConvention = CallingConvention.Cdecl)]
        private static extern float FaderGetCurrentVolumeNative(IntPtr fader);

        internal readonly struct FaderHandle : IDisposable
        {
            private readonly IntPtr _faderPtr;
            private readonly int _channels;
            internal bool IsValid => _faderPtr != IntPtr.Zero;

            private FaderHandle(IntPtr faderPtr, int channels)
            {
                _faderPtr = faderPtr;
                _channels = channels;
            }

            internal static bool TryCreate(int channels, int sampleRate, out FaderHandle handle)
            {
                handle = default;
                if (channels <= 0 || sampleRate <= 0) return false;
                IntPtr fader = IntPtr.Zero;
                try
                {
                    fader = Marshal.AllocHGlobal(Marshal.SizeOf<FaderState>());
                    var cfg = FaderConfigInit(SampleFormat.F32, (uint)channels, (uint)sampleRate);
                    if (FaderInit(ref cfg, fader) != Result.Success)
                    {
                        Marshal.FreeHGlobal(fader);
                        return false;
                    }

                    handle = new FaderHandle(fader, channels);
                    return true;
                }
                catch
                {
                    if (fader != IntPtr.Zero) Marshal.FreeHGlobal(fader);
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref FaderHandle handle, Span<float> interleaved, int frameCount)
            {
                if (!handle.IsValid || frameCount <= 0) return false;
                int maxFrames = interleaved.Length / Math.Max(1, handle._channels);
                if (frameCount > maxFrames) frameCount = maxFrames;
                fixed (float* p = interleaved)
                {
                    return FaderProcessPcmFrames(handle._faderPtr, (IntPtr)p, (IntPtr)p, (ulong)frameCount) == Result.Success;
                }
            }

            internal static void SetFade(ref FaderHandle handle, float volumeBeg, float volumeEnd, ulong lengthInFrames)
            {
                if (handle.IsValid) FaderSetFadeNative(handle._faderPtr, volumeBeg, volumeEnd, lengthInFrames);
            }

            internal static void SetFadeEx(ref FaderHandle handle, float volumeBeg, float volumeEnd, ulong lengthInFrames, long startOffsetInFrames)
            {
                if (handle.IsValid) FaderSetFadeExNative(handle._faderPtr, volumeBeg, volumeEnd, lengthInFrames, startOffsetInFrames);
            }

            internal static float GetCurrentVolume(ref FaderHandle handle)
            {
                return handle.IsValid ? FaderGetCurrentVolumeNative(handle._faderPtr) : 1f;
            }

            public void Dispose()
            {
                if (!IsValid) return;
                Marshal.FreeHGlobal(_faderPtr);
            }
        }
    }
}
