#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal readonly struct TtsPipelineSessionSnapshot
    {
        public long SessionId { get; }
        public Task Task { get; }

        public TtsPipelineSessionSnapshot(long sessionId, Task task)
        {
            SessionId = sessionId;
            Task = task ?? System.Threading.Tasks.Task.CompletedTask;
        }
    }

    internal sealed class TtsPipelineSession : IDisposable
    {
        private readonly object _sync = new object();
        private long _sessionId;
        private CancellationTokenSource _cts;
        private Task _task = Task.CompletedTask;
        private volatile bool _disposed;

        public bool EnsureStarted(Func<long, CancellationToken, Task> startFactory)
        {
            if (startFactory == null)
            {
                return false;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                if (!_task.IsCompleted)
                {
                    return false;
                }

                long nextSessionId = _sessionId + 1;
                Volatile.Write(ref _sessionId, nextSessionId);
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _task = startFactory(nextSessionId, _cts.Token) ?? Task.CompletedTask;
                return true;
            }
        }

        public TtsPipelineSessionSnapshot CancelAndGetSnapshot()
        {
            CancellationTokenSource ctsToDispose = null;
            Task taskToWait;
            long sessionId;

            lock (_sync)
            {
                if (_cts != null)
                {
                    try
                    {
                        if (!_cts.IsCancellationRequested)
                        {
                            _cts.Cancel();
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                    ctsToDispose = _cts;
                    _cts = null;
                }

                taskToWait = _task;
                sessionId = _sessionId;
            }

            DisposeCancellationSourceWhenTaskCompletes(ctsToDispose, taskToWait);
            return new TtsPipelineSessionSnapshot(sessionId, taskToWait);
        }

        public (long sessionId, Task task) CancelAndGetTask()
        {
            TtsPipelineSessionSnapshot snapshot = CancelAndGetSnapshot();
            return (snapshot.SessionId, snapshot.Task);
        }

        public Task GetTask()
        {
            lock (_sync)
            {
                return _task;
            }
        }

        public bool IsCurrent(long sessionId)
        {
            return !_disposed && Volatile.Read(ref _sessionId) == sessionId;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
            }

            CancelAndGetTask();
        }

        private static void DisposeCancellationSourceWhenTaskCompletes(CancellationTokenSource cts, Task task)
        {
            if (cts == null)
            {
                return;
            }

            if (task == null || task.IsCompleted)
            {
                cts.Dispose();
                return;
            }

            task.ContinueWith(
                _ => cts.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
#endif
