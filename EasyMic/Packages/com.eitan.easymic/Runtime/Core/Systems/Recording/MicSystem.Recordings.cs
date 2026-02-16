using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic.Runtime.Exceptions;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        public RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel)
        {
            return StartRecording(device, sampleRate, channel, null);
        }

        public RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel, IEnumerable<AudioWorkerBlueprint> blueprints)
        {
            ThrowIfDisposed();

            var chosen = ResolveDevice(device);
            if (!chosen.HasValidId)
            {
                throw new EasyMicDeviceNotFoundException("No valid capture device available.");
            }

            // check if the microphone has been in recording status.
            var IsRecording = IsDeviceRecording(device);
            if (IsRecording)
            {
                throw new EasyMicDeviceConflictException("A recording session is already in progress. Please stop the current recording before starting a new one.");
            }

            var recordingId = _nextRecordingId++;
            var session = new RecordingSession(_context, chosen, sampleRate, channel, blueprints, _logger);
            lock (_operateLock)
            {
                _activeRecordings[recordingId] = session;
            }

            return new RecordingHandle(recordingId);
        }

        public void StopRecording(RecordingHandle handle)
        {
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
                    return new RecordingInfo(session.MicDevice, session.SampleRate, session.Channel, true, session.ProcessorCount);
                }
            }

            return new RecordingInfo();
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
            if (_activeRecordings == null || _activeRecordings.Count == 0)
            {
                return false;
            }

            lock (_operateLock)
            {
                foreach (var session in _activeRecordings.Values)
                {
                    if (session.IsSameDevice(device))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsHandleAlive(RecordingHandle handle)
        {

            if (_activeRecordings == null || _activeRecordings.Count <= 0)
            {
                return false;
            }
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
    }
}
