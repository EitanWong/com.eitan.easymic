using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class AudioProcessorContractTests
    {
        [TestCase(0.1f)]
        [TestCase(0.99f)]
        [TestCase(1f)]
        [TestCase(1.1f)]
        [TestCase(2f)]
        [TestCase(100f)]
        [TestCase(float.MaxValue)]
        public void Limiter_PreservesPolaritySymmetryAndFullScaleBounds(float amplitude)
        {
            using var limiter = new SoftLimiter();
            var state = new AudioContext(1, 48000, 2);
            limiter.Initialize(state);
            var data = new[] { amplitude, -amplitude };
            limiter.OnAudioPass(data, state);
            Assert.That(data[0], Is.InRange(0f, 1f));
            Assert.That(data[1], Is.EqualTo(-data[0]).Within(1e-6f));
        }

        [Test]
        public void Limiter_TransferCurveIsMonotonicAcrossAndAboveThreshold()
        {
            using var limiter = new SoftLimiter { ThresholdDb = -6f };
            var data = new float[1001];
            for (int i = 0; i < data.Length; i++) data[i] = i / 100f;
            var state = new AudioContext(1, 48000, data.Length);
            limiter.Initialize(state);
            limiter.OnAudioPass(data, state);
            for (int i = 1; i < data.Length; i++)
                Assert.That(data[i], Is.GreaterThanOrEqualTo(data[i - 1]));
            Assert.That(data[data.Length - 1], Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void Limiter_NonfiniteInputCannotPoisonTheOutputBuffer()
        {
            using var limiter = new SoftLimiter();
            var data = new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.2f };
            var state = new AudioContext(1, 48000, data.Length);
            limiter.Initialize(state);
            limiter.OnAudioPass(data, state);
            CollectionAssert.AreEqual(new[] { 0f, 1f, -1f, 0.2f }, data);
        }

        [Test]
        public void Limiter_ZeroDbThresholdStillClampsOverload()
        {
            using var limiter = new SoftLimiter { ThresholdDb = 0 };
            var data = new[] { 0.5f, 2f, -2f };
            var state = new AudioContext(1, 48000, 3);
            limiter.Initialize(state);
            limiter.OnAudioPass(data, state);
            CollectionAssert.AreEqual(new[] { 0.5f, 1f, -1f }, data);
        }

        [Test]
        public void Writer_DoesNotProcessBeforeInitialize_WhenDisabled_OrAfterDispose()
        {
            var limiter = new SoftLimiter { MakeupDb = 6f };
            var data = new[] { 0.1f };
            var state = new AudioContext(1, 48000, 1);
            limiter.OnAudioPass(data, state);
            Assert.That(data[0], Is.EqualTo(0.1f));
            limiter.Initialize(state);
            limiter.Enabled = false;
            limiter.OnAudioPass(data, state);
            Assert.That(data[0], Is.EqualTo(0.1f));
            limiter.Enabled = true;
            limiter.Dispose();
            limiter.OnAudioPass(data, state);
            Assert.That(data[0], Is.EqualTo(0.1f));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(12)]
        [TestCase(16)]
        public void Downmix_ProducesOneSamplePerCompleteFrame_WithoutCorruptingInputFormat(int channels)
        {
            using var downmixer = new Downmixer();
            downmixer.Initialize(new AudioContext(channels, 48000, 0));
            var state = new AudioContext(channels, 48000, channels * 7);
            var data = new float[state.Length];
            for (int i = 0; i < data.Length; i++) data[i] = 0.1f;
            downmixer.OnAudioPass(data, state);
            Assert.That(state.ChannelCount, Is.EqualTo(1));
            Assert.That(state.Length, Is.EqualTo(7));
            Assert.That(state.SampleRate, Is.EqualTo(48000));
            for (int i = 0; i < state.Length; i++) Assert.That(data[i], Is.InRange(0.09f, 1f));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(6)]
        public void Gate_ZeroLookaheadLinksAllChannels_AndOpensForStrongSignal(int channels)
        {
            using var gate = new VolumeGateFilter { LookaheadTime = 0, AttackTime = 0, ThresholdDb = -20 };
            var state = new AudioContext(channels, 8000, channels * 8);
            gate.Initialize(state);
            var data = new float[state.Length];
            for (int frame = 0; frame < 8; frame++)
                for (int ch = 0; ch < channels; ch++) data[frame * channels + ch] = ch == 0 ? 0.5f : 0.25f;
            gate.OnAudioPass(data, state);
            Assert.That(gate.CurrentState, Is.EqualTo(VolumeGateFilter.VolumeGateState.Open));
            Assert.That(data[0], Is.EqualTo(0.5f));
            if (channels > 1) Assert.That(data[1], Is.EqualTo(0.25f));
        }

        [Test]
        public void Gate_LookaheadDelaysSignalByRequestedFramesAcrossBlocks()
        {
            using var gate = new VolumeGateFilter { LookaheadTime = 2f / 8000, AttackTime = 0, ThresholdDb = -20 };
            var state = new AudioContext(1, 8000, 1);
            gate.Initialize(state);
            var first = new[] { 0.5f };
            gate.OnAudioPass(first, state);
            Assert.That(first[0], Is.Zero);
            var second = new float[1];
            gate.OnAudioPass(second, state);
            Assert.That(second[0], Is.Zero);
            var third = new float[1];
            gate.OnAudioPass(third, state);
            Assert.That(third[0], Is.EqualTo(0.5f));
        }

        [Test]
        public void Gate_UpdatedThresholdTakesEffectWithoutChangingSampleRate()
        {
            using var gate = new VolumeGateFilter { LookaheadTime = 0, AttackTime = 0, ThresholdDb = 0 };
            var state = new AudioContext(1, 8000, 8);
            gate.Initialize(state);
            var data = new float[8];
            for (int i = 0; i < data.Length; i++) data[i] = 0.5f;
            gate.OnAudioPass(data, state);
            CollectionAssert.AreEqual(new float[8], data);
            gate.ThresholdDb = -20;
            for (int i = 0; i < data.Length; i++) data[i] = 0.5f;
            gate.OnAudioPass(data, state);
            Assert.That(data[7], Is.EqualTo(0.5f));
        }

        [Test]
        public void Gate_SilenceEventuallyClosesAfterHoldAndRelease()
        {
            using var gate = new VolumeGateFilter { LookaheadTime = 0, AttackTime = 0, HoldTime = 0, ReleaseTime = 0.001f, ThresholdDb = -20 };
            var state = new AudioContext(1, 8000, 16);
            gate.Initialize(state);
            var signal = new float[16];
            for (int i = 0; i < signal.Length; i++) signal[i] = 0.5f;
            gate.OnAudioPass(signal, state);
            var silence = new float[8000];
            state.Length = silence.Length;
            gate.OnAudioPass(silence, state);
            Assert.That(gate.CurrentState, Is.EqualTo(VolumeGateFilter.VolumeGateState.Closed));
            CollectionAssert.AreEqual(new float[8000], silence);
        }
    }
}
