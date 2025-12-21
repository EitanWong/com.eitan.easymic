using System;
using System.Collections;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void OnMicrophoneInitializedHandler(bool initialized)
        {
            var mic = Microphone;
            if (!initialized || mic == null)
            {
                return;
            }

            if (!mic.IsRecording)
            {
                StartCoroutine(DelayedInvoke(() =>
                {
                    var innerMic = Microphone;
                    if (innerMic != null && !innerMic.IsRecording)
                    {
                        innerMic.StartRecording();
                    }
                }, Config.MicStartupDelay));
            }

            _isChatActive = true;
            _pluginHost?.NotifyChatActivated();
        }

        private void OnMicrophoneLoadingProgressFeedbackHandler(string message, float progress)
        {
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

            OnChatStateChanged?.Invoke(ChatState.UserInput, preview);
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
            OnChatStateChanged?.Invoke(ChatState.UserInput, finalSubmit);
            TryDispatchBufferedInput();
        }

        private async void OnSpeakingChangedHandler(bool isSpeaking)
        {
            OnUserSpeakingStateChanged?.Invoke(isSpeaking);

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

        private IEnumerator DelayedInvoke(Action callback, float delay)
        {
            yield return new WaitForSeconds(delay);
            callback?.Invoke();
        }
    }
}
