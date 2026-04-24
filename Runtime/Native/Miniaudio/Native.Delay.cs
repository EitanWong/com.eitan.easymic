using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DelayConfig
        {
            public uint Channels;
            public uint SampleRate;
            public uint DelayInFrames;
            public int DelayStart;
            public float Wet;
            public float Dry;
            public float Decay;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DelayState
        {
            public DelayConfig Config;
            public uint Cursor;
            public uint BufferSizeInFrames;
            public IntPtr Buffer;
        }

        [DllImport(LibraryName, EntryPoint = "ma_delay_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern DelayConfig DelayConfigInit(uint channels, uint sampleRate, uint delayInFrames, float decay);

        [DllImport(LibraryName, EntryPoint = "ma_delay_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result DelayInit(ref DelayConfig config, IntPtr allocationCallbacks, IntPtr delay);

        [DllImport(LibraryName, EntryPoint = "ma_delay_uninit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DelayUninit(IntPtr delay, IntPtr allocationCallbacks);

        [DllImport(LibraryName, EntryPoint = "ma_delay_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result DelayProcessPcmFrames(IntPtr delay, IntPtr pFramesOut, IntPtr pFramesIn, uint frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_delay_set_wet", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DelaySetWetNative(IntPtr delay, float value);

        [DllImport(LibraryName, EntryPoint = "ma_delay_set_dry", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DelaySetDryNative(IntPtr delay, float value);

        [DllImport(LibraryName, EntryPoint = "ma_delay_set_decay", CallingConvention = CallingConvention.Cdecl)]
        private static extern void DelaySetDecayNative(IntPtr delay, float value);

        internal readonly struct DelayHandle : IDisposable
        {
            private readonly IntPtr _delayPtr;
            private readonly int _channels;
            internal bool IsValid => _delayPtr != IntPtr.Zero;

            private DelayHandle(IntPtr delayPtr, int channels)
            {
                _delayPtr = delayPtr;
                _channels = channels;
            }

            internal static bool TryCreate(int channels, int sampleRate, uint delayInFrames, float decay, bool delayStart, float wet, float dry, out DelayHandle handle)
            {
                handle = default;
                if (channels <= 0 || sampleRate <= 0) return false;
                IntPtr delay = IntPtr.Zero;
                try
                {
                    delay = Marshal.AllocHGlobal(Marshal.SizeOf<DelayState>());
                    var cfg = DelayConfigInit((uint)channels, (uint)sampleRate, delayInFrames, decay);
                    cfg.DelayStart = delayStart ? 1 : 0;
                    cfg.Wet = wet;
                    cfg.Dry = dry;
                    cfg.Decay = decay;
                    if (DelayInit(ref cfg, IntPtr.Zero, delay) != Result.Success)
                    {
                        Marshal.FreeHGlobal(delay);
                        return false;
                    }

                    DelaySetWetNative(delay, wet);
                    DelaySetDryNative(delay, dry);
                    DelaySetDecayNative(delay, decay);

                    handle = new DelayHandle(delay, channels);
                    return true;
                }
                catch
                {
                    if (delay != IntPtr.Zero) Marshal.FreeHGlobal(delay);
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref DelayHandle handle, Span<float> interleaved, int frameCount)
            {
                if (!handle.IsValid || frameCount <= 0) return false;
                int maxFrames = interleaved.Length / Math.Max(1, handle._channels);
                if (frameCount > maxFrames) frameCount = maxFrames;
                fixed (float* p = interleaved)
                {
                    return DelayProcessPcmFrames(handle._delayPtr, (IntPtr)p, (IntPtr)p, (uint)frameCount) == Result.Success;
                }
            }

            internal static void SetWet(ref DelayHandle handle, float wet)
            {
                if (handle.IsValid) DelaySetWetNative(handle._delayPtr, Clamp01(wet));
            }

            internal static void SetDry(ref DelayHandle handle, float dry)
            {
                if (handle.IsValid) DelaySetDryNative(handle._delayPtr, Clamp01(dry));
            }

            internal static void SetDecay(ref DelayHandle handle, float decay)
            {
                if (handle.IsValid) DelaySetDecayNative(handle._delayPtr, Clamp01(decay));
            }

            public void Dispose()
            {
                if (!IsValid) return;
                DelayUninit(_delayPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_delayPtr);
            }

            private static float Clamp01(float value)
            {
                if (value < 0f) return 0f;
                if (value > 1f) return 1f;
                return value;
            }
        }
    }
}
