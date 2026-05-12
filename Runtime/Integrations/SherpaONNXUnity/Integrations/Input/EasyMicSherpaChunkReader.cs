#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using Eitan.EasyMic.Runtime;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input
{
    /// <summary>
    /// Aggregates EasyMic transport frames into fixed-size mono chunks for Sherpa streaming inputs.
    /// Runs on AudioReader's worker thread; it must not call Unity APIs or Sherpa inference APIs.
    /// </summary>
    internal sealed class EasyMicSherpaChunkReader : AudioReader
    {
        private readonly int _expectedSampleRate;
        private readonly int _expectedChannelCount;
        private readonly int _chunkFrameCount;
        private readonly Action<float[], int> _onChunkReady;
        private readonly Action<int, int, int, int> _onFormatMismatch;

        private float[] _aggregateBuffer;
        private int _aggregateFill;

        public EasyMicSherpaChunkReader(
            int expectedSampleRate,
            int expectedChannelCount,
            int chunkFrameCount,
            Action<float[], int> onChunkReady,
            Action<int, int, int, int> onFormatMismatch)
            : base(capacitySeconds: 2)
        {
            _expectedSampleRate = Math.Max(1, expectedSampleRate);
            _expectedChannelCount = Math.Max(1, expectedChannelCount);
            _chunkFrameCount = Math.Max(1, chunkFrameCount);
            _onChunkReady = onChunkReady ?? throw new ArgumentNullException(nameof(onChunkReady));
            _onFormatMismatch = onFormatMismatch;
            _aggregateBuffer = new float[_chunkFrameCount];
        }

        public override void Initialize(AudioContext state)
        {
            _aggregateFill = 0;
            if (_aggregateBuffer == null || _aggregateBuffer.Length != _chunkFrameCount)
            {
                _aggregateBuffer = new float[_chunkFrameCount];
            }

            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            if (audiobuffer.IsEmpty)
            {
                return;
            }

            int sampleRate = Math.Max(1, CurrentSampleRate);
            int channels = Math.Max(1, CurrentChannelCount);
            if (sampleRate != _expectedSampleRate || channels != _expectedChannelCount)
            {
                _aggregateFill = 0;
                _onFormatMismatch?.Invoke(sampleRate, channels, _expectedSampleRate, _expectedChannelCount);
                return;
            }

            int usableLength = audiobuffer.Length - (audiobuffer.Length % channels);
            if (usableLength <= 0)
            {
                return;
            }

            int offset = 0;
            while (offset < usableLength)
            {
                int writable = _chunkFrameCount - _aggregateFill;
                int copy = Math.Min(writable, usableLength - offset);
                audiobuffer.Slice(offset, copy).CopyTo(_aggregateBuffer.AsSpan(_aggregateFill, copy));
                _aggregateFill += copy;
                offset += copy;

                if (_aggregateFill == _chunkFrameCount)
                {
                    var chunk = new float[_chunkFrameCount];
                    Array.Copy(_aggregateBuffer, chunk, _chunkFrameCount);
                    _aggregateFill = 0;
                    _onChunkReady(chunk, _expectedSampleRate);
                }
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            _aggregateFill = 0;
            _aggregateBuffer = null;
        }
    }
}
#endif
