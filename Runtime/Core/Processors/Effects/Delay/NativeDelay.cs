using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Delay/Echo effect backed by an EasyMic native miniaudio opaque handle.
    /// </summary>
    public sealed class NativeDelay : AudioWriter
    {
        private Native.DelayHandle _native;
        private bool _nativeReady;
        private bool _loggedNativeUnavailable;
        private bool _loggedReinitRequired;
        private int _configuredChannels;
        private int _configuredSampleRate;
        private uint _configuredDelayInFrames;
        private bool _configuredDelayStart;

        private volatile float _pendingWet = 1f;
        private volatile float _pendingDry = 1f;
        private volatile float _pendingDecay = 0f;
        private volatile float _pendingDelaySeconds = 0.25f;
        private volatile bool _pendingDelayStart;
        private int _paramsDirty;

        public float DelaySeconds { get => _pendingDelaySeconds; set { _pendingDelaySeconds = value; Interlocked.Exchange(ref _paramsDirty, 1); } }
        public float Wet { get => _pendingWet; set { _pendingWet = value; Interlocked.Exchange(ref _paramsDirty, 1); } }
        public float Dry { get => _pendingDry; set { _pendingDry = value; Interlocked.Exchange(ref _paramsDirty, 1); } }
        public float Decay { get => _pendingDecay; set { _pendingDecay = value; Interlocked.Exchange(ref _paramsDirty, 1); } }
        public bool DelayStart { get => _pendingDelayStart; set { _pendingDelayStart = value; Interlocked.Exchange(ref _paramsDirty, 1); } }

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);
            _nativeReady = false;
            _loggedNativeUnavailable = false;
            _loggedReinitRequired = false;
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

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (!_nativeReady || !_native.IsValid || state == null) return;

            int channels = Math.Max(1, state.ChannelCount);
            int sampleRate = Math.Max(1, state.SampleRate);
            if (channels != _configuredChannels || sampleRate != _configuredSampleRate)
            {
                _nativeReady = false;
                return;
            }

            int usableSamples = audiobuffer.Length - (audiobuffer.Length % channels);
            if (usableSamples <= 0) return;

            if (Interlocked.Exchange(ref _paramsDirty, 0) == 1)
            {
                float wet = Clamp01(_pendingWet);
                float dry = Clamp01(_pendingDry);
                float decay = Clamp01(_pendingDecay);
                Native.DelayHandle.SetWet(ref _native, wet);
                Native.DelayHandle.SetDry(ref _native, dry);
                Native.DelayHandle.SetDecay(ref _native, decay);

                uint desiredDelayInFrames = ComputeDelayFrames(_pendingDelaySeconds, _configuredSampleRate);
                bool desiredDelayStart = _pendingDelayStart;
                if (desiredDelayInFrames != _configuredDelayInFrames || desiredDelayStart != _configuredDelayStart)
                {
                    LogReinitRequiredOnce();
                }
            }

            int frameCount = usableSamples / channels;
            if (!Native.DelayHandle.ProcessInPlace(ref _native, audiobuffer.Slice(0, usableSamples), frameCount))
            {
                _nativeReady = false;
            }
        }

        private void TryInitNative()
        {
            try
            {
                float delaySeconds = _pendingDelaySeconds;
                if (delaySeconds < 0f) delaySeconds = 0f;
                else if (delaySeconds > 10f) delaySeconds = 10f;

                uint delayInFrames = ComputeDelayFrames(delaySeconds, _configuredSampleRate);
                float wet = Clamp01(_pendingWet);
                float dry = Clamp01(_pendingDry);
                float decay = Clamp01(_pendingDecay);

                _nativeReady = Native.DelayHandle.TryCreate(
                    _configuredChannels,
                    _configuredSampleRate,
                    delayInFrames,
                    decay,
                    _pendingDelayStart,
                    wet,
                    dry,
                    out _native) && _native.IsValid;

                if (_nativeReady)
                {
                    _configuredDelayInFrames = delayInFrames;
                    _configuredDelayStart = _pendingDelayStart;
                }
                else
                {
                    LogNativeUnavailableOnce("native delay unavailable");
                }
            }
            catch (DllNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio native library not found");
            }
            catch (EntryPointNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio delay symbols not found");
            }
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static uint ComputeDelayFrames(float delaySeconds, int sampleRate)
        {
            double s = delaySeconds;
            if (s < 0d) s = 0d;
            else if (s > 10d) s = 10d;
            return (uint)Math.Max(0, (int)Math.Round(s * Math.Max(1, sampleRate)));
        }

        private void LogReinitRequiredOnce()
        {
            if (_loggedReinitRequired) return;
            _loggedReinitRequired = true;
            UnityEngine.Debug.LogWarning($"[{nameof(NativeDelay)}] Changing DelaySeconds/DelayStart requires reinitialization; updates are ignored until the pipeline/session is rebuilt.");
        }

        private void LogNativeUnavailableOnce(string reason)
        {
            if (_loggedNativeUnavailable) return;
            _loggedNativeUnavailable = true;
            _nativeReady = false;
            _native = default;
            UnityEngine.Debug.LogWarning($"[{nameof(NativeDelay)}] Native delay disabled ({reason}). Processor will pass-through.");
        }
    }
}
