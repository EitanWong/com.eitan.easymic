using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Peaking EQ biquad filter (native miniaudio processing).
    /// </summary>
    public sealed class PeakingEQ : NativeBiquadFilter
    {
        private readonly float _centerHz;
        private readonly float _gainDb;
        private readonly float _q;

        public PeakingEQ(float centerHz, float gainDb, float q = 1.0f)
        {
            _centerHz = centerHz;
            _gainDb = gainDb;
            _q = q;
        }

        protected override void UpdateCoefficients(int sampleRate)
        {
            float A = MathF.Pow(10f, _gainDb / 40f);
            float w0 = 2f * MathF.PI * (_centerHz / Math.Max(1, sampleRate));
            float cosw0 = MathF.Cos(w0);
            float sinw0 = MathF.Sin(w0);
            float q = MathF.Max(0.001f, _q);
            float alpha = sinw0 / (2f * q);

            float b0n = 1 + alpha * A;
            float b1n = -2 * cosw0;
            float b2n = 1 - alpha * A;
            float a0n = 1 + alpha / A;
            float a1n = -2 * cosw0;
            float a2n = 1 - alpha / A;

            B0 = b0n / a0n;
            B1 = b1n / a0n;
            B2 = b2n / a0n;

            A0 = 1f;
            A1 = a1n / a0n;
            A2 = a2n / a0n;
        }
    }
}

