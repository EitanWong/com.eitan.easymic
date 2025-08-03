// MicSystem.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using AOT;
using SoundIO;
using System.Linq; // Required for MonoPInvokeCallback

namespace Eitan.EasyMic.Runtime
{
    public sealed class MicSystem : IDisposable
    {
        private IntPtr _context;
        private bool _disposed = false;
        private int _nextRecordingId = 1;

        // Thread-safe collections for managing multiple recordings
        private readonly Dictionary<int, RecordingSession> _activeRecordings = new Dictionary<int, RecordingSession>();
        private readonly object _operateLock = new object();

        public MicDevice[] Devices { get; private set; }
        public int DeviceCount { get; private set; }
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

        // GC handle to prevent garbage collection of the MicSystem instance itself
        private GCHandle _gcHandle;

        public MicSystem()
        {
            _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);

            _context = Native.AllocateContext();
            var result = Native.ContextInit(IntPtr.Zero, 0, IntPtr.Zero, _context);
            if (result != Native.Result.Success)
            {

                throw new InvalidOperationException($"Unable to init context. {result}");
            }

            // Register for Unity application quit events to ensure cleanup
            Application.quitting += OnApplicationQuitting;

            Refresh();
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

            // Unregister from Unity application quit events
            Application.quitting -= OnApplicationQuitting;

            // Stop all recordings
            StopAllRecordings();

            // Free unmanaged resources
            if (_context != IntPtr.Zero)
            {
                try
                {
                    Native.ContextUninit(_context);
                    Native.Free(_context);
                    _context = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error uninitializing context: {ex.Message}");
                }
            }

            if (_gcHandle.IsAllocated)
            {
                _gcHandle.Free();
            }


            _disposed = true;
        }

        #region Public Methods


