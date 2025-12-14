using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Stereo pan behavior.
    /// 立体声声像模式。
    /// </summary>
    public enum StereoPanMode
    {
        /// <summary>
        /// Balance: attenuates one side without blending to the other.
        /// Balance：只做左右衰减，不会把一侧混合到另一侧。
        /// </summary>
        Balance = 0,

        /// <summary>
        /// True pan: moves/blends one side into the other.
        /// 真正的 Pan：会把声音“移动/混合”到另一侧。
        /// </summary>
        Pan = 1
    }

    /// <summary>
    /// Stereo panner backed by miniaudio's <c>ma_panner</c>.
    /// 立体声声像（Pan）处理器，基于 miniaudio 的 <c>ma_panner</c>（原生实现）。
    ///
    /// Usage / 用法：
    /// - Only active for stereo (2 channels). Other channel counts pass-through.
    ///   仅对立体声（2 通道）生效，其它通道数将直通。
    /// - Set <see cref="Pan"/> from the main thread; it is applied on the audio thread safely.
    ///   在主线程设置 <see cref="Pan"/>，会在音频线程安全生效。
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
            if (_configuredChannels != 2)
            {
                return;
            }

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

        public void SetPan(float pan)
        {
            _pendingPan = pan;
            Interlocked.Exchange(ref _panDirty, 1);
        }

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (!_nativeReady || !_native.IsValid || state == null)
            {
                return;
            }

            if (Math.Max(1, state.ChannelCount) != 2)
            {
                _nativeReady = false;
                return;
            }

            int usableSamples = audiobuffer.Length - (audiobuffer.Length % 2);
            if (usableSamples <= 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _panDirty, 0) == 1)
            {
                Native.PannerHandle.SetPan(ref _native, _pendingPan);
            }

            int frameCount = usableSamples / 2;
            if (!Native.PannerHandle.ProcessInPlace(ref _native, audiobuffer.Slice(0, usableSamples), frameCount))
            {
                _nativeReady = false;
                if (_native.IsValid)
                {
                    _native.Dispose();
                    _native = default;
                }
            }
        }

        private void TryInitNative()
        {
            try
            {
                _nativeReady = Native.PannerHandle.TryCreate(_configuredChannels, (Native.PanMode)Mode, _pendingPan, out _native) && _native.IsValid;
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
            if (_loggedNativeUnavailable)
            {
                return;
            }

            _loggedNativeUnavailable = true;
            _nativeReady = false;
            _native = default;
            UnityEngine.Debug.LogWarning($"[{nameof(NativePanner)}] Native panner disabled ({reason}). Processor will pass-through.");
        }
    }
}
