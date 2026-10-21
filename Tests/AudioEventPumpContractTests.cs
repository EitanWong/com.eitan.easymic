using System;
using System.Threading;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class AudioEventPumpContractTests
    {
        private QueuedAudioContext _context;
        private SynchronizationContext _previousContext;
        private PlaybackAudioSource _source;
        private AudioMixer _mixer;
        private int _id;
        private float[] _received;
        private int _channels, _rate, _thread, _completions;

        [SetUp]
        public void SetUp()
        {
            _previousContext = (SynchronizationContext)typeof(EasyMicAudioEventPump).GetField("s_mainContext", AudioTestAccess.Static).GetValue(null);
            EasyMicAudioEventPump.Shutdown();
            _received = null;
            _completions = 0;
            _context = new QueuedAudioContext();
            EasyMicAudioEventPump.SetMainThreadContext(_context);
            _mixer = new AudioMixer();
            _source = new PlaybackAudioSource(2, 48000, attachTo: _mixer);
            EasyMicAudioEventPump.SetMainThreadContext(_context);
            _id = (int)AudioTestAccess.Get(_source, "_eventSourceId");
            _source.OnAudioPlayback += (data, channels, rate) =>
            {
                _received = (float[])data.Clone(); // Payload is pooled and valid only during dispatch.
                _channels = channels; _rate = rate;
                _thread = Thread.CurrentThread.ManagedThreadId;
            };
            _source.OnPlaybackCompleted += s => _completions++;
        }

        [TearDown]
        public void TearDown()
        {
            _source.Dispose();
            _mixer.Dispose();
            EasyMicAudioEventPump.Shutdown();
            _context.Drain();
            typeof(EasyMicAudioEventPump).GetField("s_mainContext", AudioTestAccess.Static).SetValue(null, _previousContext);
        }

        [TestCase(-1)]
        [TestCase(524288)]
        [TestCase(int.MaxValue)]
        public void InvalidOrOversizePayload_IsRejected_AndNextEventRemainsParseable(int count)
        {
            var invalid = EasyMicAudioEventPump.TryBeginPlaybackFrame(_id, 2, 48000, count);
            Assert.That(invalid.IsValid, Is.False);
            Send(new[] { 0.25f, -0.25f });
            WaitAndDrain();
            CollectionAssert.AreEqual(new[] { 0.25f, -0.25f }, _received);
        }

        [Test]
        public void DeferredFrame_CopiesData_AndDispatchesOnContextThread()
        {
            var input = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
            Send(input);
            Array.Clear(input, 0, input.Length);
            AudioTestAccess.Eventually(() => _context.Count > 0, "Frame was not posted.");
            Assert.That(_received, Is.Null, "Worker must defer delivery to the supplied context.");
            _context.Drain();
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0.3f, 0.4f }, _received);
            Assert.That(_channels, Is.EqualTo(2));
            Assert.That(_rate, Is.EqualTo(48000));
            Assert.That(_thread, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
        }

        [Test]
        public void IncompleteWriter_PadsZerosWithoutCorruptingFollowingCompletion()
        {
            var writer = EasyMicAudioEventPump.TryBeginPlaybackFrame(_id, 2, 48000, 6);
            Assert.That(writer.Write(new[] { 0.1f, 0.2f }), Is.True);
            Assert.That(writer.Remaining, Is.EqualTo(4));
            Assert.That(writer.Write(new float[5]), Is.False);
            Assert.That(writer.Remaining, Is.EqualTo(4));
            writer.Commit();
            var completed = EasyMicAudioEventPump.TryBeginPlaybackCompleted(_id, 2, 48000);
            completed.Commit();
            AudioTestAccess.Eventually(() => _context.Count >= 2, "Frame and completion were not both posted.");
            _context.Drain();
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0f, 0f, 0f, 0f }, _received);
            Assert.That(_completions, Is.EqualTo(1));
        }

        [Test]
        public void ZeroPadding_LargerThanInternalChunk_PreservesFraming()
        {
            var writer = EasyMicAudioEventPump.TryBeginPlaybackFrame(_id, 2, 48000, 2050);
            Assert.That(writer.WriteZeros(2050), Is.True);
            Assert.That(writer.Remaining, Is.Zero);
            writer.Commit();
            WaitAndDrain();
            Assert.That(_received.Length, Is.EqualTo(2050));
            foreach (var sample in _received) Assert.That(sample, Is.Zero);
        }

        [Test]
        public void UnregisterBeforeContextDispatch_DropsQueuedNotification()
        {
            Send(new float[2]);
            AudioTestAccess.Eventually(() => _context.Count > 0, "Frame was not posted.");
            EasyMicAudioEventPump.UnregisterPlaybackSource(_id);
            _context.Drain();
            Assert.That(_received, Is.Null);
        }

        [Test]
        public void ShutdownAndRestart_DoesNotDeliverOldNotificationsToNewSources()
        {
            Send(new float[2]);
            AudioTestAccess.Eventually(() => _context.Count > 0, "Frame was not posted.");
            EasyMicAudioEventPump.Shutdown();
            EasyMicAudioEventPump.SetMainThreadContext(_context);
            using var replacement = new PlaybackAudioSource(2, 48000, attachTo: _mixer);
            int deliveries = 0;
            replacement.OnAudioPlayback += (data, ch, rate) => deliveries++;
            _context.Drain();
            Assert.That(deliveries, Is.Zero, "Posted work from the previous pump generation must be invalidated.");
            Assert.That(_received, Is.Null);
        }

        [Test]
        public void ThrowingSubscriber_DoesNotPreventFollowingEventDelivery()
        {
            _source.OnAudioPlayback += (data, ch, rate) => throw new InvalidOperationException("subscriber failure");
            Send(new float[2]);
            var completed = EasyMicAudioEventPump.TryBeginPlaybackCompleted(_id, 2, 48000);
            completed.Commit();
            AudioTestAccess.Eventually(() => _context.Count >= 2, "Events were not posted.");
            Assert.DoesNotThrow(() => _context.Drain());
            Assert.That(_completions, Is.EqualTo(1));
        }

        private void Send(float[] input)
        {
            var writer = EasyMicAudioEventPump.TryBeginPlaybackFrame(_id, 2, 48000, input.Length);
            Assert.That(writer.IsValid, Is.True);
            Assert.That(writer.Write(input), Is.True);
            writer.Commit();
        }
        private void WaitAndDrain()
        {
            AudioTestAccess.Eventually(() => _context.Count > 0, "Frame was not posted.");
            _context.Drain();
        }
    }
}
