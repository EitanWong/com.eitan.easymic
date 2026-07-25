#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.Threading;
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

        [Test]
        public void FullDuplexCoordinator_BeginTurnShouldCancelPreviousTurn()
        {
            using var coordinator = new FullDuplexTurnCoordinator();

            FullDuplexTurn first = coordinator.BeginTurn();
            FullDuplexTurn second = coordinator.BeginTurn();

            Assert.AreEqual(first.TurnId + 1, second.TurnId);
            Assert.IsTrue(first.Token.IsCancellationRequested);
            Assert.IsFalse(second.Token.IsCancellationRequested);
            Assert.IsTrue(coordinator.IsCurrent(second.TurnId));
        }

        [Test]
        public void FullDuplexCoordinator_InterruptShouldClearBusyStateAtomically()
        {
            using var coordinator = new FullDuplexTurnCoordinator();
            FullDuplexTurn turn = coordinator.BeginTurn();
            Assert.IsTrue(coordinator.TrySetAssistantSpeaking(turn.TurnId, true));

            FullDuplexInterruption interruption = coordinator.Interrupt(advanceGeneration: true);
            FullDuplexTurnSnapshot snapshot = coordinator.GetSnapshot();

            Assert.IsTrue(interruption.HadActiveTurn);
            Assert.IsTrue(turn.Token.IsCancellationRequested);
            Assert.IsFalse(snapshot.LlmInFlight);
            Assert.IsFalse(snapshot.AssistantSpeaking);
            Assert.IsFalse(snapshot.IsBusy);
            Assert.Greater(snapshot.TurnId, turn.TurnId);
        }

        [Test]
        public void FullDuplexCoordinator_StaleCallbacksShouldNotMutateNewTurn()
        {
            using var coordinator = new FullDuplexTurnCoordinator();
            FullDuplexTurn oldTurn = coordinator.BeginTurn();
            FullDuplexTurn currentTurn = coordinator.BeginTurn();

            Assert.IsFalse(coordinator.TryCompleteLlm(oldTurn.TurnId, oldTurn.CancellationSource));
            Assert.IsFalse(coordinator.TrySetAssistantSpeaking(oldTurn.TurnId, false));

            FullDuplexTurnSnapshot snapshot = coordinator.GetSnapshot();
            Assert.AreEqual(currentTurn.TurnId, snapshot.TurnId);
            Assert.IsTrue(snapshot.LlmInFlight);
        }
    }
}
#endif
