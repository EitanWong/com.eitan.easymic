#if EASYMIC_SHERPA_ONNX_INTEGRATION

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void OnSpeechSynthesizerProgressFeedbackHandler(string message, float progress)
        {
            if (_initializationFailed)
            {
                return;
            }

            UpdateServiceLoading(SERVICE_TTS_INIT_KEY, progress);
        }

        private void OnLocalTtsStateChanged(bool isSpeaking)
        {
            if (!Config.UseLocalTts)
            {
                return;
            }

            SetAssistantSpeakingState(isSpeaking);
        }
    }
}
#endif
