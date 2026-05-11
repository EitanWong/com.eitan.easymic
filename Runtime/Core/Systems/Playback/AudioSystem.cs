using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Miniaudio-backed audio output system. Manages a single playback device,
    /// a list of playback sources, and performs real-time mixing in the device callback.
    /// Exposes an event for the final mixed frame (for AEC far-end reference).
    /// </summary>
    public sealed class AudioSystem : IDisposable
    {
        public delegate void MixedFrameHandler(ReadOnlySpan<float> interleaved, int channels, int sampleRate);
        public event MixedFrameHandler OnMixedFrame;
        internal event MixedFrameHandler OnMixedFrameRaw;

        private static readonly object s_lock = new object();
        private static AudioSystem s_instance;
        private static HotState s_hotState;
        public static AudioSystem Instance
        {
            get
            {
                lock (s_lock)
                {
                    if (s_instance == null)
                    {
                        s_instance = new AudioSystem();
                    }

                    return s_instance;
                }
            }
        }

        private AudioMixer _masterMixer;

        private uint _sampleRate = 48000;
        private uint _channels = 2;

        // miniaudio state
        private IntPtr _context = IntPtr.Zero;
        private Native.NativeAllocationSource _contextAllocationSource;
        private IntPtr _device = IntPtr.Zero;
        private IntPtr _deviceConfig = IntPtr.Zero;
#pragma warning disable 0414
        private Native.AudioCallback _cb;
#pragma warning restore 0414
        private PlaybackRenderTransport _renderTransport;
        private HotState _hotState;
        private bool _running;
        private bool _setRunInBackground;
        private bool _previousRunInBackground;
        private readonly RealtimeAudioTelemetry _telemetry = new RealtimeAudioTelemetry();
        private EasyMicLatencyProfile _latencyProfile = EasyMicLatencyProfile.LowLatency;

        private sealed class HotState
        {
            internal PlaybackRenderTransport Transport;
            internal RealtimeAudioTelemetry Telemetry;
            internal int Channels;
            internal int Stopping;
            internal int Disposed;
            internal int ActiveCallbacks;
        }

        // Diagnostics meters
        private float[] _peak; // per-channel
        private float[] _rms;  // per-channel
        private int _meteringEnabled;

        private AudioSystem() { }

        public void Configure(uint sampleRate, uint channels)
        {
            if (_running)
            {
                return; // must stop before reconfigure
            }


            _sampleRate = Math.Max(8000u, sampleRate);
            _channels = Math.Max(1u, channels);
        }

        /// <summary>
        /// Prefer native device format; will be resolved on Start() using Unity output hints and fallbacks.
        /// </summary>
        public void PreferNativeFormat()
        {
            if (_running)
            {
                return;
            }


            _sampleRate = 0;
            _channels = 0;
        }

        public bool Start()
        {
            if (_running)
            {
                return true;
            }

            try
            {
                EasyMicRuntimeSettings.ApplyTo(this);
                try
                {
                    EasyMicAudioEventPump.SetMainThreadContext(SynchronizationContext.Current);
                    EasyMicAudioEventPump.SetAudioSystem(this);
                }
                catch { }

                if (_sampleRate == 0 || _channels == 0)
                {
                    TryPickUnityOutputFormat(out _sampleRate, out _channels);
                }

                _masterMixer = new AudioMixer();
                _masterMixer.Initialize((int)_channels, (int)_sampleRate);
                _masterMixer.name = "Master";
                _masterMixer.Pipeline.AddWorker(new SoftLimiter());

                _context = Native.AllocateContext(out _contextAllocationSource);
                Check(_context != IntPtr.Zero, "AllocateContext");
                var initContext = Native.ContextInit(IntPtr.Zero, 0, IntPtr.Zero, _context);
                Check(initContext == Native.Result.Success, $"ContextInit: {initContext}");

                if (!TryAllocatePlaybackDevice(_sampleRate, _channels))
                {
                    TryFallbackFormats();
                }

                _masterMixer.Initialize((int)_channels, (int)_sampleRate);
                if (Volatile.Read(ref _meteringEnabled) != 0)
                {
                    EnsureMeterBuffers((int)_channels);
                }

                _renderTransport = new PlaybackRenderTransport(
                    _masterMixer,
                    (int)_channels,
                    (int)_sampleRate,
                    _latencyProfile,
                    _telemetry,
                    DispatchMixedFrameRaw,
                    DispatchMixedFrameFromTransport);
                _hotState = new HotState
                {
                    Transport = _renderTransport,
                    Telemetry = _telemetry,
                    Channels = (int)_channels
                };
                Volatile.Write(ref s_hotState, _hotState);

                var startResult = Native.DeviceStart(_device);
                Check(startResult == Native.Result.Success, $"DeviceStart: {startResult}");

                try
                {
                    _previousRunInBackground = UnityEngine.Application.runInBackground;
                    if (!_previousRunInBackground)
                    {
                        UnityEngine.Application.runInBackground = true;
                        _setRunInBackground = true;
                    }
                }
                catch { _setRunInBackground = false; }

                try { UnityEngine.Application.quitting += OnApplicationQuitting; } catch { }
                _running = true;
                return true;
            }
            catch
            {
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                try { UnityEngine.Application.quitting -= OnApplicationQuitting; } catch { }
                var hot = Volatile.Read(ref _hotState);
                if (hot != null)
                {
                    Volatile.Write(ref hot.Stopping, 1);
                }

                if (_device != IntPtr.Zero)
                {
                    try { Native.DeviceStop(_device); } catch { }
                }
                WaitForCallbacksToDrain(hot, 250);
                if (ReferenceEquals(Volatile.Read(ref s_hotState), hot))
                {
                    Volatile.Write(ref s_hotState, null);
                }

                if (hot != null)
                {
                    Volatile.Write(ref hot.Disposed, 1);
                    hot.Transport = null;
                }

                try { _renderTransport?.Dispose(); } catch { }
                _renderTransport = null;
                _hotState = null;
            }
            finally
            {
                ReleaseDeviceHandles();
                try { if (_context != IntPtr.Zero) { Native.ContextUninit(_context); } } catch { }
                if (_context != IntPtr.Zero) { try { Native.FreeAllocated(_context, _contextAllocationSource); } catch { } _context = IntPtr.Zero; }
                if (_setRunInBackground)
                {
                    try { UnityEngine.Application.runInBackground = _previousRunInBackground; } catch { }
                    _setRunInBackground = false;
                }
                // Clear event handlers to avoid leaks to user delegates

                OnMixedFrame = null;
                OnMixedFrameRaw = null;
                // Dispose mixer and release references to sources/pipelines
                try { _masterMixer?.Dispose(); } catch { }
                _masterMixer = null;
                _cb = null;
                _running = false;
            }
        }

        private void OnApplicationQuitting()
        {
            try { Stop(); } catch { }
        }

        public void Dispose()
        {
            Stop();
        }

        public uint SampleRate => _sampleRate;
        public uint Channels => _channels;
        public bool IsRunning => _running;
        public AudioMixer MasterMixer => _masterMixer;
        public EasyMicTelemetrySnapshot Telemetry => _telemetry.GetPublicSnapshot();
        public EasyMicRealtimeStats RealtimeStats => new EasyMicRealtimeStats(Telemetry);
        public EasyMicLatencyStats LatencyStats => new EasyMicLatencyStats(_latencyProfile, _sampleRate, _channels, Telemetry);
        public EasyMicPlaybackPipelineSnapshot PipelineSnapshot => new EasyMicPlaybackPipelineSnapshot(
            _running,
            BackendName,
            DeviceName,
            _channels,
            _sampleRate,
            _latencyProfile,
            _telemetry.GetPublicSnapshot(),
            _masterMixer != null
                ? _masterMixer.GetPipelineSnapshot()
                : new EasyMicMixerSnapshot("Master", 1f, false, false, Array.Empty<EasyMicProcessorSnapshot>(), Array.Empty<EasyMicPlaybackSourceSnapshot>(), Array.Empty<EasyMicMixerSnapshot>()));
        public EasyMicLatencyProfile LatencyProfile
        {
            get => _latencyProfile;
            set
            {
                if (_running)
                {
                    throw new InvalidOperationException("Stop AudioSystem before changing latency profile.");
                }

                _latencyProfile = value;
            }
        }
        public bool MeteringEnabled
        {
            get => Volatile.Read(ref _meteringEnabled) != 0;
            set
            {
                Volatile.Write(ref _meteringEnabled, value ? 1 : 0);
                if (value)
                {
                    EnsureMeterBuffers((int)_channels);
                }
            }
        }
        public void AddSource(PlaybackAudioSource source) => _masterMixer?.AddSource(source);
        public void RemoveSource(PlaybackAudioSource source) => _masterMixer?.RemoveSource(source);
        internal void RecordEventQueueDrop() => _telemetry.IncrementEventQueueDrop();

        private static void Render(HotState hot, IntPtr output, uint frameCount)
        {
            using var _ = EasyMicThreading.EnterAudioThread();
            if (hot == null)
            {
                ZeroFill(output, frameCount, 2);
                return;
            }

            Interlocked.Increment(ref hot.ActiveCallbacks);
            long callbackStart = hot.Telemetry != null ? hot.Telemetry.BeginCallback() : 0;

            try
            {
                if (output == IntPtr.Zero)
                {
                    return;
                }

                int channels = Math.Max(1, hot.Channels);
                ulong samplesU = (ulong)frameCount * (ulong)channels;
                if (samplesU == 0 || samplesU > int.MaxValue)
                {
                    return;
                }

                int samples = (int)samplesU;
                unsafe
                {
                    var outSpan = new Span<float>((void*)output, samples);
                    if (Volatile.Read(ref hot.Stopping) != 0 || Volatile.Read(ref hot.Disposed) != 0)
                    {
                        outSpan.Clear();
                        hot.Telemetry?.AddZeroFilledFrames((int)frameCount);
                        return;
                    }

                    var transport = Volatile.Read(ref hot.Transport);
                    int read = transport?.ReadInto(outSpan) ?? 0;
                    if (read <= 0)
                    {
                        outSpan.Clear();
                        if (transport == null)
                        {
                            hot.Telemetry?.AddZeroFilledFrames((int)frameCount);
                        }
                    }
                }
            }
            catch
            {
                hot.Telemetry?.IncrementCallbackException();
                hot.Telemetry?.AddZeroFilledFrames((int)frameCount);
                ZeroFill(output, frameCount, Math.Max(1, hot.Channels));
            }
            finally
            {
                hot.Telemetry?.EndCallback(callbackStart);
                Interlocked.Decrement(ref hot.ActiveCallbacks);
            }
        }

#if UNITY_IOS || UNITY_ANDROID || ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(Native.AudioCallback))]
#endif
        private static void OnCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
        {
            Render(Volatile.Read(ref s_hotState), output, length);
        }

        private static unsafe void ZeroFill(IntPtr output, uint frameCount, int channels)
        {
            if (output == IntPtr.Zero)
            {
                return;
            }

            ulong samplesU = (ulong)frameCount * (ulong)Math.Max(1, channels);
            if (samplesU == 0 || samplesU > int.MaxValue)
            {
                return;
            }

            new Span<float>((void*)output, (int)samplesU).Clear();
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
        private static void ResetStatics()
        {
            Volatile.Write(ref s_hotState, null);
            lock (s_lock)
            {
                try { s_instance?.Stop(); } catch { }
                s_instance = null;
            }
            EasyMicAudioEventPump.Shutdown();
        }

        private static void Check(bool cond, string msg)
        {
            if (!cond)
            {
                throw new InvalidOperationException(msg);
            }

        }

        private bool TryAllocatePlaybackDevice(uint desiredSampleRate, uint desiredChannels)
        {
            ReleaseDeviceHandles();

            var rate = Math.Max(8000u, desiredSampleRate);
            var channels = Math.Max(1u, desiredChannels);

            _cb = OnCallback;
            IntPtr hotKey = new IntPtr(Environment.TickCount);

            _deviceConfig = Native.AllocateDeviceConfig(
                Native.DeviceType.Playback,
                Native.SampleFormat.F32,
                channels,
                rate,
                IntPtr.Zero,
                IntPtr.Zero,
                _cb,
                _latencyProfile,
                hotKey,
                out _);

            if (_deviceConfig == IntPtr.Zero)
                return false;

            _device = Native.AllocateDevice();
            if (_device == IntPtr.Zero)
            {
                if (_deviceConfig != IntPtr.Zero)
                {
                    try { Native.Free(_deviceConfig); } catch { }
                    _deviceConfig = IntPtr.Zero;
                }
                return false;
            }

            var initResult = Native.DeviceInit(_context, _deviceConfig, _device);
            if (initResult != Native.Result.Success)
            {
                try { Native.Free(_device); } catch { }
                _device = IntPtr.Zero;
                if (_deviceConfig != IntPtr.Zero)
                {
                    try { Native.Free(_deviceConfig); } catch { }
                    _deviceConfig = IntPtr.Zero;
                }
                return false;
            }

            _sampleRate = rate;
            _channels = channels;
            return true;
        }

        private void TryFallbackFormats()
        {
            var candidates = new (uint sr, uint ch)[]
            {
                (48000u, 2u), (44100u, 2u), (48000u, 1u), (44100u, 1u)
            };
            foreach (var c in candidates)
            {
                try
                {
                    if (TryAllocatePlaybackDevice(c.sr, c.ch))
                    {
                        _masterMixer?.Initialize((int)_channels, (int)_sampleRate);
                        return;
                    }
                }
                catch { }
            }
            throw new InvalidOperationException("Failed to initialize playback device with fallback formats.");
        }

        private void ReleaseDeviceHandles()
        {
            if (_device != IntPtr.Zero)
            {
                try { Native.DeviceUninit(_device); } catch { }
                try { Native.Free(_device); } catch { }
                _device = IntPtr.Zero;
            }

            if (_deviceConfig != IntPtr.Zero)
            {
                try { Native.Free(_deviceConfig); } catch { }
                _deviceConfig = IntPtr.Zero;
            }
        }

        private static void TryPickUnityOutputFormat(out uint sr, out uint ch)
        {
            sr = 48000; ch = 2;
            try
            {
                int uSr = UnityEngine.AudioSettings.outputSampleRate;
                var sm = UnityEngine.AudioSettings.speakerMode;
                sr = (uint)Math.Max(8000, uSr);
                ch = (uint)Math.Max(1, SpeakerModeToChannels(sm));
            }
            catch { }
        }

        private static int SpeakerModeToChannels(UnityEngine.AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case UnityEngine.AudioSpeakerMode.Mono: return 1;
                case UnityEngine.AudioSpeakerMode.Stereo: return 2;
                case UnityEngine.AudioSpeakerMode.Quad: return 4;
                case UnityEngine.AudioSpeakerMode.Surround: return 5;
                case UnityEngine.AudioSpeakerMode.Mode5point1: return 6;
                case UnityEngine.AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        private void UpdateMeters(Span<float> interleaved, int channels)
        {
            EnsureMeterBuffers(channels);
            var peak = _peak;
            var rms = _rms;
            if (channels <= 0 || peak == null || rms == null || peak.Length < channels || rms.Length < channels)
            {
                return;
            }

            int frames = interleaved.Length / channels;
            Span<double> sumSq = stackalloc double[channels];
            for (int ch = 0; ch < channels; ch++) { peak[ch] = 0f; sumSq[ch] = 0.0; }
            for (int i = 0; i < frames; i++)
            {
                int baseIdx = i * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    float s = interleaved[baseIdx + ch];
                    float a = MathF.Abs(s);
                    if (a > peak[ch])
                    {
                        peak[ch] = a;
                    }

                    sumSq[ch] += s * s;
                }
            }
            for (int ch = 0; ch < channels; ch++)
            {
                rms[ch] = (float)Math.Sqrt(sumSq[ch] / Math.Max(1, frames));
            }

        }

        public void GetMeters(out float[] peak, out float[] rms)
        {
            if (Volatile.Read(ref _meteringEnabled) == 0)
            {
                peak = Array.Empty<float>();
                rms = Array.Empty<float>();
                return;
            }

            peak = (float[])_peak?.Clone() ?? Array.Empty<float>();
            rms = (float[])_rms?.Clone() ?? Array.Empty<float>();
        }

        private void EnsureMeterBuffers(int channels)
        {
            if (channels <= 0)
            {
                return;
            }

            if ((_peak == null || _peak.Length < channels) && !EasyMicThreading.IsAudioThread)
            {
                _peak = new float[channels];
            }

            if ((_rms == null || _rms.Length < channels) && !EasyMicThreading.IsAudioThread)
            {
                _rms = new float[channels];
            }
        }

        // Diagnostics info (backend, device name) – defaults; can be extended via native helpers
        public string BackendName { get; private set; } = "miniaudio";
        public string DeviceName { get; private set; } = "Default Output";

        internal void DispatchMixedFrame(float[] interleaved, int channels, int sampleRate)
        {
            var handler = OnMixedFrame;
            if (handler == null || interleaved == null)
            {
                return;
            }

            try { handler(new ReadOnlySpan<float>(interleaved), channels, sampleRate); } catch { }
        }

        private void DispatchMixedFrameRaw(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
        {
            var handler = OnMixedFrameRaw;
            if (handler == null)
            {
                return;
            }

            try { handler(interleaved, channels, sampleRate); } catch { }
        }

        private void DispatchMixedFrameFromTransport(ReadOnlySpan<float> interleaved, int channels, int sampleRate)
        {
            if (OnMixedFrame == null)
            {
                return;
            }

            var writer = EasyMicAudioEventPump.TryBeginMixedFrame(channels, sampleRate, interleaved.Length);
            if (!writer.IsValid)
            {
                return;
            }

            if (!writer.Write(interleaved))
            {
                writer.WriteZeros(interleaved.Length);
            }

            writer.Commit();
        }
    }
}
