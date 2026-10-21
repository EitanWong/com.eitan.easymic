using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class AudioBufferContractTests
    {
        [TestCase(false, 0, 1)]
        [TestCase(true, 0, 1)]
        [TestCase(false, -1, 1)]
        [TestCase(true, -1, 1)]
        [TestCase(false, 8, 0)]
        [TestCase(true, 8, 0)]
        [TestCase(false, int.MaxValue, 1)]
        [TestCase(true, int.MaxValue, 1)]
        [TestCase(false, 8, int.MaxValue)]
        [TestCase(true, 8, int.MaxValue)]
        public void InvalidOrOverflowingCapacity_IsRejected(bool unmanaged, int capacity, int stride)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { using var ring = new Ring(unmanaged, capacity, stride); });
        }

        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 3)]
        [TestCase(false, 6)]
        [TestCase(true, 6)]
        public void FullQueue_ReportsNoWritableFrame_AndDoesNotOverwrite(bool unmanaged, int stride)
        {
            using var ring = new Ring(unmanaged, 11, stride);
            int capacity = ring.Writable;
            var input = Sequence(capacity);
            Assert.That(ring.Write(input), Is.EqualTo(capacity));
            Assert.That(ring.IsFull, Is.True);
            Assert.That(ring.Write(new float[stride]), Is.Zero);
            Assert.That(ring.ExactWrite(new float[stride]), Is.False);
            var output = new float[capacity + stride];
            Assert.That(ring.Read(output), Is.EqualTo(capacity));
            CollectionAssert.AreEqual(input, new ArraySegment<float>(output, 0, capacity));
            Assert.That(ring.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedExactOperations_AreAtomic_AndDoNotTouchDestination(bool unmanaged)
        {
            using var ring = new Ring(unmanaged, 5, 2);
            ring.ExactWrite(new[] { 1f, 2f });
            var output = new[] { -1f, -1f, -1f, -1f };
            Assert.That(ring.ExactRead(output, 4), Is.False);
            CollectionAssert.AreEqual(new[] { -1f, -1f, -1f, -1f }, output);
            Assert.That(ring.Readable, Is.EqualTo(2));
            Assert.That(ring.ExactWrite(new float[ring.Writable + 2]), Is.False);
            Assert.That(ring.Readable, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => ring.ExactWrite(new float[1]));
            Assert.Throws<ArgumentException>(() => ring.ExactRead(output, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ring.ExactRead(output, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ring.ExactRead(output, 5));
            Assert.That(ring.ExactRead(output, 0), Is.True);
            Assert.That(ring.ExactWrite(Array.Empty<float>()), Is.True);
        }

        [TestCase(false, 1)]
        [TestCase(true, 1)]
        [TestCase(false, 2)]
        [TestCase(true, 2)]
        [TestCase(false, 3)]
        [TestCase(true, 3)]
        [TestCase(false, 6)]
        [TestCase(true, 6)]
        public void RandomOperations_MatchIndependentFifoModelAcrossWraparound(bool unmanaged, int stride)
        {
            using var ring = new Ring(unmanaged, 19, stride);
            int capacity = ring.Writable;
            var expected = new Queue<float>();
            var random = new Random(62413 + stride);
            int next = 1;
            for (int iteration = 0; iteration < 2000; iteration++)
            {
                int count = random.Next(0, capacity + 2 * stride);
                int aligned = count - count % stride;
                var data = new float[count];
                switch (random.Next(6))
                {
                    case 0:
                        for (int i = 0; i < count; i++) data[i] = next++;
                        int written = ring.Write(data);
                        Assert.That(written, Is.EqualTo(Math.Min(aligned, capacity - expected.Count)));
                        for (int i = 0; i < written; i++) expected.Enqueue(data[i]);
                        break;
                    case 1:
                        int read = ring.Read(data);
                        Assert.That(read, Is.EqualTo(Math.Min(aligned, expected.Count)));
                        for (int i = 0; i < read; i++) Assert.That(data[i], Is.EqualTo(expected.Dequeue()));
                        break;
                    case 2:
                        int peeked = ring.Peek(data);
                        Assert.That(peeked, Is.EqualTo(Math.Min(aligned, expected.Count)));
                        var snapshot = expected.ToArray();
                        for (int i = 0; i < peeked; i++) Assert.That(data[i], Is.EqualTo(snapshot[i]));
                        break;
                    case 3:
                        int skipped = ring.Skip(count);
                        Assert.That(skipped, Is.EqualTo(Math.Min(aligned, expected.Count)));
                        for (int i = 0; i < skipped; i++) expected.Dequeue();
                        break;
                    case 4:
                        ring.Clear();
                        expected.Clear();
                        break;
                    default:
                        var exact = Sequence(aligned);
                        bool fits = aligned <= capacity - expected.Count;
                        Assert.That(ring.ExactWrite(exact), Is.EqualTo(fits));
                        if (fits) foreach (float sample in exact) expected.Enqueue(sample);
                        break;
                }
                Assert.That(ring.Readable, Is.EqualTo(expected.Count), $"iteration={iteration}");
                Assert.That(ring.Writable, Is.EqualTo(capacity - expected.Count));
                Assert.That(ring.IsEmpty, Is.EqualTo(expected.Count == 0));
                Assert.That(ring.IsFull, Is.EqualTo(expected.Count == capacity));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SingleProducerConsumer_PreservesEverySampleUnderContention(bool unmanaged)
        {
            using var ring = new Ring(unmanaged, 64, 3);
            const int samples = 90000;
            var clock = Stopwatch.StartNew();
            var producer = Task.Run(() =>
            {
                var data = new float[21];
                int sent = 0;
                while (sent < samples && clock.ElapsedMilliseconds < 5000)
                {
                    int count = Math.Min(data.Length, samples - sent);
                    for (int i = 0; i < count; i++) data[i] = sent + i;
                    sent += ring.Write(data.AsSpan(0, count).ToArray());
                    Thread.Yield();
                }
                Assert.That(sent, Is.EqualTo(samples), "Producer timed out.");
            });
            var consumer = Task.Run(() =>
            {
                var data = new float[15];
                int received = 0;
                while (received < samples && clock.ElapsedMilliseconds < 5000)
                {
                    int read = ring.Read(data);
                    for (int i = 0; i < read; i++)
                        Assert.That(data[i], Is.EqualTo((float)received++));
                    Thread.Yield();
                }
                Assert.That(received, Is.EqualTo(samples), "Consumer timed out.");
            });
            Assert.That(Task.WaitAll(new[] { producer, consumer }, 7000), Is.True);
            Assert.That(ring.IsEmpty, Is.True);
        }

        [Test]
        public void UnmanagedDispose_IsIdempotent_AndDataOperationsRejectUseAfterFree()
        {
            var ring = new UnsafeAudioRingBuffer(8);
            ring.Dispose();
            Assert.DoesNotThrow(ring.Dispose);
            Assert.Throws<ObjectDisposedException>(() => ring.Write(new float[1]));
            Assert.Throws<ObjectDisposedException>(() => ring.Read(new float[1]));
            Assert.Throws<ObjectDisposedException>(() => ring.Peek(new float[1]));
            Assert.Throws<ObjectDisposedException>(() => ring.Skip(1));
            Assert.Throws<ObjectDisposedException>(() => ring.TryWriteExact(new float[1]));
            Assert.Throws<ObjectDisposedException>(() => ring.TryReadExact(new float[1], 1));
        }

        private static float[] Sequence(int count)
        {
            var result = new float[count];
            for (int i = 0; i < count; i++) result[i] = i + 1;
            return result;
        }

        private sealed class Ring : IDisposable
        {
            private readonly AudioBuffer _managed;
            private readonly UnsafeAudioRingBuffer _native;
            internal Ring(bool unmanaged, int capacity, int stride)
            {
                if (unmanaged) _native = new UnsafeAudioRingBuffer(capacity, stride);
                else _managed = new AudioBuffer(capacity, stride);
            }
            internal int Readable => _native?.ReadableCount ?? _managed.ReadableCount;
            internal int Writable => _native?.WritableCount ?? _managed.WritableCount;
            internal bool IsEmpty => _native?.IsEmpty ?? _managed.IsEmpty;
            internal bool IsFull => _native?.IsFull ?? _managed.IsFull;
            internal int Write(float[] data) => _native != null ? _native.Write(data) : _managed.Write(data);
            internal int Read(float[] data) => _native != null ? _native.Read(data) : _managed.Read(data);
            internal int Peek(float[] data) => _native != null ? _native.Peek(data) : _managed.Peek(data);
            internal int Skip(int count) => _native != null ? _native.Skip(count) : _managed.Skip(count);
            internal bool ExactWrite(float[] data) => _native != null ? _native.TryWriteExact(data) : _managed.TryWriteExact(data);
            internal bool ExactRead(float[] data, int count) => _native != null ? _native.TryReadExact(data, count) : _managed.TryReadExact(data, count);
            internal void Clear() { if (_native != null) _native.Clear(); else _managed.Clear(); }
            public void Dispose() => _native?.Dispose();
        }
    }
}
