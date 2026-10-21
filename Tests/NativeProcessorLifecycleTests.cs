using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class NativeProcessorLifecycleTests
    {
        [TestCase("gain")]
        [TestCase("pan")]
        [TestCase("fade")]
        [TestCase("delay")]
        [TestCase("biquad")]
        [TestCase("shelf")]
        [TestCase("eq")]
        [TestCase("resample")]
        [TestCase("resample44100")]
        public void ChunkedProcessing_MatchesContinuousProcessingAcrossBlockBoundaries(string kind)
        {
            using var whole = Create(kind);
            using var split = Create(kind);
            const int channels = 2, frames = 2400;
            int rate = kind == "resample44100" ? 44100 : 48000;
            whole.Initialize(new AudioContext(channels, rate, frames * channels));
            split.Initialize(new AudioContext(channels, rate, frames * channels));
            string readyField = kind.StartsWith("resample", StringComparison.Ordinal) ? "_useNative" : "_nativeReady";
            var nativeType = whole is NativeBiquadFilter ? typeof(NativeBiquadFilter) : whole.GetType();
            Assert.That((bool)nativeType.GetField(readyField, AudioTestAccess.Instance).GetValue(whole), Is.True, "Native processing must actually be active.");
            var signal = new float[frames * channels];
            for (int i = 0; i < frames; i++) { signal[i * 2] = (float)Math.Sin(i * 0.03) * 0.3f; signal[i * 2 + 1] = -signal[i * 2]; }
            var expected = (float[])signal.Clone();
            var state = new AudioContext(channels, rate, expected.Length);
            whole.OnAudioPass(expected, state);
            var output = new System.Collections.Generic.List<float>();
            int offset = 0;
            foreach (int chunkFrames in new[] { 7, 33, 160, 800, 1400 })
            {
                var chunk = new float[chunkFrames * channels];
                Array.Copy(signal, offset, chunk, 0, chunk.Length);
                offset += chunk.Length;
                var context = new AudioContext(channels, rate, chunk.Length);
                split.OnAudioPass(chunk, context);
                for (int i = 0; i < context.Length; i++) output.Add(chunk[i]);
            }
            Assert.That(output.Count, Is.EqualTo(state.Length));
            for (int i = 0; i < output.Count; i++) Assert.That(output[i], Is.EqualTo(expected[i]).Within(1e-5f), $"{kind} sample {i}");
        }

        [TestCase("gain")]
        [TestCase("pan")]
        [TestCase("fade")]
        [TestCase("delay")]
        [TestCase("biquad")]
        [TestCase("shelf")]
        [TestCase("eq")]
        [TestCase("resample")]
        public void ReinitializeThenDispose_DoesNotContinueProcessingOrTouchPartialFrames(string kind)
        {
            using var processor = Create(kind);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                processor.Initialize(new AudioContext(2, 48000, 4));
                var input = new[] { 0.25f, -0.25f, 0.5f, -0.5f, 99f };
                var state = new AudioContext(2, 48000, input.Length);
                processor.OnAudioPass(input, state);
                Assert.That(input[4], Is.EqualTo(99f), "Trailing partial frame must remain untouched.");
                processor.Dispose(); processor.Dispose();
                var sentinel = new[] { 0.25f, -0.25f, 0.5f, -0.5f };
                processor.OnAudioPass(sentinel, new AudioContext(2, 48000, 4));
                CollectionAssert.AreEqual(new[] { 0.25f, -0.25f, 0.5f, -0.5f }, sentinel, $"{kind} cycle {cycle}");
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        public void Gainer_MultipliesGainAndMasterVolume_AndAppliesUpdates(int channels)
        {
            using var gain = new NativeGainer { Gain = 0.5f, MasterVolume = 0.25f, SmoothTimeSeconds = 0 };
            var state = new AudioContext(channels, 48000, channels * 4);
            gain.Initialize(state);
            var samples = new float[channels * 4]; Array.Fill(samples, 0.8f);
            gain.OnAudioPass(samples, state);
            foreach (var sample in samples) Assert.That(sample, Is.EqualTo(0.1f).Within(1e-6));
            gain.Gain = 0.25f; gain.MasterVolume = 1f;
            Array.Fill(samples, 0.8f);
            gain.OnAudioPass(samples, state);
            foreach (var sample in samples) Assert.That(sample, Is.EqualTo(0.2f).Within(1e-6));
        }

        [TestCase(-1f, 0.4f, 0f)]
        [TestCase(0f, 0.4f, 0.8f)]
        [TestCase(1f, 0f, 0.8f)]
        public void Panner_BalanceExtremesAndCenter(float pan, float left, float right)
        {
            using var panner = new NativePanner { Pan = pan, Mode = StereoPanMode.Balance };
            var state = new AudioContext(2, 48000, 2); panner.Initialize(state);
            var signal = new[] { 0.4f, 0.8f }; panner.OnAudioPass(signal, state);
            Assert.That(signal[0], Is.EqualTo(left).Within(1e-6));
            Assert.That(signal[1], Is.EqualTo(right).Within(1e-6));
        }

        [Test]
        public void Panner_MonoAndSurroundPassThrough()
        {
            foreach (int channels in new[] { 1, 6 })
            {
                using var pan = new NativePanner { Pan = 1f };
                var state = new AudioContext(channels, 48000, channels);
                pan.Initialize(state);
                var signal = new float[channels]; Array.Fill(signal, 0.3f);
                pan.OnAudioPass(signal, state);
                foreach (var sample in signal) Assert.That(sample, Is.EqualTo(0.3f));
            }
        }

        [Test]
        public void Delay_ImpulseEchoOccursAtConfiguredFrameAcrossBlocks()
        {
            using var delay = new NativeDelay { DelaySeconds = 0.01f, Dry = 1, Wet = 1, Decay = 0, DelayStart = true };
            var state = new AudioContext(1, 8000, 100); delay.Initialize(state);
            var first = new float[40]; first[0] = 1f;
            delay.OnAudioPass(first, new AudioContext(1, 8000, 40));
            foreach (var sample in first) Assert.That(sample, Is.Zero);
            var second = new float[60]; delay.OnAudioPass(second, new AudioContext(1, 8000, 60));
            for (int i = 0; i < second.Length; i++) Assert.That(second[i], Is.EqualTo(i == 40 ? 1f : 0f).Within(1e-6));
        }

        [Test]
        public void Fader_DeferredRequestProgressesMonotonicallyAndLinksChannels()
        {
            using var fader = new NativeFader();
            fader.FadeIn(0.01f);
            fader.Initialize(new AudioContext(2, 8000, 200));
            var signal = new float[200]; Array.Fill(signal, 1f);
            fader.OnAudioPass(signal, new AudioContext(2, 8000, 200));
            float previous = -1;
            for (int i = 0; i < signal.Length; i += 2)
            {
                Assert.That(signal[i], Is.InRange(0f, 1f));
                Assert.That(signal[i], Is.GreaterThanOrEqualTo(previous));
                Assert.That(signal[i + 1], Is.EqualTo(signal[i])); previous = signal[i];
            }
            Assert.That(signal[198], Is.EqualTo(1f));
        }

        [Test]
        public void HighPass_RemovesDcAfterSettling()
        {
            using var filter = new HighPassFilter(120f);
            var signal = new float[48000]; Array.Fill(signal, 0.5f);
            var state = new AudioContext(1, 48000, signal.Length); filter.Initialize(state); filter.OnAudioPass(signal, state);
            for (int i = signal.Length - 100; i < signal.Length; i++) Assert.That(Math.Abs(signal[i]), Is.LessThan(1e-4));
        }

        private static AudioWriter Create(string kind)
        {
            switch (kind)
            {
                case "gain": return new NativeGainer { Gain = 0.5f, SmoothTimeSeconds = 0.01f };
                case "pan": return new NativePanner { Pan = -0.25f };
                case "fade": var fader = new NativeFader(); fader.FadeIn(0.02f); return fader;
                case "delay": return new NativeDelay { DelaySeconds = 0.01f, Wet = 0.5f, Dry = 0.7f, Decay = 0.3f };
                case "biquad": return new HighPassFilter(120f);
                case "shelf": return new LowShelfFilter(120f, 6f);
                case "eq": return new PeakingEQ(120f, -6f);
                case "resample44100": return new Resampler(16000);
                case "resample": return new Resampler(16000);
                default: throw new ArgumentException(kind);
            }
        }
    }
}
