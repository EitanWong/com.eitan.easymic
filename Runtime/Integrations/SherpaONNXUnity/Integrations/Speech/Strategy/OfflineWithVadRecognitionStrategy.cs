#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Speech
{
    /// <summary>
    /// Recognition strategy for offline recognition assisted by VAD.
    /// </summary>
    public sealed class OfflineWithVadRecognitionStrategy : IRecognitionStrategy
    {
        /// <inheritdoc />
        public RecognitionMode Mode => RecognitionMode.OfflineWithVad;

        /// <inheritdoc />
        public bool RequiresStreaming => false;

        /// <inheritdoc />
        public bool RequiresOffline => true;

        /// <inheritdoc />
        public bool RequiresVoiceActivity => true;

        /// <inheritdoc />
        public bool AllowsStreamingFinalCommit => false;

        /// <inheritdoc />
        public bool AppliesStreamingVoiceActivity => false;

        /// <inheritdoc />
        public bool SubmitWhenSpeakingEndsWithoutOffline => false;
    }
}
#endif
