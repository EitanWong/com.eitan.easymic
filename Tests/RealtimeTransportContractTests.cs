using System;
using System.Collections.Concurrent;
using System.Threading;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class RealtimeTransportContractTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        public void Capture_PreservesOrderFormatAndCompleteFrames_OnWorkerThread(int channels)
        {
            using var pipeline = new AudioPipeline();
            var reader = new Probe();
            pipeline.AddWorker(reader);
            pipeline.Initialize(new AudioContext(channels, 48000, 0));
            var telemetry = new RealtimeAudioTelemetry();
            using var transport = new CaptureAudioTransport(pipeline, channels, 48000, EasyMicLatencyProfile.LowLatency, telemetry, 17);
            var first = new float[channels * 8 + channels - 1];
            for (int i = 0; i < first.Length; i++) first[i] = i * 0.01f;
            var second = new float[channels * 4];
            Array.Fill(second, 0.75f);
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            Assert.That(transport.TryWrite(first, 8), Is.True);
            Assert.That(transport.TryWrite(second, 4), Is.True);
            AudioTestAccess.Eventually(() => reader.Blocks.Count == 2, "Capture worker did not drain both blocks.");
            Assert.That(reader.Blocks.TryDequeue(out var actual), Is.True);
            CollectionAssert.AreEqual(new ArraySegment<float>(first, 0, channels * 8), actual);
            Assert.That(reader.Blocks.TryDequeue(out actual), Is.True);
            CollectionAssert.AreEqual(second, actual);
            Assert.That(reader.Channels, Is.EqualTo(channels));
            Assert.That(reader.Rate, Is.EqualTo(48000));
            Assert.That(reader.Delay, Is.InRange(17, 500));
            Assert.That(reader.ThreadId, Is.Not.EqualTo(callerThread));
            Assert.That(reader.TransportThread, Is.True);
            Assert.That(telemetry.GetSnapshot().FramesReceived, Is.EqualTo(12));
        }

        [Test]
        public void Capture_HugeFrameCountIsClampedWithoutIntegerOverflow()
        {
            using var pipeline = new AudioPipeline();
            var probe = new Probe();
            pipeline.AddWorker(probe);
            pipeline.Initialize(new AudioContext(2, 48000, 0));
            using var transport = new CaptureAudioTransport(pipeline, 2, 48000, EasyMicLatencyProfile.LowLatency, null);
            Assert.That(transport.TryWrite(new[] { 1f, 2f, 3f, 4f }, int.MaxValue), Is.True);
            AudioTestAccess.Eventually(() => probe.Blocks.Count == 1, "Oversize frame count lost a valid input block.");
        }

        [Test]
        public void Capture_FullQueueDropsWholePacket_AndRecoversAfterDrain()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var pipeline = new AudioPipeline();
            var probe = new Probe { Entered = entered, Release = release };
            pipeline.AddWorker(probe);
            pipeline.Initialize(new AudioContext(1, 8000, 0));
            var telemetry = new RealtimeAudioTelemetry();
            var transport = new CaptureAudioTransport(pipeline, 1, 8000, EasyMicLatencyProfile.UltraLowLatency, telemetry);
            try
            {
                Assert.That(transport.TryWrite(new[] { 0.1f }, 1), Is.True);
                Assert.That(entered.Wait(3000), Is.True, "Worker did not enter the controlled processor.");
                int packetSamples = ((UnsafeAudioRingBuffer)AudioTestAccess.Get(transport, "_queue")).Capacity - 3;
                Assert.That(transport.TryWrite(new float[packetSamples], packetSamples), Is.True);
                Assert.That(transport.TryWrite(new float[100], 100), Is.False);
                Assert.That(telemetry.GetSnapshot().TransportOverruns, Is.EqualTo(1));
                Assert.That(telemetry.GetSnapshot().FramesDropped, Is.EqualTo(100));
                release.Set();
                AudioTestAccess.Eventually(() => probe.Blocks.Count == 2, "Queued packet did not recover after overflow.");
                Assert.That(transport.TryWrite(new[] { 0.2f }, 1), Is.True);
                AudioTestAccess.Eventually(() => probe.Blocks.Count == 3, "Capture stopped after overflow.");
            }
            finally { release.Set(); transport.Dispose(); }
        }

        [Test]
        public void Capture_DisposeDrainsAcceptedAudio_AndRejectsFurtherWrites()
        {
            using var pipeline = new AudioPipeline();
            var probe = new Probe();
            pipeline.AddWorker(probe);
            pipeline.Initialize(new AudioContext(2, 48000, 0));
            var transport = new CaptureAudioTransport(pipeline, 2, 48000, EasyMicLatencyProfile.Balanced, null);
            Assert.That(transport.TryWrite(new float[20], 10), Is.True);
            transport.Dispose();
            transport.Dispose();
            Assert.That(probe.Blocks.Count, Is.EqualTo(1));
            Assert.That(transport.TryWrite(new float[2], 1), Is.False);
            Assert.That(transport.TryWrite(Array.Empty<float>(), 0), Is.False);
        }

        [TestCase(EasyMicLatencyProfile.UltraLowLatency)]
        [TestCase(EasyMicLatencyProfile.LowLatency)]
        [TestCase(EasyMicLatencyProfile.Balanced)]
        [TestCase(EasyMicLatencyProfile.SafeStreaming)]
        public void Playback_RendersAwayFromCaller_AndIsolatesThrowingSinks(EasyMicLatencyProfile profile)
        {
            using var mixer = new AudioMixer();
            var telemetry = new RealtimeAudioTelemetry();
            var frames = new ConcurrentQueue<float[]>();
            int callbackThread = 0, delay = -1;
            bool tagged = false;
            using var transport = new PlaybackRenderTransport(mixer, 2, 48000, profile, telemetry,
                (data, ch, rate) => throw new InvalidOperationException("raw subscriber failure"),
                (data, ch, rate) => { callbackThread = Thread.CurrentThread.ManagedThreadId; tagged = EasyMicThreading.IsTransportThread; frames.Enqueue(data.ToArray()); },
                (data, ch, rate, ms) => { delay = ms; throw new InvalidOperationException("reference failure"); }, 13);
            AudioTestAccess.Eventually(() => frames.Count >= 2, "Playback worker stopped after subscriber exception.");
            Assert.That(callbackThread, Is.Not.EqualTo(Thread.CurrentThread.ManagedThreadId));
            Assert.That(tagged, Is.True);
            Assert.That(delay, Is.InRange(13, 500));
            var destination = new float[128];
            Array.Fill(destination, 99f);
            Assert.That(transport.ReadInto(destination), Is.EqualTo(destination.Length));
            foreach (var sample in destination) Assert.That(sample, Is.Zero, "Silent graph must overwrite destination.");
        }

        [Test]
        public void Playback_UnderrunZeroFillsOnlyCompleteFrames_AndReportsTelemetry()
        {
            using var mixer = new AudioMixer();
            var telemetry = new RealtimeAudioTelemetry();
            using var transport = new PlaybackRenderTransport(mixer, 2, 8000, EasyMicLatencyProfile.UltraLowLatency, telemetry, null, null, null, 0);
            var output = new float[65537];
            Array.Fill(output, 99f);
            int read = transport.ReadInto(output);
            Assert.That(read % 2, Is.Zero);
            Assert.That(read, Is.LessThan(65536));
            for (int i = 0; i < 65536; i++) Assert.That(output[i], Is.Zero);
            Assert.That(output[65536], Is.EqualTo(99f), "Incomplete frame must remain untouched.");
            Assert.That(telemetry.GetSnapshot().TransportUnderruns, Is.EqualTo(1));
            Assert.That(telemetry.GetSnapshot().ZeroFilledFrames, Is.EqualTo((65536 - read) / 2));
            transport.Dispose();
            var sentinel = new[] { 99f, 99f };
            Assert.That(transport.ReadInto(sentinel), Is.Zero);
            CollectionAssert.AreEqual(new[] { 99f, 99f }, sentinel);
        }

        private sealed class Probe : AudioWriter
        {
            internal readonly ConcurrentQueue<float[]> Blocks = new ConcurrentQueue<float[]>();
            internal ManualResetEventSlim Entered, Release;
            internal int Channels, Rate, Delay, ThreadId;
            internal bool TransportThread;
            protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
            {
                Entered?.Set();
                if (Release != null && !Release.Wait(5000)) throw new TimeoutException("Test did not release capture worker.");
                Channels = state.ChannelCount; Rate = state.SampleRate; Delay = state.EstimatedCaptureDelayMs;
                ThreadId = Thread.CurrentThread.ManagedThreadId; TransportThread = EasyMicThreading.IsTransportThread;
                Blocks.Enqueue(buffer.ToArray());
            }
        }
    }
}
