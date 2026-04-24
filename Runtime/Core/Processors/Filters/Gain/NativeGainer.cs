using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Smooth gain (volume) processor backed by an EasyMic native miniaudio opaque handle.
    /// </summary>
    public sealed class NativeGainer : AudioWriter
    {
        private Native.GainerHandle _native;
        private bool _nativeReady;
        private bool _loggedNativeUnavailable;
        private int _configuredChannels;
        private int _configuredSampleRate;

        private volatile float _pendingGain = 1f;
        private int _gainDirty;

        private volatile float _pendingMasterVolume = 1f;
        private int _masterDirty;

        public float SmoothTimeSeconds { get; set; } = 0.02f;

        public float Gain
        {
            get => _pendingGain;
            set => SetGain(value);
        }

        public float MasterVolume
        {
            get => _pendingMasterVolume;
            set => SetMasterVolume(value);
        }

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);
            _nativeReady = false;
            _loggedNativeUnavailable = false;
            _native = default;
            _configuredChannels = Math.Max(1, state.ChannelCount);
            _configuredSampleRate = Math.Max(1, state.SampleRate);
            TryInitNative();
        }

        public override void Dispose()
        {
            if (_native.IsValid)
            {
                _native.Dispose();
                _native = default;
            }

            _nativeReady = false;
            base.Dispose();
        }

        public void SetGain(float gain)
        {
            _pendingGain = gain;
            Interlocked.Exchange(ref _gainDirty, 1);
        }

        public void SetMasterVolume(float volume)
        {
            _pendingMasterVolume = volume;
            Interlocked.Exchange(ref _masterDirty, 1);
        }

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (!_nativeReady || !_native.IsValid || state == null)
            {
                return;
            }

            int channels = Math.Max(1, state.ChannelCount);
            int sampleRate = Math.Max(1, state.SampleRate);
            if (channels != _configuredChannels || sampleRate != _configuredSampleRate)
            {
                _nativeReady = false;
                return;
            }

            int usableSamples = audiobuffer.Length - (audiobuffer.Length % channels);
            if (usableSamples <= 0) return;

            int frameCount = usableSamples / channels;

            if (Interlocked.Exchange(ref _gainDirty, 0) == 1)
            {
                Native.GainerHandle.SetGain(ref _native, _pendingGain);
            }

            if (Interlocked.Exchange(ref _masterDirty, 0) == 1)
            {
                Native.GainerHandle.SetMasterVolume(ref _native, _pendingMasterVolume);
            }

            if (!Native.GainerHandle.ProcessInPlace(ref _native, audiobuffer.Slice(0, usableSamples), frameCount))
            {
                _nativeReady = false;
            }
        }

        private void TryInitNative()
        {
            double smoothSeconds = SmoothTimeSeconds;
            if (smoothSeconds < 0d) smoothSeconds = 0d;
            else if (smoothSeconds > 10d) smoothSeconds = 10d;

            uint smoothTimeInFrames = (uint)Math.Max(0, (int)Math.Round(smoothSeconds * _configuredSampleRate));

            try
            {
                _nativeReady = Native.GainerHandle.TryCreate(_configuredChannels, smoothTimeInFrames, out _native) && _native.IsValid;
                if (_nativeReady)
                {
                    Native.GainerHandle.SetGain(ref _native, _pendingGain);
                    Native.GainerHandle.SetMasterVolume(ref _native, _pendingMasterVolume);
                }
                else
                {
                    LogNativeUnavailableOnce("native gainer unavailable");
                }
            }
            catch (DllNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio native library not found");
            }
            catch (EntryPointNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio gainer symbols not found");
            }
        }

        private void LogNativeUnavailableOnce(string reason)
        {
            if (_loggedNativeUnavailable) return;
            _loggedNativeUnavailable = true;
            _nativeReady = false;
            _native = default;
            UnityEngine.Debug.LogWarning($"[{nameof(NativeGainer)}] Native gainer disabled ({reason}). Processor will pass-through.");
        }
    }
}
