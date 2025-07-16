using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// A volume gate audio filter that silences audio below a certain volume threshold,
    /// with smooth, sample-accurate transitions. This version features a proper lookahead
    /// implementation for preserving transients and is fully multi-channel aware.
    /// </summary>
    public class VolumeGateFilter : AudioWriter
    {
        /// <summary>
        /// Represents the current state of the noise gate.
        /// </summary>
        public enum VolumeGateState
        {
            Closed,
            Attacking,
            Open,
            Holding,
            Releasing
        }

        // --- Configuration ---
        public float ThresholdDb { get; set; } = -35.0f;
        public float AttackTime { get; set; } = 0.005f;  // Time to fully open the gate (5ms)
        public float HoldTime { get; set; } = 0.25f;   // Time to wait before starting to close (250ms)
        public float ReleaseTime { get; set; } = 0.2f;    // Time to fully close the gate (200ms)
        public float LookaheadTime { get; set; } = 0.005f; // Time to look into the future to catch transients (5ms)

        // --- State ---
        public VolumeGateState CurrentState { get; private set; } = VolumeGateState.Closed;
        public float CurrentDb => _envelope > 0 ? 20 * MathF.Log10(_envelope) : -144.0f;

        // --- Private Internals ---
        private float _timeBelowThreshold;
        private float _gateLevel;   // 0.0 (closed) to 1.0 (open) gain multiplier
        private float _envelope;    // Current detected signal envelope (linear amplitude)

        // --- Lookahead Buffer ---
        private float[] _internalBuffer;
        private int _bufferSize;
        private int _writePosition;
        
        // --- Pre-calculated Parameters ---
        private int _sampleRate;
        private int _channelCount;
        private float _thresholdLinear;
        private float _attackIncrementPerSample;
        private float _releaseDecrementPerSample;
        private float _envelopeReleaseCoeff;
        private int _lookaheadFrames;

        /// <summary>
        /// Processes the audio buffer, applying the noise gate effect.
        /// </summary>
        public override void OnAudioWrite(Span<float> audioBuffer, AudioState state)
        {


            if (state.SampleRate <= 0 || state.ChannelCount <= 0 || audioBuffer.IsEmpty)
            {
                return;
            }

            // Update parameters if the audio format has changed
            UpdateParameters(state);

            // Process audio frame by frame
            ProcessAudio(audioBuffer);
        }

        /// <summary>
        /// Applies the gate logic to the audio buffer on a frame-by-frame basis.
        /// This is the core of the filter.
        /// </summary>
        private void ProcessAudio(Span<float> audioBuffer)
        {
            float sampleDeltaTime = 1.0f / _sampleRate;
            int frameCount = audioBuffer.Length / _channelCount;

            for (int i = 0; i < frameCount; i++)
            {
                // The position to read the "future" audio for detection
                int detectionReadPos = (_writePosition + _lookaheadFrames * _channelCount) % _bufferSize;
                
                // The position to read the "present" audio for processing
                int processReadPos = _writePosition;

                // --- Write incoming audio to the circular buffer ---
                for (int ch = 0; ch < _channelCount; ch++)
                {
                    _internalBuffer[processReadPos + ch] = audioBuffer[i * _channelCount + ch];
                }

                // 1. --- Envelope Detection (using future audio) ---
                float maxInFrame = 0f;
                for (int ch = 0; ch < _channelCount; ch++)
                {
                    float sample = MathF.Abs(_internalBuffer[detectionReadPos + ch]);
                    if (sample > maxInFrame)
                    {
                        maxInFrame = sample;
                    }
                }
                
                // Update envelope follower
                if (maxInFrame > _envelope)
                {
                    _envelope = maxInFrame; // Instant attack
                }
                else
                {
                    _envelope *= _envelopeReleaseCoeff; // Smooth release
                }
                
                bool isSignalAboveThreshold = _envelope >= _thresholdLinear;

                // 2. --- Update the gate's state machine ---
                UpdateState(isSignalAboveThreshold, sampleDeltaTime);

                // 3. --- Adjust the gain level based on the current state ---
                switch (CurrentState)
                {
                    case VolumeGateState.Attacking:
                        _gateLevel = Math.Min(1.0f, _gateLevel + _attackIncrementPerSample);
                        break;
                    case VolumeGateState.Releasing:
                        _gateLevel = Math.Max(0.0f, _gateLevel - _releaseDecrementPerSample);
                        break;
                    case VolumeGateState.Open:
                    case VolumeGateState.Holding:
                        _gateLevel = 1.0f;
                        break;
                    case VolumeGateState.Closed:
                        _gateLevel = 0.0f;
                        break;
                }

                // 4. --- Apply the gain to the "present" audio and write to output ---
                for (int ch = 0; ch < _channelCount; ch++)
                {
                    // Read the delayed sample, apply gain, and write to the output buffer
                    audioBuffer[i * _channelCount + ch] = _internalBuffer[processReadPos + ch] * _gateLevel;
                }

                // 5. --- Advance the write position in the circular buffer ---
                _writePosition = (processReadPos + _channelCount) % _bufferSize;
            }
        }
        
        /// <summary>
        /// Updates the state of the gate based on the signal level and timers.
        /// </summary>
        private void UpdateState(bool isSignalAboveThreshold, float sampleDeltaTime)
        {
            switch (CurrentState)
            {
                case VolumeGateState.Closed:
                    if (isSignalAboveThreshold)
                    {
                        CurrentState = VolumeGateState.Attacking;
                    }
                    break;
                
                case VolumeGateState.Attacking:
                    if (_gateLevel >= 1.0f)
                    {
                        CurrentState = VolumeGateState.Open;
                    }
                    else if (!isSignalAboveThreshold)
                    {
                        CurrentState = VolumeGateState.Releasing;
                    }
                    break;

                case VolumeGateState.Open:
                    if (!isSignalAboveThreshold)
                    {
                        CurrentState = VolumeGateState.Holding;
                        _timeBelowThreshold = 0;
                    }
                    break;

                case VolumeGateState.Holding:
                    if (isSignalAboveThreshold)
                    {
                        CurrentState = VolumeGateState.Open;
                    }
                    else
                    {
                        _timeBelowThreshold += sampleDeltaTime;
                        if (_timeBelowThreshold >= HoldTime)
                        {
                            CurrentState = VolumeGateState.Releasing;
                        }
                    }
                    break;

                case VolumeGateState.Releasing:
                    if (isSignalAboveThreshold)
                    {
                        CurrentState = VolumeGateState.Attacking;
                    }
                    else if (_gateLevel <= 0.0f)
                    {
                        CurrentState = VolumeGateState.Closed;
                    }
                    break;
            }
        }
        
        /// <summary>
        /// Initializes or updates audio parameters and recalculates derived values.
        /// </summary>
        private void UpdateParameters(AudioState state)
        {
            if (_sampleRate == state.SampleRate && _channelCount == state.ChannelCount) return;
            
            _sampleRate = state.SampleRate;
            _channelCount = state.ChannelCount;

            // Convert dB threshold to linear amplitude
            _thresholdLinear = MathF.Pow(10, ThresholdDb / 20.0f);
            
            // Calculate lookahead buffer size. It must be large enough to hold the lookahead data.
            // Using a power of 2 for the size can sometimes be more efficient for modulo operations, but isn't strictly necessary.
            _lookaheadFrames = (int)(LookaheadTime * _sampleRate);
            int requiredBufferSize = (_lookaheadFrames + 1) * _channelCount * 2; // Make it larger to be safe
            _bufferSize = requiredBufferSize;
            _internalBuffer = new float[_bufferSize];
            _writePosition = 0;

            // Calculate per-sample increments for attack and release for sample-accurate ramps
            float attackSamples = AttackTime * _sampleRate;
            _attackIncrementPerSample = attackSamples > 0 ? 1.0f / attackSamples : 1.0f;

            float releaseSamples = ReleaseTime * _sampleRate;
            _releaseDecrementPerSample = releaseSamples > 0 ? 1.0f / releaseSamples : 1.0f;
            
            // Calculate coefficient for the envelope follower's release (e.g., 100ms release)
            float envelopeReleaseTime = 0.1f;
            _envelopeReleaseCoeff = MathF.Exp(-1.0f / (envelopeReleaseTime * _sampleRate));

            // Reset state
            _gateLevel = 0.0f;
            _envelope = 0.0f;
            CurrentState = VolumeGateState.Closed;
            Array.Clear(_internalBuffer, 0, _internalBuffer.Length);
        }
    }
}