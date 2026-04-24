#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.ASR
{
    /// <summary>
    /// Recognition strategy for streaming-only scenarios.
    /// </summary>
    public sealed class StreamingRecognitionStrategy : IRecognitionStrategy
    {
        /// <inheritdoc />
        public RecognitionMode Mode => RecognitionMode.Streaming;

        /// <inheritdoc />
        public bool RequiresStreaming => true;

        /// <inheritdoc />
        public bool RequiresOffline => false;

        /// <inheritdoc />
        public bool RequiresVoiceActivity => false;

        /// <inheritdoc />
        public bool AllowsStreamingFinalCommit => true;

        /// <inheritdoc />
        public bool AppliesStreamingVoiceActivity => true;

        /// <inheritdoc />
        public bool SubmitWhenSpeakingEndsWithoutOffline => true;
    }
}
#endif
