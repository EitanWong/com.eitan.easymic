#if EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Default heuristic turn detector based on sentence count, punctuation and silence duration.
    /// </summary>
    public sealed class HeuristicTurnDetector : TurnDetector
    {
        private readonly float _minDelay;
        private readonly float _maxDelay;

        public HeuristicTurnDetector(TurnDetectionOptions settings)
        {
            settings = settings.EnsureValid();
            _minDelay = settings.MinDelaySeconds;
            _maxDelay = settings.MaxDelaySeconds;
        }

        public override float EvaluateDelay(in TurnDetectionContext context)
        {
            if (context.SilenceSeconds >= _maxDelay)
            {
                return 0f;
            }

            if (context.SegmentCount <= 1 && !context.EndsWithPunctuation)
            {
                return Mathf.Clamp(_minDelay - context.SilenceSeconds, 0f, _maxDelay);
            }

            if (context.SegmentCount >= 3 || context.EndsWithPunctuation)
            {
                return Mathf.Clamp(_maxDelay - context.SilenceSeconds, 0f, _maxDelay);
            }

            float target = Mathf.Lerp(_minDelay, _maxDelay, 0.5f);
            return Mathf.Clamp(target - context.SilenceSeconds, 0f, _maxDelay);
        }
    }
}
#endif
