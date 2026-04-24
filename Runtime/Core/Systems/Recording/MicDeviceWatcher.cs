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

        private MicDeviceWatcher(MicSystem system, float intervalSeconds)
        {
            _system = system ?? throw new ArgumentNullException(nameof(system));
            _intervalMs = ClampInterval(intervalSeconds);
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
                var context = EasyMicUnityThread.MainContext;
                if (context != null)
                {
                    context.Post(s => ExecuteRefresh((MicDeviceWatcher)s), this);
                }
                else
                {
                    if (EasyMicPlatformSupport.RequiresAndroidMainThread)
                    {
                        // Android device enumeration may call JNI paths that are unsafe from worker timer threads.
                        Interlocked.Exchange(ref _isTicking, 0);
                        return;
                    }

                    ExecuteRefresh(this);
                }
            }
            catch
            {
                if (EasyMicPlatformSupport.RequiresAndroidMainThread)
                {
                    Interlocked.Exchange(ref _isTicking, 0);
                    return;
                }

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
                    if (EasyMicPlatformSupport.RequiresAndroidMainThread && !EasyMicUnityThread.IsMainThread)
                    {
                        return;
                    }
                    watcher._system.Refresh();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (EasyMicUnityThread.IsMainThread)
                    {
                        watcher._system?.Log($"EasyMic: Device watcher refresh failed: {ex.Message}", MicSystem.LogLevel.Error);
                    }
                }
                catch
                {
                    // Never let background refresh crash the process.
                }
            }
            finally
            {
                Interlocked.Exchange(ref watcher._isTicking, 0);
            }
        }
    }
}
