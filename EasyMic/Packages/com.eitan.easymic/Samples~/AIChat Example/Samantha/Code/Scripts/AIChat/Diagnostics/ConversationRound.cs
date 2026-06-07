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
        public float TtsFirstAudioTime = -1f;
        public float TtsLastCompleteTime = -1f;
        public float PlaybackStartTime = -1f;
        public float PlaybackEndTime = -1f;

        public int SentenceCount;
        public int CompletedSentenceCount;
        public bool IsComplete;
        public bool WasCancelled;

        internal int Revision;

        public float AsrMs => BothValid(AsrStartTime, AsrEndTime) ? Ms(AsrStartTime, AsrEndTime) : -1f;
        public float LlmMs => BothValid(LlmRequestTime, LlmLastTokenTime) ? Ms(LlmRequestTime, LlmLastTokenTime) : -1f;
        public float FirstTokenMs => BothValid(LlmRequestTime, LlmFirstTokenTime) ? Ms(LlmRequestTime, LlmFirstTokenTime) : -1f;
        public float FirstSentenceMs => BothValid(LlmRequestTime, TtsFirstSentenceTime) ? Ms(LlmRequestTime, TtsFirstSentenceTime) : -1f;
        public float LlmToFirstSentenceMs => BothValid(LlmFirstTokenTime, TtsFirstSentenceTime) ? Ms(LlmFirstTokenTime, TtsFirstSentenceTime) : -1f;
        public float TtsQueueToFirstAudioMs => BothValid(TtsFirstSentenceTime, TtsFirstAudioTime) ? Ms(TtsFirstSentenceTime, TtsFirstAudioTime) : -1f;
        public float TtsMs => BothValid(TtsFirstSentenceTime, TtsLastCompleteTime) ? Ms(TtsFirstSentenceTime, TtsLastCompleteTime) : -1f;
        public float PlaybackMs => BothValid(TtsFirstAudioTime, PlaybackEndTime) ? Ms(TtsFirstAudioTime, PlaybackEndTime) : -1f;
        public float DrainMs => BothValid(TtsLastCompleteTime, PlaybackEndTime) ? Ms(TtsLastCompleteTime, PlaybackEndTime) : -1f;
        public float UserWaitToFirstTokenMs => BothValid(AsrEndTime, LlmFirstTokenTime) ? Ms(AsrEndTime, LlmFirstTokenTime) : -1f;
        public float UserWaitToFirstSentenceMs => BothValid(AsrEndTime, TtsFirstSentenceTime) ? Ms(AsrEndTime, TtsFirstSentenceTime) : -1f;
        public float UserWaitToFirstAudioMs => E2EMs;

        /// <summary>
        /// User-perceived latency: final ASR/turn detection to first audible assistant audio.
        /// Falls back to first sentence dispatch for local paths that cannot report audio start.
        /// </summary>
        public float E2EMs
        {
            get
            {
                if (BothValid(AsrEndTime, TtsFirstAudioTime))
                    return Ms(AsrEndTime, TtsFirstAudioTime);
                if (BothValid(AsrEndTime, TtsFirstSentenceTime))
                    return Ms(AsrEndTime, TtsFirstSentenceTime);
                if (BothValid(AsrEndTime, LlmLastTokenTime))
                    return Ms(AsrEndTime, LlmLastTokenTime);
                return -1f;
            }
        }

        public float TotalMs => BothValid(startTime, finalEndTime) ? Ms(startTime, finalEndTime) : -1f;
        internal float startTime => AsrStartTime >= 0f ? AsrStartTime : LlmRequestTime;
        internal float finalEndTime => PlaybackEndTime >= 0f ? PlaybackEndTime : (TtsLastCompleteTime >= 0f ? TtsLastCompleteTime : LlmLastTokenTime);

        public float RunningTotalMs
        {
            get
            {
                float start = AsrStartTime >= 0f ? AsrStartTime : LlmRequestTime;
                if (start < 0f) return -1f;
                float end = finalEndTime >= 0f ? finalEndTime : Time.realtimeSinceStartup;
                return BothValid(start, end) ? Ms(start, end) : -1f;
            }
        }

        public string BottleneckStage
        {
            get
            {
                float max = -1f;
                string stage = "-";
                SetMax("ASR", AsrMs, ref max, ref stage);
                SetMax("LLM", LlmMs, ref max, ref stage);
                SetMax("TTS", TtsMs, ref max, ref stage);
                SetMax("Playback", PlaybackMs, ref max, ref stage);
                return stage;
            }
        }

        public float BottleneckMs
        {
            get
            {
                float max = -1f;
                string stage = "-";
                SetMax("ASR", AsrMs, ref max, ref stage);
                SetMax("LLM", LlmMs, ref max, ref stage);
                SetMax("TTS", TtsMs, ref max, ref stage);
                SetMax("Playback", PlaybackMs, ref max, ref stage);
                return max;
            }
        }

        private static void SetMax(string name, float value, ref float max, ref string stage)
        {
            if (value > max)
            {
                max = value;
                stage = name;
            }
        }

        private static bool BothValid(float a, float b) => a >= 0f && b >= 0f && b >= a;
        private static float Ms(float start, float end) => (end - start) * 1000f;
    }
}
