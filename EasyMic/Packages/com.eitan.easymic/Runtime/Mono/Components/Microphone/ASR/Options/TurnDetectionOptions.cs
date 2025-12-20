#if EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
{
    /// <summary>
    /// Serialized settings controlling silence delays before finalizing turns.
    /// </summary>
    [System.Serializable]
    public struct TurnDetectionOptions
    {
        [Min(0f)] public float MinDelaySeconds;
        [Min(0f)] public float MaxDelaySeconds;

        public TurnDetectionOptions(float minDelaySeconds, float maxDelaySeconds)
        {
            MinDelaySeconds = Mathf.Max(0f, minDelaySeconds);
            MaxDelaySeconds = Mathf.Max(MinDelaySeconds, maxDelaySeconds);
        }

        public TurnDetectionOptions EnsureValid()
        {
            float min = Mathf.Max(0f, MinDelaySeconds);
            float max = Mathf.Max(min, MaxDelaySeconds);
            return new TurnDetectionOptions(min, max);
        }

        public static TurnDetectionOptions Default => new TurnDetectionOptions(0.5f, 2.4f);
    }
}
#endif
