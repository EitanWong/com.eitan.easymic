using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Unmanaged SPSC float ring for real-time audio handoff.
    /// Producer and consumer must each be single-threaded.
    /// </summary>
    internal sealed unsafe class UnsafeAudioRingBuffer : IDisposable
    {
        private readonly int _size;
        private readonly int _mask;
        private readonly int _frameStride;
        private float* _buffer;
        private PaddedInt _writePos;
        private PaddedInt _readPos;

        public int Capacity => _size - 1;

        public int FrameStride => _frameStride;

        [StructLayout(LayoutKind.Sequential)]
        private struct PaddedInt
        {
            public int Value;
#pragma warning disable IDE0051
            private long _pad1, _pad2, _pad3, _pad4, _pad5, _pad6, _pad7;
#pragma warning restore IDE0051
        }

        public UnsafeAudioRingBuffer(int capacity, int frameStride = 1)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (frameStride <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameStride));
            }

            _frameStride = frameStride;
            _size = NextPowerOfTwo(Math.Max(2, AlignCapacity(capacity, frameStride) + 1));
            _mask = _size - 1;
            _buffer = (float*)Marshal.AllocHGlobal(checked(_size * sizeof(float)));
            new Span<float>(_buffer, _size).Clear();
        }

        public int ReadableCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int readable = AvailableToRead(Volatile.Read(ref _writePos.Value), Volatile.Read(ref _readPos.Value));
                return readable - readable % _frameStride;
            }
        }

        public int WritableCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int writable = AvailableToWrite(Volatile.Read(ref _writePos.Value), Volatile.Read(ref _readPos.Value));
                return writable - writable % _frameStride;
            }
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _writePos.Value) == Volatile.Read(ref _readPos.Value);
        }

        public bool IsFull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                int w = Volatile.Read(ref _writePos.Value);
                int r = Volatile.Read(ref _readPos.Value);
                return Advance(w, 1) == r;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Write(ReadOnlySpan<float> data)
        {
            ThrowIfDisposed();
            if (data.IsEmpty)
            {
                return 0;
            }

            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);
            int toWrite = Math.Min(data.Length, AvailableToWrite(w, r));
            toWrite -= toWrite % _frameStride;
            if (toWrite == 0)
            {
                return 0;
            }

            CopyFromSpan(data.Slice(0, toWrite), w);
            Volatile.Write(ref _writePos.Value, Advance(w, toWrite));
            return toWrite;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWriteExact(ReadOnlySpan<float> data)
        {
            ThrowIfDisposed();
            if (data.IsEmpty)
            {
                return true;
            }

            if ((data.Length % _frameStride) != 0)
            {
                throw new ArgumentException("Data length must align with frame stride.", nameof(data));
            }

            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);
            if (AvailableToWrite(w, r) < data.Length)
            {
                return false;
            }

            CopyFromSpan(data, w);
            Volatile.Write(ref _writePos.Value, Advance(w, data.Length));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(Span<float> destination)
        {
            ThrowIfDisposed();
            if (destination.IsEmpty)
            {
                return 0;
            }

            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);
            int toRead = Math.Min(destination.Length, AvailableToRead(w, r));
            toRead -= toRead % _frameStride;
            if (toRead == 0)
            {
                return 0;
            }

            CopyToSpan(r, destination.Slice(0, toRead));
            Volatile.Write(ref _readPos.Value, Advance(r, toRead));
            return toRead;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadExact(Span<float> destination, int count)
        {
            ThrowIfDisposed();
            if (count < 0 || count > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if ((count % _frameStride) != 0)
            {
                throw new ArgumentException("Count must align with frame stride.", nameof(count));
            }

            if (count == 0)
            {
                return true;
            }

            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);
            if (AvailableToRead(w, r) < count)
            {
                return false;
            }

            CopyToSpan(r, destination.Slice(0, count));
            Volatile.Write(ref _readPos.Value, Advance(r, count));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Peek(Span<float> destination)
        {
            ThrowIfDisposed();
            if (destination.IsEmpty)
            {
                return 0;
            }

            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);
            int toCopy = Math.Min(destination.Length, AvailableToRead(w, r));
            toCopy -= toCopy % _frameStride;
            if (toCopy == 0)
            {
                return 0;
            }

            CopyToSpan(r, destination.Slice(0, toCopy));
            return toCopy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Skip(int count)
        {
            ThrowIfDisposed();
            if (count <= 0)
            {
                return 0;
            }

            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);
            int toSkip = Math.Min(count, AvailableToRead(w, r));
            toSkip -= toSkip % _frameStride;
            if (toSkip == 0)
            {
                return 0;
            }

            Volatile.Write(ref _readPos.Value, Advance(r, toSkip));
            return toSkip;
        }

        public void Clear()
        {
            Volatile.Write(ref _writePos.Value, 0);
            Volatile.Write(ref _readPos.Value, 0);
        }

        public void Dispose()
        {
            float* buffer = _buffer;
            if (buffer == null)
            {
                return;
            }

            _buffer = null;
            Marshal.FreeHGlobal((IntPtr)buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyFromSpan(ReadOnlySpan<float> source, int writeIndex)
        {
            int first = Math.Min(source.Length, _size - writeIndex);
            source.Slice(0, first).CopyTo(new Span<float>(_buffer + writeIndex, first));
            int second = source.Length - first;
            if (second > 0)
            {
                source.Slice(first, second).CopyTo(new Span<float>(_buffer, second));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyToSpan(int readIndex, Span<float> destination)
        {
            int first = Math.Min(destination.Length, _size - readIndex);
            new ReadOnlySpan<float>(_buffer + readIndex, first).CopyTo(destination.Slice(0, first));
            int second = destination.Length - first;
            if (second > 0)
            {
                new ReadOnlySpan<float>(_buffer, second).CopyTo(destination.Slice(first, second));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AvailableToRead(int w, int r) => w >= r ? w - r : w + _size - r;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AvailableToWrite(int w, int r) => w >= r ? _size - w + r - 1 : r - w - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Advance(int pos, int count) => (pos + count) & _mask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_buffer == null)
            {
                throw new ObjectDisposedException(nameof(UnsafeAudioRingBuffer));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int AlignCapacity(int requested, int stride)
        {
            return ((requested + stride - 1) / stride) * stride;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOfTwo(int value)
        {
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value < 2 ? 2 : value;
        }
    }
}
