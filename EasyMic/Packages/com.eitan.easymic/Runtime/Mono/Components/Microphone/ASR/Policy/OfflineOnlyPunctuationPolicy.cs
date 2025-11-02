#if EASYMIC_SHERPA_ONNX_INTEGRATION

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Applies punctuation only to final non-streaming submissions to avoid jitter.
    /// </summary>
    public sealed class OfflineOnlyPunctuationPolicy : IPunctuationPolicy
    {
        /// <inheritdoc />
        public bool ShouldApplyPunctuation(PunctuationRequestContext context)
        {
            return context.IsFinal && !context.IsStreaming;
        }
    }
}

#endif
