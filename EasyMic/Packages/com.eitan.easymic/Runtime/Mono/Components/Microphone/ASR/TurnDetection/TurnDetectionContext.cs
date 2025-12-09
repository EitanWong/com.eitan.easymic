#if EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// /// Extended turn detection context with additional linguistic features.
    /// </summary>
    public readonly struct TurnDetectionContext
    {
        public readonly float SilenceSeconds;
        public readonly int SegmentCount;
        public readonly int CharacterCount;
        public readonly bool EndsWithPunctuation;
        public readonly bool EndsWithConjunction;
        public readonly bool HasOpenParentheses;
        public readonly bool HasOpenQuotes;
        public readonly char LastCharacter;

        public TurnDetectionContext(
            float silenceSeconds,
            int segmentCount,
            int characterCount,
            bool endsWithPunctuation,
            bool endsWithConjunction = false,
            bool hasOpenParentheses = false,
            bool hasOpenQuotes = false,
            char lastCharacter = '\0')
        {
            SilenceSeconds = silenceSeconds;
            SegmentCount = segmentCount;
            CharacterCount = characterCount;
            EndsWithPunctuation = endsWithPunctuation;
            EndsWithConjunction = endsWithConjunction;
            HasOpenParentheses = hasOpenParentheses;
            HasOpenQuotes = hasOpenQuotes;
            LastCharacter = lastCharacter;
        }
    }
}
#endif
