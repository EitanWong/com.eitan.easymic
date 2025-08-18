// MicSystem.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using AOT;

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

            // 设备有效性校验与兜底：避免底层初始化失败导致的难以定位的问题
            MicDevice chosen = device;
            if (chosen.Id == IntPtr.Zero)
            {
                // 优先默认设备，其次第一个可用设备
                var devs = Devices ?? Array.Empty<MicDevice>();
                for (int i = 0; i < devs.Length; i++)
                {
                    if (devs[i].IsDefault) { chosen = devs[i]; break; }
                }
                if (chosen.Id == IntPtr.Zero && devs.Length > 0) chosen = devs[0];
                if (chosen.Id == IntPtr.Zero)
                {
                    // 无可用设备：返回默认句柄，调用侧可据此识别失败
                    UnityEngine.Debug.LogError("EasyMic: No valid capture device available.");
                    return default;
                }
            }

            var recordingId = _nextRecordingId++;

            // create recording Session
            var session = new RecordingSession(_context, chosen, sampleRate, channel, null);
            lock (_operateLock)
            {
                _activeRecordings[recordingId] = session;
            }

            return new RecordingHandle(recordingId, chosen);
        }

        public RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel, System.Collections.Generic.IEnumerable<AudioWorkerBlueprint> blueprints)
        {
            ThrowIfDisposed();

            MicDevice chosen = device;
            if (chosen.Id == IntPtr.Zero)
            {
                var devs = Devices ?? Array.Empty<MicDevice>();
                for (int i = 0; i < devs.Length; i++)
                {
                    if (devs[i].IsDefault) { chosen = devs[i]; break; }
                }
                if (chosen.Id == IntPtr.Zero && devs.Length > 0) chosen = devs[0];
                if (chosen.Id == IntPtr.Zero)
                {
                    UnityEngine.Debug.LogError("EasyMic: No valid capture device available.");
                    return default;
                }
            }

            var recordingId = _nextRecordingId++;

            var session = new RecordingSession(_context, chosen, sampleRate, channel, blueprints);
            lock (_operateLock)
            {
                _activeRecordings[recordingId] = session;
            }

            return new RecordingHandle(recordingId, chosen);
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

        public void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
        {
            if (!handle.IsValid)
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
            if (!handle.IsValid)
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
            if (!handle.IsValid) return null;
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
            private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, RecordingSession> _devicePtrSessionMap = new System.Collections.Concurrent.ConcurrentDictionary<IntPtr, RecordingSession>();

            public readonly MicDevice MicDevice;
            public readonly SampleRate SampleRate;
            public readonly Channel Channel;

            private readonly IntPtr _devicePtr;
            private readonly IntPtr _context;
            private readonly IntPtr _deviceConfig;
            private readonly AudioPipeline _audioPipeline;
            private bool _disposed = false;

            private readonly System.Collections.Generic.Dictionary<AudioWorkerBlueprint, IAudioWorker> _workerMap = new System.Collections.Generic.Dictionary<AudioWorkerBlueprint, IAudioWorker>();

            private readonly uint _channelCount;
            private readonly uint _sampleRate;
            private readonly AudioState _state;

            private GCHandle _gcHandle; // Still needed to prevent GC of this object

            private static readonly Native.AudioCallback _staticAudioCallback = StaticAudioCallback;
            private static readonly Native.AudioCallbackEx _staticAudioCallbackEx = StaticAudioCallbackEx;
            private bool _usingUserDataCallback;

            public int ProcessorCount => _audioPipeline.WorkerCount;

            public RecordingSession(IntPtr context, MicDevice device, SampleRate sampleRate, Channel channel, System.Collections.Generic.IEnumerable<AudioWorkerBlueprint> blueprints)
            {
                try
                {
                    this._context = context;
                    MicDevice = device;
                    SampleRate = sampleRate;
                    Channel = channel;
                    _channelCount = (uint)channel;
                    _sampleRate = (uint)sampleRate;
                    _audioPipeline = new AudioPipeline();
                    _state = new AudioState((int)_channelCount, (int)_sampleRate, (int)(_channelCount * _sampleRate));
                    // Build pipeline from worker blueprints if provided (create fresh workers per session)
                    if (blueprints != null)
                    {
                        foreach (var bp in blueprints)
                        {
                            if (bp == null) continue;
                            var w = bp.Create();
                            _workerMap[bp] = w;
                            _audioPipeline.AddWorker(w);
                        }
                    }
                    _audioPipeline.Initialize(_state);

                    // Pin this object so the native layer can safely reference it via the callback
                    _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);

                    // Prefer extended config with pUserData when available; otherwise use simplified helper
                    try
                    {
                        _deviceConfig = Native.AllocateDeviceConfigEx(
                            Native.DeviceType.Record,
                            Native.SampleFormat.F32,
                            _channelCount,
                            _sampleRate,
                            _staticAudioCallbackEx,
                            GCHandle.ToIntPtr(_gcHandle),
                            IntPtr.Zero,
                            device.Id);
                        _usingUserDataCallback = true;
                    }
                    catch (System.EntryPointNotFoundException)
                    {
                        // Newer simplified config helper (no userData, device defaults)
                        _deviceConfig = Native.AllocateDeviceConfig(
                            Native.Capability.Record,
                            _sampleRate,
                            _staticAudioCallback,
                            IntPtr.Zero);
                        _usingUserDataCallback = false;
                    }

                    _devicePtr = Native.AllocateDevice();
                    var result = Native.DeviceInit(this._context, _deviceConfig, _devicePtr);
                    if (result != Native.Result.Success)
                    {
                        throw new InvalidOperationException($"Unable to init device. {result}");
                    }

                    if (!_usingUserDataCallback)
                    {
                        _devicePtrSessionMap[_devicePtr] = this;
                    }

                    result = Native.DeviceStart(_devicePtr);
                    if (result != Native.Result.Success)
                    {
                        throw new InvalidOperationException($"Unable to start device. {result}");
                    }
                }
                catch
                {
                    this.Dispose();
                    throw;
                }
            }

            [MonoPInvokeCallback(typeof(Native.AudioCallback))]
            private static void StaticAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                if (!_devicePtrSessionMap.TryGetValue(device, out var session))
                {
                    return; // Not found or was disposed
                }

                // Call the instance method
                session.HandleAudioCallback(device, output, input, length);
            }

            [MonoPInvokeCallback(typeof(Native.AudioCallbackEx))]
            private static void StaticAudioCallbackEx(IntPtr device, IntPtr output, IntPtr input, uint length, IntPtr userData)
            {
                if (userData == IntPtr.Zero) { StaticAudioCallback(device, output, input, length); return; }
                var handle = GCHandle.FromIntPtr(userData);
                if (handle.Target is RecordingSession session && !session._disposed)
                {
                    session.HandleAudioCallback(device, output, input, length);
                }
            }

            private void HandleAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                if (_disposed) return;

                var sampleCount = (int)(length * _channelCount);
                // RT-safe: compute sample count → pass to pipeline (readers enqueue via SPSC)
                _state.Length = sampleCount;
                ProcessAudioBuffer(GetSpan<float>(input, sampleCount), _state);
            }

            // Pass state through
            private void ProcessAudioBuffer(Span<float> buffer, AudioState state) => _audioPipeline.OnAudioPass(buffer, state);

            public void AddProcessor(AudioWorkerBlueprint blueprint)
            {
                if (blueprint == null) return;
                if (_workerMap.ContainsKey(blueprint)) return;
                var w = blueprint.Create();
                _workerMap[blueprint] = w;
                _audioPipeline.AddWorker(w);
            }

            public void RemoveProcessor(AudioWorkerBlueprint blueprint)
            {
                if (blueprint == null) return;
                if (_workerMap.TryGetValue(blueprint, out var w))
                {
                    _audioPipeline.RemoveWorker(w);
                    _workerMap.Remove(blueprint);
                }
            }

            public IAudioWorker GetProcessor(AudioWorkerBlueprint blueprint)
            {
                if (blueprint == null) return null;
                return _workerMap.TryGetValue(blueprint, out var w) ? w : null;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_devicePtr != IntPtr.Zero && !_usingUserDataCallback)
                {
                    _devicePtrSessionMap.TryRemove(_devicePtr, out _);
                }

                if (_devicePtr != IntPtr.Zero)
                {
                    try
                    {
                        Native.DeviceStop(_devicePtr);
                        Native.DeviceUninit(_devicePtr);
                        Native.Free(_devicePtr);
                    }
                    catch (Exception ex) { Debug.LogError($"Error disposing device: {ex.Message}"); }
                }

                if (_deviceConfig != IntPtr.Zero)
                {
                    try { Native.Free(_deviceConfig); }
                    catch (Exception ex) { Debug.LogError($"Error freeing config: {ex.Message}"); }
                }
                // Pipeline is owned by this session; dispose to release resources
                try { _audioPipeline?.Dispose(); } catch { }
                _workerMap.Clear();

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
