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

            BeginAssistantResponse(payload);
            return true;
        }

        public new void SendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            BeginAssistantResponse(message.Trim());
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
                NetworkQuality = CurrentNetworkQuality
            };
        }
    }
}
