using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    public enum StereoPanMode
    {
        Balance = 0,
        Pan = 1
    }

    /// <summary>
    /// Stereo panner backed by an EasyMic native miniaudio opaque handle.
    /// </summary>
    public sealed class NativePanner : AudioWriter
    {
        private Native.PannerHandle _native;
        private bool _nativeReady;
        private bool _loggedNativeUnavailable;
        private int _configuredChannels;

        private volatile float _pendingPan;
        private int _panDirty;

        public float Pan
        {
            get => _pendingPan;
            set => SetPan(value);
        }

        public StereoPanMode Mode { get; set; } = StereoPanMode.Balance;

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);
            _nativeReady = false;
            _loggedNativeUnavailable = false;
            _native = default;
            _configuredChannels = Math.Max(1, state.ChannelCount);
            if (_configuredChannels == 2)
            {
                TryInitNative();
            }
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

        public void SetPan(float pan)
        {
            _pendingPan = pan;
            Interlocked.Exchange(ref _panDirty, 1);
        }

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (!_nativeReady || !_native.IsValid || state == null) return;
            if (Math.Max(1, state.ChannelCount) != 2)
            {
                _nativeReady = false;
                return;
            }

            int usableSamples = audiobuffer.Length - (audiobuffer.Length % 2);
            if (usableSamples <= 0) return;

            if (Interlocked.Exchange(ref _panDirty, 0) == 1)
            {
                Native.PannerHandle.SetPan(ref _native, _pendingPan);
            }

            int frameCount = usableSamples / 2;
            if (!Native.PannerHandle.ProcessInPlace(ref _native, audiobuffer.Slice(0, usableSamples), frameCount))
            {
                _nativeReady = false;
            }
        }

        private void TryInitNative()
        {
            try
            {
                _nativeReady = Native.PannerHandle.TryCreate(_configuredChannels, (Native.PanMode)Mode, _pendingPan, out _native) && _native.IsValid;
                if (!_nativeReady) LogNativeUnavailableOnce("native panner unavailable");
            }
            catch (DllNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio native library not found");
            }
            catch (EntryPointNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio panner symbols not found");
            }
        }

        private void LogNativeUnavailableOnce(string reason)
        {
            if (_loggedNativeUnavailable) return;
            _loggedNativeUnavailable = true;
            _nativeReady = false;
            _native = default;
            UnityEngine.Debug.LogWarning($"[{nameof(NativePanner)}] Native panner disabled ({reason}). Processor will pass-through.");
        }
    }
}
