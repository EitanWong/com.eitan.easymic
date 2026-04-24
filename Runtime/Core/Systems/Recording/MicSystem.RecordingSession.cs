using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        private sealed class RecordingSession : IDisposable
        {
            private static readonly Native.AudioCallback s_staticAudioCallback = StaticAudioCallback;
            private const int MaxLegacyCallbackSessions = 16;
            private static readonly IntPtr s_legacyPendingKey = new IntPtr(-1);
            private static readonly IntPtr[] s_legacyDevicePtrs = new IntPtr[MaxLegacyCallbackSessions];
            private static readonly RecordingSession[] s_legacySessions = new RecordingSession[MaxLegacyCallbackSessions];

            private readonly object _sessionLock = new object();
            private readonly Dictionary<AudioWorkerBlueprint, IAudioWorker> _workerMap = new Dictionary<AudioWorkerBlueprint, IAudioWorker>();
            private readonly List<AudioWorkerBlueprint> _blueprints = new List<AudioWorkerBlueprint>();
            private readonly AudioPipeline _audioPipeline = new AudioPipeline();
            private readonly IntPtr _context;
            private readonly AudioContext _state;
            private readonly MicDevice _preferredDevice;
            private readonly string _preferredIdentifier;
            private readonly SampleRate _requestedSampleRate;
            private readonly Channel _requestedChannel;
            private bool _usingFallback;

            private bool _disposed;
            private IntPtr _devicePtr;
            private IntPtr _deviceConfig;
            private IntPtr _deviceIdHandle;
            private uint _channelCount;
            private uint _sampleRate;

            public MicDevice MicDevice { get; private set; }
            public SampleRate SampleRate { get; private set; }
            public Channel Channel { get; private set; }

            public int ProcessorCount => _audioPipeline.WorkerCount;
            private ILogger _logger;

            public RecordingSession(IntPtr context, MicDevice device, SampleRate sampleRate, Channel channel, IEnumerable<AudioWorkerBlueprint> blueprints, ILogger logger)
            {
                _context = context;
                _state = new AudioContext((int)channel, (int)sampleRate, Math.Max(1, (int)channel * (int)sampleRate));
                _preferredDevice = device;
                _preferredIdentifier = device.GetIdentifier();
                _requestedSampleRate = sampleRate;
                _requestedChannel = channel;
                _usingFallback = false;
                _logger = logger;

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
                        Log($"EasyMic: Recording session switched to '{MicDevice.Name}' at {_sampleRate} Hz, {_channelCount} ch.", LogLevel.Info);
                        _usingFallback = !MicDevice.SameIdentityAs(_preferredDevice);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to switch capture session to '{targetDevice.Name}'. {ex.Message}", LogLevel.Error);
                        ReleaseNativeResources();
                        ApplyFormat(previousDevice, previousRate, previousChannel);
                        _usingFallback = previousFallback;
                        try
                        {
                            InitializeNativeDeviceCore();
                        }
                        catch (Exception restoreEx)
                        {
                            Log($"Failed to restore capture session on '{previousDevice.Name}'. {restoreEx.Message}", LogLevel.Error);
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

            public bool IsSameDevice(MicDevice device)
            {
                return MicDevice.SameIdentityAs(device);
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

                    Volatile.Write(ref _disposed, true);
                    ReleaseNativeResources();
                }

                try
                {
                    _audioPipeline.Dispose();
                }
                catch { }

                _workerMap.Clear();
                _blueprints.Clear();

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

                _deviceConfig = Native.AllocateDeviceConfig(
                    Native.DeviceType.Record,
                    Native.SampleFormat.F32,
                    _channelCount,
                    _sampleRate,
                    IntPtr.Zero,
                    _deviceIdHandle,
                    s_staticAudioCallback,
                    out _);
                if (_deviceConfig == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unable to allocate device config.");
                }

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

                RegisterLegacyCallbackSession(_devicePtr, this);

                var startResult = Native.DeviceStart(_devicePtr);
                if (startResult != Native.Result.Success)
                {
                    throw new InvalidOperationException($"Unable to start device. {startResult}");
                }

                _audioPipeline.Initialize(_state);

                Log($"Capture started on '{MicDevice.Name}' at {_sampleRate} Hz, {_channelCount} ch.", LogLevel.Info);
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
                    var oldDevicePtr = _devicePtr;
                    UnregisterLegacyCallbackSession(oldDevicePtr);
                    try { Native.DeviceStop(_devicePtr); } catch { }
                    try { Native.DeviceUninit(_devicePtr); } catch { }
                    try { Native.Free(_devicePtr); } catch { }
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
            }

#if UNITY_IOS || UNITY_ANDROID || ENABLE_IL2CPP
            [AOT.MonoPInvokeCallback(typeof(Native.AudioCallback))]
#endif
            private static void StaticAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                var session = FindLegacyCallbackSession(device);
                if (session != null)
                {
                    session.HandleAudioCallback(device, output, input, length);
                }
            }

            private void HandleAudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
            {
                using var _ = EasyMicThreading.EnterAudioThread();

                if (Volatile.Read(ref _disposed) || input == IntPtr.Zero)
                {
                    return;
                }

                ulong sampleCountU = (ulong)length * (ulong)_channelCount;
                if (sampleCountU == 0 || sampleCountU > int.MaxValue)
                {
                    return;
                }

                int sampleCount = (int)sampleCountU;
                _state.ChannelCount = (int)_channelCount;
                _state.SampleRate = (int)_sampleRate;
                _state.Length = sampleCount;

                try
                {
                    ProcessAudioBuffer(GetSpan<float>(input, sampleCount), _state);
                }
                catch { }
            }

            private void ProcessAudioBuffer(Span<float> buffer, AudioContext state)
            {
                _audioPipeline.OnAudioPass(buffer, state);
            }

            private unsafe Span<T> GetSpan<T>(IntPtr ptr, int length) where T : unmanaged
            {
                return new Span<T>((void*)ptr, length);
            }

            private static void RegisterLegacyCallbackSession(IntPtr devicePtr, RecordingSession session)
            {
                if (devicePtr == IntPtr.Zero || session == null)
                {
                    return;
                }

                for (int i = 0; i < MaxLegacyCallbackSessions; i++)
                {
                    var key = Volatile.Read(ref s_legacyDevicePtrs[i]);
                    if (key == devicePtr)
                    {
                        Volatile.Write(ref s_legacySessions[i], session);
                        return;
                    }

                    if (key != IntPtr.Zero)
                    {
                        continue;
                    }

                    if (Interlocked.CompareExchange(ref s_legacyDevicePtrs[i], s_legacyPendingKey, IntPtr.Zero) == IntPtr.Zero)
                    {
                        Volatile.Write(ref s_legacySessions[i], session);
                        Volatile.Write(ref s_legacyDevicePtrs[i], devicePtr);
                        return;
                    }
                }

                throw new InvalidOperationException("EasyMic: Too many concurrent recording sessions for legacy callback registry.");
            }

            private static void UnregisterLegacyCallbackSession(IntPtr devicePtr)
            {
                if (devicePtr == IntPtr.Zero)
                {
                    return;
                }

                for (int i = 0; i < MaxLegacyCallbackSessions; i++)
                {
                    if (Volatile.Read(ref s_legacyDevicePtrs[i]) != devicePtr)
                    {
                        continue;
                    }

                    Volatile.Write(ref s_legacySessions[i], null);
                    Volatile.Write(ref s_legacyDevicePtrs[i], IntPtr.Zero);
                    return;
                }
            }

            private static RecordingSession FindLegacyCallbackSession(IntPtr devicePtr)
            {
                if (devicePtr == IntPtr.Zero)
                {
                    return null;
                }

                for (int i = 0; i < MaxLegacyCallbackSessions; i++)
                {
                    if (Volatile.Read(ref s_legacyDevicePtrs[i]) == devicePtr)
                    {
                        return Volatile.Read(ref s_legacySessions[i]);
                    }
                }

                return null;
            }


            #region Logger
            private void Log(string message, LogLevel type)
            {
                if (this._logger == null)
                {
                    this._logger = new Mono.UnityLogger();
                    // throw new NullReferenceException("MicSystem has no logger set up. Set a logger first.");
                }
                switch (type)
                {
                    case LogLevel.Info:
                        _logger.LogInfo(message);
                        break;
                    case LogLevel.Warning:
                        _logger.LogWarning(message);
                        break;
                    case LogLevel.Error:
                        _logger.LogError(message);
                        break;
                    default:
                        throw new System.Exception($"{type} logtype not support.");
                }
            }
            #endregion
        }

    }
}
