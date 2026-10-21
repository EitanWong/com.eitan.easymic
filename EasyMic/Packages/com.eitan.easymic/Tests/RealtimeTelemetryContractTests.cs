using System;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class RealtimeTelemetryContractTests
    {
        [Test]
        public void EmptySnapshot_HasZeroCountersAndFiniteDurations()
        {
            var snapshot = new RealtimeAudioTelemetry().GetSnapshot();
            Assert.That(snapshot.CallbackCount, Is.Zero);
            Assert.That(snapshot.ActiveCallbacks, Is.Zero);
            Assert.That(snapshot.MinQueueDepthSamples, Is.Zero);
            Assert.That(snapshot.AverageCallbackMicroseconds, Is.Zero);
            Assert.That(snapshot.MaxWorkerMicroseconds, Is.Zero);
        }

        [Test]
        public void ConcurrentWriters_PreserveCountersAndQueueExtrema()
        {
            var telemetry = new RealtimeAudioTelemetry();
            Parallel.For(0, 4, worker =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var start = telemetry.BeginCallback();
                    telemetry.AddFramesReceived(2);
                    telemetry.AddFramesDropped(1);
                    telemetry.AddZeroFilledFrames(3);
                    telemetry.IncrementCallbackException();
                    telemetry.IncrementProcessorException();
                    telemetry.IncrementEventQueueDrop();
                    telemetry.IncrementTransportOverrun();
                    telemetry.IncrementTransportUnderrun();
                    telemetry.IncrementWorkerLate();
                    telemetry.ObserveWorkerTicks(i + 1);
                    telemetry.ObserveQueueDepth(i);
                    telemetry.EndCallback(start);
                }
            });
            var snapshot = telemetry.GetSnapshot();
            Assert.That(snapshot.CallbackCount, Is.EqualTo(4000));
            Assert.That(snapshot.FramesReceived, Is.EqualTo(8000));
            Assert.That(snapshot.FramesDropped, Is.EqualTo(4000));
            Assert.That(snapshot.ZeroFilledFrames, Is.EqualTo(12000));
            Assert.That(snapshot.CallbackExceptions, Is.EqualTo(4000));
            Assert.That(snapshot.ProcessorExceptions, Is.EqualTo(4000));
            Assert.That(snapshot.EventQueueDrops, Is.EqualTo(4000));
            Assert.That(snapshot.TransportOverruns, Is.EqualTo(4000));
            Assert.That(snapshot.TransportUnderruns, Is.EqualTo(4000));
            Assert.That(snapshot.WorkerLateCount, Is.EqualTo(4000));
            Assert.That(snapshot.ActiveCallbacks, Is.Zero);
            Assert.That(snapshot.MinQueueDepthSamples, Is.Zero);
            Assert.That(snapshot.MaxQueueDepthSamples, Is.EqualTo(999));
            Assert.That(snapshot.WorkerTicksMax, Is.EqualTo(1000));
        }

        [Test]
        public void InvalidObservations_DoNotSubtractCountersOrCreateNegativeDepths()
        {
            var telemetry = new RealtimeAudioTelemetry();
            telemetry.AddFramesReceived(-1); telemetry.AddFramesDropped(-1); telemetry.AddZeroFilledFrames(-1);
            telemetry.ObserveQueueDepth(-1); telemetry.ObserveWorkerTicks(-1);
            var snapshot = telemetry.GetSnapshot();
            Assert.That(snapshot.FramesReceived + snapshot.FramesDropped + snapshot.ZeroFilledFrames, Is.Zero);
            Assert.That(snapshot.LastQueueDepthSamples, Is.Zero);
            Assert.That(snapshot.MinQueueDepthSamples, Is.Zero);
            Assert.That(snapshot.WorkerTicksMax, Is.Zero);
        }

        [Test]
        public void NestedThreadScopes_RestoreTagsAfterExceptions_AndStayThreadLocal()
        {
            Assert.That(EasyMicThreading.IsRealtimeSensitiveThread, Is.False);
            using (EasyMicThreading.EnterAudioThread())
            {
                Assert.That(EasyMicThreading.IsAudioThread, Is.True);
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using var audio = EasyMicThreading.EnterAudioThread();
                    using var transport = EasyMicThreading.EnterTransportThread();
                    Assert.That(EasyMicThreading.IsTransportThread, Is.True);
                    throw new InvalidOperationException();
                });
                Assert.That(EasyMicThreading.IsAudioThread, Is.True);
                Assert.That(EasyMicThreading.IsTransportThread, Is.False);
                Assert.That(Task.Run(() => EasyMicThreading.IsRealtimeSensitiveThread).Result, Is.False);
            }
            Assert.That(EasyMicThreading.IsRealtimeSensitiveThread, Is.False);
        }
    }
}
