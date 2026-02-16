using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        // Oversized to remain ABI-safe across miniaudio versions/platforms. Over-allocation is safe.
        internal const int DelayStructSizeBytes = 256;

        [StructLayout(LayoutKind.Sequential)]
        internal struct DelayConfig
        {
            public uint Channels;
            public uint SampleRate;
            public uint DelayInFrames;
            public int DelayStart; // ma_bool32
            public float Wet;
            public float Dry;
            public float Decay;
        }

        [DllImport(LibraryName, EntryPoint = "ma_delay_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern DelayConfig DelayConfigInit(uint channels, uint sampleRate, uint delayInFrames, float decay);

        [DllImport(LibraryName, EntryPoint = "ma_delay_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result DelayInit(ref DelayConfig config, IntPtr allocationCallbacks, IntPtr delay);

        [DllImport(LibraryName, EntryPoint = "ma_delay_uninit", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DelayUninit(IntPtr delay, IntPtr allocationCallbacks);

        [DllImport(LibraryName, EntryPoint = "ma_delay_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result DelayProcessPcmFrames(IntPtr delay, IntPtr pFramesOut, IntPtr pFramesIn, uint frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_delay_set_wet", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DelaySetWet(IntPtr delay, float value);

        [DllImport(LibraryName, EntryPoint = "ma_delay_set_dry", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DelaySetDry(IntPtr delay, float value);

        [DllImport(LibraryName, EntryPoint = "ma_delay_set_decay", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void DelaySetDecay(IntPtr delay, float value);

        /// <summary>
        /// miniaudio ma_delay handle.
        /// miniaudio 的 ma_delay 句柄。
        ///
        /// Note: ma_delay allocates its internal buffer during init (not RT thread).
        /// 注意：ma_delay 会在初始化阶段分配内部缓冲区（非音频线程）。
        /// </summary>
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

            internal static bool TryCreate(
                int channels,
                int sampleRate,
                uint delayInFrames,
                float decay,
                bool delayStart,
                float wet,
                float dry,
                out DelayHandle handle)
            {
                handle = default;
                if (channels <= 0 || sampleRate <= 0)
                {
                    return false;
                }

                IntPtr delay = IntPtr.Zero;
                try
                {
                    delay = Marshal.AllocHGlobal(DelayStructSizeBytes);

                    var cfg = DelayConfigInit((uint)channels, (uint)sampleRate, delayInFrames, decay);
                    cfg.DelayStart = delayStart ? 1 : 0;
                    cfg.Wet = wet;
                    cfg.Dry = dry;
                    cfg.Decay = decay;

                    var result = DelayInit(ref cfg, IntPtr.Zero, delay);
                    if (result != Result.Success)
                    {
                        Marshal.FreeHGlobal(delay);
                        return false;
                    }

                    // Apply wet/dry/decay to ensure config matches runtime values.
                    DelaySetWet(delay, wet);
                    DelaySetDry(delay, dry);
                    DelaySetDecay(delay, decay);

                    handle = new DelayHandle(delay, channels);
                    return true;
                }
                catch
                {
                    if (delay != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(delay);
                    }

                    return false;
                }
            }

            internal static bool ProcessInPlace(ref DelayHandle handle, Span<float> interleaved, int frameCount)
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
                        var result = DelayProcessPcmFrames(handle._delayPtr, (IntPtr)p, (IntPtr)p, (uint)frameCount);
                        return result == Result.Success;
                    }
                }
            }

            internal static void SetWet(ref DelayHandle handle, float wet)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                if (wet < 0f) wet = 0f;
                else if (wet > 1f) wet = 1f;
                DelaySetWet(handle._delayPtr, wet);
            }

            internal static void SetDry(ref DelayHandle handle, float dry)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                if (dry < 0f) dry = 0f;
                else if (dry > 1f) dry = 1f;
                DelaySetDry(handle._delayPtr, dry);
            }

            internal static void SetDecay(ref DelayHandle handle, float decay)
            {
                if (!handle.IsValid)
                {
                    return;
                }

                if (decay < 0f) decay = 0f;
                else if (decay > 1f) decay = 1f;
                DelaySetDecay(handle._delayPtr, decay);
            }

            public void Dispose()
            {
                if (!IsValid)
                {
                    return;
                }

                DelayUninit(_delayPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_delayPtr);
            }
        }
    }
}

