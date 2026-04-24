using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Exceptions;
using UnityEngine;

namespace Eitan.EasyMic
{
    public static class EasyMicAPI
    {
        private static readonly object _lock = new object();
        private static MicSystem _micSystem;
        private static bool _eventsHooked;
        private static List<AudioWorkerBlueprint> _defaultWorkers;
        private static Exception _initializationException;

        public static event Action<MicDevicesChangedEventArgs> DevicesChanged;

        private static MicSystem MicSys
        {
            get
            {
                return RequireMicSys();
            }
        }

        public static bool IsAvailable => TryGetMicSys(out _, logFailure: false);

        public static string UnavailabilityReason => _initializationException?.Message;

        private static MicSystem RequireMicSys()
        {
            if (TryGetMicSys(out var system))
            {
                return system;
            }

            throw new InvalidOperationException(
                _initializationException?.Message ?? "EasyMic microphone system is unavailable.",
                _initializationException);
        }

        private static bool TryGetMicSys(out MicSystem system, bool logFailure = true)
        {
            var current = _micSystem;
            if (current != null && !current.IsDisposed)
            {
                system = current;
                return true;
            }

            lock (_lock)
            {
                current = _micSystem;
                if (current != null)
                {
                    if (!current.IsDisposed)
                    {
                        system = current;
                        return true;
                    }

                    ReleaseMicSystemReference(current, dispose: false);
                }

                if (_initializationException == null)
                {
                    if (!EasyMicPlatformSupport.IsCurrentPlatformSupported(out string unsupportedReason))
                    {
                        _initializationException = new PlatformNotSupportedException(unsupportedReason);
                    }

                    if (_initializationException != null)
                    {
                        system = _micSystem;
                        goto EndLock;
                    }

                    try
                    {
                        _micSystem = new MicSystem();
                        HookSystemEvents(_micSystem);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is EntryPointNotFoundException || ex is DllNotFoundException || ex is PlatformNotSupportedException)
                    {
                        _initializationException = ex;
                    }
                }

            EndLock:
                system = _micSystem;
            }

            if (system != null)
            {
                return true;
            }

            if (logFailure && _initializationException != null)
            {
                Debug.LogWarning($"EasyMic is unavailable: {_initializationException.Message}");
            }

            return false;
        }

        private static void HookSystemEvents(MicSystem system)
        {
            if (system == null || _eventsHooked)
            {
                return;
            }

            system.DevicesChanged += RaiseDevicesChanged;
            _eventsHooked = true;
        }

        private static void ReleaseMicSystemReference(MicSystem system, bool dispose)
        {
            if (system == null || !ReferenceEquals(_micSystem, system))
            {
                return;
            }

            if (_eventsHooked)
            {
                system.DevicesChanged -= RaiseDevicesChanged;
                _eventsHooked = false;
            }

            if (dispose)
            {
                system.Dispose();
            }

            _micSystem = null;
        }

        private static void RaiseDevicesChanged(MicDevicesChangedEventArgs args)
        {
            DevicesChanged?.Invoke(args);
        }

        public static MicDevice[] Devices
        {
            get
            {
                if (!EnsurePermission("enumerate devices"))
                {
                    return Array.Empty<MicDevice>();
                }

                return TryGetMicSys(out var system, logFailure: false)
                    ? system.Devices
                    : Array.Empty<MicDevice>();
            }
        }

        public static MicDevice Default
        {
            get
            {
                if (!TryGetMicSys(out _, logFailure: false) && _initializationException != null)
                {
                    throw new InvalidOperationException(_initializationException.Message, _initializationException);
                }

                Refresh();
                if (Devices == null || Devices.Length <= 0)
                {
                    throw new EasyMicDeviceNotFoundException("No microphone devices found!");
                }
                for (int i = 0; i < Devices.Length; i++)
                {
                    if (Devices[i].IsDefault)
                    {
                        return Devices[i];
                    }
                }
                return Devices[0];
            }
        }

        public static bool isWorking => TryGetMicSys(out var system, logFailure: false) && system.HasActiveRecordings;

        public static void Refresh()
        {
            if (!EnsurePermission("refresh devices"))
            {
                return;
            }

            if (TryGetMicSys(out var system, logFailure: false))
            {
                system.Refresh();
            }
        }

        public static void EnableDeviceAutoRefresh(float seconds = 1f)
        {
            if (TryGetMicSys(out var system, logFailure: false))
            {
                system.EnableAutoRefresh(Math.Max(0.25f, seconds));
            }
        }

        public static void DisableDeviceAutoRefresh()
        {
            if (TryGetMicSys(out var system, logFailure: false))
            {
                system.DisableAutoRefresh();
            }
        }

        public static RecordingHandle StartRecording(SampleRate sampleRate = SampleRate.Hz16000)
        {
            return StartRecordingInternal(null, null, sampleRate, null, _defaultWorkers);
        }

        public static RecordingHandle StartRecording(string name, SampleRate sampleRate = SampleRate.Hz16000, Channel channel = Channel.Mono)
        {
            return StartRecordingInternal(null, name, sampleRate, channel, _defaultWorkers);
        }

        public static RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate = SampleRate.Hz16000, Channel channel = Channel.Mono)
        {
            return StartRecordingInternal(device, null, sampleRate, channel, _defaultWorkers);
        }

