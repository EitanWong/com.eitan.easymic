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
            private static readonly HotState[] s_legacyHotStates = new HotState[MaxLegacyCallbackSessions];
            private static readonly int[] s_legacyGenerations = new int[MaxLegacyCallbackSessions];
            private static int s_nextGeneration;

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
            private readonly EasyMicLatencyProfile _latencyProfile;
            private readonly RealtimeAudioTelemetry _telemetry = new RealtimeAudioTelemetry();
            private bool _usingFallback;

            private bool _disposed;
            private IntPtr _devicePtr;
            private IntPtr _deviceConfig;
            private IntPtr _deviceIdHandle;
            private CaptureAudioTransport _captureTransport;
            private HotState _hotState;
            private uint _channelCount;
            private uint _sampleRate;
            private long _nativeCallbackCount;
            private long _nativeInputNullCount;
            private long _nativeOutputNullCount;
            private long _nativeNonZeroCallbackCount;
            private long _nativeNonZeroByteCallbackCount;
            private long _nativeNonZeroOutputByteCallbackCount;
            private int _lastRawInputPeakPpm;
            private int _maxRawInputPeakPpm;
            private int _lastRawInputNonZeroBytes;
            private int _maxRawInputNonZeroBytes;
            private int _lastRawOutputNonZeroBytes;
            private int _maxRawOutputNonZeroBytes;
            private int _callbackDiagnosticsEnabled;

            private sealed class HotState
            {
                internal CaptureAudioTransport Transport;
                internal RealtimeAudioTelemetry Telemetry;
                internal uint ChannelCount;
                internal int Generation;
                internal int Stopping;
                internal int Disposed;
                internal int ActiveCallbacks;
                internal int CallbackDiagnosticsEnabled;
                internal long NativeCallbackCount;
                internal long NativeInputNullCount;
                internal long NativeOutputNullCount;
                internal long NativeNonZeroCallbackCount;
            }

            public MicDevice MicDevice { get; private set; }
            public SampleRate SampleRate { get; private set; }
            public Channel Channel { get; private set; }

            public int ProcessorCount => _audioPipeline.WorkerCount;
            private ILogger _logger;

            public RecordingSession(
                IntPtr context,
                MicDevice device,
                SampleRate sampleRate,
                Channel channel,
                IEnumerable<AudioWorkerBlueprint> blueprints,
                ILogger logger,
                bool callbackDiagnosticsEnabled,
                EasyMicLatencyProfile latencyProfile)
            {
                _context = context;
                _state = new AudioContext((int)channel, (int)sampleRate, Math.Max(1, (int)channel * (int)sampleRate));
                _preferredDevice = device;
                _preferredIdentifier = device.GetIdentifier();
                _requestedSampleRate = sampleRate;
                _requestedChannel = channel;
                _latencyProfile = latencyProfile;
                _usingFallback = false;
                _logger = logger;
                _callbackDiagnosticsEnabled = callbackDiagnosticsEnabled ? 1 : 0;

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

            public void SetCallbackDiagnosticsEnabled(bool enabled)
            {
                Volatile.Write(ref _callbackDiagnosticsEnabled, enabled ? 1 : 0);
                var hot = Volatile.Read(ref _hotState);
                if (hot != null)
                {
                    Volatile.Write(ref hot.CallbackDiagnosticsEnabled, enabled ? 1 : 0);
                }
            }

            public RecordingInfo GetInfo()
            {
                var hot = Volatile.Read(ref _hotState);
                return new RecordingInfo(
                    MicDevice,
                    SampleRate,
                    Channel,
                    true,
                    ProcessorCount,
                    hot != null ? Interlocked.Read(ref hot.NativeCallbackCount) : Interlocked.Read(ref _nativeCallbackCount),
                    hot != null ? Interlocked.Read(ref hot.NativeInputNullCount) : Interlocked.Read(ref _nativeInputNullCount),
                    hot != null ? Interlocked.Read(ref hot.NativeOutputNullCount) : Interlocked.Read(ref _nativeOutputNullCount),
                    hot != null ? Interlocked.Read(ref hot.NativeNonZeroCallbackCount) : Interlocked.Read(ref _nativeNonZeroCallbackCount),
                    Interlocked.Read(ref _nativeNonZeroByteCallbackCount),
                    Interlocked.Read(ref _nativeNonZeroOutputByteCallbackCount),
                    PeakPpmToFloat(Volatile.Read(ref _lastRawInputPeakPpm)),
                    PeakPpmToFloat(Volatile.Read(ref _maxRawInputPeakPpm)),
                    Volatile.Read(ref _lastRawInputNonZeroBytes),
                    Volatile.Read(ref _maxRawInputNonZeroBytes),
                    Volatile.Read(ref _lastRawOutputNonZeroBytes),
                    Volatile.Read(ref _maxRawOutputNonZeroBytes),
                    _telemetry.GetPublicSnapshot(),
                    _latencyProfile);
            }

            public EasyMicRecordingPipelineSnapshot GetPipelineSnapshot(RecordingHandle handle)
            {
                return new EasyMicRecordingPipelineSnapshot(
                    handle,
                    GetInfo(),
                    _latencyProfile,
                    _usingFallback,
                    _audioPipeline.GetProcessorSnapshots());
            }

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

                int generation = Interlocked.Increment(ref s_nextGeneration);
                _deviceConfig = Native.AllocateDeviceConfig(
                    Native.DeviceType.Record,
                    Native.SampleFormat.F32,
                    _channelCount,
                    _sampleRate,
                    IntPtr.Zero,
                    _deviceIdHandle,
                    s_staticAudioCallback,
                    _latencyProfile,
                    new IntPtr(generation),
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

                _audioPipeline.Initialize(_state);
                _captureTransport = new CaptureAudioTransport(
                    _audioPipeline,
                    (int)_channelCount,
                    (int)_sampleRate,
                    _latencyProfile,
                    _telemetry);
                _hotState = new HotState
                {
                    Transport = _captureTransport,
                    Telemetry = _telemetry,
                    ChannelCount = _channelCount,
                    Generation = generation,
                    CallbackDiagnosticsEnabled = Volatile.Read(ref _callbackDiagnosticsEnabled)
                };
                RegisterLegacyCallbackSession(_devicePtr, _hotState);

                var startResult = Native.DeviceStart(_devicePtr);
                if (startResult != Native.Result.Success)
                {
                    throw new InvalidOperationException($"Unable to start device. {startResult}");
                }

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
                    var hot = Volatile.Read(ref _hotState);
                    if (hot != null)
                    {
                        Volatile.Write(ref hot.Stopping, 1);
                    }

                    try { Native.DeviceStop(_devicePtr); } catch { }
                    WaitForCallbacksToDrain(hot, 250);
                    UnregisterLegacyCallbackSession(oldDevicePtr, hot);
                    if (hot != null)
                    {
                        Volatile.Write(ref hot.Disposed, 1);
                        hot.Transport = null;
                    }

                    try { _captureTransport?.Dispose(); } catch { }
                    _captureTransport = null;
                    _hotState = null;
                    try { Native.DeviceUninit(_devicePtr); } catch { }
                    try { Native.Free(_devicePtr); } catch { }
                    _devicePtr = IntPtr.Zero;
                }
                else
                {
                    var hot = Volatile.Read(ref _hotState);
                    if (hot != null)
                    {
                        Volatile.Write(ref hot.Stopping, 1);
                        WaitForCallbacksToDrain(hot, 250);
                        Volatile.Write(ref hot.Disposed, 1);
                        hot.Transport = null;
                    }

                    try { _captureTransport?.Dispose(); } catch { }
                    _captureTransport = null;
                    _hotState = null;
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
                var hot = FindLegacyCallbackHotState(device);
                HandleAudioCallback(hot, output, input, length);
            }

            private static void HandleAudioCallback(HotState hot, IntPtr output, IntPtr input, uint length)
            {
                using var _ = EasyMicThreading.EnterAudioThread();
                if (hot == null)
                {
                    return;
                }

                Interlocked.Increment(ref hot.ActiveCallbacks);
                long callbackStart = hot.Telemetry != null ? hot.Telemetry.BeginCallback() : 0;
                Interlocked.Increment(ref hot.NativeCallbackCount);
                try
                {
                    if (output == IntPtr.Zero)
                    {
                        Interlocked.Increment(ref hot.NativeOutputNullCount);
                    }

                    if (Volatile.Read(ref hot.Stopping) != 0 || Volatile.Read(ref hot.Disposed) != 0 || input == IntPtr.Zero)
                    {
                        if (input == IntPtr.Zero)
                        {
                            Interlocked.Increment(ref hot.NativeInputNullCount);
                        }
                        return;
                    }

                    uint channelCount = hot.ChannelCount;
                    ulong sampleCountU = (ulong)length * (ulong)channelCount;
                    if (sampleCountU == 0 || sampleCountU > int.MaxValue)
                    {
                        return;
                    }

                    int sampleCount = (int)sampleCountU;
                    var transport = Volatile.Read(ref hot.Transport);
                    if (transport == null)
                    {
                        return;
                    }

                    unsafe
                    {
                        var inputSamples = new ReadOnlySpan<float>((void*)input, sampleCount);
                        if (!transport.TryWrite(inputSamples, (int)length))
                        {
                            return;
                        }
                    }

                    if (Volatile.Read(ref hot.CallbackDiagnosticsEnabled) != 0)
                    {
                        Interlocked.Increment(ref hot.NativeNonZeroCallbackCount);
                    }
                }
                catch
                {
                    hot.Telemetry?.IncrementCallbackException();
                }
                finally
                {
                    hot.Telemetry?.EndCallback(callbackStart);
                    Interlocked.Decrement(ref hot.ActiveCallbacks);
                }
            }

            private void ProcessAudioBuffer(Span<float> buffer, AudioContext state)
            {
                _audioPipeline.OnAudioPass(buffer, state);
            }

            private void UpdateRawInputByteStats(Span<byte> bytes)
            {
                int nonZeroBytes = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] != 0)
                    {
                        nonZeroBytes++;
                    }
                }

                Volatile.Write(ref _lastRawInputNonZeroBytes, nonZeroBytes);
                if (nonZeroBytes > 0)
                {
                    Interlocked.Increment(ref _nativeNonZeroByteCallbackCount);
                }

                int currentMax;
                do
                {
                    currentMax = Volatile.Read(ref _maxRawInputNonZeroBytes);
                    if (nonZeroBytes <= currentMax)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref _maxRawInputNonZeroBytes, nonZeroBytes, currentMax) != currentMax);
            }

            private void UpdateRawOutputByteStats(Span<byte> bytes)
            {
                int nonZeroBytes = 0;
                for (int i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] != 0)
                    {
                        nonZeroBytes++;
                    }
                }

                Volatile.Write(ref _lastRawOutputNonZeroBytes, nonZeroBytes);
                if (nonZeroBytes > 0)
                {
                    Interlocked.Increment(ref _nativeNonZeroOutputByteCallbackCount);
                }

                int currentMax;
                do
                {
                    currentMax = Volatile.Read(ref _maxRawOutputNonZeroBytes);
                    if (nonZeroBytes <= currentMax)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref _maxRawOutputNonZeroBytes, nonZeroBytes, currentMax) != currentMax);
            }

            private void UpdateRawInputPeak(Span<float> samples)
            {
                float peak = 0f;
                for (int i = 0; i < samples.Length; i++)
                {
                    float value = samples[i];
                    float abs = value < 0f ? -value : value;
                    if (abs > peak)
                    {
                        peak = abs;
                    }
                }

                int peakPpm = FloatToPeakPpm(peak);
                Volatile.Write(ref _lastRawInputPeakPpm, peakPpm);
                if (peakPpm > 0)
                {
                    Interlocked.Increment(ref _nativeNonZeroCallbackCount);
                }

                int currentMax;
                do
                {
                    currentMax = Volatile.Read(ref _maxRawInputPeakPpm);
                    if (peakPpm <= currentMax)
                    {
                        return;
                    }
                }
                while (Interlocked.CompareExchange(ref _maxRawInputPeakPpm, peakPpm, currentMax) != currentMax);
            }

            private static int FloatToPeakPpm(float peak)
            {
                if (peak <= 0f)
                {
                    return 0;
                }

                if (peak >= 1f)
                {
                    return 1000000;
                }

                return (int)(peak * 1000000f);
            }

            private static float PeakPpmToFloat(int peakPpm)
            {
                return peakPpm <= 0 ? 0f : peakPpm / 1000000f;
            }

            private static void RegisterLegacyCallbackSession(IntPtr devicePtr, HotState hotState)
            {
                if (devicePtr == IntPtr.Zero || hotState == null)
                {
                    return;
                }

                for (int i = 0; i < MaxLegacyCallbackSessions; i++)
                {
                    var key = Volatile.Read(ref s_legacyDevicePtrs[i]);
                    if (key == devicePtr)
                    {
                        Volatile.Write(ref s_legacyGenerations[i], hotState.Generation);
                        Volatile.Write(ref s_legacyHotStates[i], hotState);
                        return;
                    }

                    if (key != IntPtr.Zero)
                    {
                        continue;
                    }

                    if (Interlocked.CompareExchange(ref s_legacyDevicePtrs[i], s_legacyPendingKey, IntPtr.Zero) == IntPtr.Zero)
                    {
                        Volatile.Write(ref s_legacyGenerations[i], hotState.Generation);
                        Volatile.Write(ref s_legacyHotStates[i], hotState);
                        Volatile.Write(ref s_legacyDevicePtrs[i], devicePtr);
                        return;
                    }
                }

                throw new InvalidOperationException("EasyMic: Too many concurrent recording sessions for legacy callback registry.");
            }

            private static void UnregisterLegacyCallbackSession(IntPtr devicePtr, HotState expected)
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

                    if (expected != null && !ReferenceEquals(Volatile.Read(ref s_legacyHotStates[i]), expected))
                    {
                        return;
                    }

                    Volatile.Write(ref s_legacyHotStates[i], null);
                    Volatile.Write(ref s_legacyGenerations[i], 0);
                    Volatile.Write(ref s_legacyDevicePtrs[i], IntPtr.Zero);
                    return;
                }
            }

            private static HotState FindLegacyCallbackHotState(IntPtr devicePtr)
            {
                if (devicePtr == IntPtr.Zero)
                {
                    return null;
                }

                for (int i = 0; i < MaxLegacyCallbackSessions; i++)
                {
                    if (Volatile.Read(ref s_legacyDevicePtrs[i]) == devicePtr)
                    {
                        var hot = Volatile.Read(ref s_legacyHotStates[i]);
                        if (hot == null)
                        {
                            return null;
                        }

                        int generation = Volatile.Read(ref s_legacyGenerations[i]);
                        if (generation == 0 || generation != Volatile.Read(ref hot.Generation))
                        {
                            return null;
                        }

                        if (Volatile.Read(ref hot.Disposed) != 0)
                        {
                            return null;
                        }

                        return hot;
                    }
                }

                return null;
            }

            private static void WaitForCallbacksToDrain(HotState hot, int timeoutMs)
            {
                if (hot == null)
                {
                    return;
                }

                int waited = 0;
                while (Volatile.Read(ref hot.ActiveCallbacks) > 0 && waited < timeoutMs)
                {
                    Thread.Sleep(1);
                    waited++;
                }
            }

            [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetStaticCallbackRegistry()
            {
                for (int i = 0; i < MaxLegacyCallbackSessions; i++)
                {
                    Volatile.Write(ref s_legacyHotStates[i], null);
                    Volatile.Write(ref s_legacyGenerations[i], 0);
                    Volatile.Write(ref s_legacyDevicePtrs[i], IntPtr.Zero);
                }

                Volatile.Write(ref s_nextGeneration, 0);
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
