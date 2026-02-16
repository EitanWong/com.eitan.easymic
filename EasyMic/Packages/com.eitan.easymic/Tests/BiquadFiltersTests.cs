#if UNITY_INCLUDE_TESTS
using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public sealed class BiquadFiltersTests
    {
        [Test]
        public void HighPassFilter_ProcessesNonZeroSignal()
        {
            const int sampleRate = 48000;
            const int channels = 1;

            int frames = sampleRate / 10;
            var buffer = new float[frames * channels];
            for (int i = 0; i < frames; i++)
            {
                // A DC-ish + sine mix ensures HPF has something to attenuate and pass.
                buffer[i] = 0.25f + (float)Math.Sin(2.0 * Math.PI * 440.0 * i / sampleRate) * 0.25f;
            }

            var ctx = new AudioContext(channels, sampleRate, buffer.Length);
            var filter = new HighPassFilter(cutoffHz: 120f, q: 0.707f);

            filter.Initialize(ctx);
            filter.OnAudioPass(buffer.AsSpan(), ctx);
            filter.Dispose();

            double sumSq = 0d;
            for (int i = 0; i < buffer.Length; i++)
            {
                double v = buffer[i];
                sumSq += v * v;
            }

            double rms = Math.Sqrt(sumSq / Math.Max(1, buffer.Length));
            Assert.That(rms, Is.GreaterThan(0.001d));
        }
    }
}
#endif

