using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class AudioMixerContractTests
    {
        [Test]
        public void GraphRegistration_IsIdempotent_AndRemovalDoesNotDisposeSources()
        {
            using var mixer = NewMixer();
            using var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            mixer.AddSource(source);
            Assert.That(mixer.GetSources().Length, Is.EqualTo(1));
            mixer.RemoveSource(source);
            mixer.RemoveSource(source);
            mixer.RemoveSource(null);
            Assert.That(mixer.GetSources(), Is.Empty);
            Assert.That(source.Enqueue(new[] { 0.25f }), Is.EqualTo(1));
        }

        [Test]
        public void GraphRejectsNullsAndCycles_BeforeTheyCanRecurse()
        {
            using var root = NewMixer();
            using var child = NewMixer();
            using var leaf = NewMixer();
            Assert.Throws<ArgumentNullException>(() => root.AddSource(null));
            Assert.Throws<ArgumentNullException>(() => root.AddMixer(null));
            Assert.Throws<InvalidOperationException>(() => root.AddMixer(root));
            root.AddMixer(child);
            child.AddMixer(leaf);
            Assert.Throws<InvalidOperationException>(() => leaf.AddMixer(root));
            Assert.That(leaf.GetChildren(), Is.Empty);
            Assert.That(root.GetChildren().Length, Is.EqualTo(1));
        }

        [Test]
        public void Render_IsAdditive_AndMasterVolumeAppliesAfterNormalization()
        {
            using var mixer = NewMixer();
            mixer.Headroom = 1f;
            mixer.MasterVolume = 0.5f;
            using var first = Source(mixer, 0.2f);
            using var second = Source(mixer, 0.6f);
            var output = RenderSettled(mixer, 0.1f);
            Assert.That(output[output.Length - 1], Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void SoloSource_ExcludesOtherSources_AndUnsoloRestoresMix()
        {
            using var mixer = NewMixer();
            mixer.Headroom = 1f;
            using var first = Source(mixer, 0.2f);
            using var second = Source(mixer, 0.6f);
            first.Solo = true;
            Assert.That(Last(RenderSettled(mixer)), Is.EqualTo(0.2f).Within(1e-5f));
            first.Solo = false;
            Assert.That(Last(RenderSettled(mixer)), Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void SoloChildMixer_ExcludesSiblingBranch()
        {
            using var root = NewMixer();
            using var child = NewMixer();
            root.Headroom = child.Headroom = 1;
            root.AddMixer(child);
            using var nested = Source(child, 0.2f);
            using var sibling = Source(root, 0.6f);
            nested.Solo = true;
            Assert.That(Last(RenderSettled(root)), Is.EqualTo(0.2f).Within(1e-5f));
        }

        [Test]
        public void MutingChild_NotifiesParentAndRebalancesRemainingBranch()
        {
            using var root = NewMixer();
            using var child = NewMixer();
            root.Headroom = child.Headroom = 1;
            root.AddMixer(child);
            using var nested = Source(child, 0.2f);
            using var sibling = Source(root, 0.6f);
            Assert.That(Last(RenderSettled(root)), Is.EqualTo(0.4f).Within(1e-5f));
            child.Mute = true;
            Assert.That(Last(RenderSettled(root)), Is.EqualTo(0.6f).Within(1e-5f));
        }

        [Test]
        public void MutedMixer_LeavesDestinationAndQueuesUntouched()
        {
            using var mixer = NewMixer();
            using var source = Source(mixer, 0.2f);
            int queued = source.QueuedSamples;
            mixer.Mute = true;
            var output = RenderSettled(mixer, 0.1f);
            foreach (float sample in output) Assert.That(sample, Is.EqualTo(0.1f));
            Assert.That(source.QueuedSamples, Is.EqualTo(queued));
        }

        [Test]
        public void GainRamp_IsSmoothAndConvergesToTarget()
        {
            using var mixer = NewMixer();
            mixer.Headroom = 1;
            using var source = Source(mixer, 0.5f);
            var output = RenderSettled(mixer);
            Assert.That(output[0], Is.Zero);
            for (int i = 1; i < 256; i++)
                Assert.That(output[i], Is.GreaterThanOrEqualTo(output[i - 1] - 1e-6f));
            Assert.That(Last(output), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void DisposingMixer_DetachesGraphButDoesNotOwnSourceLifetime()
        {
            var mixer = NewMixer();
            using var source = Source(mixer, 0.2f);
            mixer.Dispose();
            mixer.Dispose();
            Assert.That(mixer.GetSources(), Is.Empty);
            Assert.DoesNotThrow(() => source.Volume = 0.5f);
            Assert.Throws<ObjectDisposedException>(() => mixer.AddSource(source));
        }

        private static AudioMixer NewMixer()
        {
            var mixer = new AudioMixer();
            mixer.Initialize(1, 48000);
            return mixer;
        }

        private static PlaybackAudioSource Source(AudioMixer mixer, float sample)
        {
            var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            var data = new float[4096];
            for (int i = 0; i < data.Length; i++) data[i] = sample;
            source.Enqueue(data);
            return source;
        }

        private static float[] RenderSettled(AudioMixer mixer, float initial = 0)
        {
            var data = new float[512];
            for (int i = 0; i < data.Length; i++) data[i] = initial;
            mixer.RenderAdditive(data, 1, 48000);
            return data;
        }

        private static float Last(float[] data) => data[data.Length - 1];
    }
}
