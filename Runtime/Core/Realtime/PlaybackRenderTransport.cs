using System;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Pre-renders managed playback graph blocks away from the miniaudio callback.
    /// The callback consumes interleaved float samples from the ring and zero-fills underruns.
    /// </summary>
    internal sealed class PlaybackRenderTransport : IDisposable
    {
        internal delegate void MixedFrameSink(ReadOnlySpan<float> interleaved, int channels, int sampleRate);

        private readonly UnsafeAudioRingBuffer _queue;
        private readonly AudioMixer _mixer;
        private readonly RealtimeAudioTelemetry _telemetry;
        private readonly MixedFrameSink _mixedFrameRaw;
        private readonly MixedFrameSink _mixedFrameDeferred;
        private readonly int _channels;
        private readonly int _sampleRate;
        private readonly int _blockSamples;
        private readonly int _targetBufferedSamples;
        private readonly int _lowWatermarkSamples;
        private readonly int _highWatermarkSamples;
        private readonly AutoResetEvent _wakeEvent;
        private readonly Thread _worker;
        private float[] _scratch;
        private int _running;
        private int _disposed;

        public PlaybackRenderTransport(
            AudioMixer mixer,
            int channels,
            int sampleRate,
            EasyMicLatencyProfile profile,
            RealtimeAudioTelemetry telemetry,
            MixedFrameSink mixedFrameRaw,
            MixedFrameSink mixedFrameDeferred)
        {
            _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
            _channels = Math.Max(1, channels);
            _sampleRate = Math.Max(8000, sampleRate);
            _telemetry = telemetry ?? new RealtimeAudioTelemetry();
            _mixedFrameRaw = mixedFrameRaw;
            _mixedFrameDeferred = mixedFrameDeferred;

            int blockFrames = CalculateBlockFrames(_sampleRate, profile);
            _blockSamples = Math.Max(_channels * 64, blockFrames * _channels);
            int queueSamples = CalculateQueueSamples(_channels, _sampleRate, profile);
            int targetFrames = CalculateTargetBufferedFrames(_sampleRate, profile);
            _targetBufferedSamples = Math.Min(queueSamples - _blockSamples, Math.Max(_blockSamples * 2, targetFrames * _channels));
            _lowWatermarkSamples = Math.Max(_blockSamples, (int)(_targetBufferedSamples * 0.35f));
            _highWatermarkSamples = Math.Max(_lowWatermarkSamples + _blockSamples, (int)(_targetBufferedSamples * 0.85f));
            _scratch = new float[_blockSamples];
            _queue = new UnsafeAudioRingBuffer(queueSamples, _channels);
            _wakeEvent = new AutoResetEvent(false);
            _mixer.PrepareForRealtimeRender(_blockSamples, _channels, _sampleRate);

            Volatile.Write(ref _running, 1);
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "EasyMic-PlaybackRenderTransport"
            };
            _worker.Start();
        }

        public int ReadInto(Span<float> destination)
        {
            if (Volatile.Read(ref _disposed) != 0 || destination.IsEmpty)
            {
                return 0;
            }

            int aligned = destination.Length - destination.Length % _channels;
            if (aligned <= 0)
            {
                return 0;
            }

            int read = _queue.Read(destination.Slice(0, aligned));
            if (read < aligned)
            {
                destination.Slice(read, aligned - read).Clear();
                _telemetry.IncrementTransportUnderrun();
                _telemetry.AddZeroFilledFrames((aligned - read) / _channels);
            }

            _telemetry.ObserveQueueDepth(_queue.ReadableCount);
            return read;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _running, 0);
            try { _wakeEvent.Set(); } catch { }
            try
            {
                if (_worker.IsAlive)
                {
                    _worker.Join(1000);
                }
            }
            catch { }

            try { _queue.Dispose(); } catch { }
            try { _wakeEvent.Dispose(); } catch { }
            _scratch = null;
        }

        private void WorkerLoop()
        {
            while (Volatile.Read(ref _running) != 0)
            {
                int readable = _queue.ReadableCount;
                if (readable < _lowWatermarkSamples)
                {
                    RenderUntil(_targetBufferedSamples);
                    continue;
                }

                if (readable < _highWatermarkSamples && _queue.WritableCount >= _blockSamples)
                {
                    RenderOneBlock();
                    continue;
                }

                try { _wakeEvent.WaitOne(2); } catch { }
            }
        }

        private void RenderUntil(int targetSamples)
        {
            int guard = 0;
            while (Volatile.Read(ref _running) != 0 &&
                   _queue.ReadableCount < targetSamples &&
                   _queue.WritableCount >= _blockSamples &&
                   guard++ < 64)
            {
                RenderOneBlock();
            }

            if (_queue.ReadableCount < _lowWatermarkSamples)
            {
                _telemetry.IncrementWorkerLate();
            }
        }

        private void RenderOneBlock()
        {
            using var _ = EasyMicThreading.EnterTransportThread();
            var scratch = _scratch;
            if (scratch == null)
            {
                return;
            }

            var span = new Span<float>(scratch, 0, _blockSamples);
            span.Clear();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();

            try
            {
                _mixer.RenderMaster(span, _channels, _sampleRate);
            }
            catch
            {
                _telemetry.IncrementProcessorException();
                span.Clear();
            }
            finally
            {
                _telemetry.ObserveWorkerTicks(System.Diagnostics.Stopwatch.GetTimestamp() - start);
            }

            int written = _queue.Write(span);
            if (written < span.Length)
            {
                _telemetry.IncrementTransportOverrun();
            }

            _telemetry.ObserveQueueDepth(_queue.ReadableCount);

            if (written > 0)
            {
                var mixed = new ReadOnlySpan<float>(scratch, 0, written);
                try { _mixedFrameRaw?.Invoke(mixed, _channels, _sampleRate); } catch { }
                try { _mixedFrameDeferred?.Invoke(mixed, _channels, _sampleRate); } catch { }
            }
        }

        private static int CalculateBlockFrames(int sampleRate, EasyMicLatencyProfile profile)
        {
            switch (profile)
            {
                case EasyMicLatencyProfile.UltraLowLatency:
                    return Math.Max(64, sampleRate / 200);
                case EasyMicLatencyProfile.SafeStreaming:
                    return Math.Max(128, sampleRate / 50);
                case EasyMicLatencyProfile.Balanced:
                    return Math.Max(128, sampleRate / 100);
                default:
                    return Math.Max(96, sampleRate / 100);
            }
        }

        private static int CalculateQueueSamples(int channels, int sampleRate, EasyMicLatencyProfile profile)
        {
            double seconds;
            switch (profile)
            {
                case EasyMicLatencyProfile.UltraLowLatency:
                    seconds = 0.04;
                    break;
                case EasyMicLatencyProfile.SafeStreaming:
                    seconds = 0.30;
                    break;
                case EasyMicLatencyProfile.Balanced:
                    seconds = 0.12;
                    break;
                default:
                    seconds = 0.08;
                    break;
            }

            return Math.Max(channels * 1024, (int)Math.Ceiling(channels * sampleRate * seconds));
        }

        private static int CalculateTargetBufferedFrames(int sampleRate, EasyMicLatencyProfile profile)
        {
            int latencyMs;
            switch (profile)
            {
                case EasyMicLatencyProfile.UltraLowLatency:
                    latencyMs = 20;
                    break;
                case EasyMicLatencyProfile.SafeStreaming:
                    latencyMs = 160;
                    break;
                case EasyMicLatencyProfile.Balanced:
                    latencyMs = 80;
                    break;
                default:
                    latencyMs = 45;
                    break;
            }

            return Math.Max(64, sampleRate * latencyMs / 1000);
        }
    }
}
