using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Native-only biquad filter powered by miniaudio's <c>ma_biquad</c>.
    /// Processing is always done in native code; if native is unavailable, this filter becomes a pass-through.
    /// </summary>
    public abstract class NativeBiquadFilter : AudioWriter
    {
        protected float A0, A1, A2, B0, B1, B2;

        private Native.BiquadHandle _native;
        private bool _nativeReady;
        private int _configuredChannels;
        private int _configuredSampleRate;
        private bool _loggedNativeUnavailable;

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);

            _configuredChannels = Math.Max(1, state.ChannelCount);
            _configuredSampleRate = Math.Max(1, state.SampleRate);

            _nativeReady = false;
            _loggedNativeUnavailable = false;
            _native = default;

            UpdateCoefficients(_configuredSampleRate);
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

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (!_nativeReady || !_native.IsValid)
            {
                return;
            }

            int channels = Math.Max(1, state.ChannelCount);
            int sampleRate = Math.Max(1, state.SampleRate);
            if (channels != _configuredChannels || sampleRate != _configuredSampleRate)
            {
                // Do not reinitialize on the audio thread. Keep RT-safe and remain pass-through.
                return;
            }

            int frameCount = audiobuffer.Length / channels;
            if (frameCount <= 0)
            {
                return;
            }

            if (!Native.BiquadHandle.ProcessInPlace(ref _native, audiobuffer, frameCount))
            {
                // If native fails mid-stream, permanently disable native to avoid repeated overhead.
                _nativeReady = false;
                if (_native.IsValid)
                {
                    _native.Dispose();
                    _native = default;
                }
            }
        }

        protected abstract void UpdateCoefficients(int sampleRate);

        private void TryInitNative()
        {
            try
            {
                var cfg = Native.BiquadConfigInit(
                    Native.SampleFormat.F32,
                    (uint)_configuredChannels,
                    B0, B1, B2,
                    A0, A1, A2);

                _nativeReady = Native.BiquadHandle.TryCreate(in cfg, out _native) && _native.IsValid;
            }
            catch (DllNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio native library not found");
            }
            catch (EntryPointNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio biquad symbols not found");
            }
        }

        private void LogNativeUnavailableOnce(string reason)
        {
            if (_loggedNativeUnavailable)
            {
                return;
            }

            _loggedNativeUnavailable = true;
            _nativeReady = false;
            _native = default;
            UnityEngine.Debug.LogWarning($"[{GetType().Name}] Native biquad disabled ({reason}). Filter will pass-through.");
        }
    }
}

