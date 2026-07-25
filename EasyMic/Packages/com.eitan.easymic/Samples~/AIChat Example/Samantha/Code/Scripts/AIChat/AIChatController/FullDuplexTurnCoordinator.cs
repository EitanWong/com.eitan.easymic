#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal readonly struct FullDuplexTurn
    {
        public long TurnId { get; }
        public CancellationTokenSource CancellationSource { get; }
        public CancellationToken Token => CancellationSource.Token;

        public FullDuplexTurn(long turnId, CancellationTokenSource cancellationSource)
        {
            TurnId = turnId;
            CancellationSource = cancellationSource ?? throw new ArgumentNullException(nameof(cancellationSource));
        }
    }

    internal readonly struct FullDuplexTurnSnapshot
    {
        public long TurnId { get; }
        public bool LlmInFlight { get; }
        public bool AssistantSpeaking { get; }
        public bool IsBusy => LlmInFlight || AssistantSpeaking;

        public FullDuplexTurnSnapshot(long turnId, bool llmInFlight, bool assistantSpeaking)
        {
            TurnId = turnId;
            LlmInFlight = llmInFlight;
            AssistantSpeaking = assistantSpeaking;
        }
    }

    internal readonly struct FullDuplexInterruption
    {
        public bool HadActiveTurn { get; }
        public long InterruptedTurnId { get; }
        public long CurrentTurnId { get; }

        public FullDuplexInterruption(bool hadActiveTurn, long interruptedTurnId, long currentTurnId)
        {
            HadActiveTurn = hadActiveTurn;
            InterruptedTurnId = interruptedTurnId;
            CurrentTurnId = currentTurnId;
        }
    }

    /// <summary>
    /// Owns assistant turn identity and cancellation so capture and playback can overlap
    /// without allowing callbacks from an interrupted turn to mutate the current turn.
    /// </summary>
    internal sealed class FullDuplexTurnCoordinator : IDisposable
    {
        private readonly object _sync = new object();
        private long _turnId;
        private CancellationTokenSource _activeCancellation;
        private bool _llmInFlight;
        private bool _assistantSpeaking;
        private bool _disposed;

        public long CurrentTurnId
        {
            get
            {
                lock (_sync)
                {
                    return _turnId;
                }
            }
        }

        public FullDuplexTurn BeginTurn()
        {
            CancellationTokenSource previous;
            FullDuplexTurn turn;

            lock (_sync)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(FullDuplexTurnCoordinator));
                }

                previous = _activeCancellation;
                var next = new CancellationTokenSource();
                _turnId++;
                _activeCancellation = next;
                _llmInFlight = true;
                _assistantSpeaking = false;
                turn = new FullDuplexTurn(_turnId, next);
            }

            Cancel(previous);
            return turn;
        }

        public FullDuplexInterruption Interrupt(bool advanceGeneration)
        {
            CancellationTokenSource cancellation;
            FullDuplexInterruption result;

            lock (_sync)
            {
                long interruptedTurnId = _turnId;
                bool hadActiveTurn = _activeCancellation != null || _llmInFlight || _assistantSpeaking;
                cancellation = _activeCancellation;
                _activeCancellation = null;
                _llmInFlight = false;
                _assistantSpeaking = false;

                if (advanceGeneration)
                {
                    _turnId++;
                }

                result = new FullDuplexInterruption(hadActiveTurn, interruptedTurnId, _turnId);
            }

            Cancel(cancellation);
            return result;
        }

        public bool TryCompleteLlm(long turnId, CancellationTokenSource expectedCancellation)
        {
            lock (_sync)
            {
                if (_disposed || turnId != _turnId || !ReferenceEquals(_activeCancellation, expectedCancellation))
                {
                    return false;
                }

                _activeCancellation = null;
                _llmInFlight = false;
                return true;
            }
        }

        public bool TrySetAssistantSpeaking(long turnId, bool speaking)
        {
            lock (_sync)
            {
                if (_disposed || turnId != _turnId)
                {
                    return false;
                }

                _assistantSpeaking = speaking;
                return true;
            }
        }

        public bool IsCurrent(long turnId)
        {
            lock (_sync)
            {
                return !_disposed && turnId == _turnId;
            }
        }

        public FullDuplexTurnSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new FullDuplexTurnSnapshot(_turnId, _llmInFlight, _assistantSpeaking);
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _turnId++;
                cancellation = _activeCancellation;
                _activeCancellation = null;
                _llmInFlight = false;
                _assistantSpeaking = false;
            }

            Cancel(cancellation);
        }

        private static void Cancel(CancellationTokenSource cancellation)
        {
            if (cancellation == null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
#endif
