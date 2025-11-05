#if EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Carries contextual information used by turn detectors.
    /// </summary>
    public readonly struct TurnDetectionContext
    {
        public TurnDetectionContext(string transcript, int segmentCount, bool endsWithPunctuation, float silenceSeconds)
        {
            Transcript = transcript ?? string.Empty;
            SegmentCount = Mathf.Max(0, segmentCount);
            EndsWithPunctuation = endsWithPunctuation;
            SilenceSeconds = Mathf.Max(0f, silenceSeconds);
        }

        public string Transcript { get; }
        public int SegmentCount { get; }
        public bool EndsWithPunctuation { get; }
        public float SilenceSeconds { get; }
    }
}
#endif
