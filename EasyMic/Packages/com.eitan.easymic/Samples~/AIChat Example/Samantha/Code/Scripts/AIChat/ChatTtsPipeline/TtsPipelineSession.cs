#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class TtsPipelineSession : IDisposable
    {
        private readonly object _sync = new object();
        private long _sessionId;
        private CancellationTokenSource _cts;
        private Task _task = Task.CompletedTask;
        private bool _disposed;

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

                _sessionId++;
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _task = startFactory(_sessionId, _cts.Token) ?? Task.CompletedTask;
                return true;
            }
        }

        public (long sessionId, Task task) CancelAndGetTask()
        {
            CancellationTokenSource ctsToDispose = null;
            Task taskToWait;

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
            }

            DisposeCancellationSourceWhenTaskCompletes(ctsToDispose, taskToWait);
            return (_sessionId, taskToWait);
        }

        public Task GetTask()
        {
            lock (_sync)
            {
                return _task;
            }
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
