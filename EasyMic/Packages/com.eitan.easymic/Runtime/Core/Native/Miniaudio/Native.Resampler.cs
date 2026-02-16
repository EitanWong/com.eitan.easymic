using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        // Oversized to remain ABI-safe across miniaudio versions/platforms. Over-allocation is safe.
        internal const int LinearResamplerStructSizeBytes = 1024;

        [StructLayout(LayoutKind.Sequential)]
        internal struct LinearResamplerConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRateIn;
            public uint SampleRateOut;
            public uint LpfOrder;
            public double LpfNyquistFactor;
        }

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern LinearResamplerConfig LinearResamplerConfigInit(
            SampleFormat format,
            uint channels,
            uint sampleRateIn,
            uint sampleRateOut);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_get_heap_size", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result LinearResamplerGetHeapSize(ref LinearResamplerConfig config, out UIntPtr heapSizeInBytes);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_init_preallocated", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result LinearResamplerInitPreallocated(ref LinearResamplerConfig config, IntPtr heap, IntPtr resampler);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result LinearResamplerProcessPcmFrames(
            IntPtr resampler,
            IntPtr pFramesIn,
            ref ulong pFrameCountIn,
            IntPtr pFramesOut,
            ref ulong pFrameCountOut);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_get_expected_output_frame_count", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result LinearResamplerGetExpectedOutputFrameCount(
            IntPtr resampler,
            ulong frameCountIn,
            out ulong frameCountOut);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_uninit", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void LinearResamplerUninit(IntPtr resampler, IntPtr allocationCallbacks);

        internal readonly struct LinearResamplerHandle : IDisposable
        {
            private readonly IntPtr _resamplerPtr;
            private readonly IntPtr _heapPtr;
            private readonly int _channels;

            internal bool IsValid => _resamplerPtr != IntPtr.Zero;

            private LinearResamplerHandle(IntPtr resamplerPtr, IntPtr heapPtr, int channels)
            {
                _resamplerPtr = resamplerPtr;
                _heapPtr = heapPtr;
                _channels = channels;
            }

            internal static bool TryCreate(
                int channels,
                int sampleRateIn,
                int sampleRateOut,
                uint lpfOrder,
                float nyquistFactor,
                out LinearResamplerHandle handle)
            {
                handle = default;
                if (channels <= 0 || sampleRateIn <= 0 || sampleRateOut <= 0)
                {
                    return false;
                }

                var cfg = LinearResamplerConfigInit(
                    SampleFormat.F32,
                    (uint)channels,
                    (uint)sampleRateIn,
                    (uint)sampleRateOut);
                cfg.LpfOrder = lpfOrder;
                double clampedNyquist = nyquistFactor;
                if (clampedNyquist < 0.1d)
                {
                    clampedNyquist = 0.1d;
                }
                else if (clampedNyquist > 1d)
                {
                    clampedNyquist = 1d;
                }
                cfg.LpfNyquistFactor = clampedNyquist;

                if (LinearResamplerGetHeapSize(ref cfg, out var heapSize) != Result.Success)
                {
                    return false;
                }

                var heapSizeBytes = heapSize.ToUInt64();
                if (heapSizeBytes == 0 || heapSizeBytes > 16UL * 1024UL * 1024UL)
                {
                    return false;
                }

                IntPtr heap = IntPtr.Zero;
                IntPtr resampler = IntPtr.Zero;

                try
                {
                    heap = Marshal.AllocHGlobal(checked((IntPtr)(long)heapSizeBytes));
                    resampler = Marshal.AllocHGlobal(LinearResamplerStructSizeBytes);

                    var initResult = LinearResamplerInitPreallocated(ref cfg, heap, resampler);
                    if (initResult != Result.Success)
                    {
                        Marshal.FreeHGlobal(heap);
                        Marshal.FreeHGlobal(resampler);
                        return false;
                    }

                    handle = new LinearResamplerHandle(resampler, heap, channels);
                    return true;
                }
                catch
                {
                    if (heap != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(heap);
                    }

                    if (resampler != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(resampler);
                    }

                    return false;
                }
            }

            internal static int EstimateOutputFrames(ref LinearResamplerHandle handle, int framesIn)
            {
                if (!handle.IsValid || framesIn <= 0)
                {
                    return 0;
                }

                if (LinearResamplerGetExpectedOutputFrameCount(handle._resamplerPtr, (ulong)framesIn, out var outFrames) != Result.Success)
                {
                    return (int)Math.Ceiling(framesIn * 0.34);
                }

                return (int)Math.Min((ulong)int.MaxValue, outFrames);
            }

            internal static int Process(ref LinearResamplerHandle handle, Span<float> input, int framesIn, float[] scratchOut)
            {
                if (!handle.IsValid || framesIn <= 0)
                {
                    return 0;
                }

                ulong inFrames = (ulong)framesIn;
                ulong outFrames = (ulong)(scratchOut.Length / Math.Max(1, handle._channels));

                unsafe
                {
                    fixed (float* pIn = input)
                    fixed (float* pOut = scratchOut)
                    {
                        var result = LinearResamplerProcessPcmFrames(
                            handle._resamplerPtr,
                            (IntPtr)pIn,
                            ref inFrames,
                            (IntPtr)pOut,
                            ref outFrames);

                        if (result != Result.Success)
                        {
                            return 0;
                        }
                    }
                }

                return (int)outFrames;
            }

            public void Dispose()
            {
                if (!IsValid)
                {
                    return;
                }

                LinearResamplerUninit(_resamplerPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_resamplerPtr);
                Marshal.FreeHGlobal(_heapPtr);
            }
        }
    }
}
