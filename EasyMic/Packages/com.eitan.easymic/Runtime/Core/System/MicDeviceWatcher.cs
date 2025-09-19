using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Lightweight polling helper that keeps microphone devices up to date without relying on Unity MonoBehaviours.
    /// Runs a timer on a background thread and invokes <see cref="MicSystem.Refresh"/> at the requested cadence.
    /// </summary>
    internal sealed class MicDeviceWatcher : IDisposable
    {
        private readonly MicSystem _system;
        private readonly object _lock = new object();
        private Timer _timer;
        private int _intervalMs;
        private int _isTicking;
        private bool _disposed;
        private readonly SynchronizationContext _syncContext;

        private MicDeviceWatcher(MicSystem system, float intervalSeconds)
        {
            _system = system ?? throw new ArgumentNullException(nameof(system));
            _intervalMs = ClampInterval(intervalSeconds);
            _syncContext = SynchronizationContext.Current;
            _timer = new Timer(OnTimer, null, _intervalMs, _intervalMs);
        }

        public static MicDeviceWatcher Ensure(MicSystem system, float intervalSeconds)
        {
            return new MicDeviceWatcher(system, intervalSeconds);
        }

        public void Attach(MicSystem system, float intervalSeconds)
        {
            if (!ReferenceEquals(system, _system))
            {
                throw new InvalidOperationException("MicDeviceWatcher instance is bound to a different MicSystem.");
            }

            ChangeInterval(intervalSeconds);
        }

        public void Detach(MicSystem system)
        {
            if (!ReferenceEquals(system, _system))
            {
                return;
            }

            Dispose();
        }

        private void ChangeInterval(float seconds)
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                var newInterval = ClampInterval(seconds);
                if (newInterval == _intervalMs)
                {
                    return;
                }

                _intervalMs = newInterval;
                _timer?.Change(_intervalMs, _intervalMs);
            }
        }

        private void OnTimer(object state)
        {
            if (_disposed || _system == null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _isTicking, 1, 0) != 0)
            {
                return; // skip re-entrancy if refresh takes longer than the interval
            }

            try
            {
                if (_syncContext != null)
                {
                    _syncContext.Post(s => ExecuteRefresh((MicDeviceWatcher)s), this);
                }
                else
                {
                    ExecuteRefresh(this);
                }
            }
            catch
            {
                ExecuteRefresh(this);
            }
        }

        private int ClampInterval(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            {
                seconds = 1f;
            }

            var clamped = Math.Max(0.25d, (double)seconds);
            var interval = (int)(clamped * 1000d);
            return Math.Max(250, interval);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
                try { _timer?.Dispose(); } catch { }
                _timer = null;
            }
        }

        private static void LogError(string message)
        {
#if UNITY_2018_1_OR_NEWER
            try { UnityEngine.Debug.LogError(message); } catch { }
#else
            System.Diagnostics.Debug.WriteLine(message);
#endif
        }

        private static void ExecuteRefresh(MicDeviceWatcher watcher)
        {
            if (watcher == null)
            {
                return;
            }

            try
            {
                if (!watcher._disposed && watcher._system != null)
                {
                    watcher._system.Refresh();
                }
            }
            catch (Exception ex)
            {
                LogError($"EasyMic: Device watcher refresh failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref watcher._isTicking, 0);
            }
        }
    }
}
