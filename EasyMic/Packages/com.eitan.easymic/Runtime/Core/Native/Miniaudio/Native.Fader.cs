using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        // Oversized to remain ABI-safe across miniaudio versions/platforms. Over-allocation is safe.
        internal const int FaderStructSizeBytes = 128;

        [StructLayout(LayoutKind.Sequential)]
        internal struct FaderConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRate;
        }

        [DllImport(LibraryName, EntryPoint = "ma_fader_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern FaderConfig FaderConfigInit(SampleFormat format, uint channels, uint sampleRate);

        [DllImport(LibraryName, EntryPoint = "ma_fader_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result FaderInit(ref FaderConfig config, IntPtr fader);

        [DllImport(LibraryName, EntryPoint = "ma_fader_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result FaderProcessPcmFrames(
            IntPtr fader,
            IntPtr pFramesOut,
            IntPtr pFramesIn,
            ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_fader_set_fade", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FaderSetFade(IntPtr fader, float volumeBeg, float volumeEnd, ulong lengthInFrames);

        [DllImport(LibraryName, EntryPoint = "ma_fader_set_fade_ex", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FaderSetFadeEx(IntPtr fader, float volumeBeg, float volumeEnd, ulong lengthInFrames, long startOffsetInFrames);

        [DllImport(LibraryName, EntryPoint = "ma_fader_get_current_volume", CallingConvention = CallingConvention.Cdecl)]
        internal static extern float FaderGetCurrentVolume(IntPtr fader);

        /// <summary>
        /// miniaudio ma_fader handle.
        /// miniaudio 的 ma_fader 句柄。
        /// </summary>
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
                if (channels <= 0 || sampleRate <= 0)
                {
                    return false;
                }

                IntPtr fader = IntPtr.Zero;
                try
                {
                    fader = Marshal.AllocHGlobal(FaderStructSizeBytes);
                    var cfg = FaderConfigInit(SampleFormat.F32, (uint)channels, (uint)sampleRate);

                    var result = FaderInit(ref cfg, fader);
                    if (result != Result.Success)
                    {
                        Marshal.FreeHGlobal(fader);
                        return false;
                    }

                    handle = new FaderHandle(fader, channels);
                    return true;
                }
                catch
                {
                    if (fader != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(fader);
                    }
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref FaderHandle handle, Span<float> interleaved, int frameCount)
            {
                if (!handle.IsValid || frameCount <= 0)
                {
                    return false;
                }

                int maxFramesByBuffer = interleaved.Length / Math.Max(1, handle._channels);
                if (frameCount > maxFramesByBuffer)
                {
                    frameCount = maxFramesByBuffer;
                }

                unsafe
                {
                    fixed (float* p = interleaved)
                    {
                        var result = FaderProcessPcmFrames(handle._faderPtr, (IntPtr)p, (IntPtr)p, (ulong)frameCount);
                        return result == Result.Success;
                    }
                }
            }

            internal static void SetFade(ref FaderHandle handle, float volumeBeg, float volumeEnd, ulong lengthInFrames)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                FaderSetFade(handle._faderPtr, volumeBeg, volumeEnd, lengthInFrames);
            }

            internal static void SetFadeEx(ref FaderHandle handle, float volumeBeg, float volumeEnd, ulong lengthInFrames, long startOffsetInFrames)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                FaderSetFadeEx(handle._faderPtr, volumeBeg, volumeEnd, lengthInFrames, startOffsetInFrames);
            }

            internal static float GetCurrentVolume(ref FaderHandle handle)
            {
                if (!handle.IsValid)
                {
                    return 1f;
                }

                return FaderGetCurrentVolume(handle._faderPtr);
            }

            public void Dispose()
            {
                if (!IsValid)
                {
                    return;
                }

                Marshal.FreeHGlobal(_faderPtr);
            }
        }
    }
}

