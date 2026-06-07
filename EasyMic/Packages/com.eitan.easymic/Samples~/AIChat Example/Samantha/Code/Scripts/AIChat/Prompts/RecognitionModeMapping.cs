using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Shared mapping between RecognitionMode enum and integer indices.
    /// Used for serialization in runtime config and configuration policies.
    /// Previously duplicated in AIChatConfigurationPolicy and JsonAIChatRuntimeConfigStore.
    /// </summary>
    internal static class RecognitionModeMapping
    {
        public static bool TryMapRecognitionMode(int index, out RecognitionMode mode)
        {
            switch (index)
            {
                case 0:
                    mode = RecognitionMode.Streaming;
                    return true;
                case 1:
                    mode = RecognitionMode.OfflineWithVad;
                    return true;
                case 2:
                    mode = RecognitionMode.Hybrid;
                    return true;
                default:
                    mode = RecognitionMode.OfflineWithVad;
                    return false;
            }
        }

        public static int MapRecognitionModeToIndex(RecognitionMode mode)
        {
            switch (mode)
            {
                case RecognitionMode.Streaming:
                    return 0;
                case RecognitionMode.OfflineWithVad:
                    return 1;
                case RecognitionMode.Hybrid:
                    return 2;
                default:
                    return -1;
            }
        }
    }
}
