using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class ControllerStateTransitionTests
    {
        [Test]
        public void StateProperties_ShouldReflectLatestValues()
        {
            var state = new AIChatControllerState();

            state.LlmInFlight = true;
            state.IsAssistantSpeaking = true;
            state.IsChatActive = true;
            state.IsInitialized = true;
            state.InitializationFailed = false;
            state.IsIdle = false;
            state.LastLoadingProgress = 0.85f;
            state.IsShuttingDown = false;

            Assert.IsTrue(state.LlmInFlight);
            Assert.IsTrue(state.IsAssistantSpeaking);
            Assert.IsTrue(state.IsChatActive);
            Assert.IsTrue(state.IsInitialized);
            Assert.IsFalse(state.InitializationFailed);
            Assert.IsFalse(state.IsIdle);
            Assert.AreEqual(0.85f, state.LastLoadingProgress);
            Assert.IsFalse(state.IsShuttingDown);

            state.LlmInFlight = false;
            state.IsAssistantSpeaking = false;
            state.IsIdle = true;

            Assert.IsFalse(state.LlmInFlight);
            Assert.IsFalse(state.IsAssistantSpeaking);
            Assert.IsTrue(state.IsIdle);
        }
    }
}
