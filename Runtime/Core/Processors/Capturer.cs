using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    public interface IAudioSink
    {
        /// <summary>
        /// Consume captured audio samples.
        /// Implementations may perform worker-thread work such as file I/O only when invoked behind
        /// an <see cref="AudioReader"/> dispatch path. Sinks that are called directly from native
        /// audio callbacks must be allocation-free, non-blocking, Unity-API-free, and file-I/O-free.
        /// </summary>
        /// <param name="samples">Interleaved PCM float samples.</param>
        /// <param name="sampleRate">Sample rate of the provided samples.</param>
        /// <param name="channels">Channel count of the provided samples.</param>
        void OnSamples(ReadOnlySpan<float> samples, int sampleRate, int channels);
    }

    public class Capturer : AudioReader
    {
        // Auto-managed preview cache heuristics tuned for editor playback.
        private const int DefaultPreviewSeconds = 12;
        private const int MinPreviewSeconds = 4;
        private const int MaxPreviewSeconds = 24;
        private const int PreviewMemoryBudgetBytes = 8 * 1024 * 1024; // 8 MB cap for preview cache
        private static readonly int PreviewMemoryBudgetSamples = PreviewMemoryBudgetBytes / sizeof(float);

        private AudioBuffer _audioBuffer;
        private readonly object _resizeLock = new object(); // only for buffer (re)allocation

        private AudioContext _AudioContext;
        private readonly int _targetSampleRate; // 0 => follow input rate
        private float[] _resampleWork; // reused buffer for resampled output
        private volatile bool _hasCapturedData;
        private IAudioSink _sink; // optional streaming sink (to disk, network, etc.)
        private int _frameStride = 1; // channels alignment for ring buffer

        public Capturer()
            : this(0) { }

        /// <param name="targetSampleRate">Optional desired capture sample rate. 0 means keep input rate.</param>
        public Capturer(int targetSampleRate)
        {
            _targetSampleRate = Math.Max(0, targetSampleRate);
        }

        public void SetSink(IAudioSink sink)
        {
            _sink = sink;
        }

        public override void Initialize(AudioContext state)
        {
            int captureRate = _targetSampleRate > 0 ? _targetSampleRate : state.SampleRate; // samples stored at this rate
            int ch = Math.Max(1, state.ChannelCount);
            _frameStride = ch;

            EnsureBufferCapacity(captureRate, ch);
            _AudioContext = state;
            _resampleWork = Array.Empty<float>();
            _hasCapturedData = false;
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
                TryWriteSamples(audiobuffer);
                _sink?.OnSamples(audiobuffer, dstSR, ch);
                return;
            }

            if (srcSR == dstSR)
            {
                TryWriteSamples(audiobuffer);
                _sink?.OnSamples(audiobuffer, dstSR, ch);
                return;
            }

            int inFrames = audiobuffer.Length / ch;
            if (inFrames <= 0)
            {
                return;
            }

            double ratio = (double)dstSR / Math.Max(1, srcSR);
            int outFrames = (int)(inFrames * ratio);
            if (outFrames <= 0)
            {
                return;
            }

            int outSamples = outFrames * ch;
            if (_resampleWork.Length < outSamples)
            {
                _resampleWork = new float[outSamples];
            }

            double step = (double)srcSR / dstSR; // source frames per one output frame
            for (int chIdx = 0; chIdx < ch; chIdx++)
            {
                int outBase = chIdx;
                for (int of = 0; of < outFrames; of++)
                {
                    double phase = of * step;
                    int i0 = (int)phase;
                    double t = phase - i0;
                    int i1 = Math.Min(inFrames - 1, i0 + 1);
                    int src0 = i0 * ch + chIdx;
                    int src1 = i1 * ch + chIdx;
                    float s0 = audiobuffer[src0];
                    float s1 = audiobuffer[src1];
                    _resampleWork[outBase + of * ch] = (float)(s0 + (s1 - s0) * t);
                }
            }

            var span = new ReadOnlySpan<float>(_resampleWork, 0, outSamples);
            TryWriteSamples(span);
            _sink?.OnSamples(span, dstSR, ch);
        }

        /// <summary>
        /// Gets the captured audio samples. Can optionally downmix to mono.
        /// 获取捕获的音频样本。可以选择性地混音到单声道。
        /// </summary>
        /// <param name="downmix">If true, multi-channel audio will be downmixed to mono. 如果为 true，多声道音频将被缩混为单声道。</param>
        /// <returns>An array of audio samples. The channel count depends on the 'downmix' parameter. 一个音频样本数组。声道数取决于 'downmix' 参数。</returns>
        public float[] GetCapturedAudioSamples()
        {
            var buffer = _audioBuffer;
            if ((!IsInitialized && !_hasCapturedData) || buffer == null)
            {
                return null;
            }

            int readable = buffer.ReadableCount;
            if (readable <= 0)
            {
                if (!IsInitialized)
                {
                    _hasCapturedData = false;
                }
                return Array.Empty<float>();
            }

            var samples = new float[readable];
            if (samples.Length == 0)
            {
                return samples;
            }

            buffer.Read(samples);
            _hasCapturedData = buffer.ReadableCount > 0;
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
            if (!IsInitialized && !_hasCapturedData)
            {
                return null;
            }


            var samples = GetCapturedAudioSamples();
            if (samples == null || samples.Length == 0)
            {
                return null;
            }


            // Determine the channel count of the resulting clip.
            // IMPORTANT: Don't use the initialization AudioContext for format.
            // The pipeline may change ChannelCount/SampleRate at runtime (e.g. downmix to mono).
            // We must use the last observed runtime format to avoid metadata mismatches that change
            // playback speed/pitch when constructing the AudioClip.
            int resultChannels = CurrentChannelCount > 0
                ? CurrentChannelCount
                : Math.Max(1, _AudioContext?.ChannelCount ?? 1);

            int srcSampleRate = CurrentSampleRate > 0
                ? CurrentSampleRate
                : Math.Max(1, _AudioContext?.SampleRate ?? 48000);

            int resultSampleRate = _targetSampleRate > 0 ? _targetSampleRate : srcSampleRate;

            // The length for AudioClip.Create is the number of samples *per channel*.
            int alignedSamples = samples.Length - (samples.Length % resultChannels);
            if (alignedSamples <= 0)
            {
                return null;
            }

            int lengthSamplesPerChannel = alignedSamples / resultChannels;

            AudioClip createdAudioClip = AudioClip.Create(
                $"CapturedClip_{resultSampleRate}_{resultChannels}_{DateTime.Now:HHmmss}",
                lengthSamplesPerChannel,
                resultChannels,
                resultSampleRate,
                false
            );

            if (alignedSamples != samples.Length)
            {
                var trimmed = new float[alignedSamples];
                Array.Copy(samples, trimmed, alignedSamples);
                createdAudioClip.SetData(trimmed, 0);
            }
            else
            {
                createdAudioClip.SetData(samples, 0);
            }
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
            int desiredCapacity = ComputePreviewCapacity(worstCaseRate, channels);

            var current = _audioBuffer;
            if (current != null && desiredCapacity <= current.Capacity && current.FrameStride == channels)
            {
                return;
            }

            lock (_resizeLock)
            {
                if (_audioBuffer == null)
                {
                    _audioBuffer = new AudioBuffer(desiredCapacity, channels);
                    return;
                }

                bool needsResize = desiredCapacity > _audioBuffer.Capacity || _audioBuffer.FrameStride != channels;
                if (!needsResize)
                {
                    return;
                }

                var newBuffer = new AudioBuffer(desiredCapacity, channels);
                int readable = _audioBuffer.ReadableCount;
                if (readable > 0)
                {
                    var temp = new float[readable];
                    _audioBuffer.Read(temp);
                    newBuffer.Write(temp);
                    _hasCapturedData = true;
                }

                _audioBuffer = newBuffer;
            }
        }

        private static int ComputePreviewCapacity(int sampleRate, int channels)
        {
            channels = Math.Max(1, channels);
            sampleRate = Math.Max(8000, sampleRate);

            long defaultSamples = (long)sampleRate * DefaultPreviewSeconds * channels;
            long minSamples = (long)sampleRate * MinPreviewSeconds * channels;
            long maxSamples = (long)sampleRate * MaxPreviewSeconds * channels;
            long budgetSamples = PreviewMemoryBudgetSamples > 0 ? PreviewMemoryBudgetSamples : int.MaxValue;

            long cappedDefault = Math.Min(defaultSamples, budgetSamples);
            long cappedMax = Math.Min(maxSamples, budgetSamples);

            long chosen = Math.Max(cappedDefault, minSamples);
            chosen = Math.Min(chosen, cappedMax);
            chosen = Math.Max(chosen, channels);
            return chosen >= int.MaxValue ? int.MaxValue : (int)chosen;
        }

        private void TryWriteSamples(ReadOnlySpan<float> samples)
        {
            var buf = _audioBuffer;
            if (buf == null)
            {
                return;
            }

            int written = buf.Write(samples);
            if (written > 0)
            {
                _hasCapturedData = true;
            }
        }

    }
}