        public RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel)
        {
            ThrowIfDisposed();

            var recordingId = _nextRecordingId++;

            // create recording Sesstion
            var session = new RecordingSession(_context, device, sampleRate, channel);
            lock (_operateLock)
            {
                _activeRecordings[recordingId] = session;
            }

            return new RecordingHandle(recordingId, device);
        }

        public void StopRecording(RecordingHandle handle)
        {
            if (!handle.IsValid)
            {
                return;
            }


            RecordingSession session;
            lock (_operateLock)
            {
                if (!_activeRecordings.TryGetValue(handle.Id, out session))
                {
                    return;
                }


                _activeRecordings.Remove(handle.Id);
            }

            session.Dispose();
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
                session.Dispose();
            }
        }

        public void AddProcessor(RecordingHandle handle, IAudioWorker processor)
        {
            if (!handle.IsValid)
            {
                return;
            }


            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    session.AddProcessor(processor);
                }
            }
        }

        public void RemoveProcessor(RecordingHandle handle, IAudioWorker processor)
        {
            if (!handle.IsValid)
            {
                return;
            }


            lock (_operateLock)
            {
                if (_activeRecordings.TryGetValue(handle.Id, out var session))
                {
                    session.RemoveProcessor(processor);
                }
            }
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

        public void Refresh()
        {
            ThrowIfDisposed();
            Devices = GetDevices();
            DeviceCount = Devices.Length;
        }

        #endregion

        #region Private Methods

        private void OnApplicationQuitting()
        {
            // Ensure all recordings are stopped when Unity application quits
            StopAllRecordings();
        }

        private MicDevice[] GetDevices()
        {
            ThrowIfDisposed();

            var result = Native.GetDevices(_context, out var pPlaybackDevices, out var pCaptureDevices,
                out var playbackDeviceCount, out var captureDeviceCount);

            if (result != Native.Result.Success)
            {

                throw new InvalidOperationException("Unable to get devices.");
            }


            var deviceCount = (int)captureDeviceCount;

            try
            {
                if (pCaptureDevices == IntPtr.Zero || captureDeviceCount == IntPtr.Zero)
                {

                    return Array.Empty<MicDevice>();
                }


                return pCaptureDevices.ReadArray<MicDevice>(deviceCount);
            }
            finally
            {
                if (pPlaybackDevices != IntPtr.Zero)
                {
                    Native.Free(pPlaybackDevices);
                }

                if (pCaptureDevices != IntPtr.Zero)
                {
                    Native.Free(pCaptureDevices);
                }

            }
        }


        private void ThrowIfDisposed()
        {
            if (_disposed)
            {

                throw new ObjectDisposedException(nameof(MicSystem));
            }

        }

        #endregion

        #region RecordingSession
        private sealed class RecordingSession : IDisposable
        {
            // Static dictionary to map session IDs to instances for the static callback
            private static readonly Dictionary<int, RecordingSession> _sessionLookup = new Dictionary<int, RecordingSession>();
            private static readonly object _sessionLookupLock = new object();
            private static int _nextSessionId = 1;

            public readonly MicDevice MicDevice;
            public readonly SampleRate SampleRate;
            public readonly Channel Channel;

            private readonly IntPtr _devicePtr;
            private readonly IntPtr _context;
            private readonly IntPtr _deviceConfig;
            // private readonly List<IAudioWorker> _processors = new List<IAudioWorker>();

            private readonly AudioPipeline _audioPipeline;

            private readonly object _processorsLock = new object();
            private bool _disposed = false;

            private readonly uint _channelCount;
            private readonly uint _sampleRate;
            private readonly int _sessionId;

            // GC handle to prevent garbage collection of this session object
            private GCHandle _gcHandle;

            // private AudioState _originalState;
            // private AudioState _streamingState;


            // Static delegate instance - IL2CPP compatible
            private static readonly Native.AudioCallback _staticAudioCallback = StaticAudioCallback;

            public int ProcessorCount
            {
                get
                {
                    lock (_processorsLock)
                    {
                        return _audioPipeline.WorkerCount;
                    }
                }
            }

            public RecordingSession(IntPtr context, MicDevice device, SampleRate sampleRate, Channel channel)
            {
                try
                {
                    this._context = context;
                    MicDevice = device;
                    SampleRate = sampleRate;
                    Channel = channel;

                    _channelCount = (uint)channel;
                    _sampleRate = (uint)sampleRate;

                    // Initialize the audio pipeline

                    _audioPipeline = new AudioPipeline();

                    _audioPipeline.Initialize(new AudioState((int)_channelCount, (int)_sampleRate,((int)sampleRate*(int)_channelCount)));// initialize state,do not use _streamingState here, because it could be changed by the processor

                    // Assign a unique session ID and register this instance
                    lock (_sessionLookupLock)
                    {
                        _sessionId = _nextSessionId++;
                        _sessionLookup[_sessionId] = this;
                    }

                    // Allocate GC handle to prevent this instance from being collected
                    _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);

                    // Pass the session ID as user data to the native code
                    var userDataPtr = new IntPtr(_sessionId);

                    _deviceConfig = Native.AllocateDeviceConfig(
                        Native.DeviceType.Record,
                        Native.SampleFormat.F32,
                        _channelCount,
                        _sampleRate,
                        _staticAudioCallback, // Use static callback
                        IntPtr.Zero,
                        device.Id);

                    _devicePtr = Native.AllocateDevice();
                    var result = Native.DeviceInit(this._context, _deviceConfig, _devicePtr);

                    if (result != Native.Result.Success)
                    {
                        throw new InvalidOperationException($"Unable to init device. {result}");
                    }

                    result = Native.DeviceStart(_devicePtr);
                    if (result != Native.Result.Success)
                    {
                        throw new InvalidOperationException($"Unable to start device. {result}");
                    }
                }
                catch
                {
                    // If any error occurs during construction, ensure we clean up everything.
                    this.Dispose();
                    throw; // Re-throw the original exception.
                }
            }

            // Static callback method - IL2CPP compatible with MonoPInvokeCallback attribute
            [MonoPInvokeCallback(typeof(Native.AudioCallback))]
            private static void StaticAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                try
                {
                    // We need to find a way to identify which session this callback belongs to
                    // Since we can't pass user data through the current API, we'll need to iterate through active sessions
                    // This is not ideal but works for the IL2CPP constraint

                    RecordingSession[] activeSessions;
                    lock (_sessionLookupLock)
                    {
                        activeSessions = new RecordingSession[_sessionLookup.Count];
                        _sessionLookup.Values.CopyTo(activeSessions, 0);
                    }

                    // Find the session that owns this device pointer
                    foreach (var session in activeSessions)
                    {
                        if (session != null && !session._disposed && session._devicePtr == device)
                        {
                            session.HandleAudioCallback(device, output, input, length);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error in static audio callback: {ex}");
                }
            }

            // Instance method to handle the callback
            private void HandleAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                if (_disposed)
                {
                    return;
                }


                var sampleCount = (int)(length * _channelCount);
                try
                {
                    this.ProcessAudioBuffer(GetSpan<float>(input, sampleCount));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error in audio callback: {ex}");
                }
            }

            public void AddProcessor(IAudioWorker processor) => _audioPipeline.AddWorker(processor);
            public void RemoveProcessor(IAudioWorker processor) => _audioPipeline.RemoveWorker(processor);

            private void ProcessAudioBuffer(Span<float> buffer) => _audioPipeline.OnAudioPass(buffer, new AudioState((int)_channelCount, (int)_sampleRate,buffer.Length));

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }


                _disposed = true; // Set disposed flag immediately to stop any in-flight callbacks.

                // Remove from session lookup
                lock (_sessionLookupLock)
                {
                    _sessionLookup.Remove(_sessionId);
                }

                // Stop and cleanup device
                if (_devicePtr != IntPtr.Zero)
                {
                    try
                    {
                        Native.DeviceStop(_devicePtr);
                        Native.DeviceUninit(_devicePtr);
                        Native.Free(_devicePtr);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error disposing recording device: {ex.Message}");
                    }
                }

                // Free device config
                if (_deviceConfig != IntPtr.Zero)
                {
                    try
                    {
                        Native.Free(_deviceConfig);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error freeing device config: {ex.Message}");
                    }
                }

                // do not dispose the audio pipeline here, because it could be used by other processors, user should dispose it manually
                // lock (_processorsLock)
                // {
                //     _audioPipeline.Dispose();
                // }

                // Finally, free the GC Handle
                if (_gcHandle.IsAllocated)
                {
                    _gcHandle.Free();
                }

            }

            private unsafe Span<T> GetSpan<T>(IntPtr ptr, int length) where T : unmanaged
            {
                return new Span<T>((void*)ptr, length);
            }
        }

        #endregion

    }
    // Extension method for reading arrays from IntPtr
    public static class IntPtrExtensions
    {
        public static T[] ReadArray<T>(this IntPtr ptr, int count) where T : struct
        {
            if (ptr == IntPtr.Zero || count <= 0)
            {

                return Array.Empty<T>();
            }


            var result = new T[count];
            var elementSize = Marshal.SizeOf<T>();

            for (int i = 0; i < count; i++)
            {
                var elementPtr = IntPtr.Add(ptr, i * elementSize);
                result[i] = Marshal.PtrToStructure<T>(elementPtr);
            }

            return result;
        }
    }
}