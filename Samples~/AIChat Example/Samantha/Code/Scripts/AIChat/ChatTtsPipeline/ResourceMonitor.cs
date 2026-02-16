#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class ResourceMonitor
    {
        private readonly int _maxParallelism;
        private readonly int _minParallelism;
        private volatile int _currentParallelism;
        private readonly object _lock = new object();
        private DateTime _lastAdjustment = DateTime.UtcNow;
        private readonly TimeSpan _adjustmentCooldown = TimeSpan.FromSeconds(2);
        private long _totalGenerationTimeMs;
        private int _generationCount;

        public ResourceMonitor(int minParallelism = 1, int maxParallelism = 0)
        {
            _minParallelism = Math.Max(1, minParallelism);
            _maxParallelism = maxParallelism > 0
                ? maxParallelism
                : Math.Max(2, Environment.ProcessorCount - 1);
            _currentParallelism = Math.Min(
                _maxParallelism,
                Math.Max(_minParallelism, Environment.ProcessorCount / 2));
        }

        public int CurrentParallelism => _currentParallelism;

        public void RecordGeneration(long elapsedMs)
        {
            lock (_lock)
            {
                _totalGenerationTimeMs += elapsedMs;
                _generationCount++;
            }
        }

        public float AverageGenerationTimeMs
        {
            get
            {
                lock (_lock)
                {
                    return _generationCount > 0
                        ? (float)_totalGenerationTimeMs / _generationCount
                        : 0f;
                }
            }
        }

        public void AdjustBasedOnLoad()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _lastAdjustment < _adjustmentCooldown)
                {
                    return;
                }

                long memoryUsed = GC.GetTotalMemory(false);
                long memoryThreshold = 500 * 1024 * 1024; // 500MB

                int newParallelism = _currentParallelism;

                if (memoryUsed > memoryThreshold)
                {
                    newParallelism = Math.Max(_minParallelism, _currentParallelism - 1);
                }
                else if (memoryUsed < memoryThreshold / 2)
                {
                    newParallelism = Math.Min(_maxParallelism, _currentParallelism + 1);
                }

                if (newParallelism != _currentParallelism)
                {
                    _currentParallelism = newParallelism;
                    _lastAdjustment = DateTime.UtcNow;
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _totalGenerationTimeMs = 0;
                _generationCount = 0;
            }
        }
    }
}
#endif
