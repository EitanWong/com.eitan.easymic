using System;
using System.Threading;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// 一个高性能、低延迟、无锁的环回播放器（最终平滑版）。
    /// 它从麦克风实时接收音频数据，并立即通过指定的 AudioSource 播放出来。
    /// 核心优化：
    /// 1. **大容量安全缓冲**：通过大幅增加缓冲容量和预缓冲阈值，为系统提供极高的抗抖动能力，从根源上防止缓冲区耗尽。
    /// 2. **防御性消费**：只有当缓冲区数据足够填满整个播放请求时才进行读取，确保了播放的绝对连续性。
    /// 3. **无锁设计**：采用单生产者/单消费者(SPSC)队列模型，避免使用锁，消除了OnAudioRead中的阻塞风险。
    /// 4. **自管理预缓冲**：在音频回调内部处理预缓冲逻辑，移除了对外部轮询的依赖。
    /// </summary>
    public class LoopbackPlayer : AudioReader
    {
        private readonly AudioSource _source;
        private AudioClip _streamClip;
        private float[] _circularBuffer;
        
        // --- 无锁环形缓冲区的核心 ---
        private volatile int _writePosition;
        private volatile int _readPosition;
        
        // --- 播放状态与预缓冲控制 ---
        private bool _isPrebufferingComplete;
        private int _prebufferThreshold;


        public LoopbackPlayer(AudioSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source), "AudioSource component cannot be null.");
            }
            this._source = source;
            this._source.playOnAwake = false;
            this._source.loop = true;
        }

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);

            // --- 关键修改：增加缓冲区容量和阈值 ---
            // 将缓冲区容量增加到500毫秒，以提供非常大的抖动吸收空间。
            int bufferCapacity = state.SampleRate * state.ChannelCount / 2; // 500ms buffer
            _circularBuffer = new float[bufferCapacity];
            
            // 将预缓冲阈值提高到150毫秒。这是确保稳定性的最关键参数。
            // 这会稍微增加启动延迟，但能从根本上防止后续播放中的数据欠载。
            _prebufferThreshold = state.SampleRate * state.ChannelCount * 3 / 20; // 150ms

            _writePosition = 0;
            _readPosition = 0;
            _isPrebufferingComplete = false;

            // 音频片段的长度保持不变，以实现低延迟的输出响应。
            int clipBufferLength = state.SampleRate * state.ChannelCount / 50; // 20ms
            _streamClip = AudioClip.Create("RealtimeLoopbackStream_Stable", clipBufferLength, state.ChannelCount, state.SampleRate, true, OnAudioSourceStreamRead);

            _source.clip = _streamClip;
            
            // 直接在这里开始播放。播放器将在OnAudioSourceStreamRead中自行处理预缓冲。
            _source.Play();
        }

        public override void OnAudioRead(ReadOnlySpan<float> audioBuffer, AudioState state)
        {
            int readPos = _readPosition;
            int writePos = _writePosition;
            int bufferLength = _circularBuffer.Length;

            int freeSpace = (readPos - writePos - 1 + bufferLength) % bufferLength;
            if (freeSpace == 0)
            {
                return;
            }

            int amountToWrite = Math.Min(audioBuffer.Length, freeSpace);
            ReadOnlySpan<float> dataToWrite = audioBuffer.Slice(0, amountToWrite);

            var bufferSpan = new Span<float>(_circularBuffer);
            if (writePos + dataToWrite.Length > bufferLength)
            {
                int firstChunkSize = bufferLength - writePos;
                dataToWrite.Slice(0, firstChunkSize).CopyTo(bufferSpan.Slice(writePos));
                dataToWrite.Slice(firstChunkSize).CopyTo(bufferSpan.Slice(0));
            }
            else
            {
                dataToWrite.CopyTo(bufferSpan.Slice(writePos));
            }
            
            Thread.MemoryBarrier(); 
            _writePosition = (writePos + dataToWrite.Length) % bufferLength;
        }
        
        private void OnAudioSourceStreamRead(float[] data)
        {
            int writePos = _writePosition;
            int readPos = _readPosition;
            int bufferLength = _circularBuffer.Length;
            int availableData = (writePos - readPos + bufferLength) % bufferLength;

            // 1. 检查初始预缓冲是否完成
            if (!_isPrebufferingComplete)
            {
                if (availableData >= _prebufferThreshold)
                {
                    _isPrebufferingComplete = true;
                }
                else
                {
                    Array.Clear(data, 0, data.Length);
                    return;
                }
            }
            
            // --- 关键：严格的“防御性消费”逻辑 ---
            // 只有当缓冲区中的数据足够填满整个请求时，才进行读取。
            if (availableData >= data.Length)
            {
                // 缓冲区数据充足，正常读取
                var requestedDataSpan = new Span<float>(data);
                var bufferSpan = new Span<float>(_circularBuffer);
                int amountToRead = data.Length;
            
                if (readPos + amountToRead > bufferLength)
                {
                    int firstChunkSize = bufferLength - readPos;
                    bufferSpan.Slice(readPos, firstChunkSize).CopyTo(requestedDataSpan.Slice(0));
                    bufferSpan.Slice(0, amountToRead - firstChunkSize).CopyTo(requestedDataSpan.Slice(firstChunkSize));
                }
                else
                {
                    bufferSpan.Slice(readPos, amountToRead).CopyTo(requestedDataSpan.Slice(0));
                }

                Thread.MemoryBarrier();
                _readPosition = (readPos + amountToRead) % bufferLength;
            }
            else
            {
                // 发生欠载（underrun）。由于我们有非常大的安全缓冲，这应该是一个极罕见的事件。
                // 在这种情况下，播放静音是最安全的选择，它能给生产者一个完整的周期来恢复。
                Array.Clear(data, 0, data.Length);
            }
        }

        public override void Dispose()
        {
            Stop();
            base.Dispose();
        }
        
        public void Stop()
        {
            if (_source != null)
            {
                _source.Stop();
            }
            _isPrebufferingComplete = false;
            _writePosition = 0;
            _readPosition = 0;
        }
    }
}
