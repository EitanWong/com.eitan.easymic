#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.AudioProcessing
{
    using System;
    using Eitan.SherpaONNXUnity.Runtime.Modules;

    /// <summary>
    /// Optional EasyMic transport filter for Sherpa speech enhancement.
    /// Runs on EasyMic's pipeline worker, uses Sherpa's streaming denoiser, and keeps channel count unchanged.
    /// Heavy models can still add queue pressure, so validate latency before enabling it in low-latency sessions.
    /// </summary>
    public sealed class SherpaSpeechEnhancementFilter : AudioWriter, IAudioTransportProcessor, IDisposable
    {
        private readonly SpeechEnhancement _enhancement;
        private readonly int? _modelSampleRate;

        private float[][] _channelBuffers = Array.Empty<float[]>();
        private float[] _monoProcessBuffer = Array.Empty<float>();
        private int _disposed;

        public SherpaSpeechEnhancementFilter(SpeechEnhancement enhancement, int? modelSampleRate = null)
        {
            _enhancement = enhancement ?? throw new ArgumentNullException(nameof(enhancement));
            _modelSampleRate = modelSampleRate;
        }

        public void ResetStreaming()
        {
            if (_disposed == 0)
            {
                _enhancement.ResetStreaming();
            }
        }

        protected override void OnAudioWrite(Span<float> audioBuffer, AudioContext state)
        {
            if (_disposed != 0 || audioBuffer.IsEmpty || state.Length <= 0)
            {
                return;
            }

            int channels = Math.Max(1, state.ChannelCount);
            int length = Math.Min(state.Length, audioBuffer.Length);
            int frames = length / channels;
            if (frames <= 0)
            {
                state.Length = 0;
                return;
            }

            if (channels == 1)
            {
                ProcessMono(audioBuffer.Slice(0, frames), state.SampleRate);
                state.Length = frames;
                return;
            }

            EnsureChannelBuffers(channels, frames);
            Deinterleave(audioBuffer.Slice(0, frames * channels), channels, frames, _channelBuffers);

            for (int channel = 0; channel < channels; channel++)
            {
                ProcessMono(_channelBuffers[channel].AsSpan(0, frames), state.SampleRate);
            }

            Interleave(_channelBuffers, audioBuffer.Slice(0, frames * channels), channels, frames);
            state.Length = frames * channels;
        }

        public override void Dispose()
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
            base.Dispose();
        }

        private void ProcessMono(Span<float> samples, int sampleRate)
        {
            if (samples.IsEmpty)
            {
                return;
            }

            if (_monoProcessBuffer.Length != samples.Length)
            {
                _monoProcessBuffer = new float[samples.Length];
            }

            samples.CopyTo(_monoProcessBuffer);
            var enhanced = _enhancement.ProcessStreamingSync(_monoProcessBuffer, _modelSampleRate ?? sampleRate);
            if (enhanced == null || enhanced.Length == 0)
            {
                return;
            }

            int copyLength = Math.Min(samples.Length, enhanced.Length);
            enhanced.AsSpan(0, copyLength).CopyTo(samples);
            if (copyLength < samples.Length)
            {
                samples.Slice(copyLength).Clear();
            }
        }

        private void EnsureChannelBuffers(int channels, int frames)
        {
            if (_channelBuffers.Length != channels)
            {
                _channelBuffers = new float[channels][];
            }

            for (int i = 0; i < channels; i++)
            {
                if (_channelBuffers[i] == null || _channelBuffers[i].Length != frames)
                {
                    _channelBuffers[i] = new float[frames];
                }
            }
        }

        private static void Deinterleave(ReadOnlySpan<float> source, int channels, int frames, float[][] destination)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int baseIndex = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    destination[channel][frame] = source[baseIndex + channel];
                }
            }
        }

        private static void Interleave(float[][] source, Span<float> destination, int channels, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int baseIndex = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    destination[baseIndex + channel] = source[channel][frame];
                }
            }
        }
    }
}
#endif
