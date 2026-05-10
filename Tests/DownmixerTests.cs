#if UNITY_INCLUDE_TESTS
using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public sealed class DownmixerTests
    {
        [Test]
        public void StereoDownmix_DoesNotCancelPhaseInvertedMicChannelsToSilence()
        {
            var state = new AudioContext(2, 48000, 8);
            var downmixer = new Downmixer();
            var buffer = new float[]
            {
                0.40f, -0.40f,
                -0.25f, 0.25f,
                0.10f, -0.10f,
                -0.75f, 0.75f
            };

            downmixer.Initialize(state);

            state.ChannelCount = 2;
            state.Length = buffer.Length;
            downmixer.OnAudioPass(buffer.AsSpan(), state);
            downmixer.Dispose();

            Assert.That(state.ChannelCount, Is.EqualTo(1));
            Assert.That(state.Length, Is.EqualTo(4));

            for (int i = 0; i < state.Length; i++)
            {
                Assert.That(Math.Abs(buffer[i]), Is.GreaterThan(0.001f));
            }
        }
    }
}
#endif
