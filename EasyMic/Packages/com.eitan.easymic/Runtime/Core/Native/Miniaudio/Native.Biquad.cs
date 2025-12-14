using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        // Oversized to remain ABI-safe across miniaudio versions/platforms. Over-allocation is safe.
        internal const int BiquadStructSizeBytes = 512;

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

        [DllImport(LibraryName, EntryPoint = "ma_biquad_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern BiquadConfig BiquadConfigInit(
            SampleFormat format,
            uint channels,
            double b0,
            double b1,
            double b2,
            double a0,
            double a1,
            double a2);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_get_heap_size", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result BiquadGetHeapSize(ref BiquadConfig config, out UIntPtr heapSizeInBytes);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_init_preallocated", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result BiquadInitPreallocated(ref BiquadConfig config, IntPtr heap, IntPtr biquad);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result BiquadProcessPcmFrames(IntPtr biquad, IntPtr pFramesOut, IntPtr pFramesIn, ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_biquad_uninit", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void BiquadUninit(IntPtr biquad, IntPtr allocationCallbacks);

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

            internal static bool TryCreate(in BiquadConfig config, out BiquadHandle handle)
            {
                handle = default;

                var cfg = config;
                if (BiquadGetHeapSize(ref cfg, out var heapSize) != Result.Success)
                {
                    return false;
                }

                var heapSizeBytes = heapSize.ToUInt64();
                if (heapSizeBytes == 0 || heapSizeBytes > 4UL * 1024UL * 1024UL)
                {
                    return false;
                }

                IntPtr heap = IntPtr.Zero;
                IntPtr biquad = IntPtr.Zero;

                try
                {
                    heap = Marshal.AllocHGlobal(checked((IntPtr)(long)heapSizeBytes));
                    biquad = Marshal.AllocHGlobal(BiquadStructSizeBytes);

                    var initResult = BiquadInitPreallocated(ref cfg, heap, biquad);
                    if (initResult != Result.Success)
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
                    if (heap != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(heap);
                    }

                    if (biquad != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(biquad);
                    }

                    return false;
                }
            }

            internal static bool ProcessInPlace(ref BiquadHandle handle, Span<float> interleaved, int frames)
            {
                if (!handle.IsValid || frames <= 0 || interleaved.IsEmpty)
                {
                    return false;
                }

                unsafe
                {
                    fixed (float* p = interleaved)
                    {
                        var r = BiquadProcessPcmFrames(handle._biquadPtr, (IntPtr)p, (IntPtr)p, (ulong)frames);
                        return r == Result.Success;
                    }
                }
            }

            public void Dispose()
            {
                if (!IsValid)
                {
                    return;
                }

                BiquadUninit(_biquadPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_biquadPtr);
                Marshal.FreeHGlobal(_heapPtr);
            }
        }
    }
}
