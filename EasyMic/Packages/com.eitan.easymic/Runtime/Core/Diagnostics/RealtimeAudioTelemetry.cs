using System;
using System.Diagnostics;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Realtime-safe counters. Audio threads only write atomics and integer timestamps;
    /// formatting and interpretation happen on control/diagnostics threads.
    /// </summary>
    internal sealed class RealtimeAudioTelemetry
    {
        private long _callbackCount;
        private long _callbackExceptions;
        private long _callbackTicksTotal;
        private long _callbackTicksMax;
        private long _transportOverruns;
        private long _transportUnderruns;
        private long _framesReceived;
        private long _framesDropped;
        private long _zeroFilledFrames;
        private long _workerLateCount;
        private long _workerTicksMax;
        private long _processorExceptions;
        private long _eventQueueDrops;
        private int _lastQueueDepthSamples;
        private int _minQueueDepthSamples = int.MaxValue;
        private int _maxQueueDepthSamples;
        private int _activeCallbacks;
        private int _lastCallbackThreadId;

        public long BeginCallback()
        {
            Interlocked.Increment(ref _callbackCount);
            Interlocked.Increment(ref _activeCallbacks);
#if EASYMIC_RT_DIAGNOSTICS
            if (Volatile.Read(ref _lastCallbackThreadId) == 0)
            {
                Volatile.Write(ref _lastCallbackThreadId, Thread.CurrentThread.ManagedThreadId);
            }
            return Stopwatch.GetTimestamp();
#else
            return 0;
#endif
        }

        public void EndCallback(long startTimestamp)
        {
            Interlocked.Decrement(ref _activeCallbacks);
#if EASYMIC_RT_DIAGNOSTICS
            long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
            if (elapsed < 0)
            {
                return;
            }

            Interlocked.Add(ref _callbackTicksTotal, elapsed);
            UpdateMax(ref _callbackTicksMax, elapsed);
#endif
        }

        public void IncrementCallbackException()
        {
            Interlocked.Increment(ref _callbackExceptions);
        }

        public void AddFramesReceived(int frames)
        {
            if (frames > 0)
            {
                Interlocked.Add(ref _framesReceived, frames);
            }
        }

        public void AddFramesDropped(int frames)
        {
            if (frames > 0)
            {
                Interlocked.Add(ref _framesDropped, frames);
            }
        }

        public void AddZeroFilledFrames(int frames)
        {
            if (frames > 0)
            {
                Interlocked.Add(ref _zeroFilledFrames, frames);
            }
        }

        public void IncrementWorkerLate()
        {
            Interlocked.Increment(ref _workerLateCount);
        }

        public void ObserveWorkerTicks(long ticks)
        {
            if (ticks > 0)
            {
                UpdateMax(ref _workerTicksMax, ticks);
            }
        }

        public void IncrementProcessorException()
        {
            Interlocked.Increment(ref _processorExceptions);
        }

        public void IncrementEventQueueDrop()
        {
            Interlocked.Increment(ref _eventQueueDrops);
        }

        public void IncrementTransportOverrun()
        {
            Interlocked.Increment(ref _transportOverruns);
        }

        public void IncrementTransportUnderrun()
        {
            Interlocked.Increment(ref _transportUnderruns);
        }

        public void ObserveQueueDepth(int samples)
        {
            if (samples < 0)
            {
                samples = 0;
            }

            Volatile.Write(ref _lastQueueDepthSamples, samples);
            UpdateMin(ref _minQueueDepthSamples, samples);
            UpdateMax(ref _maxQueueDepthSamples, samples);
        }

        public Snapshot GetSnapshot()
        {
            return new Snapshot(
                Interlocked.Read(ref _callbackCount),
                Interlocked.Read(ref _callbackTicksTotal),
                Interlocked.Read(ref _callbackTicksMax),
                Interlocked.Read(ref _transportOverruns),
                Interlocked.Read(ref _transportUnderruns),
                Interlocked.Read(ref _framesReceived),
                Interlocked.Read(ref _framesDropped),
                Interlocked.Read(ref _zeroFilledFrames),
                Interlocked.Read(ref _workerLateCount),
                Interlocked.Read(ref _workerTicksMax),
                Interlocked.Read(ref _processorExceptions),
                Interlocked.Read(ref _eventQueueDrops),
                Volatile.Read(ref _lastQueueDepthSamples),
                NormalizeMin(Volatile.Read(ref _minQueueDepthSamples)),
                Volatile.Read(ref _maxQueueDepthSamples),
                Volatile.Read(ref _activeCallbacks),
                Volatile.Read(ref _lastCallbackThreadId),
                Interlocked.Read(ref _callbackExceptions),
                Stopwatch.Frequency);
        }

        public EasyMicTelemetrySnapshot GetPublicSnapshot()
        {
            return new EasyMicTelemetrySnapshot(GetSnapshot());
        }

        private static void UpdateMax(ref long target, long value)
        {
            long current;
            do
            {
                current = Volatile.Read(ref target);
                if (value <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }

        private static void UpdateMax(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (value <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }

        private static void UpdateMin(ref int target, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref target);
                if (value >= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref target, value, current) != current);
        }

        private static int NormalizeMin(int value)
        {
            return value == int.MaxValue ? 0 : value;
        }

        internal readonly struct Snapshot
        {
            public Snapshot(
                long callbackCount,
                long callbackTicksTotal,
                long callbackTicksMax,
                long transportOverruns,
                long transportUnderruns,
                long framesReceived,
                long framesDropped,
                long zeroFilledFrames,
                long workerLateCount,
                long workerTicksMax,
                long processorExceptions,
                long eventQueueDrops,
                int lastQueueDepthSamples,
                int minQueueDepthSamples,
                int maxQueueDepthSamples,
                int activeCallbacks,
                int lastCallbackThreadId,
                long callbackExceptions,
                long stopwatchFrequency)
            {
                CallbackCount = callbackCount;
                CallbackTicksTotal = callbackTicksTotal;
                CallbackTicksMax = callbackTicksMax;
                TransportOverruns = transportOverruns;
                TransportUnderruns = transportUnderruns;
                FramesReceived = framesReceived;
                FramesDropped = framesDropped;
                ZeroFilledFrames = zeroFilledFrames;
                WorkerLateCount = workerLateCount;
                WorkerTicksMax = workerTicksMax;
                ProcessorExceptions = processorExceptions;
                EventQueueDrops = eventQueueDrops;
                LastQueueDepthSamples = lastQueueDepthSamples;
                MinQueueDepthSamples = minQueueDepthSamples;
                MaxQueueDepthSamples = maxQueueDepthSamples;
                ActiveCallbacks = activeCallbacks;
                LastCallbackThreadId = lastCallbackThreadId;
                CallbackExceptions = callbackExceptions;
                StopwatchFrequency = stopwatchFrequency;
            }

            public long CallbackCount { get; }
            public long CallbackTicksTotal { get; }
            public long CallbackTicksMax { get; }
            public long TransportOverruns { get; }
            public long TransportUnderruns { get; }
            public long FramesReceived { get; }
            public long FramesDropped { get; }
            public long ZeroFilledFrames { get; }
            public long WorkerLateCount { get; }
            public long WorkerTicksMax { get; }
            public long ProcessorExceptions { get; }
            public long EventQueueDrops { get; }
            public int LastQueueDepthSamples { get; }
            public int MinQueueDepthSamples { get; }
            public int MaxQueueDepthSamples { get; }
            public int ActiveCallbacks { get; }
            public int LastCallbackThreadId { get; }
            public long CallbackExceptions { get; }
            public long StopwatchFrequency { get; }

            public double MaxCallbackMicroseconds =>
                StopwatchFrequency <= 0 ? 0.0 : CallbackTicksMax * 1000000.0 / StopwatchFrequency;

            public double AverageCallbackMicroseconds =>
                StopwatchFrequency <= 0 || CallbackCount <= 0
                    ? 0.0
                    : (CallbackTicksTotal / (double)CallbackCount) * 1000000.0 / StopwatchFrequency;

            public double MaxWorkerMicroseconds =>
                StopwatchFrequency <= 0 ? 0.0 : WorkerTicksMax * 1000000.0 / StopwatchFrequency;
        }
    }
}
