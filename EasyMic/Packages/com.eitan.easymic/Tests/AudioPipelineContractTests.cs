using System;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class AudioPipelineContractTests
    {
        [Test]
        public void Workers_ExecuteInOrder_AndChangesFlowDownstream()
        {
            using var pipeline = new AudioPipeline();
            pipeline.AddWorker(new ProbeWorker((data, state) => { for (int i = 0; i < data.Length; i++) data[i] += 2; }));
            pipeline.AddWorker(new ProbeWorker((data, state) => { for (int i = 0; i < data.Length; i++) data[i] *= 3; }));
            var state = new AudioContext(1, 48000, 3);
            pipeline.Initialize(state);
            var samples = new[] { 1f, 2f, 3f };
            pipeline.OnAudioPass(samples, state);
            CollectionAssert.AreEqual(new[] { 9f, 12f, 15f }, samples);
        }

        [TestCase(-4, 0)]
        [TestCase(0, 0)]
        [TestCase(2, 2)]
        [TestCase(100, 4)]
        public void DynamicLength_IsClampedBeforeEveryStage(int newLength, int expectedLength)
        {
            using var pipeline = new AudioPipeline();
            pipeline.AddWorker(new ProbeWorker((data, state) => state.Length = newLength));
            int seen = -1;
            pipeline.AddWorker(new ProbeWorker((data, state) => seen = data.Length));
            var state = new AudioContext(1, 48000, 4);
            pipeline.Initialize(state);
            pipeline.OnAudioPass(new float[4], state);
            Assert.That(seen, Is.EqualTo(expectedLength));
        }

        [Test]
        public void DuplicateRegistration_DoesNotReinitializeOrDuplicateWorker()
        {
            using var pipeline = new AudioPipeline();
            pipeline.Initialize(new AudioContext(2, 48000, 0));
            var worker = new ProbeWorker();
            pipeline.AddWorker(worker);
            pipeline.AddWorker(worker);
            Assert.That(pipeline.WorkerCount, Is.EqualTo(1));
            Assert.That(worker.Initializations, Is.EqualTo(1));
            Assert.That(pipeline.Contains(worker), Is.True);
            Assert.That(pipeline.GetWorker<ProbeWorker>(), Is.SameAs(worker));
        }

        [Test]
        public void FormatChangingWorker_InitializesDownstreamInOutputFormat()
        {
            using var pipeline = new AudioPipeline();
            pipeline.AddWorker(new Downmixer());
            var worker = new ProbeWorker();
            pipeline.AddWorker(worker);
            var input = new AudioContext(2, 48000, 64);
            pipeline.Initialize(input);
            Assert.That(worker.InitialChannels, Is.EqualTo(1));
            Assert.That(input.ChannelCount, Is.EqualTo(2), "Pipeline initialization must not mutate its caller's format.");
        }

        [Test]
        public void ThrowingProcessor_DoesNotPreventDownstreamProcessing()
        {
            using var pipeline = new AudioPipeline();
            pipeline.AddWorker(new ProbeWorker((data, state) => throw new InvalidOperationException("processor failure")));
            var next = new ProbeWorker();
            pipeline.AddWorker(next);
            var state = new AudioContext(1, 48000, 1);
            pipeline.Initialize(state);
            Assert.DoesNotThrow(() => pipeline.OnAudioPass(new float[1], state));
            Assert.That(next.Passes, Is.EqualTo(1));
        }

        [Test]
        public void RemoveAndDispose_AreIdempotent_AndOwnWorkersExactlyOnce()
        {
            var pipeline = new AudioPipeline();
            var first = new ProbeWorker();
            var second = new ProbeWorker { ThrowOnDispose = true };
            pipeline.AddWorker(first);
            pipeline.AddWorker(second);
            pipeline.RemoveWorker(first);
            pipeline.RemoveWorker(first);
            pipeline.RemoveWorker(null);
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.DoesNotThrow(pipeline.Dispose);
            Assert.DoesNotThrow(pipeline.Dispose);
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(pipeline.WorkerCount, Is.Zero);
            Assert.That(pipeline.GetWorker<ProbeWorker>(), Is.Null);
            Assert.Throws<ObjectDisposedException>(() => pipeline.AddWorker(new ProbeWorker()));
            Assert.Throws<ObjectDisposedException>(() => pipeline.Initialize(new AudioContext(1, 48000, 0)));
        }

        [Test]
        public void RemovedWorker_IsKeptAliveUntilInFlightPassFinishes()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var pipeline = new AudioPipeline();
            var worker = new ProbeWorker((data, state) => { entered.Set(); release.Wait(3000); });
            pipeline.AddWorker(worker);
            var state = new AudioContext(1, 48000, 1);
            pipeline.Initialize(state);
            var pass = Task.Run(() => pipeline.OnAudioPass(new float[1], state));
            try
            {
                Assert.That(entered.Wait(2000), Is.True);
                pipeline.RemoveWorker(worker);
                Assert.That(worker.Disposals, Is.Zero);
            }
            finally
            {
                release.Set();
                Assert.That(pass.Wait(3000), Is.True);
            }
            Assert.That(SpinWait.SpinUntil(() => worker.Disposals == 1, 2000), Is.True);
            pipeline.OnAudioPass(new float[1], state);
            Assert.That(worker.Passes, Is.EqualTo(1));
        }

        [Test]
        public void NewWorkers_AppearAtNextBlockBoundary()
        {
            using var pipeline = new AudioPipeline();
            var later = new ProbeWorker();
            pipeline.AddWorker(new ProbeWorker((data, state) => pipeline.AddWorker(later)));
            var state = new AudioContext(1, 48000, 1);
            pipeline.Initialize(state);
            pipeline.OnAudioPass(new float[1], state);
            Assert.That(later.Passes, Is.Zero);
            pipeline.OnAudioPass(new float[1], state);
            Assert.That(later.Passes, Is.EqualTo(1));
        }

        [Test]
        public void NullAndForbiddenWorkers_AreRejectedWithoutChangingPipeline()
        {
            using var pipeline = new AudioPipeline();
            Assert.Throws<ArgumentNullException>(() => pipeline.Initialize(null));
            Assert.Throws<ArgumentNullException>(() => pipeline.AddWorker(null));
            Assert.Throws<ArgumentNullException>(() => pipeline.Contains(null));
            Assert.Throws<InvalidOperationException>(() => pipeline.AddWorker(new MainThreadWorker()));
            Assert.That(pipeline.WorkerCount, Is.Zero);
        }

        private sealed class MainThreadWorker : ProbeWorker, IMainThreadAudioProcessor { }
    }

    internal delegate void ProbePass(Span<float> samples, AudioContext state);

    internal class ProbeWorker : IAudioWorker
    {
        private readonly ProbePass _pass;
        internal int Initializations;
        internal int InitialChannels;
        internal int Passes;
        internal int Disposals;
        internal bool ThrowOnDispose;
        internal ProbeWorker(ProbePass pass = null) => _pass = pass;
        public void Initialize(AudioContext state) { Initializations++; InitialChannels = state.ChannelCount; }
        public void OnAudioPass(Span<float> samples, AudioContext state)
        {
            Interlocked.Increment(ref Passes);
            _pass?.Invoke(samples, state);
        }
        public void Dispose()
        {
            Interlocked.Increment(ref Disposals);
            if (ThrowOnDispose) throw new InvalidOperationException("dispose failure");
        }
    }
}
