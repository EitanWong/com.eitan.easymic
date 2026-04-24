using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LinearResamplerConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRateIn;
            public uint SampleRateOut;
            public uint LpfOrder;
            public double LpfNyquistFactor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LpfState
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRate;
            public uint Lpf1Count;
            public uint Lpf2Count;
            public IntPtr Lpf1;
            public IntPtr Lpf2;
            public IntPtr Heap;
            public int OwnsHeap;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LinearResamplerState
        {
            public LinearResamplerConfig Config;
            public uint InAdvanceInt;
            public uint InAdvanceFrac;
            public uint InTimeInt;
            public uint InTimeFrac;
            public IntPtr X0;
            public IntPtr X1;
            public LpfState Lpf;
            public IntPtr Heap;
            public int OwnsHeap;
        }

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern LinearResamplerConfig LinearResamplerConfigInit(SampleFormat format, uint channels, uint sampleRateIn, uint sampleRateOut);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_get_heap_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result LinearResamplerGetHeapSize(ref LinearResamplerConfig config, out UIntPtr heapSizeInBytes);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_init_preallocated", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result LinearResamplerInitPreallocated(ref LinearResamplerConfig config, IntPtr heap, IntPtr resampler);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result LinearResamplerProcessPcmFrames(IntPtr resampler, IntPtr pFramesIn, ref ulong pFrameCountIn, IntPtr pFramesOut, ref ulong pFrameCountOut);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_get_expected_output_frame_count", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result LinearResamplerGetExpectedOutputFrameCount(IntPtr resampler, ulong frameCountIn, out ulong frameCountOut);

        [DllImport(LibraryName, EntryPoint = "ma_linear_resampler_uninit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void LinearResamplerUninit(IntPtr resampler, IntPtr allocationCallbacks);

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

            internal static bool TryCreate(int channels, int sampleRateIn, int sampleRateOut, uint lpfOrder, float nyquistFactor, out LinearResamplerHandle handle)
            {
                handle = default;
                if (channels <= 0 || sampleRateIn <= 0 || sampleRateOut <= 0) return false;
                var cfg = LinearResamplerConfigInit(SampleFormat.F32, (uint)channels, (uint)sampleRateIn, (uint)sampleRateOut);
                cfg.LpfOrder = lpfOrder;
                double clamped = nyquistFactor;
                if (clamped < 0.1d) clamped = 0.1d;
                else if (clamped > 1d) clamped = 1d;
                cfg.LpfNyquistFactor = clamped;

                if (LinearResamplerGetHeapSize(ref cfg, out var heapSize) != Result.Success) return false;
                ulong heapBytes = heapSize.ToUInt64();
                if (heapBytes == 0 || heapBytes > 16UL * 1024UL * 1024UL) return false;

                IntPtr heap = IntPtr.Zero;
                IntPtr resampler = IntPtr.Zero;
                try
                {
                    heap = Marshal.AllocHGlobal(checked((IntPtr)(long)heapBytes));
                    resampler = Marshal.AllocHGlobal(Marshal.SizeOf<LinearResamplerState>());
                    if (LinearResamplerInitPreallocated(ref cfg, heap, resampler) != Result.Success)
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
                    if (heap != IntPtr.Zero) Marshal.FreeHGlobal(heap);
                    if (resampler != IntPtr.Zero) Marshal.FreeHGlobal(resampler);
                    return false;
                }
            }

            internal static int EstimateOutputFrames(ref LinearResamplerHandle handle, int framesIn)
            {
                if (!handle.IsValid || framesIn <= 0) return 0;
                return LinearResamplerGetExpectedOutputFrameCount(handle._resamplerPtr, (ulong)framesIn, out var outFrames) == Result.Success
                    ? (int)Math.Min((ulong)int.MaxValue, outFrames)
                    : 0;
            }

            internal static int Process(ref LinearResamplerHandle handle, Span<float> input, int framesIn, float[] scratchOut)
            {
                if (!handle.IsValid || framesIn <= 0 || scratchOut == null) return 0;
                ulong inFrames = (ulong)framesIn;
                ulong outFrames = (ulong)(scratchOut.Length / Math.Max(1, handle._channels));
                fixed (float* pIn = input)
                fixed (float* pOut = scratchOut)
                {
                    return LinearResamplerProcessPcmFrames(handle._resamplerPtr, (IntPtr)pIn, ref inFrames, (IntPtr)pOut, ref outFrames) == Result.Success
                        ? (int)outFrames
                        : 0;
                }
            }

            public void Dispose()
            {
                if (!IsValid) return;
                LinearResamplerUninit(_resamplerPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_resamplerPtr);
                Marshal.FreeHGlobal(_heapPtr);
            }
        }
    }
}
