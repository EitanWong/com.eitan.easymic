using System;

namespace Eitan.EasyMic.Runtime
{
    public abstract class BiquadBase : AudioWriter
    {
        protected float a0, a1, a2, b0, b1, b2;
        private float[] z1; // per-channel state
        private float[] z2;
        protected float Cutoff;
        protected float Q;
        protected float GainDb;

        public BiquadBase(float cutoffHz, float q = 0.707f, float gainDb = 0f)
        {
            Cutoff = cutoffHz;
            Q = q;
            GainDb = gainDb;
        }

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);
            z1 = new float[Math.Max(1, state.ChannelCount)];
            z2 = new float[z1.Length];
            UpdateCoeffs(state.SampleRate);
        }

        protected abstract void UpdateCoeffs(int sampleRate);

        protected override void OnAudioWrite(Span<float> buffer, AudioState state)
        {
            int chs = Math.Max(1, state.ChannelCount);
            if (z1 == null || z1.Length != chs)
            {
                z1 = new float[chs]; z2 = new float[chs];
            }
            int frames = buffer.Length / chs;
            int idx = 0;
            for (int n = 0; n < frames; n++)
            {
                for (int ch = 0; ch < chs; ch++)
                {
                    float x = buffer[idx];
                    float y = b0 * x + z1[ch];
                    z1[ch] = b1 * x + z2[ch] - a1 * y;
                    z2[ch] = b2 * x - a2 * y;
                    buffer[idx] = y;
                    idx++;
                }
            }
        }
    }

    /// <summary>
    /// First-order like biquad high-pass (implemented as biquad) with frequency and Q.
    /// </summary>
    public sealed class HighPassFilter : BiquadBase
    {
        public HighPassFilter(float cutoffHz, float q = 0.707f) : base(cutoffHz, q) { }

        protected override void UpdateCoeffs(int sampleRate)
        {
            float w0 = 2f * MathF.PI * (float)(Cutoff / Math.Max(1, sampleRate));
            float cosw0 = MathF.Cos(w0);
            float sinw0 = MathF.Sin(w0);
            float alpha = sinw0 / (2f * MathF.Max(0.001f, Q));

            float b0n = (1 + cosw0) / 2f;
            float b1n = -(1 + cosw0);
            float b2n = (1 + cosw0) / 2f;
            float a0n = 1 + alpha;
            float a1n = -2 * cosw0;
            float a2n = 1 - alpha;

            b0 = b0n / a0n; b1 = b1n / a0n; b2 = b2n / a0n;
            a1 = a1n / a0n; a2 = a2n / a0n; a0 = 1f;
        }
    }

    /// <summary>
    /// Low-shelf EQ biquad.
    /// </summary>
    public sealed class LowShelfFilter : BiquadBase
    {
        public LowShelfFilter(float cutoffHz, float gainDb, float q = 0.707f) : base(cutoffHz, q, gainDb) { }

        protected override void UpdateCoeffs(int sampleRate)
        {
            float A = MathF.Pow(10f, GainDb / 40f);
            float w0 = 2f * MathF.PI * (float)(Cutoff / Math.Max(1, sampleRate));
            float cosw0 = MathF.Cos(w0);
            float sinw0 = MathF.Sin(w0);
            float alpha = sinw0 / (2f * MathF.Max(0.001f, Q));
            float beta = MathF.Sqrt(A) / MathF.Max(0.001f, Q);

            float b0n = A * ((A + 1) + (A - 1) * cosw0 + beta * sinw0);
            float b1n = -2 * A * ((A - 1) + (A + 1) * cosw0);
            float b2n = A * ((A + 1) + (A - 1) * cosw0 - beta * sinw0);
            float a0n = (A + 1) - (A - 1) * cosw0 + beta * sinw0;
            float a1n = 2 * ((A - 1) - (A + 1) * cosw0);
            float a2n = (A + 1) - (A - 1) * cosw0 - beta * sinw0;

            b0 = b0n / a0n; b1 = b1n / a0n; b2 = b2n / a0n;
            a1 = a1n / a0n; a2 = a2n / a0n; a0 = 1f;
        }
    }

    /// <summary>
    /// Peaking EQ biquad.
    /// </summary>
    public sealed class PeakingEQ : BiquadBase
    {
        public PeakingEQ(float centerHz, float gainDb, float q = 1.0f) : base(centerHz, q, gainDb) { }

        protected override void UpdateCoeffs(int sampleRate)
        {
            float A = MathF.Pow(10f, GainDb / 40f);
            float w0 = 2f * MathF.PI * (float)(Cutoff / Math.Max(1, sampleRate));
            float cosw0 = MathF.Cos(w0);
            float sinw0 = MathF.Sin(w0);
            float alpha = sinw0 / (2f * MathF.Max(0.001f, Q));

            float b0n = 1 + alpha * A;
            float b1n = -2 * cosw0;
            float b2n = 1 - alpha * A;
            float a0n = 1 + alpha / A;
            float a1n = -2 * cosw0;
            float a2n = 1 - alpha / A;

            b0 = b0n / a0n; b1 = b1n / a0n; b2 = b2n / a0n;
            a1 = a1n / a0n; a2 = a2n / a0n; a0 = 1f;
        }
    }
}
