#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System;
using System.Collections;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void OnMicrophoneInitializedHandler(bool initialized)
        {
            if (_initializationFailed)
            {
                return;
            }

            var mic = Microphone;
            if (!initialized || mic == null)
            {
                if (!initialized)
                {
                    ReportError("Microphone initialization failed.");
                }
                return;
            }

            if (!_initialized && mic.IsRecording)
            {
                try
                {
                    mic.StopRecording();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AIChat] Failed to stop early microphone recording: {ex.Message}");
                }
            }

            ScheduleMicStartupAfterInitialization();
            _isChatActive = true;
            if (IsOnUnityThread)
            {
                _pluginHost?.NotifyChatActivated();
            }
            else
            {
                PostToUnityThread(() => _pluginHost?.NotifyChatActivated());
            }
        }

        private void OnMicrophoneLoadingProgressFeedbackHandler(string message, float progress)
        {
            if (_initializationFailed)
            {
                return;
            }

            UpdateServiceLoading(SERVICE_ASR_INIT_KEY, progress);
        }

        private void OnAsrStreamingHandler(string preview)
        {
            if (string.IsNullOrEmpty(preview))
            {
                return;
            }

            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][ASR] Streaming: {preview}");
            }

            NotifyChatStateChanged(ChatState.UserInput, preview);
        }

        private void OnAsrSubmitHandler(string utterance)
        {
            if (string.IsNullOrWhiteSpace(utterance))
            {
                return;
            }

            string trimmed = utterance.Trim();
            MarkUserActivity();

            lock (_stateLock)
            {
                if (_userInputBuffer.Length > 0)
                {
                    _userInputBuffer.Append(' ');
                }

                if (_userInputBuffer.Length + trimmed.Length > MaxUserInputBufferSize)
                {
                    Debug.LogWarning("[AIChat] User input buffer overflow, truncating.");
                    _userInputBuffer.Clear();
                }

                _userInputBuffer.Append(trimmed);
            }
            var finalSubmit = GetUserInputBuffer();
            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][ASR] Submit: {finalSubmit}");
            }
            NotifyChatStateChanged(ChatState.UserInput, finalSubmit);
            TryDispatchBufferedInput();
        }

        private async void OnSpeakingChangedHandler(bool isSpeaking)
        {
            if (IsOnUnityThread)
            {
                OnUserSpeakingStateChanged?.Invoke(isSpeaking);
            }
            else
            {
                PostToUnityThread(() => OnUserSpeakingStateChanged?.Invoke(isSpeaking));
            }

            if (isSpeaking)
            {
                MarkUserActivity();
            }

            if (isSpeaking &&
                Config.InterruptAssistantOnUserSpeech &&
                (_llmInFlight || _isAssistantSpeaking))
            {
                await CancelActiveResponseAsync().ConfigureAwait(false);
            }
        }

        private void ScheduleMicStartupAfterInitialization()
        {
            StopPendingMicStartup();
            if (!IsUnityObjectOperational())
            {
                return;
            }

            try
            {
                _pendingMicStartupCoroutine = StartCoroutine(WaitForControllerInitializationThenStartMic());
            }
            catch (MissingReferenceException)
            {
                _pendingMicStartupCoroutine = null;
            }
        }

        private void StopPendingMicStartup()
        {
            if (_pendingMicStartupCoroutine == null)
            {
                return;
            }

            if (!IsUnityObjectOperational())
            {
                _pendingMicStartupCoroutine = null;
                return;
            }

            try
            {
                StopCoroutine(_pendingMicStartupCoroutine);
            }
            catch (MissingReferenceException)
            {
            }

            _pendingMicStartupCoroutine = null;
        }

        private IEnumerator WaitForControllerInitializationThenStartMic()
        {
            while (!_initializationFailed && !_initialized)
            {
                yield return null;
            }

            if (_initializationFailed)
            {
                _pendingMicStartupCoroutine = null;
                yield break;
            }

            float delay = Mathf.Max(0f, Config.MicStartupDelay);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (_initializationFailed)
            {
                _pendingMicStartupCoroutine = null;
                yield break;
            }

            var mic = Microphone;
            if (mic != null && !mic.IsRecording)
            {
                try
                {
                    mic.StartRecording();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AIChat] Failed to start microphone recording: {ex.Message}");
                }
            }

            _pendingMicStartupCoroutine = null;
        }
    }
}
#endif
