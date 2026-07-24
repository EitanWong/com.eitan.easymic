#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class AIChatRequestOrchestratorTests
    {
        [Test]
        public void StreamingResponse_ShouldRejectChunksFromInterruptedTurn()
        {
            var orchestrator = CreateOrchestrator();
            orchestrator.BeginResponse(1);
            Assert.AreEqual("old", orchestrator.AppendStreamingChunk(1, "old"));

            orchestrator.BeginResponse(2);

            Assert.AreEqual(string.Empty, orchestrator.AppendStreamingChunk(1, " stale"));
            Assert.AreEqual("current", orchestrator.AppendStreamingChunk(2, "current"));
            Assert.AreEqual("current", orchestrator.GetRawResponse(2));
        }

        [Test]
        public void StreamingResponse_ShouldRejectStaleFlushAfterNewTurnStarts()
        {
            var orchestrator = CreateOrchestrator();
            string emitted = string.Empty;
            orchestrator.BeginResponse(10);
            orchestrator.ProcessStreamingChunk(10, "interrupted fragment", value => emitted += value);

            orchestrator.BeginResponse(11);
            orchestrator.FlushPendingSentences(10, value => emitted += value);

            Assert.AreEqual(string.Empty, emitted);
        }

        [Test]
        public void StreamingResponse_ShouldNormalizeCumulativeChunksWithoutDuplicatingText()
        {
            var orchestrator = CreateOrchestrator();
            orchestrator.BeginResponse(20);

            Assert.AreEqual("Hel", orchestrator.AppendStreamingChunk(20, "Hel"));
            Assert.AreEqual("lo", orchestrator.AppendStreamingChunk(20, "Hello"));
            Assert.AreEqual(" world", orchestrator.AppendStreamingChunk(20, "Hello world"));
            Assert.AreEqual("Hello world", orchestrator.GetRawResponse(20));
        }

        [Test]
        public void StreamingResponse_ShouldKeepDeltaChunksIncremental()
        {
            var orchestrator = CreateOrchestrator();
            orchestrator.BeginResponse(21);

            Assert.AreEqual("Hello ", orchestrator.AppendStreamingChunk(21, "Hello "));
            Assert.AreEqual("world", orchestrator.AppendStreamingChunk(21, "world"));
            Assert.AreEqual("Hello world", orchestrator.GetRawResponse(21));
        }

        [Test]
        public void BeginResponse_ShouldNotLetOlderTurnReplaceCurrentState()
        {
            var orchestrator = CreateOrchestrator();
            Assert.IsTrue(orchestrator.BeginResponse(30));
            Assert.AreEqual("current", orchestrator.AppendStreamingChunk(30, "current"));

            Assert.IsFalse(orchestrator.BeginResponse(29));

            Assert.AreEqual("current", orchestrator.GetRawResponse(30));
            Assert.AreEqual(string.Empty, orchestrator.AppendStreamingChunk(29, "stale"));
        }

        private static AIChatRequestOrchestrator CreateOrchestrator()
        {
            return new AIChatRequestOrchestrator(
                historyTurnProvider: () => 2,
                systemPromptProvider: () => string.Empty,
                cleanText: value => value,
                maxResponseBufferSize: 4096);
        }
    }
}
#endif
