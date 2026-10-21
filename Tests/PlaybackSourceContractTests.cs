using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class PlaybackSourceContractTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        public void NativePlayback_IsAdditive_AndReportsSourceFrames(int channels)
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(channels, 48000, attachTo: mixer);
            var input = new float[32 * channels];
            var output = new float[input.Length];
            for (int i = 0; i < input.Length; i++) { input[i] = i * 0.001f; output[i] = 0.1f; }
            Assert.That(source.Enqueue(input), Is.EqualTo(input.Length));
            source.TotalSourceFrames = 64;
            Render(source, output);
            for (int i = 0; i < input.Length; i++) Assert.That(output[i], Is.EqualTo(input[i] + 0.1f).Within(1e-6f));
            Assert.That(source.PlayedSourceFrames, Is.EqualTo(32));
            Assert.That(source.NormalizedProgress, Is.EqualTo(0.5f));
            Assert.That(source.QueuedSamples, Is.Zero);
        }

        [TestCase(1, 2, 48000, 48000)]
        [TestCase(2, 1, 48000, 48000)]
        [TestCase(2, 6, 48000, 48000)]
        [TestCase(1, 1, 16000, 48000)]
        [TestCase(2, 2, 48000, 16000)]
        public void FormatConversion_ReportsConsumedInputSamples_AndPreservesConstantSignal(int inputChannels, int outputChannels, int inputRate, int outputRate)
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(outputChannels, outputRate, attachTo: mixer);
            var input = new float[96 * inputChannels];
            for (int i = 0; i < input.Length; i++) input[i] = 0.25f;
            Assert.That(source.TryEnqueue(input, inputChannels, inputRate, out int written), Is.True);
            Assert.That(written, Is.EqualTo(input.Length), "The enqueue contract uses input-domain samples.");
            int frames = (int)Math.Ceiling(96.0 * outputRate / inputRate);
            Assert.That(source.QueuedSamples, Is.EqualTo(frames * outputChannels));
            var output = new float[frames * outputChannels];
            Render(source, output);
            foreach (float value in output) Assert.That(value, Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void StereoToMono_AveragesChannels_AndMonoToStereoDuplicatesThem()
        {
            using var mixer = new AudioMixer();
            using var mono = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            mono.Enqueue(new[] { 0.2f, 0.6f, -0.2f, -0.6f }, 2, 48000);
            var output = new float[2];
            Render(mono, output);
            CollectionAssert.AreEqual(new[] { 0.4f, -0.4f }, output);
            using var stereo = new PlaybackAudioSource(2, 48000, attachTo: mixer);
            stereo.Enqueue(new[] { 0.2f, -0.4f }, 1, 48000);
            output = new float[4];
            Render(stereo, output);
            CollectionAssert.AreEqual(new[] { 0.2f, 0.2f, -0.4f, -0.4f }, output);
        }

        [Test]
        public void Pause_PreservesQueueAndDestination_ResumeConsumesIt()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            source.Enqueue(new[] { 0.25f, 0.5f });
            source.Pause();
            var output = new[] { 0.1f, 0.1f };
            Render(source, output);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.1f }, output);
            Assert.That(source.QueuedSamples, Is.EqualTo(2));
            source.Play();
            Render(source, output);
            CollectionAssert.AreEqual(new[] { 0.35f, 0.6f }, output);
        }

        [Test]
        public void Mute_DrainsAudioWithoutContributingToDestination()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(1, 48000, attachTo: mixer) { Mute = true };
            source.Enqueue(new[] { 0.5f, -0.5f });
            var output = new[] { 0.1f, 0.1f };
            Render(source, output);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.1f }, output);
            Assert.That(source.QueuedSamples, Is.Zero);
            Assert.That(source.PlayedSourceFrames, Is.EqualTo(2));
        }

        [Test]
        public void Reset_DiscardsOldAudio_ButPreservesAudioEnqueuedAfterReset()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            source.Enqueue(new[] { 0.9f, 0.9f });
            source.ResetProgress();
            source.Enqueue(new[] { 0.2f, 0.3f });
            var output = new float[2];
            Render(source, output);
            CollectionAssert.AreEqual(new[] { 0.2f, 0.3f }, output);
            Assert.That(source.PlayedSourceFrames, Is.EqualTo(2));
        }

        [Test]
        public void EndOfStream_StopsOnlyAfterDrain_AndNewBatchCanPlay()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            source.Enqueue(new[] { 0.1f, 0.2f }, 1, 48000, markEndOfStream: true);
            Render(source, new float[1]);
            Assert.That(source.IsPlaying, Is.True);
            Render(source, new float[1]);
            Assert.That(source.IsPlaying, Is.False);
            source.Enqueue(new[] { 0.3f }, 1, 48000);
            source.Play();
            var output = new float[1];
            Render(source, output);
            Assert.That(output[0], Is.EqualTo(0.3f));
            Assert.That(source.IsPlaying, Is.True, "Previous EOS must not terminate a new batch.");
        }

        [Test]
        public void PartialEnqueue_DoesNotSignalPrematureEndOfStream()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(2, 48000, attachTo: mixer);
            var input = new float[source.FreeSamples + 2];
            Assert.That(source.TryEnqueue(input, 2, 48000, out int written, true), Is.False);
            Assert.That(written % 2, Is.Zero);
            Assert.That(written, Is.GreaterThan(0));
            Assert.That(source.HasPendingStreamEnd, Is.False);
        }

        [Test]
        public void TailFade_UsesIdenticalGainForEveryChannelInAFrame()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(2, 48000, attachTo: mixer);
            source.Enqueue(new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }, 2, 48000, tailFadeSamples: 8);
            var output = new float[8];
            Render(source, output);
            for (int i = 0; i < output.Length; i += 2)
                Assert.That(output[i], Is.EqualTo(output[i + 1]).Within(1e-6f));
            Assert.That(output[6], Is.Zero);
        }

        [Test]
        public void Underflow_FadesLastSampleToSilenceWithoutRepeatingStaleAudio()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            source.Enqueue(new[] { 0.5f });
            var output = new float[64];
            Render(source, output);
            Assert.That(output[0], Is.EqualTo(0.5f));
            for (int i = 1; i < output.Length; i++)
            {
                Assert.That(output[i], Is.GreaterThanOrEqualTo(0));
                Assert.That(output[i], Is.LessThanOrEqualTo(output[i - 1]));
            }
            output = new float[64];
            Render(source, output);
            CollectionAssert.AreEqual(new float[64], output);
        }

        [Test]
        public void Meters_ArePerChannel_AndReturnedArraysAreIndependent()
        {
            using var mixer = new AudioMixer();
            using var source = new PlaybackAudioSource(2, 48000, attachTo: mixer);
            source.Enqueue(new[] { 0.25f, -0.5f, -0.25f, 0.5f });
            Render(source, new float[4]);
            source.GetMeters(out var peak, out var rms);
            CollectionAssert.AreEqual(new[] { 0.25f, 0.5f }, peak);
            CollectionAssert.AreEqual(new[] { 0.25f, 0.5f }, rms);
            peak[0] = 99;
            source.GetMeters(out peak, out rms);
            Assert.That(peak[0], Is.EqualTo(0.25f));
        }

        [Test]
        public void DisposedSource_RejectsEnqueueAndTryEnqueue()
        {
            using var mixer = new AudioMixer();
            var source = new PlaybackAudioSource(1, 48000, attachTo: mixer);
            source.Dispose();
            Assert.DoesNotThrow(source.Dispose);
            Assert.That(source.Enqueue(new float[1]), Is.Zero);
            Assert.That(source.TryEnqueue(new float[1], out int written), Is.False);
            Assert.That(written, Is.Zero);
            Assert.That(source.TryEnqueue(Array.Empty<float>(), out _), Is.False);
            Assert.DoesNotThrow(() => Render(source, new float[2]));
        }

        internal static void Render(PlaybackAudioSource source, float[] output)
        {
            var gain = new MixerGainEnvelope();
            source.RenderAdditive(output, source.Channels, source.SampleRate, ref gain, 1f, 0);
        }
    }
}
