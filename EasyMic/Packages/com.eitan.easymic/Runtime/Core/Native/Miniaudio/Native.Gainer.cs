using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        // Oversized to remain ABI-safe across miniaudio versions/platforms. Over-allocation is safe.
        internal const int GainerStructSizeBytes = 256;

        [StructLayout(LayoutKind.Sequential)]
        internal struct GainerConfig
        {
            public uint Channels;
            public uint SmoothTimeInFrames;
        }

        [DllImport(LibraryName, EntryPoint = "ma_gainer_config_init", CallingConvention = CallingConvention.Cdecl)]
        internal static extern GainerConfig GainerConfigInit(uint channels, uint smoothTimeInFrames);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_get_heap_size", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result GainerGetHeapSize(ref GainerConfig config, out UIntPtr heapSizeInBytes);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_init_preallocated", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result GainerInitPreallocated(ref GainerConfig config, IntPtr heap, IntPtr gainer);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_uninit", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GainerUninit(IntPtr gainer, IntPtr allocationCallbacks);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result GainerProcessPcmFrames(
            IntPtr gainer,
            IntPtr pFramesOut,
            IntPtr pFramesIn,
            ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_set_gain", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result GainerSetGain(IntPtr gainer, float newGain);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_set_master_volume", CallingConvention = CallingConvention.Cdecl)]
        internal static extern Result GainerSetMasterVolume(IntPtr gainer, float volume);

        /// <summary>
        /// miniaudio ma_gainer handle (preallocated heap).
        /// miniaudio 的 ma_gainer 句柄（预分配堆内存）。
        /// </summary>
        internal readonly struct GainerHandle : IDisposable
        {
            private readonly IntPtr _gainerPtr;
            private readonly IntPtr _heapPtr;
            private readonly int _channels;

            internal bool IsValid => _gainerPtr != IntPtr.Zero;

            private GainerHandle(IntPtr gainerPtr, IntPtr heapPtr, int channels)
            {
                _gainerPtr = gainerPtr;
                _heapPtr = heapPtr;
                _channels = channels;
            }

            /// <summary>
            /// Creates a native gainer with a preallocated heap.
            /// 创建一个使用预分配堆内存的原生平滑增益器。
            /// </summary>
            internal static bool TryCreate(int channels, uint smoothTimeInFrames, out GainerHandle handle)
            {
                handle = default;
                if (channels <= 0)
                {
                    return false;
                }

                var cfg = GainerConfigInit((uint)channels, smoothTimeInFrames);
                if (GainerGetHeapSize(ref cfg, out var heapSize) != Result.Success)
                {
                    return false;
                }

                ulong heapSizeBytes = heapSize.ToUInt64();
                if (heapSizeBytes == 0 || heapSizeBytes > 16UL * 1024UL * 1024UL)
                {
                    return false;
                }

                IntPtr heap = IntPtr.Zero;
                IntPtr gainer = IntPtr.Zero;

                try
                {
                    heap = Marshal.AllocHGlobal(checked((IntPtr)(long)heapSizeBytes));
                    gainer = Marshal.AllocHGlobal(GainerStructSizeBytes);

                    var initResult = GainerInitPreallocated(ref cfg, heap, gainer);
                    if (initResult != Result.Success)
                    {
                        Marshal.FreeHGlobal(heap);
                        Marshal.FreeHGlobal(gainer);
                        return false;
                    }

                    handle = new GainerHandle(gainer, heap, channels);
                    return true;
                }
                catch
                {
                    if (heap != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(heap);
                    }

                    if (gainer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(gainer);
                    }

                    return false;
                }
            }

            internal static bool ProcessInPlace(ref GainerHandle handle, Span<float> interleaved, int frameCount)
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
                        var result = GainerProcessPcmFrames(handle._gainerPtr, (IntPtr)p, (IntPtr)p, (ulong)frameCount);
                        return result == Result.Success;
                    }
                }
            }

            internal static bool SetGain(ref GainerHandle handle, float gain)
            {
                if (!handle.IsValid)
                {
                    return false;
                }

                return GainerSetGain(handle._gainerPtr, gain) == Result.Success;
            }

            internal static bool SetMasterVolume(ref GainerHandle handle, float volume)
            {
                if (!handle.IsValid)
                {
                    return false;
                }

                return GainerSetMasterVolume(handle._gainerPtr, volume) == Result.Success;
            }

            public void Dispose()
            {
                if (!IsValid)
                {
                    return;
                }

                GainerUninit(_gainerPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_gainerPtr);
                Marshal.FreeHGlobal(_heapPtr);
            }
        }
    }
}

