#if EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEngine.Scripting.APIUpdating;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Describes the context in which punctuation may be applied.
    /// </summary>
    public readonly struct PunctuationRequestContext
    {
        public PunctuationRequestContext(bool isFinal, bool isStreaming)
        {
            IsFinal = isFinal;
            IsStreaming = isStreaming;
        }

        /// <summary>
        /// Gets a value indicating whether the current submission is final.
        /// </summary>
        public bool IsFinal { get; }

        /// <summary>
        /// Gets a value indicating whether the current submission originated from the streaming recognizer.
        /// </summary>
        public bool IsStreaming { get; }
    }

    /// <summary>
    /// Provides a pluggable policy for deciding when to apply the punctuation service.
    /// </summary>
    [MovedFrom(true, "Eitan.EasyMic.Runtime.Mono", null, "VoiceMicrophone/PunctuationPolicy")]
    public interface IPunctuationPolicy
    {
        /// <summary>
        /// Returns true when punctuation should be requested for the provided context.
        /// </summary>
        bool ShouldApplyPunctuation(PunctuationRequestContext context);
    }
}
#endif
