using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Low-shelf biquad filter (native miniaudio processing).
    /// </summary>
    public sealed class LowShelfFilter : NativeBiquadFilter
    {
        private readonly float _cutoffHz;
        private readonly float _gainDb;
        private readonly float _q;

        public LowShelfFilter(float cutoffHz, float gainDb, float q = 0.707f)
        {
            _cutoffHz = cutoffHz;
            _gainDb = gainDb;
            _q = q;
        }

        protected override void UpdateCoefficients(int sampleRate)
        {
            float A = MathF.Pow(10f, _gainDb / 40f);
            float w0 = 2f * MathF.PI * (_cutoffHz / Math.Max(1, sampleRate));
            float cosw0 = MathF.Cos(w0);
            float sinw0 = MathF.Sin(w0);
            float q = MathF.Max(0.001f, _q);
            float alpha = sinw0 / (2f * q);
            float beta = MathF.Sqrt(A) / q;

            float b0n = A * ((A + 1) + (A - 1) * cosw0 + beta * sinw0);
            float b1n = -2 * A * ((A - 1) + (A + 1) * cosw0);
            float b2n = A * ((A + 1) + (A - 1) * cosw0 - beta * sinw0);
            float a0n = (A + 1) - (A - 1) * cosw0 + beta * sinw0;
            float a1n = 2 * ((A - 1) - (A + 1) * cosw0);
            float a2n = (A + 1) - (A - 1) * cosw0 - beta * sinw0;

            B0 = b0n / a0n;
            B1 = b1n / a0n;
            B2 = b2n / a0n;

            A0 = 1f;
            A1 = a1n / a0n;
            A2 = a2n / a0n;
        }
    }
}

