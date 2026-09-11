using System;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.Internal
{
    /// <summary>
    /// Streaming speech RMS levelling with a silence gate, bounded gain and a linked-channel
    /// sample-peak limiter. Processes the already available 20 ms playback block in place.
    /// This is not a BS.1770 integrated loudness or true-peak meter.
    /// </summary>
    public sealed class SpeechOutputLeveler
    {
        public const float PeakCeiling = 0.89125094f; // -1 dBFS, before output mixing.
        private const double TargetDb = -20;
        private const double SilenceDb = -55;
        private const double MaxBoostDb = 24;
        private double _gainDb;
        private double _limiterGain = 1;
        private double _volume = 1;
        private bool _hasSpeech;
        private bool _initialized;

        public float InputRmsDb { get; private set; } = -120;
        public float OutputPeakDb { get; private set; } = -120;
        public float AppliedGainDb { get; private set; }

        public void Process(float[] samples, int count, int channels, int sampleRate,
            bool normalize, float volume)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (count < 0 || count > samples.Length || channels < 1 || count % channels != 0 || sampleRate < 1)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return;

            double power = 0, peak = 0;
            for (int i = 0; i < count; i++)
            {
                float x = samples[i];
                if (float.IsNaN(x) || float.IsInfinity(x)) x = samples[i] = 0;
                power += (double)x * x;
                peak = Math.Max(peak, Math.Abs((double)x));
            }

            double rmsDb = 10 * Math.Log10(Math.Max(1e-12, power / count));
            double seconds = (double)(count / channels) / sampleRate;
            double desiredDb = normalize && rmsDb > SilenceDb
                ? Math.Max(-60, Math.Min(MaxBoostDb, TargetDb - rmsDb)) : Math.Min(0, _gainDb);
            if (!normalize) desiredDb = 0;
            double previousDb = _gainDb;
            if (normalize && !_hasSpeech && rmsDb > SilenceDb)
            {
                _hasSpeech = true;
                previousDb = _gainDb = desiredDb;
            }
            else
            {
                double timeConstant = desiredDb < _gainDb ? 0.08 : 0.8;
                _gainDb += (desiredDb - _gainDb) * (1 - Math.Exp(-seconds / timeConstant));
            }

            double requestedVolume = float.IsNaN(volume) || float.IsInfinity(volume)
                ? 1 : Math.Max(0, Math.Min(2, volume));
            if (!_initialized)
            {
                _volume = requestedVolume;
                _initialized = true;
            }
            double startGain = Math.Pow(10, previousDb / 20) * _volume;
            double endGain = Math.Pow(10, _gainDb / 20) * requestedVolume;
            // Never raise silence/background noise, including immediately after a loud phrase.
            if (normalize && rmsDb <= SilenceDb)
            {
                startGain = Math.Min(startGain, _volume);
                endGain = Math.Min(endGain, requestedVolume);
            }
            // Use the existing block as look-ahead; no extra queue or delay is introduced.
            double wantedLimiter = peak > 0
                ? Math.Min(1, PeakCeiling / (peak * Math.Max(1e-12, Math.Max(startGain, endGain)))) : 1;
            double limiterStart = Math.Min(_limiterGain, wantedLimiter);
            double limiterEnd = wantedLimiter < _limiterGain ? wantedLimiter
                : _limiterGain + (wantedLimiter - _limiterGain) * (1 - Math.Exp(-seconds / 0.12));
            _limiterGain = limiterEnd;
            _volume = requestedVolume;
            int frames = count / channels;
            double outputPeak = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                double position = (double)(frame + 1) / frames;
                double gain = (startGain + (endGain - startGain) * position)
                    * (limiterStart + (limiterEnd - limiterStart) * position);
                if (requestedVolume == 0) gain = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    int index = frame * channels + channel;
                    samples[index] = (float)Math.Max(-PeakCeiling, Math.Min(PeakCeiling, samples[index] * gain));
                    outputPeak = Math.Max(outputPeak, Math.Abs(samples[index]));
                }
            }
            InputRmsDb = (float)rmsDb;
            OutputPeakDb = (float)(20 * Math.Log10(Math.Max(1e-6, outputPeak)));
            AppliedGainDb = (float)(20 * Math.Log10(Math.Max(1e-6, endGain * limiterEnd)));
        }
    }
}
