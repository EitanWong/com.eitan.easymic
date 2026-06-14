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
        private Native.Backend[] _contextBackends;
        private AndroidCaptureBackendAttempt _androidCaptureAttempt;
        private bool _contextIsOpenSlOnly;
        private bool _usingAndroidOpenSlFallback;
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

        private sealed class NativeDeviceActivationException : InvalidOperationException
        {
            public NativeDeviceActivationException(Native.Result result, string message)
                : base(message)
            {
                Result = result;
            }

            public Native.Result Result { get; }
        }

        private readonly struct AndroidCaptureBackendAttempt
        {
            public AndroidCaptureBackendAttempt(
                string label,
                Native.AndroidCaptureDeviceConfigProfile configProfile,
                bool openSlBackend)
            {
                Label = label;
                ConfigProfile = configProfile;
                OpenSlBackend = openSlBackend;
            }

            public string Label { get; }
            public Native.AndroidCaptureDeviceConfigProfile ConfigProfile { get; }
            public bool OpenSlBackend { get; }

            public static AndroidCaptureBackendAttempt Default =>
                new AndroidCaptureBackendAttempt(
                    "default",
                    Native.AndroidCaptureDeviceConfigProfile.Default,
                    false);

            public static AndroidCaptureBackendAttempt AAudioLowLatency =>
                new AndroidCaptureBackendAttempt(
                    "AAudio low-latency",
                    Native.AndroidCaptureDeviceConfigProfile.Default,
                    false);

            public static AndroidCaptureBackendAttempt AAudioCompatibility =>
                new AndroidCaptureBackendAttempt(
                    "AAudio compatibility",
                    Native.AndroidCaptureDeviceConfigProfile.AAudioCompatibility,
                    false);

            public static AndroidCaptureBackendAttempt AAudioUltraSafe =>
                new AndroidCaptureBackendAttempt(
                    "AAudio ultra-safe",
                    Native.AndroidCaptureDeviceConfigProfile.AAudioUltraSafe,
                    false);

            public static AndroidCaptureBackendAttempt OpenSlLowLatency =>
                new AndroidCaptureBackendAttempt(
                    "OpenSL ES low-latency",
                    Native.AndroidCaptureDeviceConfigProfile.Default,
                    true);

            public static AndroidCaptureBackendAttempt OpenSlSafe =>
                new AndroidCaptureBackendAttempt(
                    "OpenSL ES safe",
                    Native.AndroidCaptureDeviceConfigProfile.OpenSlSafe,
                    true);
        }

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

            InitializeContextForCurrentPlatform();

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

        private void InitializeContextForCurrentPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && UNITY_2021_3_OR_NEWER
            if (EasyMicRuntimeSettings.Current.Android.useAAudio)
            {
                var aaudioBackends = new[] { Native.Backend.AAudio };
                var aaudioResult = Native.ContextInitWithBackends(aaudioBackends, IntPtr.Zero, _context);
                if (aaudioResult == Native.Result.Success)
                {
                    ApplyContextState(aaudioBackends, usingAndroidOpenSlFallback: false);
                    return;
                }

                Log(
                    "EasyMic: Android AAudio backend initialization failed; " +
                    "starting with OpenSL ES low-latency backend. " +
                    Native.FormatResult(aaudioResult),
                    LogLevel.Warning);
                ReallocateContextAfterFailedInitialization();
                InitializeContext(new[] { Native.Backend.OpenSl }, usingAndroidOpenSlFallback: true);
                return;
            }

            InitializeContext(new[] { Native.Backend.OpenSl });
#else
            InitializeContext(GetPreferredBackendsForCurrentPlatform());
