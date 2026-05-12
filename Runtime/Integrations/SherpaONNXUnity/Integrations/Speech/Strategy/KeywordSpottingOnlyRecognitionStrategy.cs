#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Speech
{
    /// <summary>
    /// Recognition strategy for keyword spotting-only scenarios (no ASR, no VAD).
    /// </summary>
    public sealed class KeywordSpottingOnlyRecognitionStrategy : IRecognitionStrategy
    {
        public RecognitionMode Mode => RecognitionMode.KeywordSpottingOnly;
        public bool RequiresStreaming => false;
        public bool RequiresOffline => false;
        public bool RequiresVoiceActivity => false;
        public bool AllowsStreamingFinalCommit => false;
        public bool AppliesStreamingVoiceActivity => false;
        public bool SubmitWhenSpeakingEndsWithoutOffline => false;
    }
}
#endif
