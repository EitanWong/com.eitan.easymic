#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.Threading.Tasks;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        public bool TryDispatchBufferedInput()
        {
            if (!IsIdle)
            {
                return false;
            }

            string payload;
            lock (_stateLock)
            {
                if (_userInputBuffer.Length == 0)
                {
                    return false;
                }

                payload = _userInputBuffer.ToString();
            }

            bool consumed = BeginAssistantResponse(payload, recordUserMessage: true, isProactive: false);
            if (!consumed)
            {
                if (IsIdle)
                {
                    PostToUnityThread(() => TryDispatchBufferedInput());
                }

                return false;
            }

            RemoveDispatchedUserInput(payload);
            return true;
        }

        public void SubmitUserMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            MarkUserActivity();
            BeginAssistantResponse(message.Trim(), recordUserMessage: true, isProactive: false);
        }

        public bool TrySendProactiveMessage(string prompt, bool recordUserMessage = false)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return false;
            }

            if (!IsIdle)
            {
                return false;
            }

            return BeginAssistantResponse(prompt.Trim(), recordUserMessage, isProactive: true);
        }

        public async Task CancelResponseAsync()
        {
            await CancelActiveResponseAsync().ConfigureAwait(false);
        }

        public void ClearUserInput()
        {
            lock (_stateLock)
            {
                _userInputBuffer.Clear();
            }
        }

        public string GetUserInputBuffer()
        {
            lock (_stateLock)
            {
                return _userInputBuffer.ToString();
            }
        }

        private void RemoveDispatchedUserInput(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return;
            }

            lock (_stateLock)
            {
                string current = _userInputBuffer.ToString();
                if (current.Length == 0)
                {
                    return;
                }

                if (string.Equals(current, payload, System.StringComparison.Ordinal))
                {
                    _userInputBuffer.Clear();
                    return;
                }

                if (!current.StartsWith(payload, System.StringComparison.Ordinal))
                {
                    return;
                }

                _userInputBuffer.Remove(0, payload.Length);
                while (_userInputBuffer.Length > 0 && char.IsWhiteSpace(_userInputBuffer[0]))
                {
                    _userInputBuffer.Remove(0, 1);
                }
            }
        }

        public ChatMetrics GetMetrics()
        {
            lock (_stateLock)
            {
                return new ChatMetrics
                {
                    TotalRequests = _totalRequestCount,
                    FailedRequests = _failedRequestCount,
                    AverageResponseLatencyMs = _averageResponseLatencyMs,
                    LastFirstTokenLatencyMs = _lastFirstTokenLatencyMs,
                    LastFirstSentenceLatencyMs = _lastFirstSentenceLatencyMs,
                    LastFirstAudioLatencyMs = _lastFirstAudioLatencyMs,
                    LastPlaybackBufferedSeconds = _lastPlaybackBufferedSeconds,
                    InterruptionCount = _interruptionCount,
                    NetworkQuality = CurrentNetworkQuality
                };
            }
        }

        public AIChatResolvedConfiguration GetResolvedConfiguration()
        {
            return AIChatConfigurationPolicy.CaptureResolvedConfiguration(Config);
        }

    }
}
#endif
