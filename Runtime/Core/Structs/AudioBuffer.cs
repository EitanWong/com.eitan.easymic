namespace Eitan.EasyMic.Runtime
{
    
using System;
using System.Threading;

/// <summary>
/// 一个为单生产者-单消费者 (SPSC) 场景设计的极致性能、无锁环形音频缓冲区。
///
/// **设计原则:**
/// 1. **无锁 (Lock-Free):** 专为SPSC设计，避免了 `lock` 带来的性能开销。
///    通过 `Volatile` 确保跨线程的内存可见性。
/// 2. **零GC (Zero GC):** 在核心的读写操作中不产生任何托管堆分配，避免GC暂停。
/// 3. **高性能:** 所有操作都通过内存块复制 (`Span.CopyTo`) 完成，避免了逐样本操作。
///
/// **警告:** 此类不是通用的线程安全缓冲区。只有在严格的SPSC场景下才能安全使用。
/// (例如：一个专用的音频回调线程写入，一个专用的处理线程读取)。
/// </summary>
public class AudioBuffer
{
    private readonly float[] _buffer;
    private volatile int _writePosition;
    private volatile int _readPosition;

    /// <summary>
    /// 获取缓冲区的总容量。
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// 获取当前可从缓冲区读取的样本数。
    /// 此操作是无锁的，且为消费者线程设计。
    /// </summary>
    public int ReadableCount
    {
        get
        {
                // 使用本地副本以避免在计算过程中 _writePosition 被修改

#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile

                int writePos = Volatile.Read(ref _writePosition);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile



#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile

                int readPos = Volatile.Read(ref _readPosition);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile

                if (writePos >= readPos)
                return writePos - readPos;
            return writePos + (Capacity - readPos);
        }
    }

    /// <summary>
    /// 获取当前可向缓冲区写入的样本数。
    /// 此操作是无锁的，且为生产者线程设计。
    /// </summary>
    public int WritableCount => Capacity - ReadableCount -1; // -1 to prevent writePos from catching up to readPos

    /// <summary>
    /// 初始化 <see cref="AudioBuffer"/> 的一个新实例。
    /// </summary>
    /// <param name="capacity">缓冲区的最大样本容量。建议为2的幂以提高性能，但不是必须。</param>
    public AudioBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));
        }
        // 我们需要一个额外的空间来区分 full 和 empty 状态
        Capacity = capacity + 1;
        _buffer = new float[Capacity];
        _readPosition = 0;
        _writePosition = 0;
    }

    /// <summary>
    /// **(生产者线程调用)**
    /// 向缓冲区写入数据。如果空间不足，则只写入能容纳的部分。
    /// </summary>
    /// <param name="data">要写入的样本数据。</param>
    /// <returns>实际写入的样本数量。</returns>
    public int Write(ReadOnlySpan<float> data)
    {
            if (data.IsEmpty) { return 0; }

#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile
            int writePos = Volatile.Read(ref _writePosition);
            int readPos = Volatile.Read(ref _readPosition);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile

        int available = 0;
        if(writePos >= readPos)
        {
            // 空闲空间是从写指针到读指针（可能环绕）
            available = Capacity - writePos + readPos -1;
        }
        else
        {
            available = readPos - writePos -1;
        }

        int samplesToWrite = Math.Min(data.Length, available);
        if (samplesToWrite == 0) return 0;
        
        // 第一次复制：从当前写指针到数组末尾
        int firstCopyLength = Math.Min(samplesToWrite, Capacity - writePos);
        data.Slice(0, firstCopyLength).CopyTo(new Span<float>(_buffer, writePos, firstCopyLength));

        // 第二次复制：如果发生了环绕
        int secondCopyLength = samplesToWrite - firstCopyLength;
        if (secondCopyLength > 0)
        {
            data.Slice(firstCopyLength, secondCopyLength).CopyTo(new Span<float>(_buffer, 0, secondCopyLength));
        }

            // 更新写指针。使用 Volatile.Write 确保其他线程能立即看到变化。
#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile
            Volatile.Write(ref _writePosition, (writePos + samplesToWrite) % Capacity);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile

            return samplesToWrite;
    }

    /// <summary>
    /// **(消费者线程调用)**
    /// 从缓冲区读取数据。
    /// </summary>
    /// <param name="destination">用于接收数据的目标Span。</param>
    /// <returns>实际读取的样本数量。</returns>
    public int Read(Span<float> destination)
    {
        if (destination.IsEmpty) return 0;


#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile
            int writePos = Volatile.Read(ref _writePosition);
            int readPos = Volatile.Read(ref _readPosition);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile

        int available = 0;
        if(writePos >= readPos)
        {
            available = writePos - readPos;
        }
        else
        {
            available = writePos + (Capacity - readPos);
        }

        int samplesToRead = Math.Min(destination.Length, available);
        if (samplesToRead == 0) return 0;

        // 第一次复制：从当前读指针到数组末尾
        int firstCopyLength = Math.Min(samplesToRead, Capacity - readPos);
        new ReadOnlySpan<float>(_buffer, readPos, firstCopyLength).CopyTo(destination.Slice(0, firstCopyLength));

        // 第二次复制：如果发生了环绕
        int secondCopyLength = samplesToRead - firstCopyLength;
        if (secondCopyLength > 0)
        {
            new ReadOnlySpan<float>(_buffer, 0, secondCopyLength).CopyTo(destination.Slice(firstCopyLength, secondCopyLength));
        }

            // 更新读指针。使用 Volatile.Write 确保其他线程能立即看到变化。
#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile
            Volatile.Write(ref _readPosition, (readPos + samplesToRead) % Capacity);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile

            return samplesToRead;
    }

    /// <summary>
    /// 清空缓冲区。此方法应由生产者或消费者线程单独调用，或者在两个线程都暂停时调用。
    /// </summary>
    public void Clear()
    {
#pragma warning disable CS0420 // A reference to a volatile field will not be treated as volatile
            Volatile.Write(ref _writePosition, 0);
            Volatile.Write(ref _readPosition, 0);
#pragma warning restore CS0420 // A reference to a volatile field will not be treated as volatile
    }
    
}


}