#if UNITY_INCLUDE_TESTS
using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public sealed class AudioSampleRateResamplerTests
    {
        [Test]
        public void Downsample_48k_To_16k_UpdatesStateAndProducesEnergy()
        {
            const int sourceSampleRate = 48000;
            const int targetSampleRate = 16000;
            const int channels = 1;

            int frames = sourceSampleRate / 2; // 0.5 sec
            var buffer = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                buffer[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sourceSampleRate);
            }

            var ctx = new AudioContext(channels, sourceSampleRate, buffer.Length);
            var resampler = new Resampler(targetSampleRate);

            resampler.Initialize(ctx);
            resampler.OnAudioPass(buffer.AsSpan(), ctx);
            resampler.Dispose();

            Assert.That(ctx.SampleRate, Is.EqualTo(targetSampleRate));
            Assert.That(ctx.Length, Is.GreaterThan(0));
            Assert.That(ctx.Length, Is.LessThan(buffer.Length));

            var output = new ReadOnlySpan<float>(buffer, 0, ctx.Length);
            double sumSq = 0d;
            for (int i = 0; i < output.Length; i++)
            {
                double v = output[i];
                sumSq += v * v;
            }

            double rms = Math.Sqrt(sumSq / Math.Max(1, output.Length));
            Assert.That(rms, Is.GreaterThan(0.01d));
        }

        [Test]
        public void Upsample_IsNotSupportedInPlace_LeavesSampleRateUnchanged()
        {
            const int sourceSampleRate = 8000;
            const int targetSampleRate = 16000;
            const int channels = 1;

            int frames = sourceSampleRate / 10;
            var buffer = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                buffer[i] = 0.25f;
            }

            var ctx = new AudioContext(channels, sourceSampleRate, buffer.Length);
            var resampler = new Resampler(targetSampleRate);

            resampler.Initialize(ctx);
            resampler.OnAudioPass(buffer.AsSpan(), ctx);
            resampler.Dispose();

            Assert.That(ctx.SampleRate, Is.EqualTo(sourceSampleRate));
            Assert.That(ctx.Length, Is.EqualTo(buffer.Length));
        }
    }
}
#endif

