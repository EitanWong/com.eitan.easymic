using System;
using System.Collections.Generic;
using UnityEngine;

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
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }

            var recordingId = _nextRecordingId++;

            var session = new RecordingSession(_context, chosen, sampleRate, channel, blueprints);
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
    }
}
