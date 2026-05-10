using System;
using System.IO;
using System.Reflection;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class AudioReaderSafetyTests
    {
        [Test]
        public void WorkerLoop_DoesNotUseLocalloc()
        {
            var workerLoop = typeof(AudioReader).GetMethod("WorkerLoop", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(workerLoop, Is.Not.Null);

            var body = workerLoop.GetMethodBody();
            Assert.That(body, Is.Not.Null);

            var il = body.GetILAsByteArray();
            Assert.That(il, Is.Not.Null.And.Not.Empty);

            for (int i = 0; i < il.Length - 1; i++)
            {
                bool isLocalloc = il[i] == 0xFE && il[i + 1] == 0x0F;
                Assert.That(isLocalloc, Is.False, "AudioReader.WorkerLoop must not use stackalloc/localloc inside its long-lived loop.");
            }
        }

        [Test]
        public void AudioReaderDerivedDisposers_CallBaseDispose()
        {
            string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages/com.eitan.easymic", "Runtime"));
            Assert.That(Directory.Exists(sourceRoot), Is.True, $"Runtime source root was not found: {sourceRoot}");

            var files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var source = File.ReadAllText(file);
                if (!source.Contains(": AudioReader", StringComparison.Ordinal) ||
                    !source.Contains("override void Dispose()", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(
                    source.Contains("base.Dispose();", StringComparison.Ordinal),
                    Is.True,
                    $"AudioReader subclass disposer must call base.Dispose(): {file}");
            }
        }

        [Test]
        public void Dispose_DrainsQueuedAudioBeforeWorkerStops()
        {
            var reader = new DrainProbeReader();
            var state = new AudioContext(1, 16000, 4);
            var frame = new float[] { 0.1f, -0.2f, 0.3f, -0.4f };

            reader.Initialize(state);
            reader.OnAudioPass(frame, state);
            reader.Dispose();

            Assert.That(reader.TotalSamplesRead, Is.EqualTo(frame.Length));
        }

        [Test]
        public void UnsafeAudioRingBuffer_WrapsWithoutAllocatingManagedStorage()
        {
            using var ring = new UnsafeAudioRingBuffer(7);
            Span<float> firstRead = stackalloc float[4];
            Span<float> secondRead = stackalloc float[6];

            Assert.That(ring.TryWriteExact(new float[] { 1f, 2f, 3f, 4f, 5f, 6f }), Is.True);
            Assert.That(ring.TryReadExact(firstRead, 4), Is.True);
            Assert.That(firstRead.ToArray(), Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));

            Assert.That(ring.TryWriteExact(new float[] { 7f, 8f, 9f, 10f }), Is.True);
            Assert.That(ring.TryReadExact(secondRead, 6), Is.True);
            Assert.That(secondRead.ToArray(), Is.EqualTo(new[] { 5f, 6f, 7f, 8f, 9f, 10f }));
        }

        [Test]
        public void UnsafeAudioRingBuffer_PartialReadWriteMatchesAudioBufferSemantics()
        {
            using var ring = new UnsafeAudioRingBuffer(3);
            Span<float> read = stackalloc float[4];

            Assert.That(ring.Capacity, Is.EqualTo(3));
            Assert.That(ring.IsEmpty, Is.True);
            Assert.That(ring.Write(new float[] { 1f, 2f, 3f, 4f }), Is.EqualTo(3));
            Assert.That(ring.IsFull, Is.True);
            Assert.That(ring.Read(read), Is.EqualTo(3));
            Assert.That(read.Slice(0, 3).ToArray(), Is.EqualTo(new[] { 1f, 2f, 3f }));
            Assert.That(ring.IsEmpty, Is.True);
        }

        private sealed class DrainProbeReader : AudioReader
        {
            public int TotalSamplesRead { get; private set; }

            protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
            {
                TotalSamplesRead += audiobuffer.Length;
            }
        }
    }
}
