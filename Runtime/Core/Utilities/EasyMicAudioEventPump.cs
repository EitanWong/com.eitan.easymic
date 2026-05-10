using System;
using System.Collections.Generic;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Bridges real-time audio callback threads to the Unity main thread without invoking user code from RT callbacks.
    /// Audio thread: lock-free, allocation-free enqueue into an SPSC ring.
    /// Worker thread: drains, allocates managed arrays, and posts to main thread via <see cref="SynchronizationContext"/>.
    /// </summary>
    internal static class EasyMicAudioEventPump
    {
        private const int HeaderFloats = 5; // type, id, channels, sampleRate, sampleCount
        private const int DefaultQueueCapacityFloats = 262144; // ~1MB float ring buffer
        private const int ZeroChunkFloats = 1024;
        private const int MaxPooledArraysPerSize = 8;

        private enum EventType : int
        {
            PlaybackFrame = 1,
            PlaybackCompleted = 2,
            MixedFrame = 3
        }

        private static readonly object s_initLock = new object();
        private static UnsafeAudioRingBuffer s_queue;
        private static AutoResetEvent s_signal;
        private static Thread s_thread;
        private static bool s_running;

        private static SynchronizationContext s_mainContext;
        private static AudioSystem s_audioSystem;

        private static int s_nextSourceId;
        private static readonly object s_sourcesLock = new object();
        private static readonly Dictionary<int, PlaybackAudioSource> s_sources = new Dictionary<int, PlaybackAudioSource>(64);
        private static readonly object s_payloadPoolLock = new object();
        private static readonly Dictionary<int, Stack<float[]>> s_payloadPool = new Dictionary<int, Stack<float[]>>(16);
        private static readonly object s_dispatchItemPoolLock = new object();
        private static readonly Stack<DispatchItem> s_dispatchItemPool = new Stack<DispatchItem>(32);

        private static readonly float[] s_zeroChunk = new float[ZeroChunkFloats];

        internal static void SetMainThreadContext(SynchronizationContext context)
        {
            if (context != null)
            {
                Volatile.Write(ref s_mainContext, context);
            }

            EnsureStarted();
        }

        internal static void SetAudioSystem(AudioSystem system)
        {
            Volatile.Write(ref s_audioSystem, system);
        }

        internal static int RegisterPlaybackSource(PlaybackAudioSource source)
        {
            if (source == null)
            {
                return 0;
            }

            EnsureStarted();

            int id = Interlocked.Increment(ref s_nextSourceId);
            lock (s_sourcesLock)
            {
                s_sources[id] = source;
            }

            return id;
        }

        internal static void UnregisterPlaybackSource(int id)
        {
            if (id <= 0)
            {
                return;
            }

            lock (s_sourcesLock)
            {
                s_sources.Remove(id);
            }
        }

        internal static AudioEventWriter TryBeginPlaybackFrame(int sourceId, int channels, int sampleRate, int sampleCount)
        {
            return TryBegin(EventType.PlaybackFrame, sourceId, channels, sampleRate, sampleCount);
        }

        internal static AudioEventWriter TryBeginMixedFrame(int channels, int sampleRate, int sampleCount)
        {
            return TryBegin(EventType.MixedFrame, 0, channels, sampleRate, sampleCount);
        }

        internal static AudioEventWriter TryBeginPlaybackCompleted(int sourceId, int channels, int sampleRate)
        {
            return TryBegin(EventType.PlaybackCompleted, sourceId, channels, sampleRate, 0);
        }

        private static AudioEventWriter TryBegin(EventType type, int id, int channels, int sampleRate, int sampleCount)
        {
            // AUDIO THREAD ONLY: must remain lock-free / allocation-free / exception-free.
            var queue = Volatile.Read(ref s_queue);
            if (queue == null)
            {
                return default;
            }

            if (sampleCount < 0)
            {
                return default;
            }

            int required = HeaderFloats + sampleCount;
            if (queue.WritableCount < required)
            {
                return default;
            }

            Span<float> header = stackalloc float[HeaderFloats];
            header[0] = (float)type;
            header[1] = id;
            header[2] = channels;
            header[3] = sampleRate;
            header[4] = sampleCount;

            if (!queue.TryWriteExact(header))
            {
                return default;
            }

            return new AudioEventWriter(queue, sampleCount);
        }

        internal ref struct AudioEventWriter
        {
            private readonly UnsafeAudioRingBuffer _queue;
            private int _remaining;

            internal AudioEventWriter(UnsafeAudioRingBuffer queue, int remainingSamples)
            {
                _queue = queue;
                _remaining = remainingSamples;
            }

            internal bool IsValid => _queue != null;

            internal int Remaining => _remaining;

            internal bool Write(ReadOnlySpan<float> samples)
            {
                if (_queue == null || samples.IsEmpty)
                {
                    return _queue != null;
                }

                if (samples.Length > _remaining)
                {
                    return false;
                }

                if (!_queue.TryWriteExact(samples))
                {
                    return false;
                }

                _remaining -= samples.Length;
                return true;
            }

            internal bool WriteZeros(int count)
            {
                if (_queue == null)
                {
                    return false;
                }

                if (count <= 0)
                {
                    return true;
                }

                if (count > _remaining)
                {
                    return false;
                }

                int remaining = count;
                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, s_zeroChunk.Length);
                    if (!_queue.TryWriteExact(new ReadOnlySpan<float>(s_zeroChunk, 0, chunk)))
                    {
                        return false;
                    }

                    remaining -= chunk;
                    _remaining -= chunk;
                }

                return true;
            }

            internal void Commit()
            {
                if (_queue == null)
                {
                    return;
                }

                if (_remaining > 0)
                {
                    // Best-effort: pad missing payload with zeros to keep the stream parseable.
                    WriteZeros(_remaining);
                }

                try { Volatile.Read(ref s_signal)?.Set(); } catch { }
            }
        }

        private static void EnsureStarted()
        {
            if (Volatile.Read(ref s_running))
            {
                return;
            }

            lock (s_initLock)
            {
                if (Volatile.Read(ref s_running))
                {
                    return;
                }

                Volatile.Write(ref s_queue, new UnsafeAudioRingBuffer(DefaultQueueCapacityFloats, 1));
                Volatile.Write(ref s_signal, new AutoResetEvent(false));
                Volatile.Write(ref s_running, true);
                s_thread = new Thread(ThreadLoop)
                {
                    IsBackground = true,
                    Name = "EasyMic-AudioEventPump"
                };
                s_thread.Start();
            }
        }

        private static void ThreadLoop()
        {
            var header = new float[HeaderFloats];

            while (Volatile.Read(ref s_running))
            {
                try { Volatile.Read(ref s_signal)?.WaitOne(); } catch { }
                if (!Volatile.Read(ref s_running))
                {
                    break;
                }

                try
                {
                    Drain(header);
                }
                catch
                {
                    // Never allow the pump thread to crash the process.
                }
            }
        }

        private static void Drain(float[] header)
        {
            var queue = Volatile.Read(ref s_queue);
            if (queue == null)
            {
                return;
            }

            while (TryDrainOne(queue, header))
            {
            }
        }

        private static bool TryDrainOne(UnsafeAudioRingBuffer queue, float[] header)
        {
            if (queue.ReadableCount < HeaderFloats)
            {
                return false;
            }

            int peeked = queue.Peek(header);
            if (peeked < HeaderFloats)
            {
                return false;
            }

            int type = (int)header[0];
            int id = (int)header[1];
            int channels = (int)header[2];
            int sampleRate = (int)header[3];
            int sampleCount = (int)header[4];

            if (sampleCount < 0)
            {
                // Corrupt stream; drop header and attempt to resync.
                queue.Skip(HeaderFloats);
                return true;
            }

            int required = HeaderFloats + sampleCount;
            if (queue.ReadableCount < required)
            {
                return false;
            }

            queue.Skip(HeaderFloats);

            float[] payload = sampleCount == 0 ? Array.Empty<float>() : RentPayload(sampleCount);
            if (sampleCount > 0)
            {
                queue.TryReadExact(payload, sampleCount);
            }

            Dispatch((EventType)type, id, channels, sampleRate, payload);
            return true;
        }

        private static void Dispatch(EventType type, int id, int channels, int sampleRate, float[] payload)
        {
            var ctx = Volatile.Read(ref s_mainContext);
            if (ctx != null)
            {
                DispatchItem item = null;
                try
                {
                    item = RentDispatchItem(type, id, channels, sampleRate, payload);
                    ctx.Post(DispatchOnMainThread, item);
                    return;
                }
                catch
                {
                    if (item != null)
                    {
                        item.Clear();
                        ReturnDispatchItem(item);
                    }
                    // Fall through to direct dispatch.
                }
            }

            try
            {
                DispatchDirect(type, id, channels, sampleRate, payload);
            }
            finally
            {
                ReturnPayload(payload);
            }
        }

        private static void DispatchOnMainThread(object state)
        {
            if (state is DispatchItem item)
            {
                try
                {
                    DispatchDirect(item.Type, item.Id, item.Channels, item.SampleRate, item.Payload);
                }
                finally
                {
                    ReturnPayload(item.Payload);
                    item.Clear();
                    ReturnDispatchItem(item);
                }
            }
        }

        private static void DispatchDirect(EventType type, int id, int channels, int sampleRate, float[] payload)
        {
            switch (type)
            {
                case EventType.PlaybackFrame:
                {
                    var source = FindSource(id);
                    source?.DispatchAudioPlaybackFrame(payload, channels, sampleRate);
                    return;
                }
                case EventType.PlaybackCompleted:
                {
                    var source = FindSource(id);
                    source?.DispatchPlaybackCompleted();
                    return;
                }
                case EventType.MixedFrame:
                {
                    var system = Volatile.Read(ref s_audioSystem);
                    system?.DispatchMixedFrame(payload, channels, sampleRate);
                    return;
                }
                default:
                    return;
            }
        }

        private static PlaybackAudioSource FindSource(int id)
        {
            if (id <= 0)
            {
                return null;
            }

            lock (s_sourcesLock)
            {
                return s_sources.TryGetValue(id, out var source) ? source : null;
            }
        }

        private static float[] RentPayload(int sampleCount)
        {
            if (sampleCount <= 0)
            {
                return Array.Empty<float>();
            }

            lock (s_payloadPoolLock)
            {
                if (s_payloadPool.TryGetValue(sampleCount, out var stack) && stack.Count > 0)
                {
                    return stack.Pop();
                }
            }

            return new float[sampleCount];
        }

        private static void ReturnPayload(float[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return;
            }

            lock (s_payloadPoolLock)
            {
                if (!s_payloadPool.TryGetValue(payload.Length, out var stack))
                {
                    stack = new Stack<float[]>(MaxPooledArraysPerSize);
                    s_payloadPool[payload.Length] = stack;
                }

                if (stack.Count < MaxPooledArraysPerSize)
                {
                    stack.Push(payload);
                }
            }
        }

        private static DispatchItem RentDispatchItem(EventType type, int id, int channels, int sampleRate, float[] payload)
        {
            DispatchItem item = null;
            lock (s_dispatchItemPoolLock)
            {
                if (s_dispatchItemPool.Count > 0)
                {
                    item = s_dispatchItemPool.Pop();
                }
            }

            if (item == null)
            {
                item = new DispatchItem();
            }

            item.Reset(type, id, channels, sampleRate, payload);
            return item;
        }

        private static void ReturnDispatchItem(DispatchItem item)
        {
            if (item == null)
            {
                return;
            }

            lock (s_dispatchItemPoolLock)
            {
                if (s_dispatchItemPool.Count < 64)
                {
                    s_dispatchItemPool.Push(item);
                }
            }
        }

        private sealed class DispatchItem
        {
            public void Reset(EventType type, int id, int channels, int sampleRate, float[] payload)
            {
                Type = type;
                Id = id;
                Channels = channels;
                SampleRate = sampleRate;
                Payload = payload;
            }

            public void Clear()
            {
                Payload = null;
            }

            public EventType Type { get; private set; }
            public int Id { get; private set; }
            public int Channels { get; private set; }
            public int SampleRate { get; private set; }
            public float[] Payload { get; private set; }
        }
    }
}