        public static RecordingHandle StartRecording(SampleRate sampleRate, IEnumerable<AudioWorkerBlueprint> workers)
        {
            return StartRecordingInternal(null, null, sampleRate, null, workers);
        }

        public static RecordingHandle StartRecording(string name, SampleRate sampleRate, Channel channel, IEnumerable<AudioWorkerBlueprint> workers)
        {
            return StartRecordingInternal(null, name, sampleRate, channel, workers);
        }

        public static RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel, IEnumerable<AudioWorkerBlueprint> workers)
        {
            return StartRecordingInternal(device, null, sampleRate, channel, workers);
        }

        public static List<AudioWorkerBlueprint> DefaultWorkers
        {
            get => _defaultWorkers;
            set => _defaultWorkers = value?.Distinct().ToList();
        }
        public static void StopRecording(RecordingHandle handle)
        {
            if (TryGetMicSys(out var system, logFailure: false))
            {
                system.StopRecording(handle);
            }
        }

        public static void StopAllRecordings()
        {
            if (TryGetMicSys(out var system, logFailure: false))
            {
                system.StopAllRecordings();
            }
        }

        public static bool IsDeviceRecording(MicDevice device)
        {
            return TryGetMicSys(out var system, logFailure: false) && system.IsDeviceRecording(device);
        }

        public static bool IsHandleAlive(RecordingHandle handle)
        {
            return TryGetMicSys(out var system, logFailure: false) && system.IsHandleAlive(handle);
        }

        public static void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
        {
            RequireMicSys().AddProcessor(handle, blueprint);
        }

        public static void RemoveProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
        {
            RequireMicSys().RemoveProcessor(handle, blueprint);
        }

        public static RecordingInfo GetRecordingInfo(RecordingHandle handle)
        {
            return RequireMicSys().GetRecordingInfo(handle);
        }

        public static T GetProcessor<T>(RecordingHandle handle, AudioWorkerBlueprint blueprint) where T : class, IAudioWorker
        {
            return RequireMicSys().GetProcessor<T>(handle, blueprint);
        }

        public static void Cleanup()
        {
            lock (_lock)
            {
                ReleaseMicSystemReference(_micSystem, dispose: true);

                _initializationException = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntimeLoad()
        {
            Cleanup();
            DevicesChanged = null;
            _defaultWorkers = null;
        }

        private static RecordingHandle StartRecordingInternal(MicDevice? preferredDevice, string deviceName, SampleRate sampleRate, Channel? requestedChannel, IEnumerable<AudioWorkerBlueprint> workers)
        {
            if (!EnsurePermission("start recording", asError: true))
            {
                return default;
            }


            if (!TrySelectDevice(preferredDevice, deviceName, out var chosen))
            {
                throw new EasyMicDeviceNotFoundException("EasyMic: No valid capture device available.");
            }

            var channelToUse = ResolveChannel(chosen, requestedChannel);
            var resolvedRate = chosen.ResolveSampleRate(sampleRate);
            var blueprintSet = workers ?? _defaultWorkers;

            return MicSys.StartRecording(chosen, resolvedRate, channelToUse, blueprintSet);
        }

        private static Channel ResolveChannel(MicDevice device, Channel? requested)
        {
            if (requested.HasValue && device.SupportsChannel(requested.Value))
            {
                return requested.Value;
            }

            if (requested.HasValue)
            {
                return device.GetPreferredChannel(requested.Value);
            }

            return device.GetPreferredChannel();
        }

        private static bool TrySelectDevice(MicDevice? preferredDevice, string deviceName, out MicDevice chosen)
        {
            var devices = MicSys.Devices ?? Array.Empty<MicDevice>();
            if (devices.Length == 0)
            {
                chosen = default;
                return false;
            }

            if (preferredDevice.HasValue)
            {
                var preferred = preferredDevice.Value;
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].SameIdentityAs(preferred))
                    {
                        chosen = devices[i];
                        return true;
                    }
                }

                if (!string.IsNullOrEmpty(preferred.Name))
                {
                    var byName = devices.FirstOrDefault(d => string.Equals(d.Name, preferred.Name, StringComparison.Ordinal));
                    if (byName.HasValidId)
                    {
                        chosen = byName;
                        return true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(deviceName))
            {
                var exact = devices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.Ordinal));
                if (exact.HasValidId)
                {
                    chosen = exact;
                    return true;
                }

                var ignoreCase = devices.FirstOrDefault(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
                if (ignoreCase.HasValidId)
                {
                    chosen = ignoreCase;
                    return true;
                }
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].IsDefault)
                {
                    chosen = devices[i];
                    return true;
                }
            }

            chosen = devices[0];
            return true;
        }

        private static bool EnsurePermission(string actionDescription, bool asError = false)
        {
            if (PermissionUtils.HasPermission())
            {
                return true;
            }

            var message = $"Cannot {actionDescription}. Microphone permission not granted. Call EasyMic.RequestPermission() first.";
            if (asError)
            {
                if (_micSystem != null)
                {
                    _micSystem.Log(message, MicSystem.LogLevel.Error);
                }
                else
                {
                    Debug.LogError(message);
                }
            }
            else
            {
                if (_micSystem != null)
                {
                    _micSystem.Log(message, MicSystem.LogLevel.Warning);
                }
                else
                {
                    Debug.LogWarning(message);
                }
            }

            return false;
        }
    }
}
