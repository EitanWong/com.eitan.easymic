#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Tracks voice activity and manages the transitions between idle and speaking states.
    /// </summary>
    public sealed class SpeechStateMachine
    {
        private readonly float _minSilenceAfterSpeech;
        private readonly float _maxSilenceAfterSpeech;
        private readonly float _silenceDurationScale;
        private readonly float _minSpeechSegmentDuration;

        private bool _isVoiceActive;
        private bool _lastVoiceActive;
        private bool _isSpeaking;

        private float _silenceElapsed;
        private float _silenceHold;
        private float _currentSpeechDuration;
        private float _lastSpeechDuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpeechStateMachine"/> class.
        /// </summary>
        public SpeechStateMachine(
            float minSilenceAfterSpeech,
            float maxSilenceAfterSpeech,
            float silenceDurationScale,
            float minSpeechSegmentDuration)
        {
            _minSilenceAfterSpeech = Mathf.Max(0f, minSilenceAfterSpeech);
            _maxSilenceAfterSpeech = Mathf.Max(_minSilenceAfterSpeech, maxSilenceAfterSpeech);
            _silenceDurationScale = Mathf.Max(0f, silenceDurationScale);
            _minSpeechSegmentDuration = Mathf.Max(0f, minSpeechSegmentDuration);
        }

        /// <summary>
        /// Raised when the voice activity flag changes.
        /// </summary>
        public event Action<bool> VoiceActivityChanged;

        /// <summary>
        /// Raised when the speaking state changes.
        /// </summary>
        public event Action<bool> SpeakingChanged;

        /// <summary>
        /// Raised when the machine determines that an utterance has ended.
        /// </summary>
        public event Action UtteranceEnded;

        /// <summary>
        /// Gets the current speaking state.
        /// </summary>
        public bool IsSpeaking => _isSpeaking;

        /// <summary>
        /// Gets the current voice activity state.
        /// </summary>
        public bool IsVoiceActive => _isVoiceActive;

        /// <summary>
        /// Extends the amount of silence required before transitioning to idle.
        /// </summary>
        public void ExtendSilenceHold(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            _silenceHold = Mathf.Max(_silenceHold, seconds);
        }

        /// <summary>
        /// Sets the current voice activity flag.
        /// </summary>
        public void SetVoiceActivity(bool isActive)
        {
            if (_isVoiceActive == isActive)
            {
                return;
            }

            _isVoiceActive = isActive;
            if (_isVoiceActive)
            {
                ResetSilence();
            }

            VoiceActivityChanged?.Invoke(_isVoiceActive);
        }

        /// <summary>
        /// Updates the state machine using the supplied delta time.
        /// </summary>
        public void Update(float deltaTime)
        {
            deltaTime = Mathf.Max(0f, deltaTime);

            if (_isVoiceActive)
            {
                if (!_lastVoiceActive)
                {
                    _currentSpeechDuration = 0f;
                }

                _currentSpeechDuration += deltaTime;
                ResetSilence();

                if (!_isSpeaking)
                {
                    _isSpeaking = true;
                    SpeakingChanged?.Invoke(true);
                }
            }
            else
            {
                if (_lastVoiceActive)
                {
                    FinalizeSpeechSegment();
                }

                AdvanceSilence(deltaTime);

                if (_isSpeaking && IsSilenceThresholdMet())
                {
                    _isSpeaking = false;
                    SpeakingChanged?.Invoke(false);
                    UtteranceEnded?.Invoke();
                }
            }

            _lastVoiceActive = _isVoiceActive;
        }

        /// <summary>
        /// Resets the entire state machine.
        /// </summary>
        public void Reset()
        {
            _isVoiceActive = false;
            _lastVoiceActive = false;
            _isSpeaking = false;
            _silenceElapsed = 0f;
            _silenceHold = 0f;
            _currentSpeechDuration = 0f;
            _lastSpeechDuration = _minSpeechSegmentDuration;
        }

        private void ResetSilence()
        {
            _silenceElapsed = 0f;
        }

        private void AdvanceSilence(float deltaTime)
        {
            _silenceElapsed += deltaTime;
            if (_silenceHold > 0f)
            {
                _silenceHold = Mathf.Max(0f, _silenceHold - deltaTime);
            }
        }

        private void FinalizeSpeechSegment()
        {
            _lastSpeechDuration = Mathf.Max(_currentSpeechDuration, _minSpeechSegmentDuration);
            _currentSpeechDuration = 0f;
            ResetSilence();
        }

        private bool IsSilenceThresholdMet()
        {
            float speechDuration = Mathf.Max(_lastSpeechDuration, _minSpeechSegmentDuration);
            float dynamicThreshold = Mathf.Clamp(
                speechDuration * _silenceDurationScale,
                _minSilenceAfterSpeech,
                _maxSilenceAfterSpeech);
            float required = Mathf.Max(dynamicThreshold, _silenceHold);
            return _silenceElapsed >= required;
        }
    }
}
#endif
