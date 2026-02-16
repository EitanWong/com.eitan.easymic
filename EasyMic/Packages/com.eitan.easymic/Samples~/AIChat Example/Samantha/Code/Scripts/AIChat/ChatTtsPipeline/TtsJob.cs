#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System;
using System.Collections.Concurrent;
using System.Diagnostics;

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
        public Exception Error;
        private readonly ConcurrentQueue<float[]> _streamChunks = new ConcurrentQueue<float[]>();

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

            _streamChunks.Enqueue(samples);
        }

        public bool TryDequeueStreamChunk(out float[] samples) => _streamChunks.TryDequeue(out samples);

        public bool HasPendingChunks => !_streamChunks.IsEmpty;

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
