using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using AOT;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        private sealed class RecordingSession : IDisposable
        {
            private static readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, RecordingSession> s_devicePtrSessionMap = new System.Collections.Concurrent.ConcurrentDictionary<IntPtr, RecordingSession>();
            private static readonly Native.AudioCallback s_staticAudioCallback = StaticAudioCallback;
            private static readonly Native.AudioCallbackEx s_staticAudioCallbackEx = StaticAudioCallbackEx;

            private readonly object _sessionLock = new object();
            private readonly Dictionary<AudioWorkerBlueprint, IAudioWorker> _workerMap = new Dictionary<AudioWorkerBlueprint, IAudioWorker>();
            private readonly List<AudioWorkerBlueprint> _blueprints = new List<AudioWorkerBlueprint>();
            private readonly AudioPipeline _audioPipeline = new AudioPipeline();
            private readonly IntPtr _context;
            private readonly AudioState _state;
            private readonly MicDevice _preferredDevice;
            private readonly string _preferredIdentifier;
            private readonly SampleRate _requestedSampleRate;
            private readonly Channel _requestedChannel;
            private bool _usingFallback;

            private GCHandle _gcHandle;
            private bool _disposed;
            private bool _usingUserDataCallback;
            private IntPtr _devicePtr;
            private IntPtr _deviceConfig;
            private IntPtr _deviceIdHandle;
            private uint _channelCount;
            private uint _sampleRate;

            public MicDevice MicDevice { get; private set; }
            public SampleRate SampleRate { get; private set; }
            public Channel Channel { get; private set; }

            public int ProcessorCount => _audioPipeline.WorkerCount;

            public RecordingSession(IntPtr context, MicDevice device, SampleRate sampleRate, Channel channel, IEnumerable<AudioWorkerBlueprint> blueprints)
            {
                _context = context;
                _state = new AudioState((int)channel, (int)sampleRate, Math.Max(1, (int)channel * (int)sampleRate));
                _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);
                _preferredDevice = device;
                _preferredIdentifier = device.GetIdentifier();
                _requestedSampleRate = sampleRate;
                _requestedChannel = channel;
                _usingFallback = false;

                ApplyFormat(device, sampleRate, channel);
                InitializePipeline(blueprints);

                try
                {
                    InitializeNativeDevice();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public IReadOnlyList<AudioWorkerBlueprint> Blueprints => _blueprints;
            public SampleRate RequestedSampleRate => _requestedSampleRate;
            public Channel RequestedChannel => _requestedChannel;
            public bool IsUsingPreferredDevice => MicDevice.SameIdentityAs(_preferredDevice);
            public bool IsUsingFallback => _usingFallback;

            public bool TrySwitchDevice(MicDevice targetDevice, SampleRate sampleRate, Channel channel)
            {
                lock (_sessionLock)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    var previousDevice = MicDevice;
                    var previousRate = SampleRate;
                    var previousChannel = Channel;
                    var previousFallback = _usingFallback;

                    try
                    {
                        ReleaseNativeResources();
                        ApplyFormat(targetDevice, sampleRate, channel);
                        InitializeNativeDeviceCore();
                        try
                        {
                            Debug.Log($"EasyMic: Recording session switched to '{MicDevice.Name}' at {_sampleRate} Hz, {_channelCount} ch.");
                        }
                        catch { }
                        _usingFallback = !MicDevice.SameIdentityAs(_preferredDevice);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"EasyMic: Failed to switch capture session to '{targetDevice.Name}'. {ex.Message}");
                        ReleaseNativeResources();
                        ApplyFormat(previousDevice, previousRate, previousChannel);
                        _usingFallback = previousFallback;
                        try
                        {
                            InitializeNativeDeviceCore();
                        }
                        catch (Exception restoreEx)
                        {
                            Debug.LogError($"EasyMic: Failed to restore capture session on '{previousDevice.Name}'. {restoreEx.Message}");
                            ReleaseNativeResources();
                        }
                        return false;
                    }
                }
            }

            public bool TryRestorePreferredDevice(IEnumerable<MicDevice> candidates)
            {
                if (!_usingFallback)
                {
                    return false;
                }

                if (candidates == null)
                {
                    return false;
                }

                foreach (var candidate in candidates)
                {
                    if (candidate.GetIdentifier() == _preferredIdentifier)
                    {
                        var resolvedChannel = candidate.SupportsChannel(_requestedChannel)
                            ? _requestedChannel
                            : candidate.GetPreferredChannel(_requestedChannel);

                        var resolvedRate = candidate.ResolveSampleRate(_requestedSampleRate);

                        return TrySwitchDevice(candidate, resolvedRate, resolvedChannel);
                    }
                }

                return false;
            }

            public void AddProcessor(AudioWorkerBlueprint blueprint)
            {
                if (blueprint == null)
                {
                    return;
                }

                lock (_sessionLock)
                {
                    if (_workerMap.ContainsKey(blueprint))
                    {
                        return;
                    }

                    var worker = blueprint.Create();
                    _workerMap[blueprint] = worker;
                    _blueprints.Add(blueprint);
                    _audioPipeline.AddWorker(worker);
                }
            }

            public void RemoveProcessor(AudioWorkerBlueprint blueprint)
            {
                if (blueprint == null)
                {
                    return;
                }

                lock (_sessionLock)
                {
                    if (_workerMap.TryGetValue(blueprint, out var worker))
                    {
                        _audioPipeline.RemoveWorker(worker);
                        _workerMap.Remove(blueprint);
                        _blueprints.Remove(blueprint);
                    }
                }
            }

            public IAudioWorker GetProcessor(AudioWorkerBlueprint blueprint)
            {
                if (blueprint == null)
                {
                    return null;
                }

                lock (_sessionLock)
                {
                    return _workerMap.TryGetValue(blueprint, out var worker) ? worker : null;
                }
            }

            public void Dispose()
            {
                lock (_sessionLock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    ReleaseNativeResources();
                }

                try
                {
                    _audioPipeline.Dispose();
                }
                catch { }

                foreach (var worker in _workerMap.Values)
                {
                    try { worker.Dispose(); } catch { }
                }

                _workerMap.Clear();
                _blueprints.Clear();

                if (_gcHandle.IsAllocated)
                {
                    _gcHandle.Free();
                }
            }

            private void InitializePipeline(IEnumerable<AudioWorkerBlueprint> blueprints)
            {
                if (blueprints == null)
                {
                    return;
                }

                foreach (var bp in blueprints)
                {
                    if (bp == null || _workerMap.ContainsKey(bp))
                    {
                        continue;
                    }

                    var worker = bp.Create();
                    _workerMap[bp] = worker;
                    _blueprints.Add(bp);
                    _audioPipeline.AddWorker(worker);
                }
            }

            private void InitializeNativeDevice()
            {
                lock (_sessionLock)
                {
                    InitializeNativeDeviceCore();
                }
            }

            private void InitializeNativeDeviceCore()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(RecordingSession));
                }

                _deviceIdHandle = MicDevice.AllocateDeviceIdHandle();

                GCHandle subHandle = default;
                GCHandle dtoHandle = default;
                IntPtr config;

                try
                {
                    try
                    {
                        config = Native.AllocateDeviceConfigEx(
                            Native.DeviceType.Record,
                            Native.SampleFormat.F32,
                            _channelCount,
                            _sampleRate,
                            s_staticAudioCallbackEx,
                            GCHandle.ToIntPtr(_gcHandle),
                            IntPtr.Zero,
                            _deviceIdHandle);
                        _usingUserDataCallback = true;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _usingUserDataCallback = false;
                        config = CreateLegacyDeviceConfig(out subHandle, out dtoHandle);
                    }

                    if (config == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Unable to allocate device config.");
                    }

                    _deviceConfig = config;
                    _devicePtr = Native.AllocateDevice();
                    if (_devicePtr == IntPtr.Zero)
                    {
                        throw new InvalidOperationException("Unable to allocate device.");
                    }

                    var initResult = Native.DeviceInit(_context, _deviceConfig, _devicePtr);
                    if (initResult != Native.Result.Success)
                    {
                        throw new InvalidOperationException($"Unable to init device. {initResult}");
                    }

                    var startResult = Native.DeviceStart(_devicePtr);
                    if (startResult != Native.Result.Success)
                    {
                        throw new InvalidOperationException($"Unable to start device. {startResult}");
                    }

                    if (!_usingUserDataCallback)
                    {
                        s_devicePtrSessionMap[_devicePtr] = this;
                    }

                    _audioPipeline.Initialize(_state);

                    try
                    {
                        Debug.Log($"EasyMic: Capture started on '{MicDevice.Name}' at {_sampleRate} Hz, {_channelCount} ch.");
                    }
                    catch { }
                }
                finally
                {
                    if (dtoHandle.IsAllocated) { dtoHandle.Free(); }
                    if (subHandle.IsAllocated) { subHandle.Free(); }
                }
            }

            private IntPtr CreateLegacyDeviceConfig(out GCHandle subHandle, out GCHandle dtoHandle)
            {
                subHandle = default;
                dtoHandle = default;

                var sub = new Sf_DeviceSubConfig
                {
                    format = (int)Native.SampleFormat.F32,
                    channels = _channelCount,
                    pDeviceID = _deviceIdHandle,
                    shareMode = 0
                };

                subHandle = GCHandle.Alloc(sub, GCHandleType.Pinned);

                var dto = new Sf_DeviceConfig
                {
                    periodSizeInFrames = 0,
                    periodSizeInMilliseconds = 0,
                    periods = 0,
                    noPreSilencedOutputBuffer = 0,
                    noClip = 0,
                    noDisableDenormals = 0,
                    noFixedSizedCallback = 0,
                    playback = IntPtr.Zero,
                    capture = subHandle.AddrOfPinnedObject(),
                    wasapi = IntPtr.Zero,
                    coreaudio = IntPtr.Zero,
                    alsa = IntPtr.Zero,
                    pulse = IntPtr.Zero,
                    opensl = IntPtr.Zero,
                    aaudio = IntPtr.Zero
                };

                dtoHandle = GCHandle.Alloc(dto, GCHandleType.Pinned);

                return Native.AllocateDeviceConfig(
                    Native.Capability.Record,
                    _sampleRate,
                    s_staticAudioCallback,
                    dtoHandle.AddrOfPinnedObject());
            }

            private void ApplyFormat(MicDevice device, SampleRate sampleRate, Channel channel)
            {
                MicDevice = device;
                SampleRate = sampleRate;
                Channel = channel;

                _channelCount = Math.Max(1u, (uint)channel);
                _sampleRate = Math.Max(1u, (uint)sampleRate);
                _state.ChannelCount = (int)_channelCount;
                _state.SampleRate = (int)_sampleRate;
                _state.Length = Math.Max(0, checked((int)(_channelCount * _sampleRate)));
            }

            private void ReleaseNativeResources()
            {
                if (_devicePtr != IntPtr.Zero)
                {
                    try { Native.DeviceStop(_devicePtr); } catch { }
                    try { Native.DeviceUninit(_devicePtr); } catch { }
                    try { Native.Free(_devicePtr); } catch { }
                    if (!_usingUserDataCallback)
                    {
                        s_devicePtrSessionMap.TryRemove(_devicePtr, out _);
                    }
                    _devicePtr = IntPtr.Zero;
                }

                if (_deviceConfig != IntPtr.Zero)
                {
                    try { Native.Free(_deviceConfig); } catch { }
                    _deviceConfig = IntPtr.Zero;
                }

                if (_deviceIdHandle != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(_deviceIdHandle); } catch { }
                    _deviceIdHandle = IntPtr.Zero;
                }

                _usingUserDataCallback = false;
            }

            [MonoPInvokeCallback(typeof(Native.AudioCallback))]
            private static void StaticAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                if (s_devicePtrSessionMap.TryGetValue(device, out var session))
                {
                    session.HandleAudioCallback(device, output, input, length);
                }
            }

            [MonoPInvokeCallback(typeof(Native.AudioCallbackEx))]
            private static void StaticAudioCallbackEx(IntPtr device, IntPtr output, IntPtr input, uint length, IntPtr userData)
            {
                if (userData == IntPtr.Zero)
                {
                    StaticAudioCallback(device, output, input, length);
                    return;
                }

                var handle = GCHandle.FromIntPtr(userData);
                if (handle.Target is RecordingSession session && !session._disposed)
                {
                    session.HandleAudioCallback(device, output, input, length);
                }
            }

            private void HandleAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                if (_disposed)
                {
                    return;
                }

                int sampleCount = (int)(length * _channelCount);
                _state.ChannelCount = (int)_channelCount;
                _state.SampleRate = (int)_sampleRate;
                _state.Length = sampleCount;

                ProcessAudioBuffer(GetSpan<float>(input, sampleCount), _state);
            }

            private void ProcessAudioBuffer(Span<float> buffer, AudioState state)
            {
                _audioPipeline.OnAudioPass(buffer, state);
            }

            private unsafe Span<T> GetSpan<T>(IntPtr ptr, int length) where T : unmanaged
            {
                return new Span<T>((void*)ptr, length);
            }
        }
    }
}
