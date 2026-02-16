#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Eitan.EasyMic.Runtime.Mono.Components.TTS.Internal
{
    internal sealed class SpeechSynthesisResult
    {
        private readonly ConcurrentQueue<float[]> _chunks = new ConcurrentQueue<float[]>();
        private long _startTicks;
        private long _lastChunkTicks;
        private int _hasStarted;
        private int _hasChunks;

        public int SequenceNumber { get; }
        public string Sentence { get; }
        public int Channels { get; private set; }
        public int SampleRate { get; private set; }
        public volatile bool IsComplete;
        public volatile bool IsFailed;
        public Exception Error;

        public bool HasPendingChunks => !_chunks.IsEmpty;
        public bool HasReceivedChunks => Volatile.Read(ref _hasChunks) == 1;

        public SpeechSynthesisResult(int sequenceNumber, string sentence)
        {
            SequenceNumber = sequenceNumber;
            Sentence = sentence ?? string.Empty;
        }

        public void ConfigureFormat(int channels, int sampleRate)
        {
            if (Channels == 0)
            {
                Channels = channels;
            }

            if (SampleRate == 0)
            {
                SampleRate = sampleRate;
            }
        }

        public void MarkStarted()
        {
            if (Interlocked.Exchange(ref _hasStarted, 1) == 0)
            {
                Interlocked.Exchange(ref _startTicks, DateTime.UtcNow.Ticks);
            }
        }

        public TimeSpan GetIdleDuration()
        {
            long lastTicks = Interlocked.Read(ref _lastChunkTicks);
            if (lastTicks > 0)
            {
                return DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
            }

            long startTicks = Interlocked.Read(ref _startTicks);
            if (startTicks > 0)
            {
                return DateTime.UtcNow - new DateTime(startTicks, DateTimeKind.Utc);
            }

            return TimeSpan.Zero;
        }

        public void EnqueueChunk(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            if (Volatile.Read(ref _hasStarted) == 0)
            {
                MarkStarted();
            }

            _chunks.Enqueue(samples);
            Interlocked.Exchange(ref _hasChunks, 1);
            Interlocked.Exchange(ref _lastChunkTicks, DateTime.UtcNow.Ticks);
        }

        public bool TryDequeueChunk(out float[] samples) => _chunks.TryDequeue(out samples);

        public void MarkComplete()
        {
            IsComplete = true;
        }

        public void MarkFailed(Exception error)
        {
            Error = error;
            IsFailed = true;
            IsComplete = true;
        }
    }
}
#endif
