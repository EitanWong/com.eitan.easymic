using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Smooth gain (volume) processor backed by miniaudio's <c>ma_gainer</c>.
    /// 平滑增益（音量）处理器，基于 miniaudio 的 <c>ma_gainer</c>（原生实现）。
    ///
    /// Usage / 用法：
    /// - Add early in the pipeline to apply smooth volume changes without zipper noise.
    ///   建议放在管线前部，用于无“拉链噪声”的平滑音量变化。
    /// - Call <see cref="SetGain"/> (or set <see cref="Gain"/>) from the main thread; the change is applied on the audio thread safely.
    ///   在主线程调用 <see cref="SetGain"/>（或设置 <see cref="Gain"/>），会在音频线程安全生效。
    ///
    /// Notes / 注意：
    /// - If the native plugin is unavailable, this becomes a pass-through (no-op).
    ///   若原生插件不可用，则自动退化为直通（不处理）。
    /// - No allocations on the audio thread (RT-safe). Initialization allocates once.
    ///   音频线程零分配（实时安全），初始化阶段一次性分配。
    /// </summary>
    public sealed class NativeGainer : AudioWriter
    {
        private const string NativeUnavailableReasonDll = "miniaudio native library not found";
        private const string NativeUnavailableReasonSymbol = "miniaudio gainer symbols not found";

        private Native.GainerHandle _native;
        private bool _nativeReady;
        private bool _loggedNativeUnavailable;
        private int _configuredChannels;
        private int _configuredSampleRate;

        private volatile float _pendingGain = 1f;
        private int _gainDirty;

        private volatile float _pendingMasterVolume = 1f;
        private int _masterDirty;

        /// <summary>
        /// Smooth time in seconds. Larger values -> smoother changes but slower response.
        /// 平滑时间（秒）。值越大越平滑，但响应更慢。
        /// </summary>
        public float SmoothTimeSeconds { get; set; } = 0.02f;

        /// <summary>
        /// Linear gain multiplier. 1 = unchanged.
        /// 线性增益倍率。1 表示不改变。
        /// </summary>
        public float Gain
        {
            get => _pendingGain;
            set => SetGain(value);
        }

        /// <summary>
        /// Master volume multiplier (applied on top of per-channel gains). 1 = unchanged.
        /// 主音量倍率（叠加在各通道增益之上）。1 表示不改变。
        /// </summary>
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

        /// <summary>
        /// Sets a new target gain (linear).
        /// 设置目标增益（线性）。
        /// </summary>
        public void SetGain(float gain)
        {
            _pendingGain = gain;
            Interlocked.Exchange(ref _gainDirty, 1);
        }

        /// <summary>
        /// Sets a new master volume (linear).
        /// 设置主音量（线性）。
        /// </summary>
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
                // Do not reinitialize on the audio thread. Keep RT-safe and remain pass-through.
                _nativeReady = false;
                return;
            }

            int usableSamples = audiobuffer.Length - (audiobuffer.Length % channels);
            if (usableSamples <= 0)
            {
                return;
            }

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
                // If native fails mid-stream, permanently disable native to avoid repeated overhead.
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
            uint smoothTimeInFrames = 0;
            double smoothSeconds = SmoothTimeSeconds;
            if (smoothSeconds < 0d)
            {
                smoothSeconds = 0d;
            }
            else if (smoothSeconds > 10d)
            {
                smoothSeconds = 10d;
            }

            smoothTimeInFrames = (uint)Math.Max(0, (int)Math.Round(smoothSeconds * _configuredSampleRate));

            try
            {
                _nativeReady = Native.GainerHandle.TryCreate(_configuredChannels, smoothTimeInFrames, out _native) && _native.IsValid;
                if (_nativeReady)
                {
                    // Apply initial settings.
                    Native.GainerHandle.SetGain(ref _native, _pendingGain);
                    Native.GainerHandle.SetMasterVolume(ref _native, _pendingMasterVolume);
                }
            }
            catch (DllNotFoundException)
            {
                LogNativeUnavailableOnce(NativeUnavailableReasonDll);
            }
            catch (EntryPointNotFoundException)
            {
                LogNativeUnavailableOnce(NativeUnavailableReasonSymbol);
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
            UnityEngine.Debug.LogWarning($"[{nameof(NativeGainer)}] Native gainer disabled ({reason}). Processor will pass-through.");
        }
    }
}

