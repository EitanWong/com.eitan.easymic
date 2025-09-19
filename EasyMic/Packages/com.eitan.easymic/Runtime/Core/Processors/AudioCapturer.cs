using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    public class AudioCapturer : AudioReader
    {
        private AudioBuffer _audioBuffer;
        private readonly object _lock = new object();
        private readonly int _maxCaptureDuration;

        private AudioState _audioState;
        private readonly int _targetSampleRate; // 0 => follow input rate
        private float[] _resampleWork; // reused buffer for resampled output

        public AudioCapturer(int maxDuration)
            : this(maxDuration, 0) { }

        /// <param name="targetSampleRate">Optional desired capture sample rate. 0 means keep input rate.</param>
        public AudioCapturer(int maxDuration, int targetSampleRate)
        {
            this._maxCaptureDuration = maxDuration;
            this._targetSampleRate = Math.Max(0, targetSampleRate);
        }

        public override void Initialize(AudioState state)
        {
            // 使用采样率与通道数推导容量，避免依赖 state.Length 初始化值
            // Buffer capacity sized to the larger of input/output path to avoid overflow under rate conversion.
            int effectiveTargetSR = _targetSampleRate > 0 ? _targetSampleRate : state.SampleRate;
            int worstCaseRate = Math.Max(state.SampleRate, effectiveTargetSR);
            int totalSamples = Math.Max(1, worstCaseRate * state.ChannelCount * _maxCaptureDuration);
            lock (_lock)
            {
                _audioBuffer = new AudioBuffer(totalSamples);
            }
            _audioState = state;
            _resampleWork = Array.Empty<float>();
            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            int srcSR = CurrentSampleRate;
            int dstSR = _targetSampleRate <= 0 ? srcSR : _targetSampleRate;
            int ch = Math.Max(1, CurrentChannelCount);

            EnsureBufferCapacity(Math.Max(srcSR, dstSR), ch);

            if (audiobuffer.IsEmpty)
            {
                lock (_lock)
                {
                    _audioBuffer?.TryWriteExact(audiobuffer);
                }
                return;
            }

            if (srcSR == dstSR)
            {
                lock (_lock)
                {
                    _audioBuffer?.TryWriteExact(audiobuffer);
                }
                return;
            }

            int inFrames = audiobuffer.Length / ch;
            if (inFrames <= 0)
            {
                return;
            }

            // Compute output frame count, guard against rounding drift by floor.

            double ratio = (double)dstSR / Math.Max(1, srcSR);
            int outFrames = (int)Math.Floor(inFrames * ratio);
            if (outFrames <= 0)
            {
                return;
            }


            int outSamples = outFrames * ch;
            if (_resampleWork.Length < outSamples)
            {
                _resampleWork = new float[outSamples];
            }

            // Linear interpolation per channel, forward safe using separate output buffer.
            double step = (double)srcSR / dstSR; // source frames per one output frame
            for (int chIdx = 0; chIdx < ch; chIdx++)
            {
                int outBase = chIdx;
                for (int of = 0; of < outFrames; of++)
                {
                    double phase = of * step;
                    int i0 = (int)Math.Floor(phase);
                    double t = phase - i0;
                    int i1 = Math.Min(inFrames - 1, i0 + 1);
                    int src0 = i0 * ch + chIdx;
                    int src1 = i1 * ch + chIdx;
                    float s0 = audiobuffer[src0];
                    float s1 = audiobuffer[src1];
                    _resampleWork[outBase + of * ch] = (float)(s0 + (s1 - s0) * t);
                }
            }
            lock (_lock)
            {
                _audioBuffer?.TryWriteExact(new ReadOnlySpan<float>(_resampleWork, 0, outSamples));
            }
        }

        /// <summary>
        /// Gets the captured audio samples. Can optionally downmix to mono.
        /// 获取捕获的音频样本。可以选择性地混音到单声道。
        /// </summary>
        /// <param name="downmix">If true, multi-channel audio will be downmixed to mono. 如果为 true，多声道音频将被缩混为单声道。</param>
        /// <returns>An array of audio samples. The channel count depends on the 'downmix' parameter. 一个音频样本数组。声道数取决于 'downmix' 参数。</returns>
        public float[] GetCapturedAudioSamples()
        {
            if (!IsInitialized)
            {

                return null;
            }

            // Create a buffer and read the captured audio data.

            AudioBuffer buffer;
            lock (_lock)
            {
                buffer = _audioBuffer;
            }

            if (buffer == null)
            {
                return Array.Empty<float>();
            }

            var samples = new float[buffer.ReadableCount];
            if (samples.Length == 0)
            {
                return samples;
            }


            buffer.Read(samples);


            return samples;
        }

        /// <summary>
        /// Creates a Unity AudioClip from the captured audio.
        /// 从捕获的音频创建一个 Unity AudioClip。
        /// </summary>
        /// <param name="downmix">If true, the resulting AudioClip will be mono. 如果为 true，生成的 AudioClip 将是单声道。</param>
        /// <returns>A new AudioClip, or null if no audio was captured. 一个新的 AudioClip，如果未捕获任何音频，则为 null。</returns>
        public AudioClip GetCapturedAudioClip()
        {
            if (!IsInitialized)
            {

                return null;
            }


            var samples = GetCapturedAudioSamples();
            if (samples == null || samples.Length == 0)
            {
                return null;
            }


            // Determine the channel count of the resulting clip.
            int resultChannels = _audioState.ChannelCount;
            int resultSampleRate = _targetSampleRate > 0 ? _targetSampleRate : _audioState.SampleRate;

            // The length for AudioClip.Create is the number of samples *per channel*.
            int lengthSamplesPerChannel = samples.Length / resultChannels;

            AudioClip createdAudioClip = AudioClip.Create(
                $"CapturedClip_{resultSampleRate}_{resultChannels}_{DateTime.Now:HHmmss}",
                lengthSamplesPerChannel,
                resultChannels,
                resultSampleRate,
                false
            );

            createdAudioClip.SetData(samples, 0);
            return createdAudioClip;
        }

        private void EnsureBufferCapacity(int sampleRate, int channels)
        {
            if (sampleRate <= 0 || channels <= 0)
            {
                return;
            }

            int effectiveTargetSR = _targetSampleRate > 0 ? _targetSampleRate : sampleRate;
            int worstCaseRate = Math.Max(sampleRate, effectiveTargetSR);
            int desiredCapacity = Math.Max(1, worstCaseRate * channels * _maxCaptureDuration);

            lock (_lock)
            {
                if (_audioBuffer == null)
                {
                    _audioBuffer = new AudioBuffer(desiredCapacity);
                    return;
                }

                if (desiredCapacity <= _audioBuffer.Capacity)
                {
                    return;
                }

                var newBuffer = new AudioBuffer(desiredCapacity);
                int readable = _audioBuffer.ReadableCount;
                if (readable > 0)
                {
                    var temp = new float[readable];
                    _audioBuffer.Read(temp);
                    newBuffer.TryWriteExact(new ReadOnlySpan<float>(temp));
                }

                _audioBuffer = newBuffer;
            }
        }

    }
}
