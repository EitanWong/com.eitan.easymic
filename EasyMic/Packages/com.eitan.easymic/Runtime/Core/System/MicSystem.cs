using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

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
        private bool _disposed;
        private int _nextRecordingId = 1;

        private readonly Dictionary<int, RecordingSession> _activeRecordings = new Dictionary<int, RecordingSession>();
        private readonly object _operateLock = new object();

        private MicDevice[] _devices = Array.Empty<MicDevice>();
        private MicDeviceWatcher _deviceWatcher;

        public MicDevice[] Devices
        {
            get => _devices;
            private set => _devices = value ?? Array.Empty<MicDevice>();
        }

        public int DeviceCount { get; private set; }

        public event Action<MicDevicesChangedEventArgs> DevicesChanged;

        private GCHandle _gcHandle;

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

        public MicSystem()
        {
            _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            _context = Native.AllocateContext();
            var result = Native.ContextInit(IntPtr.Zero, 0, IntPtr.Zero, _context);
            if (result != Native.Result.Success)
            {
                throw new InvalidOperationException($"Unable to init context. {result}");
            }

            Application.quitting += OnApplicationQuitting;

            RefreshDevicesInternal(true);
            EnableAutoRefresh();
        }

        ~MicSystem()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            Application.quitting -= OnApplicationQuitting;

            DisableAutoRefresh();
            StopAllRecordings();

            if (_context != IntPtr.Zero)
            {
                try
                {
                    Native.ContextUninit(_context);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error uninitializing context: {ex.Message}");
                }

                try { Native.Free(_context); } catch { }
                _context = IntPtr.Zero;
            }

            if (_gcHandle.IsAllocated)
            {
                _gcHandle.Free();
            }

            _disposed = true;
        }

        private void OnApplicationQuitting()
        {
            StopAllRecordings();
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
