using System;
using System.Collections.Generic;
using Eitan.EasyMic.Runtime.Exceptions;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        public RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel)
        {
            return StartRecording(device, sampleRate, channel, null, EasyMicLatencyProfile.Balanced);
        }

        public RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel, IEnumerable<AudioWorkerBlueprint> blueprints)
        {
            return StartRecording(device, sampleRate, channel, blueprints, EasyMicLatencyProfile.Balanced);
        }

        public RecordingHandle StartRecording(
            MicDevice device,
            SampleRate sampleRate,
            Channel channel,
            IEnumerable<AudioWorkerBlueprint> blueprints,
            EasyMicLatencyProfile latencyProfile)
        {
            lock (_operateLock)
            {
                ThrowIfDisposed();

                var chosen = ResolveDevice(device);
                if (!chosen.HasValidId)
                {
                    throw new EasyMicDeviceNotFoundException("No valid capture device available.");
                }

                ResolveFormatForDevice(chosen, ref sampleRate, ref channel);

                if (IsDeviceRecordingLocked(chosen))
                {
                    throw new EasyMicDeviceConflictException("A recording session is already in progress for this capture device. Stop it before starting another recording.");
                }

                var recordingId = _nextRecordingId++;
                RecordingSession session;
                try
                {
                    session = CreateRecordingSession(chosen, sampleRate, channel, blueprints, latencyProfile);
                }
                catch (NativeDeviceActivationException ex)
                {
                    if (!ShouldRetryWithAndroidOpenSlFallback(ex))
                    {
                        throw;
                    }

                    bool switchedToFallback;
                    try
                    {
                        switchedToFallback = TrySwitchAndroidContextToOpenSlFallback();
                    }
                    catch (Exception fallbackEx)
                    {
                        throw new InvalidOperationException(
                            "EasyMic Android capture failed on the initial backend, and switching to OpenSL ES fallback also failed. " +
                            "Initial failure: " + ex.Message,
                            fallbackEx);
                    }

                    if (!switchedToFallback)
                    {
                        throw;
                    }

                    chosen = ResolveDevice(device);
                    if (!chosen.HasValidId)
                    {
                        throw new InvalidOperationException(
                            "No valid capture device available after switching Android capture backend to OpenSL ES.", ex);
                    }

                    ResolveFormatForDevice(chosen, ref sampleRate, ref channel);

                    try
                    {
                        session = CreateRecordingSession(chosen, sampleRate, channel, blueprints, latencyProfile);
                    }
                    catch (NativeDeviceActivationException retryEx)
                    {
                        throw new InvalidOperationException(
                            "EasyMic Android capture could not start after both the initial backend and OpenSL ES fallback. " +
                            "Initial failure: " + ex.Message + " OpenSL ES failure: " + retryEx.Message,
                            retryEx);
                    }
                }

                _activeRecordings[recordingId] = session;
                return new RecordingHandle(recordingId);
            }
        }

        public void StopRecording(RecordingHandle handle)
        {
            ThrowIfDisposed();

            if (!handle.IsValid)
            {
                return;
            }

            RecordingSession session = null;
            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out session))
                {
                    _activeRecordings.Remove(handle.Id);
                }
            }

            session?.Dispose();
        }

        public void StopAllRecordings()
        {
            RecordingSession[] sessions;
            lock (_operateLock)
            {
                sessions = new RecordingSession[_activeRecordings.Count];
                _activeRecordings.Values.CopyTo(sessions, 0);
                _activeRecordings.Clear();
            }

            foreach (var session in sessions)
            {
                try { session.Dispose(); }
                catch { }
            }
        }

        public void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
        {
            if (!handle.IsValid || blueprint == null)
            {
                return;
            }

            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    session.AddProcessor(blueprint);
                }
            }
        }

        public void RemoveProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
        {
            if (!handle.IsValid || blueprint == null)
            {
                return;
            }

            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    session.RemoveProcessor(blueprint);
                }
            }
        }

        public T GetProcessor<T>(RecordingHandle handle, AudioWorkerBlueprint blueprint) where T : class, IAudioWorker
        {
            if (!handle.IsValid || blueprint == null)
            {
                return null;
            }

            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    return session.GetProcessor(blueprint) as T;
                }
            }

            return null;
        }

        public RecordingInfo GetRecordingInfo(RecordingHandle handle)
        {
            if (!handle.IsValid)
            {
                return new RecordingInfo();
            }

            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    return session.GetInfo();
                }
            }

            return new RecordingInfo();
        }

        public EasyMicRecordingPipelineSnapshot[] GetRecordingPipelineSnapshots()
        {
            lock (_operateLock)
            {
                if (_activeRecordings.Count == 0)
                {
                    return Array.Empty<EasyMicRecordingPipelineSnapshot>();
                }

                var snapshots = new EasyMicRecordingPipelineSnapshot[_activeRecordings.Count];
                int index = 0;
                foreach (var entry in _activeRecordings)
                {
                    snapshots[index++] = entry.Value.GetPipelineSnapshot(new RecordingHandle(entry.Key));
                }

                return snapshots;
            }
        }

        public void SetRecordingCallbackDiagnostics(RecordingHandle handle, bool enabled)
        {
            if (!handle.IsValid)
            {
                return;
            }

            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    session.SetCallbackDiagnosticsEnabled(enabled);
                }
            }
        }

        private MicDevice ResolveDevice(MicDevice preferred)
        {
            var devices = Devices ?? Array.Empty<MicDevice>();
            if (preferred.HasValidId)
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].SameIdentityAs(preferred))
                    {
                        return devices[i];
                    }
                }

                if (!string.IsNullOrEmpty(preferred.Name))
                {
                    for (int i = 0; i < devices.Length; i++)
                    {
                        if (string.Equals(devices[i].Name, preferred.Name, StringComparison.Ordinal))
                        {
                            return devices[i];
                        }
                    }

                    for (int i = 0; i < devices.Length; i++)
                    {
                        if (string.Equals(devices[i].Name, preferred.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            return devices[i];
                        }
                    }
                }

                if (devices.Length == 0)
                {
                    return _usingAndroidOpenSlFallback ? CreateDefaultDeviceForCurrentBackend(preferred) : preferred;
                }
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].IsDefault)
                {
                    return devices[i];
                }
            }

            if (devices.Length > 0)
            {
                return devices[0];
            }

            return default;
        }

        private static MicDevice CreateDefaultDeviceForCurrentBackend(MicDevice preferred)
        {
            return new MicDevice
            {
                Name = string.IsNullOrEmpty(preferred.Name) ? "Default Microphone" : preferred.Name,
                IsDefault = true,
                DeviceId = new byte[Native.DeviceIdSizeInBytes],
                NativeFormats = preferred.NativeFormats ?? Array.Empty<Native.NativeDataFormat>()
            };
        }

        private static void ResolveFormatForDevice(MicDevice device, ref SampleRate sampleRate, ref Channel channel)
        {
            sampleRate = device.ResolveSampleRate(sampleRate);
            channel = device.SupportsChannel(channel) ? channel : device.GetPreferredChannel(channel);
        }

        private RecordingSession CreateRecordingSession(
            MicDevice device,
            SampleRate sampleRate,
            Channel channel,
            IEnumerable<AudioWorkerBlueprint> blueprints,
            EasyMicLatencyProfile latencyProfile)
        {
            return new RecordingSession(
                _context,
                device,
                sampleRate,
                channel,
                blueprints,
                _logger,
                _recordingCallbackDiagnosticsEnabled,
                latencyProfile,
                Native.FormatBackendList(_contextBackends),
                _usingAndroidOpenSlFallback);
        }

        private static bool ShouldRetryWithAndroidOpenSlFallback(NativeDeviceActivationException ex)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return ex != null &&
                   (ex.Result == Native.Result.Error ||
                    ex.Result == Native.Result.FormatNotSupported ||
                    ex.Result == Native.Result.DeviceTypeNotSupported ||
                    ex.Result == Native.Result.NoBackend ||
                    ex.Result == Native.Result.NoDevice ||
                    ex.Result == Native.Result.InvalidDeviceConfig ||
                    ex.Result == Native.Result.BackendNotEnabled ||
                    ex.Result == Native.Result.FailedToInitBackend ||
                    ex.Result == Native.Result.FailedToOpenBackendDevice ||
                    ex.Result == Native.Result.FailedToStartBackendDevice ||
                    ex.Result == Native.Result.Unavailable ||
                    ex.Result == Native.Result.Busy ||
                    ex.Result == Native.Result.AlreadyInUse ||
                    ex.Result == Native.Result.AccessDenied);
#else
            return false;
#endif
        }

        /// <summary>
        /// Check if the devices is recording right now
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns> <summary>
        public bool IsDeviceRecording(MicDevice device)
        {
            lock (_operateLock)
            {
                return IsDeviceRecordingLocked(device);
            }
        }

        public bool IsHandleAlive(RecordingHandle handle)
        {
            if (!handle.IsValid)
            {
                return false;
            }

            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDeviceRecordingLocked(MicDevice device)
        {
            if (_activeRecordings.Count == 0)
            {
                return false;
            }

            foreach (var session in _activeRecordings.Values)
            {
                if (session.IsSameDevice(device))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
