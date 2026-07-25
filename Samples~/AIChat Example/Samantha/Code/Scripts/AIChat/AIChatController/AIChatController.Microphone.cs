#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections;
using System.Threading;
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
            _latencyTracker?.RecordAsrEnd(trimmed);
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

        private void OnSpeakingChangedHandler(bool isSpeaking)
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
                _latencyTracker?.RecordAsrStart();
                MarkUserActivity();
            }
            else
            {
                StopPendingBargeInConfirmation();
            }

            if (isSpeaking &&
                Config.InterruptAssistantOnUserSpeech)
            {
                FullDuplexTurnSnapshot stateSnapshot = _turnCoordinator.GetSnapshot();
                if (!stateSnapshot.IsBusy)
                {
                    return;
                }

                float lastAudioStartRealtime;
                lock (_stateLock) lastAudioStartRealtime = _lastAssistantAudioStartRealtime;
                float elapsedSinceAudioStart = lastAudioStartRealtime > 0f
                    ? Time.realtimeSinceStartup - lastAudioStartRealtime
                    : float.MaxValue;
                float guardSeconds = Mathf.Max(0f, Config.BargeInEchoGuardSeconds);
                if (stateSnapshot.AssistantSpeaking && elapsedSinceAudioStart < guardSeconds)
                {
                    ScheduleBargeInConfirmation(
                        stateSnapshot.TurnId,
                        Mathf.Max(0f, guardSeconds - elapsedSinceAudioStart));
                    return;
                }

                TryInterruptAssistantForUserSpeech(stateSnapshot.TurnId);
            }
        }

        private void ScheduleBargeInConfirmation(long turnId, float delaySeconds)
        {
            if (!IsOnUnityThread)
            {
                PostToUnityThread(() => ScheduleBargeInConfirmation(turnId, delaySeconds));
                return;
            }

            StopPendingBargeInConfirmation();
            if (!IsUnityObjectOperational())
            {
                return;
            }

            _pendingBargeInConfirmationCoroutine = StartCoroutine(
                ConfirmBargeInAfterEchoGuard(turnId, delaySeconds));
        }

        private IEnumerator ConfirmBargeInAfterEchoGuard(long turnId, float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }

            _pendingBargeInConfirmationCoroutine = null;
            if (Microphone != null && Microphone.IsSpeaking)
            {
                TryInterruptAssistantForUserSpeech(turnId);
            }
        }

        private void TryInterruptAssistantForUserSpeech(long turnId)
        {
            FullDuplexTurnSnapshot snapshot = _turnCoordinator.GetSnapshot();
            if (snapshot.TurnId != turnId || !snapshot.IsBusy)
            {
                return;
            }

            Interlocked.Increment(ref _interruptionCount);
            SignalCancelActiveResponse(advanceGeneration: true);
        }

        private void StopPendingBargeInConfirmation()
        {
            if (_pendingBargeInConfirmationCoroutine == null)
            {
                return;
            }

            if (IsUnityObjectOperational())
            {
                try
                {
                    StopCoroutine(_pendingBargeInConfirmationCoroutine);
                }
                catch (MissingReferenceException)
                {
                }
            }

            _pendingBargeInConfirmationCoroutine = null;
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
            StopPendingBargeInConfirmation();
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
                yield return new WaitForSecondsRealtime(delay);
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

            if (mic != null && mic.IsRecording)
            {
                _isChatActive = true;
                _pluginHost?.NotifyChatActivated();
            }

            _pendingMicStartupCoroutine = null;
        }
    }
}
#endif
