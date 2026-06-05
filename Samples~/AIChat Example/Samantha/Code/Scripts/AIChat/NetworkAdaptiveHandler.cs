using System;
using System.Collections.Generic;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Handles network latency tracking and adaptive adjustments.
    /// </summary>
    internal sealed class NetworkAdaptiveHandler
    {
        private enum NetworkQuality { Excellent, Good, Fair, Poor }

        private readonly struct LatencySample
        {
            public readonly float LatencyMs;
            public readonly DateTime Timestamp;

            public LatencySample(float latencyMs)
            {
                LatencyMs = latencyMs;
                Timestamp = DateTime.UtcNow;
            }
        }

        private const int SampleWindowSize = 20;
        private const float ExcellentThresholdMs = 100f;
        private const float GoodThresholdMs = 300f;
        private const float FairThresholdMs = 800f;
        private const float SampleExpirationSeconds = 60f;

        private readonly Queue<LatencySample> _latencySamples = new Queue<LatencySample>();
        private readonly object _lock = new object();
        private NetworkQuality _currentQuality = NetworkQuality.Good;
        private float _averageLatency = 200f;
        private float _jitter = 50f;
        private int _consecutiveTimeouts;
        private DateTime _lastQualityChange = DateTime.UtcNow;

        public void RecordLatency(float latencyMs)
        {
            lock (_lock)
            {
                _latencySamples.Enqueue(new LatencySample(latencyMs));

                var cutoff = DateTime.UtcNow.AddSeconds(-SampleExpirationSeconds);
                while (_latencySamples.Count > 0 && _latencySamples.Peek().Timestamp < cutoff)
                {
                    _latencySamples.Dequeue();
                }
                while (_latencySamples.Count > SampleWindowSize)
                {
                    _latencySamples.Dequeue();
                }

                UpdateStatistics();
                _consecutiveTimeouts = 0;
            }
        }

        public void RecordTimeout()
        {
            lock (_lock)
            {
                _consecutiveTimeouts++;

                if (_consecutiveTimeouts >= 3 && _currentQuality != NetworkQuality.Poor)
                {
                    _currentQuality = NetworkQuality.Poor;
                    _lastQualityChange = DateTime.UtcNow;
                }
            }
        }

        public NetworkQualityInfo GetCurrentInfo()
        {
            lock (_lock)
            {
                return new NetworkQualityInfo
                {
                    Quality = _currentQuality.ToString(),
                    AverageLatencyMs = _averageLatency,
                    JitterMs = _jitter,
                    ShouldUsePreemptiveStreaming = _currentQuality <= NetworkQuality.Fair,
                    RecommendedBufferSize = GetRecommendedBufferSize(),
                    RecommendedTimeoutMs = GetRecommendedTimeout()
                };
            }
        }

        private void UpdateStatistics()
        {
            if (_latencySamples.Count == 0)
            {
                return;
            }

            float sum = 0f;
            foreach (var sample in _latencySamples)
            {
                sum += sample.LatencyMs;
            }
            _averageLatency = sum / _latencySamples.Count;

            float deviationSum = 0f;
            float? previousLatency = null;
            foreach (var sample in _latencySamples)
            {
                if (previousLatency.HasValue)
                {
                    deviationSum += Math.Abs(sample.LatencyMs - previousLatency.Value);
                }
                previousLatency = sample.LatencyMs;
            }
            _jitter = _latencySamples.Count > 1 ? deviationSum / (_latencySamples.Count - 1) : 0f;

            UpdateQuality();
        }

        private void UpdateQuality()
        {
            if ((DateTime.UtcNow - _lastQualityChange).TotalSeconds < 5)
            {
                return;
            }

            NetworkQuality newQuality;

            if (_consecutiveTimeouts >= 2)
            {
                newQuality = NetworkQuality.Poor;
            }
            else if (_averageLatency <= ExcellentThresholdMs && _jitter <= 30f)
            {
                newQuality = NetworkQuality.Excellent;
            }
            else if (_averageLatency <= GoodThresholdMs && _jitter <= 100f)
            {
                newQuality = NetworkQuality.Good;
            }
            else if (_averageLatency <= FairThresholdMs)
            {
                newQuality = NetworkQuality.Fair;
            }
            else
            {
                newQuality = NetworkQuality.Poor;
            }

            if (newQuality > _currentQuality)
            {
                _currentQuality = (NetworkQuality)Math.Min((int)_currentQuality + 1, (int)NetworkQuality.Excellent);
                _lastQualityChange = DateTime.UtcNow;
            }
            else if (newQuality < _currentQuality)
            {
                _currentQuality = newQuality;
                _lastQualityChange = DateTime.UtcNow;
            }
        }

        private int GetRecommendedBufferSize()
        {
            return _currentQuality switch
            {
                NetworkQuality.Excellent => 1,
                NetworkQuality.Good => 2,
                NetworkQuality.Fair => 3,
                NetworkQuality.Poor => 5,
                _ => 2
            };
        }

        private float GetRecommendedTimeout()
        {
            float baseTimeout = Math.Max(5000f, _averageLatency * 3 + _jitter * 2);
            return _currentQuality switch
            {
                NetworkQuality.Poor => baseTimeout * 1.5f,
                NetworkQuality.Fair => baseTimeout * 1.2f,
                _ => baseTimeout
            };
        }
    }

}
