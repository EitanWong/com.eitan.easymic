using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Delay/Echo effect backed by miniaudio's <c>ma_delay</c>.
    /// 延迟/回声效果，基于 miniaudio 的 <c>ma_delay</c>（原生实现）。
    ///
    /// Usage / 用法：
    /// - Add to the pipeline, then tune parameters:
    ///   添加到管线后调参数：
    ///     var delay = new NativeDelay { DelaySeconds = 0.25f, Wet = 0.5f, Dry = 1f, Decay = 0.35f };
    ///     pipeline.AddWorker(delay);
    ///
    /// Notes / 注意：
    /// - Internal delay buffer is allocated during Initialize() (NOT on the audio thread).
    ///   内部延迟缓冲区在 Initialize() 分配（非音频线程）。
    /// - If the native plugin is unavailable, this becomes a pass-through (no-op).
    ///   若原生插件不可用，则自动退化为直通（不处理）。
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

        /// <summary>Delay time in seconds. / 延迟时间（秒）。</summary>
        public float DelaySeconds
        {
            get => _pendingDelaySeconds;
            set
            {
                _pendingDelaySeconds = value;
                Interlocked.Exchange(ref _paramsDirty, 1);
            }
        }

        /// <summary>Wet mix (0..1). / 湿声比例（0..1）。</summary>
        public float Wet
        {
            get => _pendingWet;
            set
            {
                _pendingWet = value;
                Interlocked.Exchange(ref _paramsDirty, 1);
            }
        }

        /// <summary>Dry mix (0..1). / 干声比例（0..1）。</summary>
        public float Dry
        {
            get => _pendingDry;
            set
            {
                _pendingDry = value;
                Interlocked.Exchange(ref _paramsDirty, 1);
            }
        }

        /// <summary>Feedback decay (0..1). / 反馈衰减（0..1）。</summary>
        public float Decay
        {
            get => _pendingDecay;
            set
            {
                _pendingDecay = value;
                Interlocked.Exchange(ref _paramsDirty, 1);
            }
        }

        /// <summary>
        /// If true, delays the start of output. / 若为 true，将延迟输出开始。
        /// </summary>
        public bool DelayStart
        {
            get => _pendingDelayStart;
            set
            {
                _pendingDelayStart = value;
                Interlocked.Exchange(ref _paramsDirty, 1);
            }
        }

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
            if (usableSamples <= 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _paramsDirty, 0) == 1)
            {
                // ma_delay does not support live reconfig of delay time / delayStart without re-init.
                // ma_delay 不支持在不停机的情况下调整 delay time / delayStart（需要重新初始化）。
                // Policy: wet/dry/decay are applied live; delay time changes are ignored with a warning.
                // 策略：wet/dry/decay 可实时更新；delay time 的修改将被忽略并告警。
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
                                  out _native) &&
                              _native.IsValid;

                if (_nativeReady)
                {
                    _configuredDelayInFrames = delayInFrames;
                    _configuredDelayStart = _pendingDelayStart;
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
            if (_loggedReinitRequired)
            {
                return;
            }

            _loggedReinitRequired = true;
            UnityEngine.Debug.LogWarning(
                $"[{nameof(NativeDelay)}] Changing DelaySeconds/DelayStart requires reinitialization; updates are ignored until the pipeline/session is rebuilt. " +
                "修改 DelaySeconds/DelayStart 需要重新初始化；在重建管线/会话前该修改会被忽略。");
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
            UnityEngine.Debug.LogWarning($"[{nameof(NativeDelay)}] Native delay disabled ({reason}). Processor will pass-through.");
        }
    }
}
