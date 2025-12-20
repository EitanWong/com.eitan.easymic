using System;
using System.Runtime.InteropServices;
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
        private static AudioSystem s_instanceNoLock;
        public static AudioSystem Instance
        {
            get
            {
                lock (s_lock)
                {
                    if (s_instance == null)
                    {
                        s_instance = new AudioSystem();
                        Volatile.Write(ref s_instanceNoLock, s_instance);
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
        private IntPtr _device = IntPtr.Zero;
        private IntPtr _deviceConfig = IntPtr.Zero;
#pragma warning disable 0414
        private Native.AudioCallbackEx _cbEx;
        private Native.AudioCallback _cb;
        private bool _useEx;
#pragma warning restore 0414
        private GCHandle _selfHandle;
        private bool _running;
        private bool _setRunInBackground;
        private bool _previousRunInBackground;

        // Diagnostics meters
        private float[] _peak; // per-channel
        private float[] _rms;  // per-channel

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
                Volatile.Write(ref s_instanceNoLock, this);
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

                _context = Native.AllocateContext();
                Check(_context != IntPtr.Zero, "AllocateContext");
                var initContext = Native.ContextInit(IntPtr.Zero, 0, IntPtr.Zero, _context);
                Check(initContext == Native.Result.Success, $"ContextInit: {initContext}");

                if (!TryAllocatePlaybackDevice(_sampleRate, _channels))
                {
                    TryFallbackFormats();
                }

                _masterMixer.Initialize((int)_channels, (int)_sampleRate);
                _peak = new float[Math.Max(1, (int)_channels)];
                _rms = new float[Math.Max(1, (int)_channels)];

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
                if (_device != IntPtr.Zero)
                {
                    try { Native.DeviceStop(_device); } catch { }
                }
            }
            finally
            {
                ReleaseDeviceHandles();
                try { if (_context != IntPtr.Zero) { Native.ContextUninit(_context); } } catch { }
                if (_context != IntPtr.Zero) { try { Native.Free(_context); } catch { } _context = IntPtr.Zero; }
                if (_selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
                }
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
                _cb = null; _cbEx = null; _useEx = false;
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
        public void AddSource(PlaybackAudioSource source) => _masterMixer?.AddSource(source);
        public void RemoveSource(PlaybackAudioSource source) => _masterMixer?.RemoveSource(source);

        private void Render(IntPtr output, uint frameCount)
        {
            using var _ = EasyMicThreading.EnterAudioThread();

            if (output == IntPtr.Zero)
            {
                return;
            }

            ulong samplesU = (ulong)frameCount * (ulong)_channels;
            if (samplesU == 0 || samplesU > int.MaxValue)
            {
                return;
            }

            int samples = (int)samplesU;
            unsafe
            {
                var outSpan = new Span<float>((void*)output, samples);
                outSpan.Clear();

                // Render full tree via master mixer
                try { _masterMixer?.RenderAdditive(outSpan, (int)_channels, (int)_sampleRate); }
                catch { }

                // Update meters
                UpdateMeters(outSpan, (int)_channels);

                var rawHandler = OnMixedFrameRaw;
                if (rawHandler != null)
                {
                    try { rawHandler(outSpan, (int)_channels, (int)_sampleRate); } catch { }
                }

                if (OnMixedFrame != null)
                {
                    var writer = EasyMicAudioEventPump.TryBeginMixedFrame((int)_channels, (int)_sampleRate, samples);
                    if (writer.IsValid)
                    {
                        if (!writer.Write(outSpan))
                        {
                            writer.WriteZeros(samples);
                        }
                        writer.Commit();
                    }
                }
            }
        }

#if UNITY_IOS || UNITY_ANDROID || ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(Native.AudioCallbackEx))]
#endif
        private static void OnCallbackEx(IntPtr device, IntPtr output, IntPtr input, uint length, IntPtr userData)
        {
            var handle = GCHandle.FromIntPtr(userData);
            if (handle.Target is AudioSystem self)
            {
                self.Render(output, length);
            }
        }

#if UNITY_IOS || UNITY_ANDROID || ENABLE_IL2CPP
        [AOT.MonoPInvokeCallback(typeof(Native.AudioCallback))]
#endif
        private static void OnCallback(IntPtr device, IntPtr output, IntPtr input, uint length)
        {
            // Fallback: singleton
            var self = Volatile.Read(ref s_instanceNoLock);
            if (self != null)
            {
                self.Render(output, length);
            }
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

            try
            {
                _cbEx = OnCallbackEx;
                _useEx = true;
                if (!_selfHandle.IsAllocated)
                {
                    _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
                }

                _deviceConfig = Native.AllocateDeviceConfigEx(
                    Native.DeviceType.Playback,
                    Native.SampleFormat.F32,
                    channels,
                    rate,
                    _cbEx,
                    GCHandle.ToIntPtr(_selfHandle),
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            catch (EntryPointNotFoundException)
            {
                _useEx = false;
                if (_selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
                }

                _cb = OnCallback;
                _deviceConfig = Native.AllocateDeviceConfig(Native.Capability.Playback, rate, _cb, IntPtr.Zero);
            }

            if (_deviceConfig == IntPtr.Zero)
            {
                if (_useEx && _selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
                }
                return false;
            }

            _device = Native.AllocateDevice();
            if (_device == IntPtr.Zero)
            {
                if (_useEx && _selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
                }
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
                if (_useEx && _selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
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
            peak = (float[])_peak?.Clone() ?? Array.Empty<float>();
            rms = (float[])_rms?.Clone() ?? Array.Empty<float>();
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
    }
}
