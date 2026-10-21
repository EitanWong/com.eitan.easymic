using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class NativeProcessorParameterTests
    {
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonfiniteGainAndPan_AreRejectedBeforeNativeStateCanBePoisoned(float value)
        {
            using var gain = new NativeGainer();
            Assert.Throws<ArgumentOutOfRangeException>(() => gain.Gain = value);
            Assert.Throws<ArgumentOutOfRangeException>(() => gain.MasterVolume = value);
            Assert.Throws<ArgumentOutOfRangeException>(() => gain.SmoothTimeSeconds = value);
            using var pan = new NativePanner();
            Assert.Throws<ArgumentOutOfRangeException>(() => pan.Pan = value);
            Assert.That(gain.Gain, Is.EqualTo(1f));
            Assert.That(gain.MasterVolume, Is.EqualTo(1f));
            Assert.That(pan.Pan, Is.Zero);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonfiniteFadeAndDelay_AreRejectedBeforeTimeConversionOrAllocation(float value)
        {
            using var fade = new NativeFader();
            Assert.Throws<ArgumentOutOfRangeException>(() => fade.Fade(value, 1f, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => fade.Fade(0f, value, 0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => fade.Fade(0f, 1f, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => fade.FadeEx(0f, 1f, 0.1f, value));
            using var delay = new NativeDelay();
            Assert.Throws<ArgumentOutOfRangeException>(() => delay.DelaySeconds = value);
            Assert.Throws<ArgumentOutOfRangeException>(() => delay.Wet = value);
            Assert.Throws<ArgumentOutOfRangeException>(() => delay.Dry = value);
            Assert.Throws<ArgumentOutOfRangeException>(() => delay.Decay = value);
            Assert.That(delay.DelaySeconds, Is.EqualTo(0.25f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonfiniteBiquadParameters_AreRejectedBeforeGeneratingCoefficients(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HighPassFilter(value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new HighPassFilter(120f, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LowShelfFilter(value, 6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LowShelfFilter(120f, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LowShelfFilter(120f, 6f, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PeakingEQ(value, 6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PeakingEQ(120f, value));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PeakingEQ(120f, 6f, value));
        }

        [TestCase("gain")]
        [TestCase("pan")]
        [TestCase("fade")]
        [TestCase("delay")]
        [TestCase("biquad")]
        public void NullInitialization_FailsBeforeAllocatingNativeResources(string kind)
        {
            using var processor = Create(kind);
            Assert.Throws<ArgumentNullException>(() => processor.Initialize(null));
        }
        private static AudioWriter Create(string kind)
        {
            switch (kind)
            {
                case "gain": return new NativeGainer();
                case "pan": return new NativePanner();
                case "fade": return new NativeFader();
                case "delay": return new NativeDelay();
                default: return new HighPassFilter(120f);
            }
        }
    }
}
