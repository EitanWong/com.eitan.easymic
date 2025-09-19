using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic.Runtime;
using UnityEngine;

namespace Eitan.EasyMic
{
    public static class EasyMicAPI
    {
        private static readonly object _lock = new object();
        private static MicSystem _micSystem;
        private static bool _eventsHooked;
        private static List<AudioWorkerBlueprint> _defaultWorkers;

        public static event Action<MicDevicesChangedEventArgs> DevicesChanged;

        private static MicSystem MicSys
        {
            get
            {
                if (_micSystem == null)
                {
                    lock (_lock)
                    {
                        if (_micSystem == null)
                        {
                            _micSystem = new MicSystem();
                            HookSystemEvents(_micSystem);
                        }
                    }
                }

                return _micSystem;
            }
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

                return MicSys.Devices;
            }
        }

        public static bool IsRecording => MicSys.HasActiveRecordings;

        public static void Refresh()
        {
            if (!EnsurePermission("refresh devices"))
            {
                return;
            }

            MicSys.Refresh();
        }

        public static void EnableDeviceAutoRefresh(float seconds = 1f)
        {
            MicSys.EnableAutoRefresh(Mathf.Max(0.25f, seconds));
        }

        public static void DisableDeviceAutoRefresh()
        {
            MicSys.DisableAutoRefresh();
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

        public static void StopRecording(RecordingHandle handle) => MicSys.StopRecording(handle);
        public static void StopAllRecordings() => MicSys.StopAllRecordings();
        public static void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint) => MicSys.AddProcessor(handle, blueprint);
        public static void RemoveProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint) => MicSys.RemoveProcessor(handle, blueprint);
        public static RecordingInfo GetRecordingInfo(RecordingHandle handle) => MicSys.GetRecordingInfo(handle);

        public static T GetProcessor<T>(RecordingHandle handle, AudioWorkerBlueprint blueprint) where T : class, IAudioWorker
        {
            return MicSys.GetProcessor<T>(handle, blueprint);
        }

        public static void Cleanup()
        {
            lock (_lock)
            {
                if (_micSystem != null)
                {
                    if (_eventsHooked)
                    {
                        _micSystem.DevicesChanged -= RaiseDevicesChanged;
                        _eventsHooked = false;
                    }

                    _micSystem.Dispose();
                    _micSystem = null;
                }
            }
        }

        private static RecordingHandle StartRecordingInternal(MicDevice? preferredDevice, string deviceName, SampleRate sampleRate, Channel? requestedChannel, IEnumerable<AudioWorkerBlueprint> workers)
        {
            if (!EnsurePermission("start recording", asError: true))
            {
                return default;
            }

            if (!TrySelectDevice(preferredDevice, deviceName, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }

            var channelToUse = ResolveChannel(chosen, requestedChannel);
            var resolvedRate = chosen.ResolveSampleRate(sampleRate);
            var blueprintSet = workers ?? _defaultWorkers;

            try
            {
                return MicSys.StartRecording(chosen, resolvedRate, channelToUse, blueprintSet);
            }
            catch (Exception ex)
            {
                Debug.LogError($"EasyMic: Failed to start recording on '{chosen.Name}'. {ex.Message}");
                return default;
            }
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

            var message = $"EasyMic: Cannot {actionDescription}. Microphone permission not granted. Call EasyMic.RequestPermission() first.";
            if (asError)
            {
                Debug.LogError(message);
            }
            else
            {
                Debug.LogWarning(message);
            }

            return false;
        }
    }
}
