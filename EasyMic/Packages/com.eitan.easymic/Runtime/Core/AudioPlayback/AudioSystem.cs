using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

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

        private static readonly object s_lock = new object();
        private static AudioSystem s_instance;
        public static AudioSystem Instance
        {
            get { lock (s_lock) { return s_instance ??= new AudioSystem(); } }
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
                // Resolve target format before creating the mixer
                if (_sampleRate == 0 || _channels == 0)
                {
                    TryPickUnityOutputFormat(out _sampleRate, out _channels);
                }

                // init master mixer with a valid format
                _masterMixer = new AudioMixer();
                _masterMixer.Initialize((int)_channels, (int)_sampleRate);
                _masterMixer.name = "Master";
                // Add a default soft limiter on the master to prevent overs.
                _masterMixer.Pipeline.AddWorker(new SoftLimiter());
                _context = Native.AllocateContext();
                Check(_context != IntPtr.Zero, "AllocateContext");
                var r = Native.ContextInit(IntPtr.Zero, 0, IntPtr.Zero, _context);
                Check(r == Native.Result.Success, $"ContextInit: {r}");

                _cb = OnCallback;
                _useEx = false;
                // Use simplified config helper used by newer native builds.
                _deviceConfig = Native.AllocateDeviceConfig(Native.Capability.Playback, _sampleRate, _cb, IntPtr.Zero);
                Check(_deviceConfig != IntPtr.Zero, "AllocateDeviceConfig");

                _device = Native.AllocateDevice();
                Check(_device != IntPtr.Zero, "AllocateDevice");
                r = Native.DeviceInit(_context, _deviceConfig, _device);
                if (r != Native.Result.Success)
                {
                    // Device negotiation fallback: try common formats
                    TryFallbackFormats();
                }
                r = Native.DeviceStart(_device);
                Check(r == Native.Result.Success, $"DeviceStart: {r}");
                // Subscribe to application quit to ensure graceful shutdown
                try { Application.quitting += OnApplicationQuitting; } catch { }
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
                try { Application.quitting -= OnApplicationQuitting; } catch { }
                if (_device != IntPtr.Zero)
                {
                    try { Native.DeviceStop(_device); } catch { }
                    try { Native.DeviceUninit(_device); } catch { }
                }
            }
            finally
            {
                if (_device != IntPtr.Zero) { try { Native.Free(_device); } catch { } _device = IntPtr.Zero; }
                if (_deviceConfig != IntPtr.Zero) { try { Native.Free(_deviceConfig); } catch { } _deviceConfig = IntPtr.Zero; }
                try { if (_context != IntPtr.Zero) { Native.ContextUninit(_context); } } catch { }
                if (_context != IntPtr.Zero) { try { Native.Free(_context); } catch { } _context = IntPtr.Zero; }
                if (_selfHandle.IsAllocated)
                {
                    _selfHandle.Free();
                }
                // Clear event handlers to avoid leaks to user delegates

                OnMixedFrame = null;
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
            int samples = checked((int)(frameCount * _channels));
            unsafe
            {
                var outSpan = new Span<float>((void*)output, samples);
                outSpan.Clear();

                // Render full tree via master mixer
                try { _masterMixer?.RenderAdditive(outSpan, (int)_channels, (int)_sampleRate); }
                catch { }
                
                // Update meters
                UpdateMeters(outSpan, (int)_channels);

                var handler = OnMixedFrame;
                if (handler != null)
                {
                    handler(new ReadOnlySpan<float>((void*)output, samples), (int)_channels, (int)_sampleRate);
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
            Instance.Render(output, length);
        }

        private static void Check(bool cond, string msg)
        {
            if (!cond)
            {
                throw new InvalidOperationException(msg);
            }

        }

        private void TryFallbackFormats()
        {
            // Dispose previous config/device if any
            try { if (_device != IntPtr.Zero) { Native.DeviceUninit(_device); Native.Free(_device); _device = IntPtr.Zero; } } catch { }
            try { if (_deviceConfig != IntPtr.Zero) { Native.Free(_deviceConfig); _deviceConfig = IntPtr.Zero; } } catch { }

            var candidates = new (uint sr, uint ch)[]
            {
                (48000u, 2u), (44100u, 2u), (48000u, 1u), (44100u, 1u)
            };
            foreach (var c in candidates)
            {
                try
                {
                    _deviceConfig = Native.AllocateDeviceConfig(Native.Capability.Playback, c.sr, _cb, IntPtr.Zero);
                    if (_deviceConfig == IntPtr.Zero)
                    {
                        continue;
                    }


                    _device = Native.AllocateDevice();
                    if (_device == IntPtr.Zero) { Native.Free(_deviceConfig); _deviceConfig = IntPtr.Zero; continue; }
                    var r = Native.DeviceInit(_context, _deviceConfig, _device);
                    if (r == Native.Result.Success)
                    {
                        _sampleRate = c.sr;
                        // Channel count may be decided by native defaults; keep existing unless we want to override
                        _masterMixer?.Initialize((int)_channels, (int)_sampleRate);
                        return;
                    }
                    Native.DeviceUninit(_device); Native.Free(_device); _device = IntPtr.Zero;
                    Native.Free(_deviceConfig); _deviceConfig = IntPtr.Zero;
                }
                catch { }
            }
            throw new InvalidOperationException("Failed to initialize playback device with fallback formats.");
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
            if (_peak == null || _peak.Length != channels)
            {
                _peak = new float[channels];
            }


            if (_rms == null || _rms.Length != channels)
            {
                _rms = new float[channels];
            }

            int frames = interleaved.Length / channels;
            Span<double> sumSq = stackalloc double[channels];
            for (int ch = 0; ch < channels; ch++) { _peak[ch] = 0f; sumSq[ch] = 0.0; }
            for (int i = 0; i < frames; i++)
            {
                int baseIdx = i * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    float s = interleaved[baseIdx + ch];
                    float a = MathF.Abs(s);
                    if (a > _peak[ch])
                    {
                        _peak[ch] = a;
                    }

                    sumSq[ch] += s * s;
                }
            }
            for (int ch = 0; ch < channels; ch++)
            {
                _rms[ch] = (float)Math.Sqrt(sumSq[ch] / Math.Max(1, frames));
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
    }
}
