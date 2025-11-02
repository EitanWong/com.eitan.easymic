#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Controls whether recognition results should be surfaced based on keyword spotting activity.
    /// </summary>
    public sealed class KeywordGate
    {
        private KeywordSettings _settings;
        private readonly float _minConversationTimeoutSeconds;
        private readonly float _speechHoldExtensionSeconds;
        private readonly Action<float> _extendSilenceHold;

        private bool _isActive;
        private bool _pendingFlush;
        private bool _hasEmittedDuringActivation;
        private float _silenceTimer;
        private string _activeKeyword = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="KeywordGate"/> class.
        /// </summary>
        public KeywordGate(
            KeywordSettings settings,
            float minConversationTimeoutSeconds,
            float speechHoldExtensionSeconds,
            Action<float> extendSilenceHold)
        {
            _settings = settings;
            _minConversationTimeoutSeconds = Mathf.Max(0f, minConversationTimeoutSeconds);
            _speechHoldExtensionSeconds = Mathf.Max(0f, speechHoldExtensionSeconds);
            _extendSilenceHold = extendSilenceHold;
        }

        /// <summary>
        /// Raised when the keyword gate becomes active.
        /// </summary>
        public event Action<string> Activated;

        /// <summary>
        /// Raised when the keyword gate becomes inactive.
        /// </summary>
        public event Action<string> Deactivated;

        /// <summary>
        /// Raised whenever keyword activity state changes.
        /// </summary>
        public event Action<string, bool> ActivityChanged;

        /// <summary>
        /// Gets a value indicating whether keyword spotting must be active to surface recognition.
        /// </summary>
        public bool RequiresKeyword => _settings.IsEnabled;

        /// <summary>
        /// Gets a value indicating whether the gate currently allows recognition output.
        /// </summary>
        public bool AllowsRecognition => !RequiresKeyword || _isActive;

        /// <summary>
        /// Updates keyword-driven timers using the provided frame delta.
        /// </summary>
        public void Update(float deltaTime, bool isSpeaking, bool hasVoiceActivity)
        {
            if (!_isActive || !_settings.ContinuousConversation)
            {
                return;
            }

            if (isSpeaking || hasVoiceActivity)
            {
                _silenceTimer = 0f;
                return;
            }

            _silenceTimer += Mathf.Max(0f, deltaTime);
            float timeout = Mathf.Max(_minConversationTimeoutSeconds, Mathf.Max(0f, _settings.ContinuousConversationTimeoutSeconds));
            if (_silenceTimer >= timeout)
            {
                Deactivate();
            }
        }

        /// <summary>
        /// Applies new keyword settings, resetting the gate if keyword spotting is disabled.
        /// </summary>
        public void ApplySettings(KeywordSettings settings)
        {
            _settings = settings;
            if (!RequiresKeyword)
            {
                Reset(true);
            }
        }

        /// <summary>
        /// Activates the gate for the provided keyword.
        /// </summary>
        public void Activate(string keyword)
        {
            if (!RequiresKeyword)
            {
                return;
            }

            _isActive = true;
            _silenceTimer = 0f;
            _pendingFlush = false;
            _hasEmittedDuringActivation = false;
            _activeKeyword = string.IsNullOrWhiteSpace(keyword) ? _activeKeyword : keyword;

            _extendSilenceHold?.Invoke(_speechHoldExtensionSeconds);

            if (_settings.UseTriggerSound && _settings.TriggerSoundClip != null)
            {
                var clip = _settings.TriggerSoundClip;
                AudioPlayback.PlayClip(clip);
                _extendSilenceHold?.Invoke(Mathf.Max(0f, clip.length * 2f));
            }

            Activated?.Invoke(_activeKeyword);
            ActivityChanged?.Invoke(_activeKeyword, true);
        }

        /// <summary>
        /// Deactivates the gate, optionally requesting a streaming flush.
        /// </summary>
        public void Deactivate()
        {
            bool wasActive = _isActive;
            if (!wasActive && !_pendingFlush)
            {
                return;
            }

            bool shouldFlush = wasActive && _hasEmittedDuringActivation;
            string keyword = _activeKeyword;

            _isActive = false;
            _activeKeyword = string.Empty;
            _silenceTimer = 0f;
            _hasEmittedDuringActivation = false;
            _pendingFlush |= shouldFlush;

            if (wasActive)
            {
                ActivityChanged?.Invoke(keyword, false);
                Deactivated?.Invoke(keyword);
            }
        }

        /// <summary>
        /// Notifies the gate that an utterance has ended.
        /// </summary>
        public void OnUtteranceEnded()
        {
            if (!RequiresKeyword)
            {
                return;
            }

            if (_settings.ContinuousConversation)
            {
                _silenceTimer = 0f;
                return;
            }

            Deactivate();
        }

        /// <summary>
        /// Determines whether a streaming payload should be emitted, optionally forcing a flush.
        /// </summary>
        public bool TryGetStreamingPayload(string content, out string payload)
        {
            content ??= string.Empty;

            if (!RequiresKeyword)
            {
                payload = content;
                return true;
            }

            if (_isActive)
            {
                payload = content;
                if (!string.IsNullOrEmpty(content))
                {
                    _hasEmittedDuringActivation = true;
                }

                if (string.IsNullOrEmpty(content) && !_hasEmittedDuringActivation)
                {
                    // Allow clearing UI even before a keyword emission.
                    _pendingFlush = false;
                }

                return true;
            }

            if (_pendingFlush)
            {
                payload = string.Empty;
                _pendingFlush = false;
                _hasEmittedDuringActivation = false;
                return true;
            }

            payload = string.Empty;
            return false;
        }

        /// <summary>
        /// Resets the keyword gate state.
        /// </summary>
        public void Reset(bool clearStreamingHistory)
        {
            bool wasActive = _isActive;
            bool shouldFlush = clearStreamingHistory && (_isActive || _hasEmittedDuringActivation);
            string keyword = _activeKeyword;

            _isActive = false;
            _activeKeyword = string.Empty;
            _silenceTimer = 0f;
            _hasEmittedDuringActivation = false;
            _pendingFlush = shouldFlush;

            if (wasActive)
            {
                ActivityChanged?.Invoke(keyword, false);
                Deactivated?.Invoke(keyword);
            }
        }
    }
}
#endif
