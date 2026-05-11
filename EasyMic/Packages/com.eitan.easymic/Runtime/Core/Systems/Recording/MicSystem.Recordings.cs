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

                if (IsDeviceRecordingLocked(chosen))
                {
                    throw new EasyMicDeviceConflictException("A recording session is already in progress for this capture device. Stop it before starting another recording.");
                }

                var recordingId = _nextRecordingId++;
                var session = new RecordingSession(_context, chosen, sampleRate, channel, blueprints, _logger, _recordingCallbackDiagnosticsEnabled, latencyProfile);
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
            var choice = preferred;
            if (choice.HasValidId)
            {
                return choice;
            }

            var devices = Devices ?? Array.Empty<MicDevice>();
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
