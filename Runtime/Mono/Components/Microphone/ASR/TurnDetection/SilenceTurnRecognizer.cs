#if EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Observes voice activity and silence duration to trigger turn finalization.
    /// </summary>
    public sealed class SilenceTurnRecognizer
    {
        private readonly SilenceTimer _silence = new SilenceTimer();
        private TurnDetector _detector;
        private bool _tracking;
        private float _targetDelay;

        public SilenceTurnRecognizer(TurnDetector detector)
        {
            _detector = detector;
        }

        public void ConfigureDetector(TurnDetector detector)
        {
            _detector = detector;
            Reset();
        }

        public void Reset()
        {
            _silence.Reset();
            _tracking = false;
            _targetDelay = 0f;
        }

        public void OnVoiceActivityChanged(bool isActive)
        {
            if (isActive)
            {
                Reset();
            }
            else if (!_tracking)
            {
                _tracking = true;
                _silence.Reset();
                _targetDelay = 0f;
            }
            else
            {
                _targetDelay = 0f;
            }
        }

        public void Update(float deltaTime, RecognitionBuffer buffer)
        {
            if (!_tracking || _detector == null)
            {
                return;
            }

            _silence.Update(deltaTime);

            string transcript = buffer.CurrentTranscript;
            if (string.IsNullOrEmpty(transcript))
            {
                return;
            }

            if (_targetDelay <= 0f)
            {
                _targetDelay = EstimateDelay(buffer, _silence.Elapsed);
            }

            if (_silence.Elapsed >= _targetDelay)
            {
                buffer.FinalPush();
                Reset();
            }
        }

        private float EstimateDelay(RecognitionBuffer buffer, float silenceSeconds)
        {
            if (_detector == null)
            {
                return 0f;
            }

            string transcript = buffer.CurrentTranscript;
            if (string.IsNullOrEmpty(transcript))
            {
                return 0f;
            }

            int segments = RecognitionBuffer.CountSegmentsStatic(transcript);
            bool endsWithPunctuation = RecognitionBuffer.EndsWithTerminatorStatic(transcript);
            var context = new TurnDetectionContext(transcript, segments, endsWithPunctuation, silenceSeconds);
            float rawDelay = _detector.EvaluateDelay(in context);
            if (float.IsNaN(rawDelay) || float.IsInfinity(rawDelay))
            {
                return 0f;
            }

            return Mathf.Max(0f, rawDelay);
        }
    }
}
#endif
