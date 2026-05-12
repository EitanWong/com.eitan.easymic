using System;
using System.Buffers;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Real-time, allocation-free sample-rate conversion for pipeline use.
    /// Designed for the common Voice/ASR path where capture rate (e.g. 44.1/48k) must be downsampled
    /// to a fixed model rate (e.g. 16k) before downstream processing.
    /// </summary>
    public sealed class Resampler : AudioWriter
    {
        private readonly int _targetSampleRate;

        private Native.LinearResamplerHandle _native;
        private bool _useNative;
        private int _nativeChannels;
        private int _nativeSourceSampleRate;
        private float[] _scratch;

        private int _lastSourceSampleRate;
        private double _sourcePosition; // may be in [-1, 0) at block start to allow cross-block interpolation
        private float[] _prevFrame;     // last source frame (per channel)
        private bool _hasPrevFrame;
        private bool _loggedNativeUnavailable;

        private const uint DefaultLpfOrder = 4;
        private const float DefaultNyquistFactor = 0.9f;

        public Resampler(int targetSampleRate)
        {
            _targetSampleRate = Math.Max(1, targetSampleRate);
        }

        public override void Initialize(AudioContext state)
        {
            base.Initialize(state);

            if (state == null)
            {
                return;
            }

            int channels = Math.Max(1, state.ChannelCount);
            int sourceSampleRate = Math.Max(1, state.SampleRate);

            _lastSourceSampleRate = sourceSampleRate;
            _prevFrame = new float[channels];
            _hasPrevFrame = false;
            _sourcePosition = 0d;
            _useNative = false;
            _loggedNativeUnavailable = false;
            _nativeChannels = 0;
            _nativeSourceSampleRate = 0;

            // Publish the post-resample format to downstream workers for correct initialization.
            // We only support downsampling in-place; keep the input rate if it's below the target.
            if (sourceSampleRate >= _targetSampleRate)
            {
                state.SampleRate = _targetSampleRate;
            }

            if (sourceSampleRate > _targetSampleRate)
            {
                TryEnsureNative(channels, sourceSampleRate);
                if (!_useNative)
                {
                    LogNativeUnavailableOnce("native resampler unavailable");
                }
                EnsureScratch(Math.Max(channels, state.Length));
            }
        }

        protected override void OnAudioWrite(Span<float> audiobuffer, AudioContext state)
        {
            if (state == null)
            {
                return;
            }

            int channels = Math.Max(1, state.ChannelCount);
            int sourceSampleRate = Math.Max(1, state.SampleRate);

            int usableSamples = audiobuffer.Length - (audiobuffer.Length % channels);
            if (usableSamples <= 0)
            {
                state.Length = 0;
                if (sourceSampleRate >= _targetSampleRate)
                {
                    state.SampleRate = _targetSampleRate;
                }
                return;
            }

            int inFrames = usableSamples / channels;
            if (inFrames <= 0)
            {
                state.Length = 0;
                if (sourceSampleRate >= _targetSampleRate)
                {
                    state.SampleRate = _targetSampleRate;
                }
                return;
            }

            if (_prevFrame == null || _prevFrame.Length < channels)
            {
                // RT-safety: don't allocate on the audio thread. Fall back to pass-through.
                state.Length = usableSamples;
                state.SampleRate = sourceSampleRate;
                return;
            }

            if (sourceSampleRate != _lastSourceSampleRate)
            {
                // Source format changed (device switch). Reset streaming state.
                _lastSourceSampleRate = sourceSampleRate;
                _sourcePosition = 0d;
                _hasPrevFrame = false;
            }

            // Always refresh the carry frame from the original input at the end.
            int lastFrameBase = (inFrames - 1) * channels;

            if (sourceSampleRate == _targetSampleRate)
            {
                state.Length = usableSamples;
                state.SampleRate = _targetSampleRate;

                for (int ch = 0; ch < channels; ch++)
                {
                    _prevFrame[ch] = audiobuffer[lastFrameBase + ch];
                }

                _hasPrevFrame = true;
                _sourcePosition = 0d;
                return;
            }

            if (sourceSampleRate < _targetSampleRate)
            {
                // Not supported in-place (would require expanding the buffer).
                // Keep the original format so downstream can detect the mismatch deterministically.
                state.Length = usableSamples;
                state.SampleRate = sourceSampleRate;

                for (int ch = 0; ch < channels; ch++)
                {
                    _prevFrame[ch] = audiobuffer[lastFrameBase + ch];
                }

                _hasPrevFrame = true;
                _sourcePosition = 0d;
                return;
            }

            if (TryResampleNative(audiobuffer.Slice(0, usableSamples), state))
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    _prevFrame[ch] = audiobuffer[(inFrames - 1) * channels + ch];
                }

                _hasPrevFrame = true;
                _sourcePosition = 0d;
                _lastSourceSampleRate = sourceSampleRate;
                return;
            }

            double step = sourceSampleRate / (double)_targetSampleRate; // source frames per output frame (>= 1)
            double pos = _sourcePosition;
            if (!_hasPrevFrame && pos < 0d)
            {
                pos = 0d;
            }

            int outFrames = 0;

            if (channels == 1)
            {
                float prev = _hasPrevFrame ? _prevFrame[0] : audiobuffer[0];

                while ((int)pos + 1 < inFrames)
                {
                    int i0 = (int)pos;
                    int i1 = i0 + 1;
                    float s0 = i0 >= 0 ? audiobuffer[i0] : prev;
                    float s1 = audiobuffer[i1];
                    float t = (float)(pos - i0);

                    audiobuffer[outFrames] = s0 + (s1 - s0) * t;
                    outFrames++;
                    pos += step;
                }
            }
            else
            {
                while ((int)pos + 1 < inFrames)
                {
                    int i0 = (int)pos;
                    int i1 = i0 + 1;
                    float t = (float)(pos - i0);

                    int src0 = i0 * channels;
                    int src1 = i1 * channels;
                    int dst = outFrames * channels;

                    for (int ch = 0; ch < channels; ch++)
                    {
                        float s0 = i0 >= 0 ? audiobuffer[src0 + ch] : (_hasPrevFrame ? _prevFrame[ch] : audiobuffer[ch]);
                        float s1 = audiobuffer[src1 + ch];
                        audiobuffer[dst + ch] = s0 + (s1 - s0) * t;
                    }

                    outFrames++;
                    pos += step;
                }
            }

            for (int ch = 0; ch < channels; ch++)
            {
                _prevFrame[ch] = audiobuffer[lastFrameBase + ch];
            }

            _hasPrevFrame = true;

            double nextPos = pos - inFrames;
            if (nextPos < -1d)
            {
                nextPos = -1d;
            }
            else if (nextPos >= 0d)
            {
                nextPos = 0d;
            }
            _sourcePosition = nextPos;

            int outSamples = outFrames * channels;
            if (outSamples < 0)
            {
                outSamples = 0;
            }
            else if (outSamples > usableSamples)
            {
                outSamples = usableSamples;
            }

            state.Length = outSamples;
            state.SampleRate = _targetSampleRate;
        }

        public override void Dispose()
        {
            if (_native.IsValid)
            {
                _native.Dispose();
                _native = default;
            }

            if (_scratch != null)
            {
                ArrayPool<float>.Shared.Return(_scratch);
                _scratch = null;
            }

            _prevFrame = null;
            _hasPrevFrame = false;
            _sourcePosition = 0d;
            base.Dispose();
        }

        private void TryEnsureNative(int channels, int sourceSampleRate)
        {
            if (channels <= 0 || sourceSampleRate <= 0 || sourceSampleRate <= _targetSampleRate)
            {
                _useNative = false;
                return;
            }

            if (_useNative && _native.IsValid && _nativeChannels == channels && _nativeSourceSampleRate == sourceSampleRate)
            {
                return;
            }

            if (_native.IsValid)
            {
                _native.Dispose();
                _native = default;
            }

            try
            {
                _useNative = Native.LinearResamplerHandle.TryCreate(
                    channels,
                    sourceSampleRate,
                    _targetSampleRate,
                    DefaultLpfOrder,
                    DefaultNyquistFactor,
                    out _native);
            }
            catch (DllNotFoundException)
            {
                _useNative = false;
                _native = default;
            }
            catch (EntryPointNotFoundException)
            {
                _useNative = false;
                _native = default;
            }

            if (_useNative && _native.IsValid)
            {
                _nativeChannels = channels;
                _nativeSourceSampleRate = sourceSampleRate;
            }
        }

        private void LogNativeUnavailableOnce(string reason)
        {
            if (_loggedNativeUnavailable) return;
            _loggedNativeUnavailable = true;
            UnityEngine.Debug.LogWarning($"[{nameof(Resampler)}] Native resampler disabled ({reason}). Managed fallback will be used.");
        }

        private bool TryResampleNative(Span<float> buffer, AudioContext state)
        {
            if (!_useNative) return false;

            int channels = Math.Max(1, state.ChannelCount);
            int sourceSampleRate = Math.Max(1, state.SampleRate);
            if (sourceSampleRate <= _targetSampleRate) return false;
            if (!_native.IsValid || _nativeChannels != channels || _nativeSourceSampleRate != sourceSampleRate) return false;

            int framesIn = buffer.Length / channels;
            if (framesIn <= 0)
            {
                state.Length = 0;
                state.SampleRate = _targetSampleRate;
                return true;
            }

            int estimatedOutFrames = Native.LinearResamplerHandle.EstimateOutputFrames(ref _native, framesIn);
            if (estimatedOutFrames <= 0) return false;

            int requiredSamples = estimatedOutFrames * channels;
            if (_scratch == null || _scratch.Length < requiredSamples) return false;

            int writtenFrames = Native.LinearResamplerHandle.Process(ref _native, buffer, framesIn, _scratch);
            if (writtenFrames <= 0) return false;

            int outSamples = writtenFrames * channels;
            var dst = buffer.Slice(0, Math.Min(outSamples, buffer.Length));
            new ReadOnlySpan<float>(_scratch, 0, dst.Length).CopyTo(dst);

            state.Length = dst.Length;
            state.SampleRate = _targetSampleRate;
            return true;
        }

        private void EnsureScratch(int requiredSamples)
        {
            if (requiredSamples <= 0) return;

            if (_scratch == null || _scratch.Length < requiredSamples)
            {
                if (_scratch != null)
                {
                    ArrayPool<float>.Shared.Return(_scratch);
                }

                _scratch = ArrayPool<float>.Shared.Rent(requiredSamples);
            }
        }
    }
}
