using System;
using System.Collections.Generic;
using System.Threading;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class PlaybackSessionLifecycleTests
    {
        private ManagedPlaybackTestSystem _system;
        private QueuedAudioContext _context;
        private SynchronizationContext _previousContext;
        private PlaybackAudioSession _session;
        private readonly List<AudioClip> _clips = new List<AudioClip>();

        [SetUp]
        public void SetUp()
        {
            _system = new ManagedPlaybackTestSystem();
            _previousContext = (SynchronizationContext)typeof(EasyMicAudioEventPump).GetField("s_mainContext", AudioTestAccess.Static).GetValue(null);
            _context = new QueuedAudioContext();
            EasyMicAudioEventPump.SetMainThreadContext(_context);
            _session = new PlaybackAudioSession("test");
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            foreach (var clip in _clips) UnityEngine.Object.DestroyImmediate(clip);
            _clips.Clear();
            _context.Drain();
            typeof(EasyMicAudioEventPump).GetField("s_mainContext", AudioTestAccess.Static).SetValue(null, _previousContext);
            _system.Dispose();
        }

        [Test]
        public void Defaults_AreIdleAndAllocateNoSource()
        {
            Assert.That(_session.Source, Is.Null);
            Assert.That(_session.Clip, Is.Null);
            Assert.That(_session.IsPlaying, Is.False);
            Assert.That(_session.BufferedSeconds, Is.Zero);
            Assert.That(_session.ProgressNormalized, Is.Zero);
            Assert.That(_session.EstimatedClipCacheBytes, Is.Zero);
        }

        [TestCase(-1f, 0f)]
        [TestCase(0.75f, 0.75f)]
        [TestCase(3f, 2f)]
        public void Properties_AreAppliedBeforeAndAfterSourceCreation(float volume, float expected)
        {
            _session.Volume = volume;
            _session.Mute = true;
            _session.Solo = true;
            _session.Play();
            Assert.That(_session.Source.Volume, Is.EqualTo(expected));
            Assert.That(_session.Source.Mute, Is.True);
            Assert.That(_session.Source.Solo, Is.True);
            _session.Mute = false;
            _session.Solo = false;
            _session.Volume = 0.4f;
            Assert.That(_session.Source.Volume, Is.EqualTo(0.4f));
            Assert.That(_session.Source.Mute, Is.False);
            Assert.That(_session.Source.Solo, Is.False);
        }

        [TestCase(0, 2, 48000)]
        [TestCase(-1, 2, 48000)]
        [TestCase(8, 0, 48000)]
        [TestCase(8, 2, 0)]
        public void InvalidEnqueue_DoesNotCreateSource(int count, int channels, int rate)
        {
            var result = _session.TryEnqueue(new float[8], count, channels, rate);
            Assert.That(result.Status, Is.EqualTo(EasyMicQueueStatus.InvalidFormat));
            Assert.That(_session.Source, Is.Null);
        }

        [Test]
        public void NullAndEmptyEnqueue_AreRejectedWithoutHardware()
        {
            Assert.That(_session.TryEnqueue(null, 8).Status, Is.EqualTo(EasyMicQueueStatus.InvalidFormat));
            Assert.That(_session.TryEnqueue(Array.Empty<float>(), 8, 2, 48000).Status, Is.EqualTo(EasyMicQueueStatus.InvalidFormat));
            Assert.That(_session.Source, Is.Null);
        }

        [Test]
        public void PauseAndResume_PreserveQueuedAudio_WhilePlayRestarts()
        {
            var input = new float[100];
            Array.Fill(input, 0.5f);
            Assert.That(_session.TryEnqueue(input, 100, 2, 48000).Success, Is.True);
            int queued = _session.Source.QueuedSamples;
            _session.Pause();
            Assert.That(_session.IsPlaying, Is.False);
            Assert.That(_session.Source.QueuedSamples, Is.EqualTo(queued));
            _session.Resume();
            Assert.That(_session.IsPlaying, Is.True);
            Assert.That(_session.Source.QueuedSamples, Is.EqualTo(queued));
            _session.Play();
            var output = new float[100];
            var envelope = new MixerGainEnvelope();
            _session.Source.RenderAdditive(output, 2, 48000, ref envelope, 1f, 0);
            foreach (var sample in output) Assert.That(sample, Is.Zero, "Restart must discard previously queued audio at the render boundary.");
            Assert.That(_session.Source.QueuedSamples, Is.Zero);
        }

        [Test]
        public void Streaming_DoesNotModifyCallerData_AndClampsCountToArray()
        {
            var input = new float[100];
            Array.Fill(input, 0.5f);
            var result = _session.TryEnqueue(input, int.MaxValue, 2, 48000);
            Assert.That(result.SamplesRequested, Is.EqualTo(100));
            Assert.That(result.SamplesWritten, Is.EqualTo(100));
            CollectionAssert.AreEqual(new float[100].Populate(0.5f), input);
        }

        [Test]
        public void CompleteStream_DrainsOnce_AndCompletionCanStartAnotherBatch()
        {
            int completed = 0;
            _session.OnBatchCompleted += s => { completed++; s.TryEnqueue(new float[20], 20, 2, 48000); };
            _session.TryEnqueue(new float[100], 100, 2, 48000);
            EasyMicAudioEventPump.SetMainThreadContext(_context);
            _session.CompleteStream();
            int queued = _session.Source.QueuedSamples;
            _session.CompleteStream();
            Assert.That(_session.Source.QueuedSamples, Is.EqualTo(queued), "Repeated completion must not append another tail.");
            Render(_session.Source, queued);
            AudioTestAccess.Eventually(() => _context.Count > 0, "Completion was not posted.");
            _context.Drain();
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(_session.IsPlaying, Is.True);
            Assert.That(_session.Source.QueuedSamples, Is.EqualTo(20));
        }

        [Test]
        public void CompletingEmptySession_RaisesIdleWithoutBatchCompletion()
        {
            int batches = 0, idle = 0;
            _session.OnBatchCompleted += s => batches++;
            _session.OnStreamIdle += s => idle++;
            _session.CompleteStream();
            Assert.That(batches, Is.Zero);
            Assert.That(idle, Is.EqualTo(1));
            Assert.That(_session.Source, Is.Null);
        }

        [Test]
        public void Backpressure_DoesNotMarkPartiallyAcceptedBatchComplete()
        {
            _session.Play();
            var input = new float[_session.Source.FreeSamples + 200];
            var result = _session.TryEnqueue(input, input.Length, 2, 48000, true);
            Assert.That(result.Status, Is.EqualTo(EasyMicQueueStatus.Partial));
            Assert.That(_session.Source.HasPendingStreamEnd, Is.False);
        }

        [Test]
        public void ClipCache_UsesNewClipDataAndTracksMemory_WhenReplacingClip()
        {
            var first = Clip(240, 1, 0.2f);
            var second = Clip(480, 2, 0.4f);
            _session.PlayClip(first, false, false);
            Assert.That(_session.Clip, Is.SameAs(first));
            Assert.That(_session.EstimatedClipCacheBytes, Is.EqualTo(240 * 4));
            _session.PlayClip(second, false, false);
            Assert.That(_session.EstimatedClipCacheBytes, Is.EqualTo(480 * 2 * 4));
            var cache = (float[])AudioTestAccess.Get(_session, "_clipCache");
            Assert.That(cache.Length, Is.EqualTo(960));
            Assert.That(cache[0], Is.EqualTo(0.4f).Within(1e-5));
            Assert.That(_session.IsPlaying, Is.False);
        }

        [Test]
        public void NonLoopingClip_FeedsAllFramesAndStopsAfterDrain()
        {
            _session.PlayClip(Clip(480, 2, 0.2f), false, true);
            AudioTestAccess.Eventually(() => _session.Source.HasPendingStreamEnd, "Clip feeder failed to mark the last chunk.");
            Assert.That(_session.Source.QueuedSamples, Is.EqualTo(960));
            Render(_session.Source, 960);
            Assert.That(_session.IsPlaying, Is.False);
            Assert.That(_session.ProgressNormalized, Is.EqualTo(1f));
        }

        [Test]
        public void StoppedOutput_StopsClipFeederWithoutRestartingDeviceFromWorker()
        {
            _session.PlayClip(Clip(48000, 2, 0.2f), true, true);
            var feeder = (Thread)AudioTestAccess.Get(_session, "_feedThread");
            AudioTestAccess.Set(_system.System, "_running", false);
            AudioTestAccess.Eventually(() => !feeder.IsAlive, "Clip feeder must stop when its output system stops.");
            Assert.That(_system.System.IsRunning, Is.False);
            Assert.That(_session.IsPlaying, Is.False);
        }

        [Test]
        public void StopWithClearClip_ReleasesClipReferenceAndCache()
        {
            _session.PlayClip(Clip(240, 1, 0.2f), false, false);
            _session.Stop(clearClip: true);
            Assert.That(_session.Clip, Is.Null);
            Assert.That(_session.EstimatedClipCacheBytes, Is.Zero);
            Assert.That(AudioTestAccess.Get(_session, "_clipCache"), Is.Null);
            Assert.That(_session.Source.QueuedSamples, Is.Zero);
        }

        [Test]
        public void Dispose_IsIdempotent_AndMutatingMethodsRejectUseAfterDispose()
        {
            _session.Play();
            int disposed = 0;
            _session.OnSessionDisposed += s => disposed++;
            _session.Dispose();
            _session.Dispose();
            Assert.That(disposed, Is.EqualTo(1));
            Assert.That(_session.Source, Is.Null);
            Assert.Throws<ObjectDisposedException>(() => _session.Play());
            Assert.Throws<ObjectDisposedException>(() => _session.Resume());
            Assert.Throws<ObjectDisposedException>(() => _session.Pause());
            Assert.Throws<ObjectDisposedException>(() => _session.CompleteStream());
            Assert.Throws<ObjectDisposedException>(() => _session.TryEnqueue(new float[4], 4));
            Assert.Throws<ObjectDisposedException>(() => _session.PlayClip(null, false, false));
            Assert.DoesNotThrow(() => _session.Stop());
        }

        private AudioClip Clip(int frames, int channels, float value)
        {
            var clip = AudioClip.Create("test", frames, channels, 48000, false);
            var data = new float[frames * channels];
            Array.Fill(data, value);
            Assert.That(clip.SetData(data, 0), Is.True);
            _clips.Add(clip);
            return clip;
        }
        private static void Render(PlaybackAudioSource source, int samples)
        {
            var gain = new MixerGainEnvelope();
            source.RenderAdditive(new float[samples], source.Channels, source.SampleRate, ref gain, 1f, 0);
        }
    }

    internal static class TestArrayExtensions
    {
        internal static float[] Populate(this float[] array, float value) { Array.Fill(array, value); return array; }
    }
}
