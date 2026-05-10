using System;

namespace Eitan.EasyMic.Runtime
{
    public class Downmixer : AudioWriter
    {

        // Optimized coefficients for different channel configurations
        private const float COEFF_3DB = 0.707f;  // -3dB for primary channels
        private const float COEFF_6DB = 0.5f;    // -6dB for safe summing
        private const float COEFF_12DB = 0.25f;  // -12dB for many channels
        private int _originalChannelCount;

        public override void Initialize(AudioContext state)
        {
            _originalChannelCount = state.ChannelCount;
            // Publish the post-downmix format to downstream workers during initialization.
            // This avoids initializing downstream processors with a channel layout they will never see at runtime.
            state.ChannelCount = 1;
            state.Length = 0;
            base.Initialize(state);
        }

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (!IsInitialized)
            {
                return;
            }

            int inputChannels = state.ChannelCount;
            if (inputChannels <= 1)
            {
                // Nothing to downmix; ensure Length matches current total samples
                state.Length = audiobuffer.Length;
                return;
            }

            // Perform real-time downmix directly on the buffer using current channel count
            PerformRealTimeDownmix(audiobuffer, inputChannels);

            // Update state to reflect mono output and new effective length (total samples across all channels)
            state.ChannelCount = 1;
            int samplesPerChannel = audiobuffer.Length / inputChannels;
            state.Length = samplesPerChannel; // since mono, total samples == samples per channel
        }

        /// <summary>
        /// Optimized real-time downmix that modifies the buffer in-place for minimal latency
        /// </summary>
        private void PerformRealTimeDownmix(Span<float> buffer, int inputChannel)
        {
            int numChannels = inputChannel;
            int samplesPerChannel = buffer.Length / numChannels;

            switch (inputChannel)
            {
                case 2:
                    DownmixStereo(buffer, samplesPerChannel);
                    break;
                case 4:
                    DownmixQuad(buffer, samplesPerChannel);
                    break;
                case 6:
                    DownmixSurround6(buffer, samplesPerChannel);
                    break;
                case 7:
                    DownmixSurround7(buffer, samplesPerChannel);
                    break;
                case 8:
                    DownmixSurround8(buffer, samplesPerChannel);
                    break;
                case 9:
                    DownmixSurround9(buffer, samplesPerChannel);
                    break;
                case 12:
                    DownmixSurround12(buffer, samplesPerChannel);
                    break;
                case 16:
                    DownmixSurround16(buffer, samplesPerChannel);
                    break;
                default:
                    DownmixGeneric(buffer, samplesPerChannel, numChannels);
                    break;
            }
        }

        private static void DownmixStereo(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 2)
            {
                buffer[i] = DownmixPair(buffer[baseIndex], buffer[baseIndex + 1]);
            }
        }

        private static void DownmixQuad(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 4)
            {
                buffer[i] = (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 2] + buffer[baseIndex + 3]) * COEFF_12DB;
            }
        }

        private static void DownmixSurround6(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 6)
            {
                buffer[i] = ClampOne(buffer[baseIndex + 2] + (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 4] + buffer[baseIndex + 5] + buffer[baseIndex + 3]) * COEFF_3DB);
            }
        }

        private static void DownmixSurround7(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 7)
            {
                buffer[i] = ClampOne(buffer[baseIndex + 2] + (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 4] + buffer[baseIndex + 5] + buffer[baseIndex + 6] + buffer[baseIndex + 3]) * COEFF_3DB);
            }
        }

        private static void DownmixSurround8(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 8)
            {
                buffer[i] = ClampOne(buffer[baseIndex + 2] + (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 4] + buffer[baseIndex + 5] + buffer[baseIndex + 6] + buffer[baseIndex + 7] + buffer[baseIndex + 3]) * COEFF_3DB);
            }
        }

        private static void DownmixSurround9(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 9)
            {
                buffer[i] = ClampOne(
                    buffer[baseIndex + 2] +
                    (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 3] + buffer[baseIndex + 4] + buffer[baseIndex + 5] + buffer[baseIndex + 6]) * COEFF_3DB +
                    (buffer[baseIndex + 7] + buffer[baseIndex + 8]) * COEFF_6DB);
            }
        }

        private static void DownmixSurround12(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 12)
            {
                buffer[i] = ClampOne(
                    buffer[baseIndex + 2] +
                    (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 4] + buffer[baseIndex + 5] + buffer[baseIndex + 6] + buffer[baseIndex + 7] + buffer[baseIndex + 3]) * COEFF_3DB +
                    (buffer[baseIndex + 8] + buffer[baseIndex + 9] + buffer[baseIndex + 10] + buffer[baseIndex + 11]) * COEFF_6DB);
            }
        }

        private static void DownmixSurround16(Span<float> buffer, int samplesPerChannel)
        {
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += 16)
            {
                buffer[i] = ClampOne(
                    buffer[baseIndex + 2] +
                    (buffer[baseIndex] + buffer[baseIndex + 1] + buffer[baseIndex + 6] + buffer[baseIndex + 7] + buffer[baseIndex + 8] + buffer[baseIndex + 9] + buffer[baseIndex + 3]) * COEFF_3DB +
                    (buffer[baseIndex + 4] + buffer[baseIndex + 5] + buffer[baseIndex + 10] + buffer[baseIndex + 11] + buffer[baseIndex + 12] + buffer[baseIndex + 13] + buffer[baseIndex + 14] + buffer[baseIndex + 15]) * COEFF_6DB);
            }
        }

        private static void DownmixGeneric(Span<float> buffer, int samplesPerChannel, int numChannels)
        {
            float scale = 1f / numChannels;
            for (int i = 0, baseIndex = 0; i < samplesPerChannel; i++, baseIndex += numChannels)
            {
                float monoSample = 0f;
                int end = baseIndex + numChannels;
                for (int ch = baseIndex; ch < end; ch++)
                {
                    monoSample += buffer[ch];
                }

                buffer[i] = monoSample * scale;
            }
        }

        private static float DownmixPair(float left, float right)
        {
            float mixed = (left + right) * COEFF_6DB;
            float leftAbs = Math.Abs(left);
            float rightAbs = Math.Abs(right);
            float strongest = leftAbs >= rightAbs ? left : right;
            float strongestAbs = leftAbs >= rightAbs ? leftAbs : rightAbs;

            // Microphone endpoints may expose two channels that are phase-inverted.
            // A plain L+R mono fold-down cancels those to silence, so preserve the stronger
            // channel when the summed result collapses relative to the source level.
            if (strongestAbs > 1e-6f && Math.Abs(mixed) < strongestAbs * 0.125f)
            {
                return strongest;
            }

            return mixed;
        }

        private static float ClampOne(float value)
        {
            if (value > 1f) return 1f;
            return value < -1f ? -1f : value;
        }
    }
}
