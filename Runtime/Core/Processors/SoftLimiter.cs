using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Simple soft limiter using cubic soft clip.
    /// Threshold in dBFS; makeup gain optional. Low-CPU for real-time master bus.
    /// </summary>
    public sealed class SoftLimiter : AudioWriter
    {
        private float _thresholdLinear;
        public float ThresholdDb { get; set; } = -0.1f; // near 0dB
        public float MakeupDb { get; set; } = 0f;

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);
            _thresholdLinear = DbToLin(ThresholdDb);
        }

        protected override void OnAudioWrite(Span<float> buffer, AudioState state)
        {
            if (buffer.IsEmpty)
            {
                return;
            }


            float t = _thresholdLinear;
            float makeup = DbToLin(MakeupDb);
            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i] * makeup;
                float ax = MathF.Abs(x);
                if (ax <= t) { buffer[i] = x; continue; }
                // Soft clip above threshold: cubic approach
                float sign = MathF.Sign(x);
                float y = t + (1f - t) * (1f - MathF.Pow(1f - (ax - t) / (1f - t), 2f));
                buffer[i] = sign * MathF.Min(1f, y);
            }
        }

        private static float DbToLin(float db) => MathF.Pow(10f, db / 20f);
    }
}

