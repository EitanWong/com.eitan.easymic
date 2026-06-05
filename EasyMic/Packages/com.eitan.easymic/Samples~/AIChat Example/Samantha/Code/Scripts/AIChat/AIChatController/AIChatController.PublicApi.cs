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
                _userInputBuffer.Clear();
            }

            BeginAssistantResponse(payload, recordUserMessage: true, isProactive: false);
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

            BeginAssistantResponse(prompt.Trim(), recordUserMessage, isProactive: true);
            return true;
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

        public ChatMetrics GetMetrics()
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

        public AIChatResolvedConfiguration GetResolvedConfiguration()
        {
            return AIChatConfigurationPolicy.CaptureResolvedConfiguration(Config);
        }

    }
}
#endif
