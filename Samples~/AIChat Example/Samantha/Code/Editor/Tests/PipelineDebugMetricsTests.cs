using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class PipelineDebugMetricsTests
    {
        [Test]
        public void ConversationRound_ShouldCalculateVoicePipelineHandoffs()
        {
            var round = new ConversationRound
            {
                AsrStartTime = 1.00f,
                AsrEndTime = 1.50f,
                LlmRequestTime = 1.52f,
                LlmFirstTokenTime = 1.78f,
                LlmLastTokenTime = 2.40f,
                TtsFirstSentenceTime = 1.95f,
                TtsFirstAudioTime = 2.18f,
                TtsLastCompleteTime = 3.10f,
                PlaybackEndTime = 3.50f
            };

            Assert.AreEqual(500f, round.AsrMs, 0.1f);
            Assert.AreEqual(260f, round.FirstTokenMs, 0.1f);
            Assert.AreEqual(430f, round.FirstSentenceMs, 0.1f);
            Assert.AreEqual(230f, round.TtsQueueToFirstAudioMs, 0.1f);
            Assert.AreEqual(680f, round.UserWaitToFirstAudioMs, 0.1f);
            Assert.AreEqual("TTS", round.BottleneckStage);
        }
    }
}
