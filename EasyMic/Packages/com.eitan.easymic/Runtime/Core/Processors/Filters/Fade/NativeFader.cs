using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Fade in/out processor backed by miniaudio's <c>ma_fader</c>.
    /// 淡入淡出处理器，基于 miniaudio 的 <c>ma_fader</c>（原生实现）。
    ///
    /// Usage / 用法：
    /// - Add to the pipeline, keep a reference, then trigger fades at runtime:
    ///   添加到管线并保留引用，运行时触发淡入淡出：
    ///     var fader = new NativeFader();
    ///     pipeline.AddWorker(fader);
    ///     fader.FadeIn(0.25f);
    ///
    /// Notes / 注意：
    /// - Thread-safe control: calling Fade* from main thread schedules a request applied on the audio thread.
    ///   线程安全控制：主线程调用 Fade* 只会投递请求，在音频线程中生效。
    /// - If the native plugin is unavailable, this becomes a pass-through (no-op).
    ///   若原生插件不可用，则自动退化为直通（不处理）。
    /// </summary>
    public sealed class NativeFader : AudioWriter
    {
        private const string NativeUnavailableReasonDll = "miniaudio native library not found";
        private const string NativeUnavailableReasonSymbol = "miniaudio fader symbols not found";

        private Native.FaderHandle _native;
        private bool _nativeReady;
        private bool _loggedNativeUnavailable;
        private int _configuredChannels;
        private int _configuredSampleRate;

        private struct FadeRequest
        {
            public float From;
            public float To;
            public float DurationSeconds;
            public float StartOffsetSeconds;
            public bool UseStartOffset;
        }

        private FadeRequest _pendingFade;
        private int _fadeDirty;

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);

            _nativeReady = false;
            _loggedNativeUnavailable = false;
            _native = default;

            _configuredChannels = Math.Max(1, state.ChannelCount);
            _configuredSampleRate = Math.Max(1, state.SampleRate);

            TryInitNative();

            // Apply any fade scheduled before Initialize() (safe: we're not on the audio thread here).
            if (_nativeReady && _native.IsValid && Volatile.Read(ref _fadeDirty) != 0)
            {
                ApplyPendingFade();
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

        /// <summary>
        /// Schedules a fade-in from 0 to 1.
        /// 投递一次淡入：0 到 1。
        /// </summary>
        public void FadeIn(float durationSeconds)
        {
            Fade(0f, 1f, durationSeconds);
        }

        /// <summary>
        /// Schedules a fade-out from 1 to 0.
        /// 投递一次淡出：1 到 0。
        /// </summary>
        public void FadeOut(float durationSeconds)
        {
            Fade(1f, 0f, durationSeconds);
        }

        /// <summary>
        /// Schedules a fade from <paramref name="from"/> to <paramref name="to"/>.
        /// 投递一次淡入淡出：从 <paramref name="from"/> 到 <paramref name="to"/>。
        /// </summary>
        public void Fade(float from, float to, float durationSeconds)
        {
            _pendingFade = new FadeRequest
            {
                From = from,
                To = to,
                DurationSeconds = durationSeconds,
                StartOffsetSeconds = 0f,
                UseStartOffset = false
            };

            Interlocked.Exchange(ref _fadeDirty, 1);
        }

        /// <summary>
        /// Like <see cref="Fade"/>, but allows a start offset in seconds (can be negative).
        /// 类似 <see cref="Fade"/>，但允许用秒指定开始偏移（可为负数）。
        /// </summary>
        public void FadeEx(float from, float to, float durationSeconds, float startOffsetSeconds)
        {
            _pendingFade = new FadeRequest
            {
                From = from,
                To = to,
                DurationSeconds = durationSeconds,
                StartOffsetSeconds = startOffsetSeconds,
                UseStartOffset = true
            };

            Interlocked.Exchange(ref _fadeDirty, 1);
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

            if (Interlocked.Exchange(ref _fadeDirty, 0) == 1)
            {
                ApplyFadeRequest(_pendingFade);
            }

            int frameCount = usableSamples / channels;
            if (!Native.FaderHandle.ProcessInPlace(ref _native, audiobuffer.Slice(0, usableSamples), frameCount))
            {
                _nativeReady = false;
                if (_native.IsValid)
                {
                    _native.Dispose();
                    _native = default;
                }
            }
        }

        /// <summary>
        /// Current volume value (from native). Returns 1 if native is unavailable.
        /// 当前音量值（来自原生）。若原生不可用则返回 1。
        /// </summary>
        public float GetCurrentVolume()
        {
            if (!_nativeReady || !_native.IsValid)
            {
                return 1f;
            }

            return Native.FaderHandle.GetCurrentVolume(ref _native);
        }

        private void TryInitNative()
        {
            try
            {
                _nativeReady = Native.FaderHandle.TryCreate(_configuredChannels, _configuredSampleRate, out _native) && _native.IsValid;
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

        private void ApplyPendingFade()
        {
            if (Interlocked.Exchange(ref _fadeDirty, 0) == 1)
            {
                ApplyFadeRequest(_pendingFade);
            }
        }

        private void ApplyFadeRequest(FadeRequest req)
        {
            if (!_nativeReady || !_native.IsValid)
            {
                return;
            }

            double durationSeconds = req.DurationSeconds;
            if (durationSeconds < 0d)
            {
                durationSeconds = 0d;
            }
            else if (durationSeconds > 60d)
            {
                durationSeconds = 60d;
            }

            ulong lengthInFrames = (ulong)Math.Max(1, (long)Math.Round(durationSeconds * _configuredSampleRate));

            if (req.UseStartOffset)
            {
                long startOffsetInFrames = (long)Math.Round(req.StartOffsetSeconds * _configuredSampleRate);
                Native.FaderHandle.SetFadeEx(ref _native, req.From, req.To, lengthInFrames, startOffsetInFrames);
            }
            else
            {
                Native.FaderHandle.SetFade(ref _native, req.From, req.To, lengthInFrames);
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
            UnityEngine.Debug.LogWarning($"[{nameof(NativeFader)}] Native fader disabled ({reason}). Processor will pass-through.");
        }
    }
}

