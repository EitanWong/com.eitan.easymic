using System;
using System.Diagnostics;
using System.Threading;
using Eitan.EasyMic.Runtime;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Thread-safe audio envelope shared by audio workers and the UI update loop.
    /// </summary>
    internal sealed class SpeechVisualizationLevel
    {
        private const float NoiseFloor = 0.0035f;
        private const float ReferenceRms = 0.12f;
        private const float AttackPerSecond = 28f;
        private const float ReleasePerSecond = 10f;

        private float _level;
        private long _lastSampleTicks;

        public void Push(float[] samples, int count, int channels, int sampleRate)
        {
            if (samples == null || count <= 0)
            {
                return;
            }

            int sampleCount = Math.Min(count, samples.Length);
            Push(new ReadOnlySpan<float>(samples, 0, sampleCount), channels, sampleRate);
        }

        public void Push(ReadOnlySpan<float> samples, int channels, int sampleRate)
        {
            if (samples.IsEmpty || channels <= 0 || sampleRate <= 0)
            {
                return;
            }

            double sumSquares = 0d;
            for (int i = 0; i < samples.Length; i++)
            {
                float sample = samples[i];
                sumSquares += sample * sample;
            }

            float rms = (float)Math.Sqrt(sumSquares / samples.Length);
            float rawLevel = Clamp01((rms - NoiseFloor) / (ReferenceRms - NoiseFloor));
            int frames = Math.Max(1, samples.Length / channels);
            float duration = frames / (float)sampleRate;
            float current = Volatile.Read(ref _level);
            float rate = rawLevel > current ? AttackPerSecond : ReleasePerSecond;
            float alpha = 1f - (float)Math.Exp(-rate * duration);
            Volatile.Write(ref _level, current + (rawLevel - current) * alpha);
            Interlocked.Exchange(ref _lastSampleTicks, Stopwatch.GetTimestamp());
        }

        public float GetRecentLevel(float maxAgeSeconds)
        {
            long lastTicks = Interlocked.Read(ref _lastSampleTicks);
            if (lastTicks <= 0)
            {
                return 0f;
            }

            long nowTicks = Stopwatch.GetTimestamp();
            double ageSeconds = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
            return ageSeconds <= Math.Max(0f, maxAgeSeconds)
                ? Volatile.Read(ref _level)
                : 0f;
        }

        public void Reset()
        {
            Volatile.Write(ref _level, 0f);
            Interlocked.Exchange(ref _lastSampleTicks, 0L);
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }

    internal sealed class MicrophoneSpeechVisualizationProbe : AudioWriter
    {
        private readonly SpeechVisualizationLevel _level;

        public MicrophoneSpeechVisualizationProbe(SpeechVisualizationLevel level)
        {
            _level = level ?? throw new ArgumentNullException(nameof(level));
        }

        protected override void OnAudioWrite(Span<float> audioBuffer, AudioContext state)
        {
            _level.Push(audioBuffer, state.ChannelCount, state.SampleRate);
        }
    }
}
