using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime{
    public class AudioDownmixer : AudioWriter
    {
        
        // Optimized coefficients for different channel configurations
        private static readonly float COEFF_3DB = 0.707f;  // -3dB for primary channels
        private static readonly float COEFF_6DB = 0.5f;    // -6dB for safe summing
        private static readonly float COEFF_12DB = 0.25f;  // -12dB for many channels
        private int _originalChannelCount;

        public override void Initialize(AudioState state)
        {
            _originalChannelCount = state.ChannelCount;
            base.Initialize(state);
        }

        public override void OnAudioWrite(Span<float> audiobuffer, AudioState state)
        {
            if (!IsInitialized || state.ChannelCount <= 1)
                return;
            // Perform real-time downmix directly on the buffer
            PerformRealTimeDownmix(audiobuffer, state.ChannelCount);
            state.ChannelCount = 1;
            state.Length = audiobuffer.Length / _originalChannelCount;
            // audiobuffer = audiobuffer.Slice(state.Length);

        }

        /// <summary>
        /// Optimized real-time downmix that modifies the buffer in-place for minimal latency
        /// </summary>
        private void PerformRealTimeDownmix(Span<float> buffer, int inputChannel)
        {
            int numChannels = inputChannel;
            int samplesPerChannel = buffer.Length / numChannels;
            
            // Process samples in-place for optimal performance
            for (int i = 0; i < samplesPerChannel; i++)
            {
                int baseIndex = i * numChannels;
                float monoSample = 0.0f;

                // Optimized downmix based on channel configuration
                switch (inputChannel)
                {
                    case 2: // L, R
                        monoSample = (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_6DB;
                        break;

                    case 4: // FL, FR, RL, RR
                        monoSample = (buffer[baseIndex] + buffer[baseIndex + 1] + 
                                    buffer[baseIndex + 2] + buffer[baseIndex + 3]) * COEFF_12DB;
                        break;
                    
                    case 6: // L, R, C, LFE, SL, SR
                        monoSample = Mathf.Clamp(
                            buffer[baseIndex + 2] + // C (full gain)
                            (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_3DB + // L, R
                            (buffer[baseIndex + 4] + buffer[baseIndex + 5]) * COEFF_3DB + // SL, SR
                            buffer[baseIndex + 3] * COEFF_3DB, // LFE
                            -1.0f, 1.0f);
                        break;
                    
                    case 7: // L, R, C, LFE, SL, SR, BC
                        monoSample = Mathf.Clamp(
                            buffer[baseIndex + 2] + // C
                            (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_3DB + // L, R
                            (buffer[baseIndex + 4] + buffer[baseIndex + 5]) * COEFF_3DB + // SL, SR
                            buffer[baseIndex + 6] * COEFF_3DB + // BC
                            buffer[baseIndex + 3] * COEFF_3DB, // LFE
                            -1.0f, 1.0f);
                        break;
                        
                    case 8: // L, R, C, LFE, SL, SR, BL, BR
                        monoSample = Mathf.Clamp(
                            buffer[baseIndex + 2] + // C
                            (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_3DB + // L, R
                            (buffer[baseIndex + 4] + buffer[baseIndex + 5]) * COEFF_3DB + // SL, SR
                            (buffer[baseIndex + 6] + buffer[baseIndex + 7]) * COEFF_3DB + // BL, BR
                            buffer[baseIndex + 3] * COEFF_3DB, // LFE
                            -1.0f, 1.0f);
                        break;
                    
                    case 9: // L, R, C, SL, SR, BL, BR, TFL, TFR
                        monoSample = Mathf.Clamp(
                            buffer[baseIndex + 2] + // C
                            (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_3DB + // L, R
                            (buffer[baseIndex + 3] + buffer[baseIndex + 4]) * COEFF_3DB + // SL, SR
                            (buffer[baseIndex + 5] + buffer[baseIndex + 6]) * COEFF_3DB + // BL, BR
                            (buffer[baseIndex + 7] + buffer[baseIndex + 8]) * COEFF_6DB, // Top channels
                            -1.0f, 1.0f);
                        break;

                    case 12: // L, R, C, LFE, SL, SR, BL, BR, TFL, TFR, TRL, TRR
                        monoSample = Mathf.Clamp(
                            buffer[baseIndex + 2] + // C
                            (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_3DB + // L, R
                            (buffer[baseIndex + 4] + buffer[baseIndex + 5]) * COEFF_3DB + // SL, SR
                            (buffer[baseIndex + 6] + buffer[baseIndex + 7]) * COEFF_3DB + // BL, BR
                            (buffer[baseIndex + 8] + buffer[baseIndex + 9] + 
                             buffer[baseIndex + 10] + buffer[baseIndex + 11]) * COEFF_6DB + // All top channels
                            buffer[baseIndex + 3] * COEFF_3DB, // LFE
                            -1.0f, 1.0f);
                        break;

                    case 16: // 16 channels
                        monoSample = Mathf.Clamp(
                            buffer[baseIndex + 2] + // C
                            (buffer[baseIndex] + buffer[baseIndex + 1]) * COEFF_3DB + // L, R
                            (buffer[baseIndex + 4] + buffer[baseIndex + 5]) * COEFF_6DB + // LW, RW
                            (buffer[baseIndex + 6] + buffer[baseIndex + 7] + 
                             buffer[baseIndex + 8] + buffer[baseIndex + 9]) * COEFF_3DB + // Surround channels
                            (buffer[baseIndex + 10] + buffer[baseIndex + 11] + buffer[baseIndex + 12] + 
                             buffer[baseIndex + 13] + buffer[baseIndex + 14] + buffer[baseIndex + 15]) * COEFF_6DB + // Top channels
                            buffer[baseIndex + 3] * COEFF_3DB, // LFE
                            -1.0f, 1.0f);
                        break;
                    
                    default: // Generic fallback - simple averaging
                        for (int ch = 0; ch < numChannels; ch++)
                        {
                            monoSample += buffer[baseIndex + ch];
                        }
                        monoSample /= numChannels;
                        break;
                }

                // Write mono sample back to buffer (only first channel used for mono output)
                buffer[i] = monoSample;
            }
        }
    }
}