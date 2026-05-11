using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Bounded capture data-plane. The miniaudio callback only writes framed PCM into
    /// the SPSC ring; managed processing runs on the transport worker.
    /// </summary>
    internal sealed class CaptureAudioTransport : IDisposable
    {
        private readonly UnsafeAudioRingBuffer _queue;
        private readonly AudioPipeline _pipeline;
        private readonly AudioContext _workerState;
        private readonly RealtimeAudioTelemetry _telemetry;
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly AutoResetEvent _signal;
        private readonly Thread _worker;
        private readonly float[] _header = new float[1];
        private float[] _workerBuffer;
        private int _running;
        private int _disposed;

        public CaptureAudioTransport(
            AudioPipeline pipeline,
            int channels,
            int sampleRate,
            EasyMicLatencyProfile profile,
            RealtimeAudioTelemetry telemetry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _channels = Math.Max(1, channels);
            _sampleRate = Math.Max(8000, sampleRate);
            _telemetry = telemetry ?? new RealtimeAudioTelemetry();

            int queueSamples = CalculateQueueSamples(_channels, _sampleRate, profile);
            _queue = new UnsafeAudioRingBuffer(queueSamples, 1);
            _workerState = new AudioContext(_channels, _sampleRate, 0);
            _workerBuffer = new float[Math.Max(_channels * 64, _channels * (_sampleRate / 100))];
            _signal = new AutoResetEvent(false);
            Volatile.Write(ref _running, 1);
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "EasyMic-CaptureTransport"
            };
            _worker.Start();
        }

        public bool TryWrite(ReadOnlySpan<float> interleaved, int frameCount)
        {
            if (Volatile.Read(ref _disposed) != 0 || interleaved.IsEmpty)
            {
                return false;
            }

            int samples = Math.Min(interleaved.Length, Math.Max(0, frameCount) * _channels);
            samples -= samples % _channels;
            if (samples <= 0)
            {
                return false;
            }

            int required = 1 + samples;
            if (_queue.WritableCount < required)
            {
                _telemetry.IncrementTransportOverrun();
                _telemetry.AddFramesDropped(samples / _channels);
                return false;
            }

            Span<float> header = stackalloc float[1];
            header[0] = samples;
            if (!_queue.TryWriteExact(header))
            {
                _telemetry.IncrementTransportOverrun();
                _telemetry.AddFramesDropped(samples / _channels);
                return false;
            }

            if (!_queue.TryWriteExact(interleaved.Slice(0, samples)))
            {
                _telemetry.IncrementTransportOverrun();
                _telemetry.AddFramesDropped(samples / _channels);
                return false;
            }

            _telemetry.AddFramesReceived(samples / _channels);
            _telemetry.ObserveQueueDepth(_queue.ReadableCount);
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _running, 0);
            try { _signal.Set(); } catch { }
            try
            {
                if (_worker.IsAlive)
                {
                    _worker.Join(1000);
                }
            }
            catch { }

            try { _signal.Dispose(); } catch { }
            try { _queue.Dispose(); } catch { }
            _workerBuffer = null;
        }

        private void WorkerLoop()
        {
            while (Volatile.Read(ref _running) != 0)
            {
                try { _signal.WaitOne(2); } catch { }
                Drain(discardIncomplete: false);
            }

            Drain(discardIncomplete: true);
        }

        private void Drain(bool discardIncomplete)
        {
            while (TryDrainOne(discardIncomplete))
            {
            }
        }

        private bool TryDrainOne(bool discardIncomplete)
        {
            if (_queue.ReadableCount < 1 || _queue.Peek(_header) < 1)
            {
                return false;
            }

            int samples = (int)_header[0];
            if (samples <= 0 || (samples % _channels) != 0)
            {
                _queue.Skip(1);
                _telemetry.IncrementTransportUnderrun();
                return true;
            }

            int required = 1 + samples;
            if (_queue.ReadableCount < required)
            {
                if (discardIncomplete)
                {
                    _queue.Skip(_queue.ReadableCount);
                    _telemetry.IncrementTransportUnderrun();
                }

                return false;
            }

            EnsureWorkerBuffer(samples);
            _queue.Skip(1);
            if (!_queue.TryReadExact(_workerBuffer, samples))
            {
                _telemetry.IncrementTransportUnderrun();
                return false;
            }

            _workerState.ChannelCount = _channels;
            _workerState.SampleRate = _sampleRate;
            _workerState.Length = samples;

            try
            {
                _pipeline.OnAudioPass(new Span<float>(_workerBuffer, 0, samples), _workerState);
            }
            catch
            {
            }

            _telemetry.ObserveQueueDepth(_queue.ReadableCount);
            return true;
        }

        private void EnsureWorkerBuffer(int samples)
        {
            var buffer = _workerBuffer;
            if (buffer != null && buffer.Length >= samples)
            {
                return;
            }

            _workerBuffer = new float[Math.Max(samples, _channels * 256)];
        }

        private static int CalculateQueueSamples(int channels, int sampleRate, EasyMicLatencyProfile profile)
        {
            double seconds;
            switch (profile)
            {
                case EasyMicLatencyProfile.UltraLowLatency:
                    seconds = 0.08;
                    break;
                case EasyMicLatencyProfile.LowLatency:
                    seconds = 0.12;
                    break;
                case EasyMicLatencyProfile.SafeStreaming:
                    seconds = 0.50;
                    break;
                default:
                    seconds = 0.25;
                    break;
            }

            return Math.Max(channels * 512, (int)Math.Ceiling(channels * sampleRate * seconds));
        }
    }
}
