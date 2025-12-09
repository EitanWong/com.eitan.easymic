using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Advanced turn detector using statistical analysis of speech patterns
    /// for precise, adaptive end-of-speech detection.
    /// </summary>
    public sealed class AdaptiveTurnDetector : TurnDetector
    {
        #region Configuration
        private readonly TurnDetectionOptions _options;
        private readonly float _minDelay;
        private readonly float _maxDelay;

        // Adaptive learning parameters
        private const int HistoryWindowSize = 20;
        private const float LearningRate = 0.1f;
        private const float PunctuationBonus = 0.3f;
        private const float QuestionMarkBonus = 0.4f;
        private const float ShortUtteranceThreshold = 3; // words
        private const float LongUtteranceThreshold = 15; // words
        #endregion

        #region State
        private readonly Queue<float> _pauseDurationHistory = new Queue<float>();
        private readonly Queue<int> _utteranceLengthHistory = new Queue<int>();
        private float _averagePauseDuration;
        private float _pauseStandardDeviation;
        private float _averageUtteranceLength;
        private int _consecutiveShortUtterances;
        private float _lastEvaluatedDelay;
        private bool _isInConversationalMode;
        #endregion

        public AdaptiveTurnDetector(TurnDetectionOptions settings)
        {
            _options = settings.EnsureValid();
            _minDelay = _options.MinDelaySeconds;
            _maxDelay = _options.MaxDelaySeconds;
            _averagePauseDuration = (_minDelay + _maxDelay) / 2f;
            _averageUtteranceLength = 8f; // Default average words
        }

        public override float EvaluateDelay(in TurnDetectionContext context)
        {
            float silence = Mathf.Max(0f, context.SilenceSeconds);
            bool strongEnding = context.EndsWithPunctuation &&
                                !context.EndsWithConjunction &&
                                !context.HasOpenParentheses &&
                                !context.HasOpenQuotes;

            // Immediate return if max silence exceeded
            if (silence >= _maxDelay)
            {
                return 0f;
            }

            // Fast path: strong sentence ending and already past minimum wait
            if (strongEnding && silence >= (_minDelay * 0.6f))
            {
                float fastTrack = Mathf.Max(0f, _minDelay - silence);
                _lastEvaluatedDelay = fastTrack;
                return fastTrack;
            }

            float baseDelay = CalculateBaseDelay(context);
            float adaptiveModifier = CalculateAdaptiveModifier(context);
            float contextModifier = CalculateContextModifier(context);

            float learnedAverage = Mathf.Clamp(_averagePauseDuration, _minDelay, _maxDelay);
            float blended = Mathf.Lerp(baseDelay, learnedAverage, 0.35f);

            float targetDelay = blended * adaptiveModifier * contextModifier;
            targetDelay = Mathf.Clamp(targetDelay, _minDelay, _maxDelay);

            if (strongEnding)
            {
                targetDelay = Mathf.Min(targetDelay, _minDelay + 0.08f);
            }

            float remainingDelay = Mathf.Max(0f, targetDelay - silence);
            _lastEvaluatedDelay = remainingDelay;

            return remainingDelay;
        }

        private float CalculateBaseDelay(in TurnDetectionContext context)
        {
            int estimatedWords = EstimateWordCount(context);

            // Short utterances (1-3 words): Quick response expected
            if (context.SegmentCount <= 1)
            {
                if (estimatedWords <= ShortUtteranceThreshold)
                {
                    _consecutiveShortUtterances++;
                    return Mathf.Lerp(_minDelay, _maxDelay, 0.15f);
                }
            }

            // Long utterances (15+ words): User might be thinking
            if (context.SegmentCount >= 3)
            {
                if (estimatedWords >= LongUtteranceThreshold)
                {
                    return _minDelay + (_maxDelay - _minDelay) * 0.7f;
                }
            }

            // Medium utterances: Standard delay with segment consideration
            float segmentFactor = Mathf.InverseLerp(1, 5, context.SegmentCount);
            float utteranceFactor = Mathf.InverseLerp(ShortUtteranceThreshold, LongUtteranceThreshold, estimatedWords);
            float baseLerp = Mathf.Lerp(segmentFactor * 0.6f, utteranceFactor * 0.65f, 0.5f);
            return Mathf.Lerp(_minDelay, _maxDelay, Mathf.Clamp01(baseLerp));
        }

        private float CalculateAdaptiveModifier(in TurnDetectionContext context)
        {
            // Learn from user's natural pause patterns
            if (_pauseDurationHistory.Count < 3)
            {
                return 1f; // Not enough data
            }

            // If current silence is within 1 std dev of average, user likely not done
            float zScore = (_averagePauseDuration - context.SilenceSeconds) /
                          Mathf.Max(0.1f, _pauseStandardDeviation);

            // Conversational mode: Shorter pauses expected
            if (_consecutiveShortUtterances >= 3)
            {
                _isInConversationalMode = true;
                return 0.7f; // Reduce delay for rapid exchanges
            }
            else if (_consecutiveShortUtterances == 0 && _isInConversationalMode)
            {
                _isInConversationalMode = false;
            }

            // Adjust based on deviation from learned pattern
            if (zScore > 1.5f)
            {
                return 0.8f; // Silence longer than usual - likely done
            }
            else if (zScore < -1f)
            {
                return 1.2f; // Silence shorter than usual - wait more
            }
            else if (context.SilenceSeconds > _averagePauseDuration + (_pauseStandardDeviation * 0.5f))
            {
                return 0.85f;
            }

            return 1f;
        }

        private float CalculateContextModifier(in TurnDetectionContext context)
        {
            float modifier = 1f;

            // Strong sentence-ending punctuation
            if (context.EndsWithPunctuation)
            {
                modifier *= (1f - PunctuationBonus);

                // Question marks often expect quicker response
                if (context.LastCharacter == '?' || context.LastCharacter == '？')
                {
                    modifier *= (1f - QuestionMarkBonus);
                }
            }

            // Trailing conjunctions suggest continuation
            if (context.EndsWithConjunction)
            {
                modifier *= 1.4f;
            }

            // Incomplete thought patterns
            if (context.HasOpenParentheses || context.HasOpenQuotes)
            {
                modifier *= 1.5f;
            }

            return modifier;
        }

        private int EstimateWordCount(in TurnDetectionContext context)
        {
            // Rough estimation: Average 5 characters per word
            return Mathf.Max(1, context.CharacterCount / 5);
        }

        public void RecordPause(float pauseDuration)
        {
            _pauseDurationHistory.Enqueue(pauseDuration);
            while (_pauseDurationHistory.Count > HistoryWindowSize)
            {
                _pauseDurationHistory.Dequeue();
            }

            UpdateStatistics();
        }

        public void RecordUtterance(int wordCount)
        {
            _utteranceLengthHistory.Enqueue(wordCount);
            while (_utteranceLengthHistory.Count > HistoryWindowSize)
            {
                _utteranceLengthHistory.Dequeue();
            }

            if (wordCount > ShortUtteranceThreshold)
            {
                _consecutiveShortUtterances = 0;
            }

            UpdateUtteranceStatistics();
        }

        private void UpdateStatistics()
        {
            if (_pauseDurationHistory.Count == 0)
            {
                return;
            }


            float sum = 0f;
            foreach (var pause in _pauseDurationHistory)
            {
                sum += pause;
            }
            float newAverage = sum / _pauseDurationHistory.Count;

            // Exponential moving average for smooth adaptation
            _averagePauseDuration = Mathf.Lerp(_averagePauseDuration, newAverage, LearningRate);

            // Calculate standard deviation
            float variance = 0f;
            foreach (var pause in _pauseDurationHistory)
            {
                float diff = pause - _averagePauseDuration;
                variance += diff * diff;
            }
            _pauseStandardDeviation = Mathf.Sqrt(variance / _pauseDurationHistory.Count);
        }

        private void UpdateUtteranceStatistics()
        {
            if (_utteranceLengthHistory.Count == 0)
            {
                return;
            }


            float sum = 0f;
            foreach (var length in _utteranceLengthHistory)
            {
                sum += length;
            }
            float newAverage = sum / _utteranceLengthHistory.Count;
            _averageUtteranceLength = Mathf.Lerp(_averageUtteranceLength, newAverage, LearningRate);
        }

        public void Reset()
        {
            _pauseDurationHistory.Clear();
            _utteranceLengthHistory.Clear();
            _consecutiveShortUtterances = 0;
            _isInConversationalMode = false;
            _averagePauseDuration = (_minDelay + _maxDelay) / 2f;
            _averageUtteranceLength = 8f;
            _pauseStandardDeviation = 0.2f;
        }
    }

}
