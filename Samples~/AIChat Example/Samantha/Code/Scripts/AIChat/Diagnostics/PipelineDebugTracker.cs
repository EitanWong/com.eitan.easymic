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

    public sealed class PipelineDebugTracker
    {
        public int MaxHistoryRounds { get; set; } = 50;
        public bool Enabled { get; set; } = true;

        public IReadOnlyList<ConversationRound> CompletedRounds => _completedRounds;
        public ConversationRound CurrentRound => _currentRound;
        public int TotalRounds => _roundIndex;

        public float AverageAsrMs { get; private set; }
        public float AverageLlmMs { get; private set; }
        public float AverageTtsMs { get; private set; }
        public float AverageTotalMs { get; private set; }
        public float AverageFirstTokenMs { get; private set; }
        public float AverageE2EMs { get; private set; }

        public StageStatus AsrStatus { get; private set; } = StageStatus.Waiting;
        public StageStatus LlmStatus { get; private set; } = StageStatus.Waiting;
        public StageStatus TtsStatus { get; private set; } = StageStatus.Waiting;
        public StageStatus PlaybackStatus { get; private set; } = StageStatus.Waiting;

        private readonly object _sync = new object();
        private readonly List<ConversationRound> _completedRounds = new List<ConversationRound>();
        private ConversationRound _currentRound;
        private int _roundIndex;
        private int _sentenceCountInRound;
        // Pending ASR data — saved even when no round exists yet, applied when round is created
        private float _pendingAsrStartTime = -1f;
        private float _pendingAsrEndTime = -1f;
        private string _pendingTranscript;
        // Round revision counter: incremented each time a new round is created.
        // RecordPlaybackDrained checks this to reject stale drain events from a
        // previous response that arrive after a new round has started.
        private int _roundRevision;
        private int _drainGenerationAtCancel;

        public void RecordAsrStart()
        {
            Debug.Log("[PipelineDebug] ** RecordAsrStart ENTERED **");
            if (!Enabled) return;
            lock (_sync)
            {
                // Clear stale pending data from previous utterance — this is a NEW ASR session
                _pendingAsrEndTime = -1f;
                _pendingTranscript = null;
                // Save current ASR start time (only in pending — applied to the correct round
                // in RecordLlmRequestSent.  NEVER touch _currentRound here: it may belong to a
                // previous response and overwriting its AsrStartTime corrupts the timestamps.)
                _pendingAsrStartTime = Time.realtimeSinceStartup;
                AsrStatus = StageStatus.Running;
            }
        }

        public void RecordAsrEnd(string transcript)
        {
            if (!Enabled) return;
            lock (_sync)
            {
                _pendingAsrEndTime = Time.realtimeSinceStartup;
                _pendingTranscript = transcript != null && transcript.Length > 80
                    ? transcript.Substring(0, 80) + "…" : transcript ?? "";
                if (_currentRound == null) return;
                _currentRound.AsrEndTime = _pendingAsrEndTime;
                _currentRound.UserInput = _pendingTranscript;
                AsrStatus = StageStatus.Done;
            }
        }

        public void RecordLlmRequestSent()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) EnsureRound();
                // Apply any pending ASR data — only when BOTH start and end are available (atomic set)
                // This prevents a stale end time from a previous round being applied separately after
                // the start time, which would leave AsrEndTime=-1 and cause AsrMs=-1 in the CSV.
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
                Debug.Log("[PipelineDebug] RecordLlmRequestSent called, round=" + (_currentRound != null ? _currentRound.Index.ToString() : "null") +
                    " pendingStart=" + _pendingAsrStartTime + " pendingEnd=" + _pendingAsrEndTime +
                    " roundStart=" + _currentRound.AsrStartTime + " roundEnd=" + _currentRound.AsrEndTime);
                _currentRound.LlmRequestTime = Time.realtimeSinceStartup;
                LlmStatus = StageStatus.Running;
            }
        }

        public void RecordLlmFirstToken()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                Debug.Log("[PipelineDebug] RecordLlmFirstToken called, round=" + (_currentRound != null ? _currentRound.Index.ToString() : "null"));
                _currentRound.LlmFirstTokenTime = Time.realtimeSinceStartup;
            }
        }

        public void RecordLlmLastToken()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                Debug.Log("[PipelineDebug] RecordLlmLastToken called, round=" + (_currentRound != null ? _currentRound.Index.ToString() : "null"));
                _currentRound.LlmLastTokenTime = Time.realtimeSinceStartup;
                LlmStatus = StageStatus.Done;
            }
        }

        public void RecordTtsSentenceDispatched()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                Debug.Log("[PipelineDebug] RecordTtsSentenceDispatched called, round=" + (_currentRound != null ? _currentRound.Index.ToString() : "null"));
                if (_currentRound.TtsFirstSentenceTime < 0f)
                {
                    _currentRound.TtsFirstSentenceTime = Time.realtimeSinceStartup;
                    _currentRound.PlaybackStartTime = Time.realtimeSinceStartup;
                    TtsStatus = StageStatus.Running;
                    PlaybackStatus = StageStatus.Running;
                }
                _sentenceCountInRound++;
                _currentRound.SentenceCount = _sentenceCountInRound;
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
                TtsStatus = StageStatus.Done;
            }
        }

        public void RecordPlaybackDrained()
        {
            if (!Enabled) return;
            lock (_sync)
            {
                if (_currentRound == null) return;
                Debug.Log("[PipelineDebug] RecordPlaybackDrained called, round=" + (_currentRound != null ? _currentRound.Index.ToString() : "null"));
                // Guard: If this round is already complete, the drain event is stale
                // (e.g., from a previous TTS session that fired after the round was cancelled
                // and a new round was created). Reject it to avoid corrupting the new round.
                if (_currentRound.IsComplete)
                {
                    Debug.Log("[PipelineDebug] RecordPlaybackDrained rejected — round already complete (stale event).");
                    return;
                }
                // STALE DRAIN RACE GUARD: If a cancel occurred (which bumps _roundRevision via
                // EnsureRound or the next RecordLlmRequestSent), and then a new round was created,
                // the old TTS pipeline's NotifySpeakingState(false) event can arrive asynchronously
                // (dispatched via PostToUnityThread). This drain event belongs to the cancelled round,
                // not the current one. Reject it to prevent corrupting the new round's timing data.
                if (_currentRound.Index < _drainGenerationAtCancel)
                {
                    Debug.Log($"[PipelineDebug] RecordPlaybackDrained rejected — round {_currentRound.Index} was created after cancel generation {_drainGenerationAtCancel} (stale event from previous response).");
                    return;
                }
                _currentRound.PlaybackEndTime = Time.realtimeSinceStartup;
                PlaybackStatus = StageStatus.Done;
                FinalizeRound();
            }
        }

        public void CancelCurrentRound()
        {
            Debug.Log("[PipelineDebug] CancelCurrentRound called, round=" + (_currentRound != null ? (_currentRound.Index + " complete=" + _currentRound.IsComplete) : "null"));
            lock (_sync)
            {
                if (_currentRound != null && !_currentRound.IsComplete)
                {
                    // Capture current revision so RecordPlaybackDrained can reject
                    // stale drain events from this cancelled round's TTS pipeline.
                    _drainGenerationAtCancel = _roundRevision + 1;

                    // Apply any pending ASR data that may belong to this round.
                    // ONLY apply when BOTH start AND end are set — that means the
                    // ASR session completed (RecordAsrEnd fired).  If only start is
                    // set, RecordAsrStart was just called for the NEXT round and we
                    // MUST NOT steal its timestamp for the round being cancelled.
                    if (_currentRound.AsrEndTime < 0f && _pendingAsrStartTime >= 0f && _pendingAsrEndTime >= 0f)
                    {
                        _currentRound.AsrStartTime = _pendingAsrStartTime;
                        _currentRound.AsrEndTime = _pendingAsrEndTime;
                        _currentRound.UserInput = _pendingTranscript ?? "";
                        // Clear applied pending data so RecordLlmRequestSent does
                        // not re-apply stale timestamps to the next round.
                        _pendingAsrStartTime = -1f;
                        _pendingAsrEndTime = -1f;
                        _pendingTranscript = null;
                    }

                    // Save partial round data to history before discarding
                    _currentRound.IsComplete = true;
                    _currentRound.SentenceCount = _sentenceCountInRound;
                    _completedRounds.Add(_currentRound);
                    while (_completedRounds.Count > MaxHistoryRounds)
                        _completedRounds.RemoveAt(0);
                    ComputeAverages();

                    AsrStatus = StageStatus.Waiting;
                    LlmStatus = StageStatus.Waiting;
                    TtsStatus = StageStatus.Waiting;
                    PlaybackStatus = StageStatus.Waiting;
                    _currentRound = null;
                    _sentenceCountInRound = 0;
                    // IMPORTANT: Do NOT clear pending ASR data that has only
                    // _pendingAsrStartTime set (no _pendingAsrEndTime).  RecordAsrStart
                    // may have just set _pendingAsrStartTime for the NEXT round
                    // (called from the same OnSpeakingChangedHandler that triggered
                    // this cancel).  Clearing it here would lose the ASR start
                    // timestamp for the incoming utterance.  Pending data with BOTH
                    // start and end set IS cleared above after being applied because
                    // it belongs to this round and would be stale for the next.
                }
                else if (_currentRound == null)
                {
                    // No round exists yet (pending ASR data from a cancelled ASR
                    // session that never reached RecordLlmRequestSent).  Clear
                    // pending data to prevent stale timestamps from being applied
                    // to the next round via RecordLlmRequestSent.
                    if (_pendingAsrStartTime >= 0f || _pendingAsrEndTime >= 0f)
                    {
                        Debug.Log("[PipelineDebug] CancelCurrentRound: clearing stale pending ASR data (no round existed).");
                    }
                    _pendingAsrStartTime = -1f;
                    _pendingAsrEndTime = -1f;
                    _pendingTranscript = null;
                }
            }
        }

        private void EnsureRound()
        {
            lock (_sync)
            {
                if (_currentRound == null)
                {
                    _roundRevision++;
                    _currentRound = new ConversationRound
                    {
                        Index = _roundIndex++,
                        WallClockTime = DateTime.Now
                    };
                    Debug.Log("[PipelineDebug] EnsureRound: created index=" + _currentRound.Index);
                    _drainGenerationAtCancel = 0;
                    AsrStatus = StageStatus.Waiting;
                    LlmStatus = StageStatus.Waiting;
                    TtsStatus = StageStatus.Waiting;
                    PlaybackStatus = StageStatus.Waiting;
                    _sentenceCountInRound = 0;
                }
            }
        }

        private void FinalizeRound()
        {
            if (_currentRound == null) return;
            Debug.Log("[PipelineDebug] FinalizeRound: index=" + _currentRound.Index + " AsrMs=" + _currentRound.AsrMs + " LlmMs=" + _currentRound.LlmMs + " TtsMs=" + _currentRound.TtsMs);
            _currentRound.IsComplete = true;
            _currentRound.SentenceCount = _sentenceCountInRound;
            _completedRounds.Add(_currentRound);
            while (_completedRounds.Count > MaxHistoryRounds)
                _completedRounds.RemoveAt(0);
            ComputeAverages();
            _currentRound = null;
            _sentenceCountInRound = 0;
        }

        private void ComputeAverages()
        {
            lock (_sync)
            {
                int asrC = 0, llmC = 0, ttsC = 0, totC = 0, ftC = 0, e2eC = 0;
                float asrS = 0, llmS = 0, ttsS = 0, totS = 0, ftS = 0, e2eS = 0;
                foreach (var r in _completedRounds)
                {
                    if (r.AsrMs > 0) { asrS += r.AsrMs; asrC++; }
                    if (r.LlmMs > 0) { llmS += r.LlmMs; llmC++; }
                    if (r.TtsMs > 0) { ttsS += r.TtsMs; ttsC++; }
                    if (r.TotalMs > 0) { totS += r.TotalMs; totC++; }
                    if (r.FirstTokenMs > 0) { ftS += r.FirstTokenMs; ftC++; }
                    if (r.E2EMs > 0) { e2eS += r.E2EMs; e2eC++; }
                }
                AverageAsrMs = asrC > 0 ? asrS / asrC : 0f;
                AverageLlmMs = llmC > 0 ? llmS / llmC : 0f;
                AverageTtsMs = ttsC > 0 ? ttsS / ttsC : 0f;
                AverageTotalMs = totC > 0 ? totS / totC : 0f;
                AverageFirstTokenMs = ftC > 0 ? ftS / ftC : 0f;
                AverageE2EMs = e2eC > 0 ? e2eS / e2eC : 0f;
            }
        }

        public string ExportToCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Index,Time,Input,ASR_ms,LLM_ms,TTFT_ms,TTS_ms,E2E_ms,Total_ms");
            ConversationRound[] snapshot;
            lock (_sync)
            {
                snapshot = _completedRounds.ToArray();
            }
            foreach (var r in snapshot)
            {
                string input = (r.UserInput ?? "").Replace("\"", "\"\"");
                sb.AppendLine($"{r.Index},{r.WallClockTime:HH:mm:ss},\"{input}\",{r.AsrMs:F0},{r.LlmMs:F0},{r.FirstTokenMs:F0},{r.TtsMs:F0},{r.E2EMs:F0},{r.TotalMs:F0}");
                Debug.Log($"[PipelineDebug] Export: idx={r.Index} ASR={r.AsrStartTime:F2}/{r.AsrEndTime:F2} LLM={r.LlmRequestTime:F2}/{r.LlmLastTokenTime:F2} TTSfirst={r.TtsFirstSentenceTime:F2} TTSlast={r.TtsLastCompleteTime:F2} PBstart={r.PlaybackStartTime:F2} PBend={r.PlaybackEndTime:F2} startTime={r.startTime:F2} finalEndTime={r.finalEndTime:F2} E2E={r.E2EMs:F0} Total={r.TotalMs:F0}");
            }
            return sb.ToString();
        }
    }
}
