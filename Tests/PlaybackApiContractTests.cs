using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class PlaybackApiContractTests
    {
        [Test]
        public void DefaultHandle_IsInertAndReportsDisposedEnqueue()
        {
            var handle = default(PlaybackHandle);
            Assert.That(handle.IsValid, Is.False);
            Assert.That(handle.IsPlaying, Is.False);
            Assert.That(handle.Volume, Is.Zero);
            Assert.That(handle.BufferedSeconds, Is.Zero);
            Assert.DoesNotThrow(() => { handle.Play(); handle.Pause(); handle.Stop(); handle.CompleteStream(); handle.Dispose(); handle.Volume = 1f; });
            Assert.That(handle.TryEnqueue(new float[4], 4, 2, 48000).Status, Is.EqualTo(EasyMicQueueStatus.Disposed));
        }

        [Test]
        public void NullClip_FailsBeforeOpeningDevice()
        {
            Assert.Throws<ArgumentNullException>(() => AudioPlayback.PlayClip(null));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ClipCompletion_RunsCallbackOnce_AndHonorsAutoDispose(bool autoDispose)
        {
            using var system = new ManagedPlaybackTestSystem();
            var previous = (System.Threading.SynchronizationContext)typeof(EasyMicAudioEventPump).GetField("s_mainContext", AudioTestAccess.Static).GetValue(null);
            var context = new QueuedAudioContext();
            var clip = AudioClip.Create("API completion test", 480, 2, 48000, false);
            var handle = default(PlaybackHandle);
            try
            {
                handle = AudioPlayback.PlayClip(clip, false, 1f, autoDispose);
                int id = (int)typeof(PlaybackHandle).GetField("_id", AudioTestAccess.Instance).GetValue(handle);
                var sessions = (System.Collections.Generic.Dictionary<int, PlaybackAudioSession>)typeof(AudioPlayback).GetField("s_playbacks", AudioTestAccess.Static).GetValue(null);
                var session = sessions[id];
                int completions = 0;
                // Auto-disposal is registered first; inspect its effect in the public callback.
                handle.RegisterCompletedCallback(() => completions++);
                EasyMicAudioEventPump.SetMainThreadContext(context);
                AudioTestAccess.Eventually(() => session.Source.HasPendingStreamEnd, "Clip feeder did not finish.");
                var envelope = new MixerGainEnvelope();
                session.Source.RenderAdditive(new float[960], 2, 48000, ref envelope, 1f, 0);
                AudioTestAccess.Eventually(() => context.Count > 0, "Completion was not posted.");
                context.Drain();
                Assert.That(completions, Is.EqualTo(1));
                Assert.That(handle.IsValid, Is.EqualTo(!autoDispose));
                Assert.That(handle.IsPlaying, Is.False);
            }
            finally
            {
                handle.Dispose();
                context.Drain();
                typeof(EasyMicAudioEventPump).GetField("s_mainContext", AudioTestAccess.Static).SetValue(null, previous);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void IndependentHandles_PreserveEachOthersState_AndCopiedHandleCannotResurrectDisposedSession()
        {
            using var system = new ManagedPlaybackTestSystem();
            using var first = AudioPlayback.CreateStream(0.25f);
            using var second = AudioPlayback.CreateStream(0.75f);
            var copy = first;
            Assert.That(first.IsValid && second.IsValid, Is.True);
            first.Pause();
            Assert.That(first.IsPlaying, Is.False);
            Assert.That(second.IsPlaying, Is.True);
            first.Play();
            Assert.That(copy.IsPlaying, Is.True);
            Assert.That(first.Volume, Is.EqualTo(0.25f));
            Assert.That(second.Volume, Is.EqualTo(0.75f));
            first.Dispose();
            Assert.That(copy.IsValid, Is.False);
            Assert.That(second.IsValid, Is.True);
            copy.Play(); copy.Dispose();
            Assert.That(copy.TryEnqueue(new float[4], 4, 2, 48000).Status, Is.EqualTo(EasyMicQueueStatus.Disposed));
        }
    }
}
