#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Observes voice activity and silence duration to trigger turn finalization.
    /// </summary>
    public sealed class SilenceTurnRecognizer
    {
        private static readonly string[] TrailingConjunctions =
        {
            "and", "or", "but", "so", "then", "also", "以及", "还有", "然后", "然后呢", "然后再"
        };

        private static readonly char[] QuoteOpeners = { '“', '"', '«', '‘' };
        private static readonly char[] QuoteClosers = { '”', '"', '»', '’' };
        private static readonly char[] ParenthesisOpeners = { '(', '（' };
        private static readonly char[] ParenthesisClosers = { ')', '）' };

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

            float remaining = EstimateDelay(buffer, _silence.Elapsed);
            _targetDelay = _silence.Elapsed + remaining;

            if (_silence.Elapsed >= _targetDelay)
            {
                RecordAdaptiveFeedback(transcript, _silence.Elapsed);
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
            var context = BuildContext(transcript, silenceSeconds, segments);

            float rawDelay = _detector.EvaluateDelay(in context);
            if (float.IsNaN(rawDelay) || float.IsInfinity(rawDelay))
            {
                return 0f;
            }

            return Mathf.Max(0f, rawDelay);
        }

        private TurnDetectionContext BuildContext(string transcript, float silenceSeconds, int segments)
        {
            string trimmed = transcript.Trim();
            bool endsWithPunctuation = RecognitionBuffer.EndsWithTerminatorStatic(trimmed);
            bool endsWithConjunction = EndsWithConjunction(trimmed);
            bool hasOpenParentheses = HasOpenDelimiter(trimmed, ParenthesisOpeners, ParenthesisClosers);
            bool hasOpenQuotes = HasOpenDelimiter(trimmed, QuoteOpeners, QuoteClosers);
            int characterCount = trimmed.Length;
            char lastCharacter = characterCount > 0 ? trimmed[characterCount - 1] : '\0';

            return new TurnDetectionContext(
                silenceSeconds,
                segments,
                characterCount,
                endsWithPunctuation,
                endsWithConjunction,
                hasOpenParentheses,
                hasOpenQuotes,
                lastCharacter);
        }

        private void RecordAdaptiveFeedback(string transcript, float silenceSeconds)
        {
            if (_detector is not AdaptiveTurnDetector adaptive)
            {
                return;
            }

            adaptive.RecordPause(silenceSeconds);
            adaptive.RecordUtterance(EstimateWordCount(transcript));
        }

        private static int EstimateWordCount(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return 0;
            }

            int count = 0;
            bool inWord = false;

            foreach (char ch in transcript)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    if (!inWord)
                    {
                        count++;
                        inWord = true;
                    }
                }
                else
                {
                    inWord = false;
                }
            }

            if (count == 0)
            {
                count = Mathf.Max(1, transcript.Length / 4);
            }

            return count;
        }

        private static bool EndsWithConjunction(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string trimmed = text.TrimEnd();
            int lastSpace = trimmed.LastIndexOf(' ');
            string tail = lastSpace >= 0 ? trimmed.Substring(lastSpace + 1) : trimmed;
            tail = tail.TrimEnd(',', '，', '、');
            string lowerTail = tail.ToLowerInvariant();

            for (int i = 0; i < TrailingConjunctions.Length; i++)
            {
                if (lowerTail.EndsWith(TrailingConjunctions[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOpenDelimiter(string text, IReadOnlyList<char> openers, IReadOnlyList<char> closers)
        {
            int balance = 0;

            foreach (char ch in text)
            {
                for (int i = 0; i < openers.Count; i++)
                {
                    if (ch == openers[i])
                    {
                        balance++;
                        break;
                    }
                }

                for (int i = 0; i < closers.Count; i++)
                {
                    if (ch == closers[i])
                    {
                        balance = Math.Max(0, balance - 1);
                        break;
                    }
                }
            }

            return balance > 0;
        }
    }
}
#endif
