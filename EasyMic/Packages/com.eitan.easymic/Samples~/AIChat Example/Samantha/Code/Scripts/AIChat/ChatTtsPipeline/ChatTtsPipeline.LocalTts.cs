#if EASYMIC_SHERPA_ONNX_INTEGRATION

using Eitan.EasyMic.Runtime.Mono.Components.TTS;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private void AttachLocalSynthCallbacks(SpeechSynthesizer synth)
        {
            if (_localSynthCallbacksBound || synth == null)
            {
                return;
            }

            synth.OnTTSStateChanged += OnLocalTtsStateChanged;
            _boundLocalSynthesizer = synth;
            _localSynthCallbacksBound = true;
        }

        private void DetachLocalSynthCallbacks()
        {
            if (!_localSynthCallbacksBound)
            {
                return;
            }

            if (_boundLocalSynthesizer != null)
            {
                try
                {
                    _boundLocalSynthesizer.OnTTSStateChanged -= OnLocalTtsStateChanged;
                }
                catch
                {
                }
            }

            _boundLocalSynthesizer = null;
            _localSynthCallbacksBound = false;
        }

        private void OnLocalTtsStateChanged(bool isSpeaking)
        {
            NotifySpeakingState(isSpeaking);
        }
    }
}
#endif
