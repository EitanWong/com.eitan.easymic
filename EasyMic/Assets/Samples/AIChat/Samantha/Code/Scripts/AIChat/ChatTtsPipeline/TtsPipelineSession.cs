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

        public bool EnsureStarted(Func<long, CancellationToken, Task> startFactory)
        {
            if (startFactory == null)
            {
                return false;
            }

            lock (_sync)
            {
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

                    _cts.Dispose();
                    _cts = null;
                }

                return (_sessionId, _task);
            }
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
            CancelAndGetTask();
        }
    }
}
