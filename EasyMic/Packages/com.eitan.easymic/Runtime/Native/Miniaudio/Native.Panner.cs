using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        internal enum PanMode
        {
            Balance = 0,
            Pan = 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PannerConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public PanMode Mode;
            public float Pan;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PannerState
        {
            public SampleFormat Format;
            public uint Channels;
            public PanMode Mode;
            public float Pan;
        }

        [DllImport(LibraryName, EntryPoint = "ma_panner_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern PannerConfig PannerConfigInit(SampleFormat format, uint channels);

        [DllImport(LibraryName, EntryPoint = "ma_panner_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result PannerInit(ref PannerConfig config, IntPtr panner);

        [DllImport(LibraryName, EntryPoint = "ma_panner_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result PannerProcessPcmFrames(IntPtr panner, IntPtr pFramesOut, IntPtr pFramesIn, ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_panner_set_mode", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PannerSetModeNative(IntPtr panner, PanMode mode);

        [DllImport(LibraryName, EntryPoint = "ma_panner_set_pan", CallingConvention = CallingConvention.Cdecl)]
        private static extern void PannerSetPanNative(IntPtr panner, float pan);

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
                if (channels <= 0) return false;
                IntPtr panner = IntPtr.Zero;
                try
                {
                    panner = Marshal.AllocHGlobal(Marshal.SizeOf<PannerState>());
                    var cfg = PannerConfigInit(SampleFormat.F32, (uint)channels);
                    cfg.Mode = mode;
                    cfg.Pan = pan;
                    if (PannerInit(ref cfg, panner) != Result.Success)
                    {
                        Marshal.FreeHGlobal(panner);
                        return false;
                    }

                    handle = new PannerHandle(panner, channels);
                    return true;
                }
                catch
                {
                    if (panner != IntPtr.Zero) Marshal.FreeHGlobal(panner);
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref PannerHandle handle, Span<float> interleaved, int frameCount)
            {
                if (!handle.IsValid || frameCount <= 0) return false;
                int maxFrames = interleaved.Length / Math.Max(1, handle._channels);
                if (frameCount > maxFrames) frameCount = maxFrames;
                fixed (float* p = interleaved)
                {
                    return PannerProcessPcmFrames(handle._pannerPtr, (IntPtr)p, (IntPtr)p, (ulong)frameCount) == Result.Success;
                }
            }

            internal static void SetMode(ref PannerHandle handle, PanMode mode)
            {
                if (handle.IsValid) PannerSetModeNative(handle._pannerPtr, mode);
            }

            internal static void SetPan(ref PannerHandle handle, float pan)
            {
                if (!handle.IsValid) return;
                if (pan < -1f) pan = -1f;
                else if (pan > 1f) pan = 1f;
                PannerSetPanNative(handle._pannerPtr, pan);
            }

            public void Dispose()
            {
                if (!IsValid) return;
                Marshal.FreeHGlobal(_pannerPtr);
            }
        }
    }
}