#endif
        }

        private void InitializeContext(Native.Backend[] backends)
        {
            InitializeContext(backends, usingAndroidOpenSlFallback: false);
        }

        private void InitializeContext(Native.Backend[] backends, bool usingAndroidOpenSlFallback)
        {
            var result = Native.ContextInitWithBackends(backends, IntPtr.Zero, _context);
            if (result != Native.Result.Success)
            {
                Native.FreeAllocated(_context, _contextAllocationSource);
                _context = IntPtr.Zero;
                throw new InvalidOperationException(
                    $"Unable to init context. {Native.FormatResult(result)} backends={Native.FormatBackendList(backends)}");
            }

            ApplyContextState(backends, usingAndroidOpenSlFallback);
        }

        private void ReallocateContextAfterFailedInitialization()
        {
            if (_context != IntPtr.Zero)
            {
                try { Native.FreeAllocated(_context, _contextAllocationSource); } catch { }
                _context = IntPtr.Zero;
            }

            _context = Native.AllocateContext(out _contextAllocationSource);
        }

        private void ReplaceContext(Native.Backend[] backends, bool usingAndroidOpenSlFallback)
        {
            var previousContext = _context;
            var previousAllocationSource = _contextAllocationSource;
            var previousBackends = _contextBackends;
            var previousContextIsOpenSlOnly = _contextIsOpenSlOnly;
            var previousUsingAndroidOpenSlFallback = _usingAndroidOpenSlFallback;
            var previousAndroidCaptureAttempt = _androidCaptureAttempt;
            var nextContext = Native.AllocateContext(out var nextAllocationSource);
            bool nextContextInitialized = false;

            try
            {
                var result = Native.ContextInitWithBackends(backends, IntPtr.Zero, nextContext);
                if (result != Native.Result.Success)
                {
                    throw new InvalidOperationException(
                        $"Unable to init replacement context. {Native.FormatResult(result)} backends={Native.FormatBackendList(backends)}");
                }

                nextContextInitialized = true;
                _context = nextContext;
                _contextAllocationSource = nextAllocationSource;
                ApplyContextState(backends, usingAndroidOpenSlFallback);

                if (previousContext != IntPtr.Zero)
                {
                    try { Native.ContextUninit(previousContext); } catch { }
                    try { Native.FreeAllocated(previousContext, previousAllocationSource); } catch { }
                }
            }
            catch
            {
                if (_context == nextContext)
                {
                    _context = previousContext;
                    _contextAllocationSource = previousAllocationSource;
                    _contextBackends = previousBackends;
                    _contextIsOpenSlOnly = previousContextIsOpenSlOnly;
                    _usingAndroidOpenSlFallback = previousUsingAndroidOpenSlFallback;
                    _androidCaptureAttempt = previousAndroidCaptureAttempt;
                }

                if (nextContext != IntPtr.Zero)
                {
                    if (nextContextInitialized)
                    {
                        try { Native.ContextUninit(nextContext); } catch { }
                    }

                    try { Native.FreeAllocated(nextContext, nextAllocationSource); } catch { }
                }

                throw;
            }
        }

        private void ApplyContextState(Native.Backend[] backends, bool usingAndroidOpenSlFallback)
        {
            _contextBackends = backends != null && backends.Length > 0 ? (Native.Backend[])backends.Clone() : null;
            _contextIsOpenSlOnly = IsOpenSlOnly(_contextBackends);
            _usingAndroidOpenSlFallback = usingAndroidOpenSlFallback;
            _androidCaptureAttempt = GetInitialAndroidCaptureAttempt(_contextIsOpenSlOnly);
        }

        private Native.Backend[] GetPreferredBackendsForCurrentPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && UNITY_2021_3_OR_NEWER
            return EasyMicRuntimeSettings.Current.Android.useAAudio
                ? new[] { Native.Backend.AAudio }
                : new[] { Native.Backend.OpenSl };
#else
            return null;
#endif
        }

        private bool TryAdvanceAndroidCaptureBackendFallback(NativeDeviceActivationException activationFailure)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            if (_activeRecordings.Count != 0)
            {
                return false;
            }

            if (!_contextIsOpenSlOnly &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.Default &&
                !_androidCaptureAttempt.OpenSlBackend)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.AAudioCompatibility;
                Log(
                    "EasyMic: Android AAudio low-latency capture activation failed; " +
                    "retrying with compatible AAudio capture profile. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                return true;
            }

            if (!_contextIsOpenSlOnly &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.AAudioCompatibility)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.AAudioUltraSafe;
                Log(
                    "EasyMic: Android compatible AAudio capture activation failed; " +
                    "retrying with ultra-safe AAudio capture profile. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                return true;
            }

            if (!_contextIsOpenSlOnly &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.AAudioUltraSafe)
            {
                Log(
                    "EasyMic: Android ultra-safe AAudio capture activation failed; " +
                    "retrying capture with OpenSL ES low-latency backend. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                ReplaceContext(new[] { Native.Backend.OpenSl }, usingAndroidOpenSlFallback: true);
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.OpenSlLowLatency;
                RefreshDevicesAfterAndroidBackendSwitch();
                return true;
            }

            if (_androidCaptureAttempt.OpenSlBackend &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.Default)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.OpenSlSafe;
                Log(
                    "EasyMic: Android OpenSL ES low-latency capture activation failed; " +
                    "retrying with safe OpenSL ES capture profile. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                return true;
            }

            return false;
#else
            _ = activationFailure;
            return false;
#endif
        }

        private void RefreshDevicesAfterAndroidBackendSwitch()
        {
            try
            {
                RefreshDevicesInternal(true);
            }
            catch (Exception ex)
            {
                Devices = Array.Empty<MicDevice>();
                DeviceCount = 0;
                Log(
                    "EasyMic: Android backend fallback could not refresh capture devices; " +
                    "continuing with the platform default capture endpoint. " + ex.Message,
                    LogLevel.Warning);
            }
        }

        private static AndroidCaptureBackendAttempt GetInitialAndroidCaptureAttempt(bool contextIsOpenSlOnly)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (contextIsOpenSlOnly || !EasyMicRuntimeSettings.Current.Android.useAAudio)
            {
                return AndroidCaptureBackendAttempt.OpenSlLowLatency;
            }

            return AndroidCaptureBackendAttempt.AAudioLowLatency;
#else
            _ = contextIsOpenSlOnly;
            return AndroidCaptureBackendAttempt.Default;
#endif
        }

        private static string BuildAndroidFallbackReason(NativeDeviceActivationException activationFailure)
        {
            return activationFailure == null
                ? string.Empty
                : "Previous failure: " + activationFailure.Message;
        }

        private static bool IsOpenSlOnly(Native.Backend[] backends)
        {
            return backends != null &&
                   backends.Length == 1 &&
                   backends[0] == Native.Backend.OpenSl;
        }
    }
}
