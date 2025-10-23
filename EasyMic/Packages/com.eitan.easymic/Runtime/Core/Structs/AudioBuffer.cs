// Filename: AudioBuffer.cs
// Target:  .NET Standard 2.0+ / .NET 5+ 皆可
// Scenario: 单生产者-单消费者音频缓冲（float PCM）

#nullable enable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// 为 SPSC（单生产者-单消费者）场景设计的高性能、无锁环形缓冲区（float）。
    /// - Lock-Free：依赖 Volatile.Read/Write 保证跨线程可见性（SPSC 模型下正确）。
    /// - Zero GC：核心读写不分配托管堆内存。
    /// - 性能优化：自动使用掩码（容量为 2 的幂时），避免取模。
    ///
    /// ⚠️ 安全前提：严格保证仅有 **一个**生产者线程调用 Write 系列，仅有 **一个**消费者线程调用 Read 系列。
    /// </summary>
    public sealed class AudioBuffer
    {
        /// <summary>对外真实可用容量（构造参数）。</summary>
        public int Capacity { get; }

        // 内部数组长度 = Capacity + 1（空一格用于区分满/空）
        private readonly int _size;

        // 若 _size 为 2 的幂，则使用掩码快速环绕
        private readonly bool _useMask;
        private readonly int _mask; // 仅在 _useMask=true 时有效

        private readonly float[] _buffer;
        private readonly int _frameStride;

        // 用填充结构减轻伪共享（false sharing）
        // 不影响 API/兼容性；若不需要，也可将 PaddedInt 改为普通 int 并相应修改 Volatile.Read/Write 调用。
        [StructLayout(LayoutKind.Sequential)]
        private struct PaddedInt
        {
            public int Value;
#pragma warning disable IDE0051
            // 64B cache line padding 的近似（在 64 位进程中）：1*int + 7*long ≈ 4 + 56 = 60B，考虑对象头/对齐通常足够分离
            private long _pad1, _pad2, _pad3, _pad4, _pad5, _pad6, _pad7;
#pragma warning restore IDE0051
        }

        private PaddedInt _writePos; // 仅通过 Volatile.Read/Write 访问 Value
        private PaddedInt _readPos;  // 仅通过 Volatile.Read/Write 访问 Value

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="capacity">对外可用的最大样本容量（建议接近 2 的幂，便于内部优化，但不是必须）。</param>
        /// <param name="frameStride">对齐粒度（通常等于声道数）。所有读写都会对齐至该粒度。</param>
        public AudioBuffer(int capacity, int frameStride = 1)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
            }

            if (frameStride <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameStride), "Frame stride must be positive.");
            }

            _frameStride = frameStride;

            int alignedCapacity = AlignCapacity(capacity);
            _size = NextPowerOfTwo(Math.Max(2, alignedCapacity + 1));
            Capacity = _size - 1;
            _buffer = new float[_size];

            // 根据 _size 判断是否能走掩码路径（仅当 _size 是 2 的幂）
            if (IsPowerOfTwo(_size))
            {
                _useMask = true;
                _mask = _size - 1;
            }
            else
            {
                _useMask = false;
                _mask = 0;
            }

            Volatile.Write(ref _writePos.Value, 0);
            Volatile.Write(ref _readPos.Value, 0);
        }

        /// <summary>
        /// 当前可读样本数（近似值，瞬时可能变化；为消费者侧观测提供参考）。
        /// </summary>
        public int ReadableCount
        {
            get
            {
                int w = Volatile.Read(ref _writePos.Value);
                int r = Volatile.Read(ref _readPos.Value);
                int readable = AvailableToRead(w, r);
                return readable - (readable % _frameStride);
            }
        }

        /// <summary>
        /// 当前可写样本数（近似值，瞬时可能变化；为生产者侧观测提供参考）。
        /// </summary>
        public int WritableCount
        {
            get
            {
                int w = Volatile.Read(ref _writePos.Value);
                int r = Volatile.Read(ref _readPos.Value);
                int writable = AvailableToWrite(w, r);
                return writable - (writable % _frameStride);
            }
        }

        public int FrameStride => _frameStride;

        /// <summary>缓冲区是否为空（近似判断）。</summary>
        public bool IsEmpty
        {
            get
            {
                int w = Volatile.Read(ref _writePos.Value);
                int r = Volatile.Read(ref _readPos.Value);
                return w == r;
            }
        }

        /// <summary>缓冲区是否已满（近似判断）。</summary>
        public bool IsFull
        {
            get
            {
                int w = Volatile.Read(ref _writePos.Value);
                int r = Volatile.Read(ref _readPos.Value);
                int nextW = Advance(w, 1);
                return nextW == r;
            }
        }

        /// <summary>
        /// 生产者写入（允许部分写入）。
        /// </summary>
        /// <param name="data">要写入的样本数据。</param>
        /// <returns>实际写入的样本数量（可能小于 data.Length）。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Write(ReadOnlySpan<float> data)
        {
            if (data.IsEmpty)
            {
                return 0;
            }


            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);

            int writable = AvailableToWrite(w, r);
            int toWrite = Math.Min(data.Length, writable);
            toWrite -= toWrite % _frameStride;
            if (toWrite == 0)
            {
                return 0;
            }

            // 分两段拷贝（处理环绕）
            int first = Math.Min(toWrite, _size - w);
            data.Slice(0, first).CopyTo(new Span<float>(_buffer, w, first));
            int second = toWrite - first;
            if (second > 0)
            {
                data.Slice(first, second).CopyTo(new Span<float>(_buffer, 0, second));
            }


            Volatile.Write(ref _writePos.Value, Advance(w, toWrite));
            return toWrite;
        }

        /// <summary>
        /// 生产者尝试写入“刚好” data.Length 个样本；若空间不足则不写入并返回 false。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWriteExact(ReadOnlySpan<float> data)
        {
            if (data.IsEmpty)
            {
                return true;
            }


            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);

            if ((data.Length % _frameStride) != 0)
            {
                throw new ArgumentException("Data length must align with frame stride.", nameof(data));
            }

            int writable = AvailableToWrite(w, r);
            if (writable < data.Length)
            {
                return false;
            }


            int toWrite = data.Length;
            int first = Math.Min(toWrite, _size - w);
            data.Slice(0, first).CopyTo(new Span<float>(_buffer, w, first));
            int second = toWrite - first;
            if (second > 0)
            {
                data.Slice(first, second).CopyTo(new Span<float>(_buffer, 0, second));
            }


            Volatile.Write(ref _writePos.Value, Advance(w, toWrite));
            return true;
        }

        /// <summary>
        /// 消费者读取（允许部分读取）。
        /// </summary>
        /// <param name="destination">目标 Span。</param>
        /// <returns>实际读取的样本数量（可能小于 destination.Length）。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(Span<float> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }


            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);

            int readable = AvailableToRead(w, r);
            int toRead = Math.Min(destination.Length, readable);
            toRead -= toRead % _frameStride;
            if (toRead == 0)
            {
                return 0;
            }

            int first = Math.Min(toRead, _size - r);
            new ReadOnlySpan<float>(_buffer, r, first).CopyTo(destination.Slice(0, first));
            int second = toRead - first;
            if (second > 0)
            {
                new ReadOnlySpan<float>(_buffer, 0, second).CopyTo(destination.Slice(first, second));
            }


            Volatile.Write(ref _readPos.Value, Advance(r, toRead));
            return toRead;
        }

        /// <summary>
        /// 消费者尝试“刚好”读取 count 个样本至 destination[0..count)；不足则不前进读指针并返回 false。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReadExact(Span<float> destination, int count)
        {
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

            int readable = AvailableToRead(w, r);
            if (readable < count)
            {
                return false;
            }


            int first = Math.Min(count, _size - r);
            new ReadOnlySpan<float>(_buffer, r, first).CopyTo(destination.Slice(0, first));
            int second = count - first;
            if (second > 0)
            {
                new ReadOnlySpan<float>(_buffer, 0, second).CopyTo(destination.Slice(first, second));
            }


            Volatile.Write(ref _readPos.Value, Advance(r, count));
            return true;
        }

        /// <summary>
        /// 消费者窥视（不前进读指针），返回实际复制的样本数（允许小于 destination.Length）。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Peek(Span<float> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }


            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);

            int readable = AvailableToRead(w, r);
            int toCopy = Math.Min(destination.Length, readable);
            toCopy -= toCopy % _frameStride;
            if (toCopy == 0)
            {
                return 0;
            }

            int first = Math.Min(toCopy, _size - r);
            new ReadOnlySpan<float>(_buffer, r, first).CopyTo(destination.Slice(0, first));
            int second = toCopy - first;
            if (second > 0)
            {
                new ReadOnlySpan<float>(_buffer, 0, second).CopyTo(destination.Slice(first, second));
            }


            return toCopy;
        }

        /// <summary>
        /// 消费者跳过（前进读指针）最多 count 个样本，返回实际跳过数。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Skip(int count)
        {
            if (count <= 0)
            {
                return 0;
            }


            int w = Volatile.Read(ref _writePos.Value);
            int r = Volatile.Read(ref _readPos.Value);

            int readable = AvailableToRead(w, r);
            int toSkip = Math.Min(count, readable);
            toSkip -= toSkip % _frameStride;
            if (toSkip == 0)
            {
                return 0;
            }


            Volatile.Write(ref _readPos.Value, Advance(r, toSkip));
            return toSkip;
        }

        /// <summary>
        /// 清空缓冲区（设置为空）。建议在生产者或消费者单侧调用，或二者都暂停时调用。
        /// </summary>
        public void Clear()
        {
            Volatile.Write(ref _writePos.Value, 0);
            Volatile.Write(ref _readPos.Value, 0);
        }

        // =========================
        // 内部辅助
        // =========================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AlignCapacity(int requested)
        {
            int stride = _frameStride;
            int aligned = ((requested + stride - 1) / stride) * stride;
            return aligned;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int NextPowerOfTwo(int value)
        {
            if (value <= 2)
            {
                return 2;
            }

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;
            return value < 2 ? 2 : value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AvailableToRead(int w, int r)
            => w >= r ? (w - r) : (w + (_size - r));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AvailableToWrite(int w, int r)
            // 留一格，避免满/空歧义
            => w >= r ? (_size - w + r - 1) : (r - w - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Advance(int pos, int n)
        {
            if (_useMask)
            {
                // _size 为 2 的幂：掩码环绕（更快）
                return (pos + n) & _mask;
            }
            // 非 2 的幂：一次环绕足够（n 不会超过 _size-1）
            pos += n;
            return (pos >= _size) ? (pos - _size) : pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;
    }
}
