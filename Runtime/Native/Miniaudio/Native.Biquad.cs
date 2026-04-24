using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct BiquadConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public double B0;
            public double B1;
            public double B2;
            public double A0;
            public double A1;
            public double A2;
        }

        [StructLayout(LayoutKind.Explicit, Size = 4)]
        private struct BiquadCoefficient { }

        [StructLayout(LayoutKind.Sequential)]
        private struct BiquadState
        {
            public SampleFormat Format;
            public uint Channels;
            public BiquadCoefficient B0;
            public BiquadCoefficient B1;
            public BiquadCoefficient B2;
            public BiquadCoefficient A1;
            public BiquadCoefficient A2;
            public IntPtr R1;
            public IntPtr R2;
            public IntPtr Heap;
            public int OwnsHeap;
        }

        [DllImport(LibraryName, EntryPoint = "ma_biquad_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern BiquadConfig BiquadConfigInit(SampleFormat format, uint channels, double b0, double b1, double b2, double a0, double a1, double a2);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_get_heap_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result BiquadGetHeapSize(ref BiquadConfig config, out UIntPtr heapSizeInBytes);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_init_preallocated", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result BiquadInitPreallocated(ref BiquadConfig config, IntPtr heap, IntPtr biquad);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result BiquadProcessPcmFrames(IntPtr biquad, IntPtr pFramesOut, IntPtr pFramesIn, ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_uninit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void BiquadUninit(IntPtr biquad, IntPtr allocationCallbacks);

        internal readonly struct BiquadHandle : IDisposable
        {
            private readonly IntPtr _biquadPtr;
            private readonly IntPtr _heapPtr;
            internal bool IsValid => _biquadPtr != IntPtr.Zero;

            private BiquadHandle(IntPtr biquadPtr, IntPtr heapPtr)
            {
                _biquadPtr = biquadPtr;
                _heapPtr = heapPtr;
            }

            internal static bool TryCreate(SampleFormat format, uint channels, double b0, double b1, double b2, double a0, double a1, double a2, out BiquadHandle handle)
            {
                var cfg = BiquadConfigInit(format, channels, b0, b1, b2, a0, a1, a2);
                return TryCreate(in cfg, out handle);
            }

            private static bool TryCreate(in BiquadConfig config, out BiquadHandle handle)
            {
                handle = default;
                var cfg = config;
                if (BiquadGetHeapSize(ref cfg, out var heapSize) != Result.Success) return false;
                ulong heapBytes = heapSize.ToUInt64();
                if (heapBytes == 0 || heapBytes > 4UL * 1024UL * 1024UL) return false;

                IntPtr heap = IntPtr.Zero;
                IntPtr biquad = IntPtr.Zero;
                try
                {
                    heap = Marshal.AllocHGlobal(checked((IntPtr)(long)heapBytes));
                    biquad = Marshal.AllocHGlobal(Marshal.SizeOf<BiquadState>());
                    if (BiquadInitPreallocated(ref cfg, heap, biquad) != Result.Success)
                    {
                        Marshal.FreeHGlobal(heap);
                        Marshal.FreeHGlobal(biquad);
                        return false;
                    }

                    handle = new BiquadHandle(biquad, heap);
                    return true;
                }
                catch
                {
                    if (heap != IntPtr.Zero) Marshal.FreeHGlobal(heap);
                    if (biquad != IntPtr.Zero) Marshal.FreeHGlobal(biquad);
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref BiquadHandle handle, Span<float> interleaved, int frames)
            {
                if (!handle.IsValid || frames <= 0 || interleaved.IsEmpty) return false;
                fixed (float* p = interleaved)
                {
                    return BiquadProcessPcmFrames(handle._biquadPtr, (IntPtr)p, (IntPtr)p, (ulong)frames) == Result.Success;
                }
            }

            public void Dispose()
            {
                if (!IsValid) return;
                BiquadUninit(_biquadPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_biquadPtr);
                Marshal.FreeHGlobal(_heapPtr);
            }
        }
    }
}
