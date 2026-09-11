#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using System.Threading;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private int _localSynthPreviousMaxParallel;

        private void AttachLocalSynthCallbacks(SpeechSynthesizer synth)
        {
            if (_localSynthCallbacksBound || synth == null)
            {
                return;
            }

            // Each ONNX inference already uses multiple threads; keep headroom for capture and AEC.
            _localSynthPreviousMaxParallel = synth.MaxParallelSynthesis;
            synth.MaxParallelSynthesis = 1;
            synth.OnTTSStateChanged += OnLocalTtsStateChanged;
            synth.OnSentencePlaybackStarted += OnLocalSentencePlaybackStarted;
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
                    _boundLocalSynthesizer.OnSentencePlaybackStarted -= OnLocalSentencePlaybackStarted;
                    _boundLocalSynthesizer.MaxParallelSynthesis = _localSynthPreviousMaxParallel;
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
            NotifySpeakingState(Interlocked.Read(ref _activeTurnId), isSpeaking);
        }

        private void OnLocalSentencePlaybackStarted(string sentence)
        {
            long turnId = Interlocked.Read(ref _activeTurnId);
            SafeInvoke(() =>
            {
                if (!_disposed && turnId > 0 && turnId == Interlocked.Read(ref _activeTurnId))
                {
                    OnSentenceStarted?.Invoke(turnId, sentence);
                }
            });
        }
    }
}
#endif
