using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime{
    public class AudioCapturer : AudioReader
    {
        private AudioBuffer _audioBuffer;
        private readonly int _maxCaptureDuration;

        private AudioState _audioState;

        public AudioCapturer(int maxDuration)
        {
            this._maxCaptureDuration = maxDuration;
        }

        public override void Initialize(AudioState state)
        {
            // 使用采样率与通道数推导容量，避免依赖 state.Length 初始化值
            int totalSamples = Math.Max(1, state.SampleRate * state.ChannelCount * _maxCaptureDuration);
            _audioBuffer = new AudioBuffer(totalSamples);
            _audioState = state;
            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            // 整帧入队；不足则丢弃，保证帧原子性
            _audioBuffer.TryWriteExact(audiobuffer);
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
                return null;

            // Create a buffer and read the captured audio data.
            var samples = new float[_audioBuffer.ReadableCount];
            if (samples.Length == 0) return samples;
            _audioBuffer.Read(samples);


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
                return null;

            var samples = GetCapturedAudioSamples();
            if (samples == null || samples.Length == 0)
            {
                return null;
            }


            // Determine the channel count of the resulting clip.
            int resultChannels = _audioState.ChannelCount;
            int resultSampleRate = _audioState.SampleRate;

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

    }
}
