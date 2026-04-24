#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class TtsJob
    {
        public readonly int SequenceNumber;
        public readonly string Sentence;
        public readonly Stopwatch Stopwatch;
        public float[] AudioSamples;
        public int SampleRate;
        public int Channels;
        public volatile bool IsStreaming;
        public volatile bool StreamingCompleted;
        public volatile bool HasStreamingRegistration;
        public volatile bool IsComplete;
        public volatile bool IsFailed;
        public volatile bool HasReceivedChunks;
        public Exception Error;
        private readonly ConcurrentQueue<float[]> _streamChunks = new ConcurrentQueue<float[]>();
        private long _lastChunkUtcTicks;

        public TtsJob(int sequenceNumber, string sentence)
        {
            SequenceNumber = sequenceNumber;
            Sentence = sentence;
            Stopwatch = Stopwatch.StartNew();
        }

        public void MarkComplete(float[] samples, int channels, int sampleRate)
        {
            AudioSamples = samples;
            Channels = channels;
            SampleRate = sampleRate;
            Stopwatch.Stop();
            IsComplete = true;
        }

        public void BeginStreaming(int channels, int sampleRate)
        {
            IsStreaming = true;
            Channels = channels;
            SampleRate = sampleRate;
        }

        public void EnqueueStreamChunk(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            HasReceivedChunks = true;
            Interlocked.Exchange(ref _lastChunkUtcTicks, DateTime.UtcNow.Ticks);
            _streamChunks.Enqueue(samples);
        }

        public bool TryDequeueStreamChunk(out float[] samples) => _streamChunks.TryDequeue(out samples);

        public bool HasPendingChunks => !_streamChunks.IsEmpty;

        public TimeSpan GetIdleDuration()
        {
            long ticks = Interlocked.Read(ref _lastChunkUtcTicks);
            if (ticks <= 0)
            {
                return Stopwatch.Elapsed;
            }

            long delta = DateTime.UtcNow.Ticks - ticks;
            return delta > 0 ? TimeSpan.FromTicks(delta) : TimeSpan.Zero;
        }

        public void MarkStreamingCompleted()
        {
            StreamingCompleted = true;
            IsComplete = true;
            Stopwatch.Stop();
        }

        public void MarkFailed(Exception error)
        {
            Error = error;
            Stopwatch.Stop();
            IsFailed = true;
        }
    }
}
#endif
