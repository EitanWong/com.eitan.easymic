using System;
using System.Collections.Generic;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Core lifecycle and shared state for EasyMic microphone management.
    /// Peripheral responsibilities (device enumeration, recordings) are implemented
    /// via partial class declarations to keep each module focused.
    /// </summary>
    public sealed partial class MicSystem : IDisposable
    {
        private IntPtr _context;
        private Native.NativeAllocationSource _contextAllocationSource;
        private bool _disposed;
        private int _nextRecordingId = 1;

        private readonly Dictionary<int, RecordingSession> _activeRecordings = new Dictionary<int, RecordingSession>();
        private readonly object _operateLock = new object();
        private bool _recordingCallbackDiagnosticsEnabled;

        private MicDevice[] _devices = Array.Empty<MicDevice>();
        private MicDeviceWatcher _deviceWatcher;

        public MicDevice[] Devices
        {
            get => _devices;
            private set => _devices = value ?? Array.Empty<MicDevice>();
        }

        public int DeviceCount { get; private set; }

        public event Action<MicDevicesChangedEventArgs> DevicesChanged;

        internal bool IsDisposed
        {
            get
            {
                lock (_operateLock)
                {
                    return _disposed;
                }
            }
        }

        public bool HasActiveRecordings
        {
            get
            {
                lock (_operateLock)
                {
                    return _activeRecordings.Count > 0;
                }
            }
        }

        public bool RecordingCallbackDiagnosticsEnabled
        {
            get
            {
                lock (_operateLock)
                {
                    return _recordingCallbackDiagnosticsEnabled;
                }
            }
            set
            {
                lock (_operateLock)
                {
                    _recordingCallbackDiagnosticsEnabled = value;
                    foreach (var session in _activeRecordings.Values)
                    {
                        session.SetCallbackDiagnosticsEnabled(value);
                    }
                }
            }
        }

        public MicSystem()
        {
            EasyMicUnityThread.TryCaptureFromCurrentThread();

            try
            {
                _context = Native.AllocateContext(out _contextAllocationSource);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "EasyMic miniaudio plugin is incompatible with this package build. " +
                    "The loaded native plugin does not export required miniaudio APIs.",
                    ex);
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException(
                    "EasyMic miniaudio plugin could not be loaded. " +
                    "The required miniaudio native plugin is missing or not available for this platform.",
                    ex);
            }

            var result = Native.ContextInit(IntPtr.Zero, 0, IntPtr.Zero, _context);
            if (result != Native.Result.Success)
            {
                Native.FreeAllocated(_context, _contextAllocationSource);
                _context = IntPtr.Zero;
                throw new InvalidOperationException($"Unable to init context. {result}");
            }

            UnityEngine.Application.quitting += OnApplicationQuitting;

            RefreshDevicesInternal(true);
            EnableAutoRefresh();
        }

        public void Dispose()
        {
            lock (_operateLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            UnityEngine.Application.quitting -= OnApplicationQuitting;

            DisableAutoRefresh();
            StopAllRecordings();

            if (_context != IntPtr.Zero)
            {
                try { Native.ContextUninit(_context); } catch { }

                try { Native.FreeAllocated(_context, _contextAllocationSource); } catch { }
                _context = IntPtr.Zero;
            }
        }

        private void OnApplicationQuitting()
        {
            Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MicSystem));
            }
        }
    }
}
