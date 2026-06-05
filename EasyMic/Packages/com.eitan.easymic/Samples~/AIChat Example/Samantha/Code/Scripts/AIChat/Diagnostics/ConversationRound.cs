using System;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class ConversationRound
    {
        public int Index;
        public string UserInput;
        public DateTime WallClockTime;

        public float AsrStartTime = -1f;
        public float AsrEndTime = -1f;
        public float LlmRequestTime = -1f;
        public float LlmFirstTokenTime = -1f;
        public float LlmLastTokenTime = -1f;
        public float TtsFirstSentenceTime = -1f;
        public float TtsLastCompleteTime = -1f;
        public float PlaybackStartTime = -1f;
        public float PlaybackEndTime = -1f;

        public int SentenceCount;
        public int CompletedSentenceCount;
        public bool IsComplete;

        public float AsrMs => BothValid(AsrStartTime, AsrEndTime) ? Ms(AsrStartTime, AsrEndTime) : -1f;
        public float LlmMs => BothValid(LlmRequestTime, LlmLastTokenTime) ? Ms(LlmRequestTime, LlmLastTokenTime) : -1f;
        public float FirstTokenMs => BothValid(LlmRequestTime, LlmFirstTokenTime) ? Ms(LlmRequestTime, LlmFirstTokenTime) : -1f;
        public float TtsMs => BothValid(TtsFirstSentenceTime, TtsLastCompleteTime) ? Ms(TtsFirstSentenceTime, TtsLastCompleteTime) : (BothValid(LlmLastTokenTime, TtsLastCompleteTime) ? Ms(LlmLastTokenTime, TtsLastCompleteTime) : -1f);
        public float PlaybackMs => BothValid(TtsLastCompleteTime, PlaybackEndTime) ? Ms(TtsLastCompleteTime, PlaybackEndTime) : -1f;
        /// <summary>User-perceived E2E latency: from ASR submit (user stops speaking) to first audio out.
        /// Falls back from TtsFirstSentenceTime to LlmLastTokenTime when TTS hasn't started yet
        /// (e.g. round was cancelled before TTS dispatch).</summary>
        public float E2EMs
        {
            get
            {
                if (BothValid(AsrEndTime, TtsFirstSentenceTime))
                    return Ms(AsrEndTime, TtsFirstSentenceTime);
                // Fallback: if TTS never dispatched (cancelled round), use LLM last token
                if (BothValid(AsrEndTime, LlmLastTokenTime))
                    return Ms(AsrEndTime, LlmLastTokenTime);
                return -1f;
            }
        }
        /// <summary>Total round time: from ASR start (or LLM request) to the latest completed
        /// stage (Playback -> TTS -> LLM). Always shows usable data even for partial rounds.</summary>
        public float TotalMs => BothValid(startTime, finalEndTime) ? Ms(startTime, finalEndTime) : -1f;
        internal float startTime => AsrStartTime >= 0f ? AsrStartTime : LlmRequestTime;
        internal float finalEndTime => PlaybackEndTime >= 0f ? PlaybackEndTime : (TtsLastCompleteTime >= 0f ? TtsLastCompleteTime : LlmLastTokenTime);
        public float RunningTotalMs
        {
            get
            {
                float start = AsrStartTime >= 0f ? AsrStartTime : LlmRequestTime;
                if (start < 0f) return -1f;
                float end = TtsLastCompleteTime >= 0f ? TtsLastCompleteTime : Time.realtimeSinceStartup;
                return BothValid(start, end) ? Ms(start, end) : -1f;
            }
        }

        private static bool BothValid(float a, float b) => a >= 0f && b >= 0f && b >= a;
        private static float Ms(float start, float end) => (end - start) * 1000f;
    }
}
