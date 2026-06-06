#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private Vector3 _lastMousePosition;
        private float _lastMouseMoveTime;
        private bool _cursorAutoHideInitialized;
        private bool _cursorHiddenByAutoHide;

        private void InitializeCursorAutoHideState()
        {
            _lastMousePosition = Input.mousePosition;
            _lastMouseMoveTime = Time.realtimeSinceStartup;
            _cursorAutoHideInitialized = true;
        }

        private void UpdateCursorAutoHideState()
        {
            if (!Config.AutoHideMouseCursorWhenIdle || !Input.mousePresent)
            {
                RestoreCursorVisibilityIfNeeded();
                return;
            }

            if (!_cursorAutoHideInitialized)
            {
                InitializeCursorAutoHideState();
            }

            Vector3 mousePosition = Input.mousePosition;
            float movementThreshold = Mathf.Max(0f, Config.CursorMoveThresholdPixels);
            bool hasMoved = (mousePosition - _lastMousePosition).sqrMagnitude >
                            movementThreshold * movementThreshold;

            if (hasMoved)
            {
                _lastMousePosition = mousePosition;
                _lastMouseMoveTime = Time.realtimeSinceStartup;

                if (_cursorHiddenByAutoHide)
                {
                    SetCursorVisibility(true);
                }

                return;
            }

            float hideDelay = Mathf.Max(0f, Config.MouseCursorHideDelaySeconds);
            if (_cursorHiddenByAutoHide || Time.realtimeSinceStartup - _lastMouseMoveTime < hideDelay)
            {
                return;
            }

            SetCursorVisibility(false);
        }

        private void ResetCursorAutoHideState()
        {
            RestoreCursorVisibilityIfNeeded();
            _cursorAutoHideInitialized = false;
        }

        private void RestoreCursorVisibilityIfNeeded()
        {
            if (!_cursorHiddenByAutoHide)
            {
                return;
            }

            Cursor.visible = true;
            _cursorHiddenByAutoHide = false;
        }

        private void SetCursorVisibility(bool visible)
        {
            Cursor.visible = visible;
            _cursorHiddenByAutoHide = !visible;
        }

        private void MarkUserActivity()
        {
            _lastUserActivityTime = _lastMainThreadTime;
        }

        private void MarkAssistantResponse()
        {
            _lastAssistantResponseTime = _lastMainThreadTime;
        }

        private void UpdateIdleState(bool dispatchBufferedInput = true)
        {
            if (!IsOnUnityThread)
            {
                PostToUnityThread(() => UpdateIdleState(dispatchBufferedInput));
                return;
            }

            bool newIdle = !_llmInFlight && !_isAssistantSpeaking;

            if (_lastIdleState == newIdle)
            {
                return;
            }

            _lastIdleState = newIdle;
            OnIdleStateChanged?.Invoke(newIdle);
            _pluginHost?.NotifyIdleStateChanged(newIdle);

            if (newIdle && dispatchBufferedInput)
            {
                TryDispatchBufferedInput();
            }
        }

        private void SetAssistantSpeakingState(bool isSpeaking)
        {
            if (_isAssistantSpeaking == isSpeaking)
            {
                return;
            }

            _isAssistantSpeaking = isSpeaking;
            if (IsOnUnityThread)
            {
                UpdateIdleState();
                return;
            }

            PostToUnityThread(UpdateIdleState);
        }

        private void NotifyChatStateChanged(ChatState state, string message)
        {
            if (IsOnUnityThread)
            {
                OnChatStateChanged?.Invoke(state, message);
                return;
            }

            PostToUnityThread(() => OnChatStateChanged?.Invoke(state, message));
        }

        private void ReportError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                message = "Unknown error.";
            }

            _lastErrorMessage = message;
            if (!_initializationFailed)
            {
                _initializationFailed = true;
                PostToUnityThread(AbortInitializationOnUnityThread);
            }

            NotifyChatStateChanged(ChatState.Failed, message);
        }

        private void AbortInitializationOnUnityThread()
        {
            if (!_initializationFailed)
            {
                return;
            }

            TeardownMicrophone();
            TeardownSpeechSynthesizer();
            TeardownTtsPipeline();

            if (_openAiClient != null)
            {
                _openAiClient.Dispose();
                _openAiClient = null;
            }

            _isChatActive = false;
            _initialized = false;
        }

        private void UpdateServiceLoading(string key, float progress)
        {
            if (_initializationFailed)
            {
                return;
            }

            if (_serviceLoadingRecord == null)
            {
                return;
            }

            float overall;
            lock (_serviceLoadingRecord)
            {
                _serviceLoadingRecord[key] = Mathf.Clamp01(progress);

                if (_serviceLoadingRecord.Count == 0)
                {
                    return;
                }

                float total = 0f;
                foreach (var kv in _serviceLoadingRecord)
                {
                    total += kv.Value;
                }

                overall = Mathf.Clamp01(total / _serviceLoadingRecord.Count);
            }

            _lastLoadingProgress = overall;

            if (!_initialized && overall >= 1f)
            {
                _initialized = true;
            }

            if (IsOnUnityThread)
            {
                OnLoadingCallback?.Invoke(overall);
            }
            else
            {
                PostToUnityThread(() => OnLoadingCallback?.Invoke(overall));
            }
        }

        private void UpdateAverageLatency(float latencyMs)
        {
            if (_totalRequestCount <= 1)
            {
                _averageResponseLatencyMs = latencyMs;
            }
            else
            {
                _averageResponseLatencyMs = _averageResponseLatencyMs * 0.8f + latencyMs * 0.2f;
            }
        }
    }
}
#endif
