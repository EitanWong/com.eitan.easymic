#if UNITY_INCLUDE_TESTS
using System;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public sealed class MiniaudioPrimitivesTests
    {
        [Test]
        public void Gainer_CanApplyGainInPlace()
        {
            try
            {
                const int channels = 1;
                const int frameCount = 4800;

                Assert.That(Native.GainerHandle.TryCreate(channels, smoothTimeInFrames: 0, out var gainer), Is.True, "Native gainer init failed.");
                try
                {
                    Assert.That(gainer.IsValid, Is.True);
                    Assert.That(Native.GainerHandle.SetGain(ref gainer, 0.5f), Is.True);

                    var buffer = new float[frameCount * channels];
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        buffer[i] = 1f;
                    }

                    Assert.That(Native.GainerHandle.ProcessInPlace(ref gainer, buffer.AsSpan(), frameCount), Is.True);

                    double sum = 0d;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        sum += buffer[i];
                    }

                    double avg = sum / Math.Max(1, buffer.Length);
                    Assert.That(avg, Is.InRange(0.45d, 0.55d));
                }
                finally
                {
                    gainer.Dispose();
                }
            }
            catch (DllNotFoundException)
            {
                Assert.Ignore("miniaudio native library not present for this test run.");
            }
            catch (EntryPointNotFoundException)
            {
                Assert.Ignore("miniaudio gainer symbols not present for this test run.");
            }
        }

        [Test]
        public void Fader_CanFadeInPlace()
        {
            try
            {
                const int channels = 1;
                const int sampleRate = 48000;
                const int frameCount = 4800;

                Assert.That(Native.FaderHandle.TryCreate(channels, sampleRate, out var fader), Is.True, "Native fader init failed.");
                try
                {
                    Assert.That(fader.IsValid, Is.True);
                    Native.FaderHandle.SetFade(ref fader, volumeBeg: 0f, volumeEnd: 1f, lengthInFrames: frameCount);

                    var buffer = new float[frameCount * channels];
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        buffer[i] = 1f;
                    }

                    Assert.That(Native.FaderHandle.ProcessInPlace(ref fader, buffer.AsSpan(), frameCount), Is.True);

                    float first = buffer[0];
                    float last = buffer[buffer.Length - 1];
                    Assert.That(last, Is.GreaterThan(first));

                    double sum = 0d;
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        sum += buffer[i];
                    }

                    double avg = sum / Math.Max(1, buffer.Length);
                    Assert.That(avg, Is.InRange(0.35d, 0.65d));
                }
                finally
                {
                    fader.Dispose();
                }
            }
            catch (DllNotFoundException)
            {
                Assert.Ignore("miniaudio native library not present for this test run.");
            }
            catch (EntryPointNotFoundException)
            {
                Assert.Ignore("miniaudio fader symbols not present for this test run.");
            }
        }

        [Test]
        public void Panner_CanMoveSignalToRightInPanMode()
        {
            try
            {
                const int channels = 2;
                const int frameCount = 1024;

                Assert.That(Native.PannerHandle.TryCreate(channels, Native.PanMode.Pan, pan: 1f, out var panner), Is.True, "Native panner init failed.");
                try
                {
                    Assert.That(panner.IsValid, Is.True);

                    var buffer = new float[frameCount * channels];
                    for (int i = 0; i < frameCount; i++)
                    {
                        buffer[i * 2 + 0] = 1f; // left
                        buffer[i * 2 + 1] = 0f; // right
                    }

                    Assert.That(Native.PannerHandle.ProcessInPlace(ref panner, buffer.AsSpan(), frameCount), Is.True);

                    double sumL = 0d;
                    double sumR = 0d;
                    for (int i = 0; i < frameCount; i++)
                    {
                        sumL += Math.Abs(buffer[i * 2 + 0]);
                        sumR += Math.Abs(buffer[i * 2 + 1]);
                    }

                    Assert.That(sumR, Is.GreaterThan(sumL));
                    Assert.That(sumR / Math.Max(1d, frameCount), Is.GreaterThan(0.1d));
                }
                finally
                {
                    panner.Dispose();
                }
            }
            catch (DllNotFoundException)
            {
                Assert.Ignore("miniaudio native library not present for this test run.");
            }
            catch (EntryPointNotFoundException)
            {
                Assert.Ignore("miniaudio panner symbols not present for this test run.");
            }
        }

        [Test]
        public void Delay_CanDelayImpulseInPlace()
        {
            try
            {
                const int channels = 1;
                const int sampleRate = 48000;
                const int frameCount = 512;
                const uint delayInFrames = 64;

                Assert.That(
                    Native.DelayHandle.TryCreate(
                        channels,
                        sampleRate,
                        delayInFrames,
                        decay: 0f,
                        delayStart: false,
                        wet: 1f,
                        dry: 0f,
                        out var delay),
                    Is.True,
                    "Native delay init failed.");

                try
                {
                    var buffer = new float[frameCount * channels];
                    buffer[0] = 1f; // impulse

                    Assert.That(Native.DelayHandle.ProcessInPlace(ref delay, buffer.AsSpan(), frameCount), Is.True);

                    Assert.That(buffer[0], Is.EqualTo(0f).Within(1e-6f));
                    Assert.That(buffer[(int)delayInFrames], Is.GreaterThan(0.5f));
                }
                finally
                {
                    delay.Dispose();
                }
            }
            catch (DllNotFoundException)
            {
                Assert.Ignore("miniaudio native library not present for this test run.");
            }
            catch (EntryPointNotFoundException)
            {
                Assert.Ignore("miniaudio delay symbols not present for this test run.");
            }
        }
    }
}
#endif
