#if EASYMIC_SHERPA_ONNX_INTEGRATION
namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
{
    /// <summary>
    /// Exposes the capabilities required for a particular speech recognition mode.
    /// </summary>
    public interface IRecognitionStrategy
    {
        /// <summary>
        /// Gets the associated recognition mode.
        /// </summary>
        RecognitionMode Mode { get; }

        /// <summary>
        /// Gets a value indicating whether a streaming recognizer is required.
        /// </summary>
        bool RequiresStreaming { get; }

        /// <summary>
        /// Gets a value indicating whether an offline recognizer is required.
        /// </summary>
        bool RequiresOffline { get; }

        /// <summary>
        /// Gets a value indicating whether a voice activity detector is required.
        /// </summary>
        bool RequiresVoiceActivity { get; }

        /// <summary>
        /// Gets a value indicating whether streaming results should be committed to the transcript.
        /// </summary>
        bool AllowsStreamingFinalCommit { get; }

        /// <summary>
        /// Gets a value indicating whether streaming text should drive voice activity changes.
        /// </summary>
        bool AppliesStreamingVoiceActivity { get; }

        /// <summary>
        /// Gets a value indicating whether transcripts should be submitted when speaking ends and no offline recognizer is present.
        /// </summary>
        bool SubmitWhenSpeakingEndsWithoutOffline { get; }
    }



}
#endif
