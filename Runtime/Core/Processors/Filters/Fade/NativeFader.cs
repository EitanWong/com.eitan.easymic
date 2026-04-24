using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Fade in/out processor backed by an EasyMic native miniaudio opaque handle.
    /// </summary>
    public sealed class NativeFader : AudioWriter
    {
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

        public void FadeIn(float durationSeconds) => Fade(0f, 1f, durationSeconds);
        public void FadeOut(float durationSeconds) => Fade(1f, 0f, durationSeconds);

        public void Fade(float from, float to, float durationSeconds)
        {
            _pendingFade = new FadeRequest { From = from, To = to, DurationSeconds = durationSeconds };
            Interlocked.Exchange(ref _fadeDirty, 1);
        }

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

            if (Interlocked.Exchange(ref _fadeDirty, 0) == 1)
            {
                ApplyFadeRequest(_pendingFade);
            }

            int frameCount = usableSamples / channels;
            if (!Native.FaderHandle.ProcessInPlace(ref _native, audiobuffer.Slice(0, usableSamples), frameCount))
            {
                _nativeReady = false;
            }
        }

        public float GetCurrentVolume()
        {
            return _nativeReady && _native.IsValid ? Native.FaderHandle.GetCurrentVolume(ref _native) : 1f;
        }

        private void TryInitNative()
        {
            try
            {
                _nativeReady = Native.FaderHandle.TryCreate(_configuredChannels, _configuredSampleRate, out _native) && _native.IsValid;
                if (!_nativeReady) LogNativeUnavailableOnce("native fader unavailable");
            }
            catch (DllNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio native library not found");
            }
            catch (EntryPointNotFoundException)
            {
                LogNativeUnavailableOnce("miniaudio fader symbols not found");
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
            if (!_nativeReady || !_native.IsValid) return;

            double durationSeconds = req.DurationSeconds;
            if (durationSeconds < 0d) durationSeconds = 0d;
            else if (durationSeconds > 60d) durationSeconds = 60d;

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
            if (_loggedNativeUnavailable) return;
            _loggedNativeUnavailable = true;
            _nativeReady = false;
            _native = default;
            UnityEngine.Debug.LogWarning($"[{nameof(NativeFader)}] Native fader disabled ({reason}). Processor will pass-through.");
        }
    }
}
