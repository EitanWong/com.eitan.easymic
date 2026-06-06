using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public enum PipelineStage
    {
        Asr, Llm, Tts, Playback
    }

    public enum StageStatus
    {
        Waiting, Running, Done, Failed
    }

    public sealed class PipelineLatencyStats
    {
        public int SampleCount;
        public float AverageAsrMs;
        public float AverageLlmMs;
        public float AverageTtsMs;
        public float AveragePlaybackMs;
        public float AverageTotalMs;
        public float AverageFirstTokenMs;
        public float AverageFirstSentenceMs;
        public float AverageFirstAudioMs;
        public float P50FirstAudioMs;
        public float P90FirstAudioMs;
        public float BestFirstAudioMs;
        public float WorstFirstAudioMs;
    }

    public sealed class PipelineDebugTracker
    {
        public int MaxHistoryRounds { get; set; } = 50;
        public bool Enabled { get; set; } = true;
        public bool LogEvents { get; set; }

        public IReadOnlyList<ConversationRound> CompletedRounds => _completedRounds;
        public ConversationRound CurrentRound => _currentRound;
        public int TotalRounds => _roundIndex;
        public int CancelledRounds { get; private set; }

        public float AverageAsrMs { get; private set; }
        public float AverageLlmMs { get; private set; }
        public float AverageTtsMs { get; private set; }
        public float AveragePlaybackMs { get; private set; }
        public float AverageTotalMs { get; private set; }
        public float AverageFirstTokenMs { get; private set; }
        public float AverageFirstSentenceMs { get; private set; }
        public float AverageE2EMs { get; private set; }
        public float P50E2EMs { get; private set; }
        public float P90E2EMs { get; private set; }

        public StageStatus AsrStatus { get; private set; } = StageStatus.Waiting;
        public StageStatus LlmStatus { get; private set; } = StageStatus.Waiting;
        public StageStatus TtsStatus { get; private set; } = StageStatus.Waiting;
        public StageStatus PlaybackStatus { get; private set; } = StageStatus.Waiting;

        private readonly object _sync = new object();
        private readonly List<ConversationRound> _completedRounds = new List<ConversationRound>();
        private ConversationRound _currentRound;
        private int _roundIndex;
        private int _sentenceCountInRound;
        private float _pendingAsrStartTime = -1f;
        private float _pendingAsrEndTime = -1f;
        private string _pendingTranscript;
        private int _roundRevision;
        private int _drainGenerationAtCancel;
        private PipelineLatencyStats _stats = new PipelineLatencyStats();

        public ConversationRound[] GetCompletedRoundsSnapshot()
        {
            lock (_sync)
            {
                return _completedRounds.ToArray();
            }
        }

        public PipelineLatencyStats GetStatsSnapshot()
        {
            lock (_sync)
            {
                return new PipelineLatencyStats
                {
                    SampleCount = _stats.SampleCount,
                    AverageAsrMs = _stats.AverageAsrMs,
                    AverageLlmMs = _stats.AverageLlmMs,
                    AverageTtsMs = _stats.AverageTtsMs,
                    AveragePlaybackMs = _stats.AveragePlaybackMs,
                    AverageTotalMs = _stats.AverageTotalMs,
                    AverageFirstTokenMs = _stats.AverageFirstTokenMs,
                    AverageFirstSentenceMs = _stats.AverageFirstSentenceMs,
                    AverageFirstAudioMs = _stats.AverageFirstAudioMs,
                    P50FirstAudioMs = _stats.P50FirstAudioMs,
                    P90FirstAudioMs = _stats.P90FirstAudioMs,
                    BestFirstAudioMs = _stats.BestFirstAudioMs,
                    WorstFirstAudioMs = _stats.WorstFirstAudioMs
                };
            }
        }

        public void RecordAsrStart()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                _pendingAsrEndTime = -1f;
                _pendingTranscript = null;
                _pendingAsrStartTime = Time.realtimeSinceStartup;
                AsrStatus = StageStatus.Running;
                Trace("ASR start");
            }
        }

        public void RecordAsrEnd(string transcript)
        {
            if (!Enabled) return;
            lock (_sync)
            {
                _pendingAsrEndTime = Time.realtimeSinceStartup;
                _pendingTranscript = transcript != null && transcript.Length > 120
                    ? transcript.Substring(0, 120) + "..." : transcript ?? "";
                if (_currentRound == null) return;
                _currentRound.AsrEndTime = _pendingAsrEndTime;
                _currentRound.UserInput = _pendingTranscript;
                AsrStatus = StageStatus.Done;
                Trace("ASR end");
            }
        }

        public void RecordLlmRequestSent()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) EnsureRound();
                if (_pendingAsrStartTime >= 0f && _pendingAsrEndTime >= 0f)
                {
                    _currentRound.AsrStartTime = _pendingAsrStartTime;
                    _currentRound.AsrEndTime = _pendingAsrEndTime;
                    _currentRound.UserInput = _pendingTranscript ?? "";
                    _pendingAsrStartTime = -1f;
                    _pendingAsrEndTime = -1f;
                    _pendingTranscript = null;
                    AsrStatus = StageStatus.Done;
                }

                _currentRound.LlmRequestTime = Time.realtimeSinceStartup;
                LlmStatus = StageStatus.Running;
                Trace("LLM request");
            }
        }

        public void RecordLlmFirstToken()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null || _currentRound.LlmFirstTokenTime >= 0f) return;
                _currentRound.LlmFirstTokenTime = Time.realtimeSinceStartup;
                Trace("LLM first token");
            }
        }

        public void RecordLlmLastToken()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                _currentRound.LlmLastTokenTime = Time.realtimeSinceStartup;
                LlmStatus = StageStatus.Done;
                Trace("LLM last token");
            }
        }

        public void RecordTtsSentenceDispatched()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                if (_currentRound.TtsFirstSentenceTime < 0f)
                {
                    _currentRound.TtsFirstSentenceTime = Time.realtimeSinceStartup;
                    TtsStatus = StageStatus.Running;
                }

                _sentenceCountInRound++;
                _currentRound.SentenceCount = _sentenceCountInRound;
                Trace("TTS sentence dispatched");
            }
        }

        public void RecordTtsFirstAudio()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null || _currentRound.TtsFirstAudioTime >= 0f) return;
                _currentRound.TtsFirstAudioTime = Time.realtimeSinceStartup;
                _currentRound.PlaybackStartTime = _currentRound.TtsFirstAudioTime;
                PlaybackStatus = StageStatus.Running;
                Trace("TTS first audio");
            }
        }

        public void RecordTtsSentenceCompleted()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                _currentRound.CompletedSentenceCount++;
                _currentRound.TtsLastCompleteTime = Time.realtimeSinceStartup;
                if (_currentRound.SentenceCount <= 0 ||
                    _currentRound.CompletedSentenceCount >= _currentRound.SentenceCount)
                {
                    TtsStatus = StageStatus.Done;
                }
            }
        }

        public void RecordPlaybackDrained()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                if (_currentRound.IsComplete)
                {
                    Trace("Playback drain ignored: round already complete");
                    return;
                }

                if (_currentRound.Index < _drainGenerationAtCancel)
                {
                    Trace("Playback drain ignored: stale cancel generation");
                    return;
                }

                _currentRound.PlaybackEndTime = Time.realtimeSinceStartup;
                PlaybackStatus = StageStatus.Done;
                FinalizeRound();
            }
        }

        public void CancelCurrentRound()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound != null && !_currentRound.IsComplete)
                {
                    _drainGenerationAtCancel = _roundRevision + 1;

                    if (_currentRound.AsrEndTime < 0f && _pendingAsrStartTime >= 0f && _pendingAsrEndTime >= 0f)
                    {
                        _currentRound.AsrStartTime = _pendingAsrStartTime;
                        _currentRound.AsrEndTime = _pendingAsrEndTime;
                        _currentRound.UserInput = _pendingTranscript ?? "";
                        _pendingAsrStartTime = -1f;
                        _pendingAsrEndTime = -1f;
                        _pendingTranscript = null;
                    }

                    _currentRound.WasCancelled = true;
                    _currentRound.IsComplete = true;
                    _currentRound.SentenceCount = _sentenceCountInRound;
                    _completedRounds.Add(_currentRound);
                    CancelledRounds++;
                    TrimHistory();
                    ComputeAverages();

                    ResetStatuses();
                    _currentRound = null;
                    _sentenceCountInRound = 0;
                }
                else if (_currentRound == null)
                {
                    _pendingAsrStartTime = -1f;
                    _pendingAsrEndTime = -1f;
                    _pendingTranscript = null;
                }
            }
        }

        public string ExportToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Index,Time,State,Input,ASR_ms,LLM_ms,TTFT_ms,FirstSentence_ms,TTS_FirstAudio_ms,TTS_ms,Playback_ms,TTFA_ms,Total_ms,Sentences,Bottleneck");
            ConversationRound[] snapshot = GetCompletedRoundsSnapshot();
            for (int i = 0; i < snapshot.Length; i++)
            {
                var r = snapshot[i];
                string input = (r.UserInput ?? "").Replace("\"", "\"\"");
                string state = r.WasCancelled ? "cancelled" : "completed";
                sb.AppendLine(
                    $"{r.Index},{r.WallClockTime:HH:mm:ss},{state},\"{input}\"," +
                    $"{CsvMs(r.AsrMs)},{CsvMs(r.LlmMs)},{CsvMs(r.FirstTokenMs)},{CsvMs(r.FirstSentenceMs)}," +
                    $"{CsvMs(r.TtsQueueToFirstAudioMs)},{CsvMs(r.TtsMs)},{CsvMs(r.PlaybackMs)}," +
                    $"{CsvMs(r.E2EMs)},{CsvMs(r.TotalMs)},{r.CompletedSentenceCount}/{r.SentenceCount},{r.BottleneckStage}");
            }
            return sb.ToString();
        }

        private void EnsureRound()
        {
            if (_currentRound != null) return;
            _roundRevision++;
            _currentRound = new ConversationRound
            {
                Index = _roundIndex++,
                WallClockTime = DateTime.Now
            };
            _drainGenerationAtCancel = 0;
            ResetStatuses();
            _sentenceCountInRound = 0;
        }

        private void FinalizeRound()
        {
            if (_currentRound == null) return;
            _currentRound.IsComplete = true;
            _currentRound.SentenceCount = _sentenceCountInRound;
            _completedRounds.Add(_currentRound);
            TrimHistory();
            ComputeAverages();
            _currentRound = null;
            _sentenceCountInRound = 0;
        }

        private void TrimHistory()
        {
            while (_completedRounds.Count > MaxHistoryRounds)
                _completedRounds.RemoveAt(0);
        }

        private void ResetStatuses()
        {
            AsrStatus = StageStatus.Waiting;
            LlmStatus = StageStatus.Waiting;
            TtsStatus = StageStatus.Waiting;
            PlaybackStatus = StageStatus.Waiting;
        }

        private void ComputeAverages()
        {
            int asrC = 0, llmC = 0, ttsC = 0, playC = 0, totC = 0, ftC = 0, fsC = 0, e2eC = 0;
            float asrS = 0, llmS = 0, ttsS = 0, playS = 0, totS = 0, ftS = 0, fsS = 0, e2eS = 0;
            var e2eValues = new List<float>(_completedRounds.Count);

            for (int i = 0; i < _completedRounds.Count; i++)
            {
                var r = _completedRounds[i];
                Accumulate(r.AsrMs, ref asrS, ref asrC);
                Accumulate(r.LlmMs, ref llmS, ref llmC);
                Accumulate(r.TtsMs, ref ttsS, ref ttsC);
                Accumulate(r.PlaybackMs, ref playS, ref playC);
                Accumulate(r.TotalMs, ref totS, ref totC);
                Accumulate(r.FirstTokenMs, ref ftS, ref ftC);
                Accumulate(r.FirstSentenceMs, ref fsS, ref fsC);
                if (r.E2EMs > 0f)
                {
                    e2eS += r.E2EMs;
                    e2eC++;
                    e2eValues.Add(r.E2EMs);
                }
            }

            AverageAsrMs = Average(asrS, asrC);
            AverageLlmMs = Average(llmS, llmC);
            AverageTtsMs = Average(ttsS, ttsC);
            AveragePlaybackMs = Average(playS, playC);
            AverageTotalMs = Average(totS, totC);
            AverageFirstTokenMs = Average(ftS, ftC);
            AverageFirstSentenceMs = Average(fsS, fsC);
            AverageE2EMs = Average(e2eS, e2eC);
            P50E2EMs = Percentile(e2eValues, 0.50f);
            P90E2EMs = Percentile(e2eValues, 0.90f);

            _stats = new PipelineLatencyStats
            {
                SampleCount = e2eC,
                AverageAsrMs = AverageAsrMs,
                AverageLlmMs = AverageLlmMs,
                AverageTtsMs = AverageTtsMs,
                AveragePlaybackMs = AveragePlaybackMs,
                AverageTotalMs = AverageTotalMs,
                AverageFirstTokenMs = AverageFirstTokenMs,
                AverageFirstSentenceMs = AverageFirstSentenceMs,
                AverageFirstAudioMs = AverageE2EMs,
                P50FirstAudioMs = P50E2EMs,
                P90FirstAudioMs = P90E2EMs,
                BestFirstAudioMs = e2eValues.Count > 0 ? e2eValues[0] : 0f,
                WorstFirstAudioMs = e2eValues.Count > 0 ? e2eValues[e2eValues.Count - 1] : 0f
            };
        }

        private static void Accumulate(float value, ref float sum, ref int count)
        {
            if (value <= 0f) return;
            sum += value;
            count++;
        }

        private static float Average(float sum, int count)
        {
            return count > 0 ? sum / count : 0f;
        }

        private static float Percentile(List<float> values, float percentile)
        {
            if (values == null || values.Count == 0) return 0f;
            values.Sort();
            float position = Mathf.Clamp01(percentile) * (values.Count - 1);
            int lower = Mathf.FloorToInt(position);
            int upper = Mathf.CeilToInt(position);
            if (lower == upper) return values[lower];
            float t = position - lower;
            return Mathf.Lerp(values[lower], values[upper], t);
        }

        private static string CsvMs(float value)
        {
            return value > 0f ? value.ToString("F0") : "";
        }

        private void Trace(string message)
        {
            if (LogEvents)
            {
                Debug.Log($"[PipelineDebug] {message}");
            }
        }
    }
}
