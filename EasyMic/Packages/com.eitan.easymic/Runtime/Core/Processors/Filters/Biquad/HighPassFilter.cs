using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// High-pass biquad filter (native miniaudio processing).
    /// </summary>
    public sealed class HighPassFilter : NativeBiquadFilter
    {
        private readonly float _cutoffHz;
        private readonly float _q;

        public HighPassFilter(float cutoffHz, float q = 0.707f)
        {
            _cutoffHz = cutoffHz;
            _q = q;
        }

        protected override void UpdateCoefficients(int sampleRate)
        {
            float w0 = 2f * MathF.PI * (_cutoffHz / Math.Max(1, sampleRate));
            float cosw0 = MathF.Cos(w0);
            float sinw0 = MathF.Sin(w0);
            float q = MathF.Max(0.001f, _q);
            float alpha = sinw0 / (2f * q);

            float b0n = (1 + cosw0) / 2f;
            float b1n = -(1 + cosw0);
            float b2n = (1 + cosw0) / 2f;
            float a0n = 1 + alpha;
            float a1n = -2 * cosw0;
            float a2n = 1 - alpha;

            B0 = b0n / a0n;
            B1 = b1n / a0n;
            B2 = b2n / a0n;

            A0 = 1f;
            A1 = a1n / a0n;
            A2 = a2n / a0n;
        }
    }
}

