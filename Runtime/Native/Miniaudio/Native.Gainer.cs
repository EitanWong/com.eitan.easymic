using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{
    internal static unsafe partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct GainerConfig
        {
            public uint Channels;
            public uint SmoothTimeInFrames;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GainerState
        {
            public GainerConfig Config;
            public uint T;
            public float MasterVolume;
            public IntPtr OldGains;
            public IntPtr NewGains;
            public IntPtr Heap;
            public int OwnsHeap;
        }

        [DllImport(LibraryName, EntryPoint = "ma_gainer_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern GainerConfig GainerConfigInit(uint channels, uint smoothTimeInFrames);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_get_heap_size", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result GainerGetHeapSize(ref GainerConfig config, out UIntPtr heapSizeInBytes);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_init_preallocated", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result GainerInitPreallocated(ref GainerConfig config, IntPtr heap, IntPtr gainer);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_uninit", CallingConvention = CallingConvention.Cdecl)]
        private static extern void GainerUninit(IntPtr gainer, IntPtr allocationCallbacks);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_process_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result GainerProcessPcmFrames(IntPtr gainer, IntPtr pFramesOut, IntPtr pFramesIn, ulong frameCount);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_set_gain", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result GainerSetGainNative(IntPtr gainer, float newGain);

        [DllImport(LibraryName, EntryPoint = "ma_gainer_set_master_volume", CallingConvention = CallingConvention.Cdecl)]
        private static extern Result GainerSetMasterVolumeNative(IntPtr gainer, float volume);

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

            internal static bool TryCreate(int channels, uint smoothTimeInFrames, out GainerHandle handle)
            {
                handle = default;
                if (channels <= 0) return false;
                var cfg = GainerConfigInit((uint)channels, smoothTimeInFrames);
                if (GainerGetHeapSize(ref cfg, out var heapSize) != Result.Success) return false;
                ulong heapBytes = heapSize.ToUInt64();
                if (heapBytes == 0 || heapBytes > 16UL * 1024UL * 1024UL) return false;

                IntPtr heap = IntPtr.Zero;
                IntPtr gainer = IntPtr.Zero;
                try
                {
                    heap = Marshal.AllocHGlobal(checked((IntPtr)(long)heapBytes));
                    gainer = Marshal.AllocHGlobal(Marshal.SizeOf<GainerState>());
                    if (GainerInitPreallocated(ref cfg, heap, gainer) != Result.Success)
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
                    if (heap != IntPtr.Zero) Marshal.FreeHGlobal(heap);
                    if (gainer != IntPtr.Zero) Marshal.FreeHGlobal(gainer);
                    return false;
                }
            }

            internal static bool ProcessInPlace(ref GainerHandle handle, Span<float> interleaved, int frameCount)
            {
                if (!handle.IsValid || frameCount <= 0) return false;
                int maxFrames = interleaved.Length / Math.Max(1, handle._channels);
                if (frameCount > maxFrames) frameCount = maxFrames;
                fixed (float* p = interleaved)
                {
                    return GainerProcessPcmFrames(handle._gainerPtr, (IntPtr)p, (IntPtr)p, (ulong)frameCount) == Result.Success;
                }
            }

            internal static bool SetGain(ref GainerHandle handle, float gain)
            {
                return handle.IsValid && GainerSetGainNative(handle._gainerPtr, gain) == Result.Success;
            }

            internal static bool SetMasterVolume(ref GainerHandle handle, float volume)
            {
                return handle.IsValid && GainerSetMasterVolumeNative(handle._gainerPtr, volume) == Result.Success;
            }

            public void Dispose()
            {
                if (!IsValid) return;
                GainerUninit(_gainerPtr, IntPtr.Zero);
                Marshal.FreeHGlobal(_gainerPtr);
                Marshal.FreeHGlobal(_heapPtr);
            }
        }
    }
}
