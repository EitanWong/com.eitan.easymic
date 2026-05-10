using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Simple soft limiter using cubic soft clip.
    /// Threshold in dBFS; makeup gain optional. Low-CPU for real-time master bus.
    /// </summary>
    public sealed class SoftLimiter : AudioWriter
    {
        private float _thresholdDb = -0.1f; // near 0dB
        private float _makeupDb;
        private float _thresholdLinear = DbToLin(-0.1f);
        private float _makeupLinear = 1f;

        public float ThresholdDb
        {
            get => _thresholdDb;
            set
            {
                _thresholdDb = value;
                _thresholdLinear = DbToLin(value);
            }
        }

        public float MakeupDb
        {
            get => _makeupDb;
            set
            {
                _makeupDb = value;
                _makeupLinear = DbToLin(value);
            }
        }

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);
            _thresholdLinear = DbToLin(ThresholdDb);
            _makeupLinear = DbToLin(MakeupDb);
        }

        protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
        {
            if (buffer.IsEmpty)
            {
                return;
            }


            float t = _thresholdLinear;
            float makeup = _makeupLinear;
            float range = 1f - t;
            float invRange = range > float.Epsilon ? 1f / range : 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i] * makeup;
                float ax = MathF.Abs(x);
                if (ax <= t) { buffer[i] = x; continue; }
                // Soft clip above threshold: cubic approach
                float sign = MathF.Sign(x);
                float z = 1f - (ax - t) * invRange;
                float y = t + range * (1f - z * z);
                buffer[i] = sign * MathF.Min(1f, y);
            }
        }

        private static float DbToLin(float db) => MathF.Pow(10f, db / 20f);
    }
}
