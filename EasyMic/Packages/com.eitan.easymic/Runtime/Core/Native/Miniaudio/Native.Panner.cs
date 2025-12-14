using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        // Oversized to remain ABI-safe across miniaudio versions/platforms. Over-allocation is safe.
        internal const int PannerStructSizeBytes = 64;

        internal enum PanMode
        {
            Balance = 0,
            Pan = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PannerConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public PanMode Mode;
            public float Pan;
        }

        [DllImport(LibraryName, EntryPoint = "ma_panner_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern PannerConfig PannerConfigInit(SampleFormat format, uint channels);

        [DllImport(LibraryName, EntryPoint = "ma_panner_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result PannerInit(ref PannerConfig config, IntPtr panner);

        [DllImport(LibraryName, EntryPoint = "ma_panner_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result PannerProcessPcmFrames(IntPtr panner, IntPtr pFramesOut, IntPtr pFramesIn, ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_panner_set_mode", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PannerSetMode(IntPtr panner, PanMode mode);

        [DllImport(LibraryName, EntryPoint = "ma_panner_set_pan", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void PannerSetPan(IntPtr panner, float pan);

        internal readonly struct PannerHandle : IDisposable
        {
            private readonly IntPtr _pannerPtr;
            private readonly int _channels;

            internal bool IsValid => _pannerPtr != IntPtr.Zero;

            private PannerHandle(IntPtr pannerPtr, int channels)
            {
                _pannerPtr = pannerPtr;
                _channels = channels;
            }

            internal static bool TryCreate(int channels, PanMode mode, float pan, out PannerHandle handle)
            {
                handle = default;
                if (channels <= 0)
                {
                    return false;
                }

                IntPtr panner = IntPtr.Zero;
                try
                {
                    panner = Marshal.AllocHGlobal(PannerStructSizeBytes);
                    var cfg = PannerConfigInit(SampleFormat.F32, (uint)channels);
                    cfg.Mode = mode;
                    cfg.Pan = pan;

                    var result = PannerInit(ref cfg, panner);
                    if (result != Result.Success)
                    {
                        Marshal.FreeHGlobal(panner);
                        return false;
                    }

                    handle = new PannerHandle(panner, channels);
                    return true;
                }
                catch
                {
                    if (panner != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(panner);
                    }
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref PannerHandle handle, Span<float> interleaved, int frameCount)
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
                        var result = PannerProcessPcmFrames(handle._pannerPtr, (IntPtr)p, (IntPtr)p, (ulong)frameCount);
                        return result == Result.Success;
                    }
                }
            }

            internal static void SetMode(ref PannerHandle handle, PanMode mode)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                PannerSetMode(handle._pannerPtr, mode);
            }

            internal static void SetPan(ref PannerHandle handle, float pan)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                if (pan < -1f)
                {
                    pan = -1f;
                }
                else if (pan > 1f)
                {
                    pan = 1f;
                }

                PannerSetPan(handle._pannerPtr, pan);
            }

            public void Dispose()
            {
                if (!IsValid)
                {
                    return;
                }

                Marshal.FreeHGlobal(_pannerPtr);
            }
        }
    }
}

