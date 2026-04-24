#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.Internal
{
    internal sealed class AdaptiveSynthesisScheduler
    {
        private const float MinBufferedSecondsClamp = 0.02f;
        private const float MaxBufferedSecondsClamp = 0.5f;
        private const int MaxParallelClamp = 8;
        private const float SmoothFactor = 0.2f;

        private readonly float _minBufferedSeconds;
        private readonly float _maxBufferedSeconds;
        private float _smoothedLoad;
        private bool _hasLoadSample;
        private double _lastCpuTotalSeconds;
        private float _lastSampleTime;
        private bool _hasCpuSample;

        public float TargetBufferedSeconds { get; private set; }
        public int MaxParallel { get; private set; }

        public AdaptiveSynthesisScheduler(float baseBufferedSeconds, int baseMaxParallel)
        {
            float baseBuffer = Mathf.Clamp(baseBufferedSeconds, MinBufferedSecondsClamp, MaxBufferedSecondsClamp);
            _minBufferedSeconds = Mathf.Clamp(baseBuffer * 0.5f, MinBufferedSecondsClamp, 0.2f);
            _maxBufferedSeconds = Mathf.Clamp(baseBuffer * 2.5f, 0.08f, MaxBufferedSecondsClamp);
            if (_maxBufferedSeconds <= _minBufferedSeconds)
            {
                _maxBufferedSeconds = Mathf.Min(MaxBufferedSecondsClamp, _minBufferedSeconds + 0.02f);
            }

            TargetBufferedSeconds = baseBuffer;

            int hardMax = Mathf.Clamp(Mathf.Max(1, SystemInfo.processorCount - 1), 1, MaxParallelClamp);
            int baseParallel = Mathf.Clamp(baseMaxParallel, 1, hardMax);
            MaxParallel = baseParallel;
        }

        public void Sample()
        {
            float cpuLoad = SampleCpuLoad();
            float framePressure = SampleFramePressure();
            float load = CombineLoad(cpuLoad, framePressure);
            if (load < 0f)
            {
                return;
            }

            _smoothedLoad = _hasLoadSample ? Mathf.Lerp(_smoothedLoad, load, SmoothFactor) : load;
            _hasLoadSample = true;

            int hardMax = Mathf.Clamp(Mathf.Max(1, SystemInfo.processorCount - 1), 1, MaxParallelClamp);
            MaxParallel = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(hardMax, 1, _smoothedLoad)), 1, hardMax);
            TargetBufferedSeconds = Mathf.Clamp(
                Mathf.Lerp(_minBufferedSeconds, _maxBufferedSeconds, _smoothedLoad),
                _minBufferedSeconds,
                _maxBufferedSeconds);
        }

        private float SampleCpuLoad()
        {
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                double cpuTotalSeconds = process.TotalProcessorTime.TotalSeconds;
                float now = Time.realtimeSinceStartup;

                if (!_hasCpuSample)
                {
                    _lastCpuTotalSeconds = cpuTotalSeconds;
                    _lastSampleTime = now;
                    _hasCpuSample = true;
                    return -1f;
                }

                double cpuDelta = cpuTotalSeconds - _lastCpuTotalSeconds;
                float timeDelta = now - _lastSampleTime;

                _lastCpuTotalSeconds = cpuTotalSeconds;
                _lastSampleTime = now;

                if (timeDelta <= 0.001f)
                {
                    return -1f;
                }

                int cores = Mathf.Max(1, SystemInfo.processorCount);
                double usage = cpuDelta / (timeDelta * cores);
                return Mathf.Clamp01((float)usage);
            }
            catch
            {
                return -1f;
            }
        }

        private float SampleFramePressure()
        {
            if (Time.timeScale <= 0.0001f)
            {
                return -1f;
            }

            float delta = Time.smoothDeltaTime;
            if (delta <= 0f)
            {
                return -1f;
            }

            int targetRate = Application.targetFrameRate > 0 ? Application.targetFrameRate : 60;
            if (targetRate <= 0)
            {
                return -1f;
            }

            float targetDelta = 1f / targetRate;
            if (targetDelta <= 0f)
            {
                return -1f;
            }

            float pressure = (delta - targetDelta) / targetDelta;
            return Mathf.Clamp01(pressure);
        }

        private static float CombineLoad(float cpuLoad, float framePressure)
        {
            bool cpuOk = cpuLoad >= 0f;
            bool frameOk = framePressure >= 0f;
            if (cpuOk && frameOk)
            {
                return Mathf.Max(cpuLoad, framePressure);
            }

            if (cpuOk)
            {
                return cpuLoad;
            }

            if (frameOk)
            {
                return framePressure;
            }

            return -1f;
        }
    }
}
#endif
