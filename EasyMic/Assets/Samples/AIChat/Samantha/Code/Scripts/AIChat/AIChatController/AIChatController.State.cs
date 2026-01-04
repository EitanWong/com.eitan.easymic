using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void MarkUserActivity()
        {
            _lastUserActivityTime = _lastMainThreadTime;
        }

        private void MarkAssistantResponse()
        {
            _lastAssistantResponseTime = _lastMainThreadTime;
        }

        private void UpdateIdleState()
        {
            bool newIdle = !_llmInFlight && !_isAssistantSpeaking;

            if (_lastIdleState == newIdle)
            {
                return;
            }

            _lastIdleState = newIdle;
            OnIdleStateChanged?.Invoke(newIdle);
            _pluginHost?.NotifyIdleStateChanged(newIdle);

            if (newIdle)
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

        private void UpdateServiceLoading(string key, float progress)
        {
            _serviceLoadingRecord[key] = progress;

            if (_serviceLoadingRecord.Count == 0)
            {
                return;
            }

            float total = 0f;
            foreach (var kv in _serviceLoadingRecord)
            {
                total += kv.Value;
            }

            float overall = total / _serviceLoadingRecord.Count;
            OnLoadingCallback?.Invoke(overall);

            if (!_initialized && overall >= 1f)
            {
                _initialized = true;
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
