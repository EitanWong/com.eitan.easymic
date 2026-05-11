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
        private long _callbackTicksTotal;
        private long _callbackTicksMax;
        private long _transportOverruns;
        private long _transportUnderruns;
        private long _framesReceived;
        private long _framesDropped;
        private int _lastQueueDepthSamples;
        private int _maxQueueDepthSamples;

        public long BeginCallback()
        {
            Interlocked.Increment(ref _callbackCount);
            return Stopwatch.GetTimestamp();
        }

        public void EndCallback(long startTimestamp)
        {
            long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
            if (elapsed < 0)
            {
                return;
            }

            Interlocked.Add(ref _callbackTicksTotal, elapsed);
            UpdateMax(ref _callbackTicksMax, elapsed);
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
                Volatile.Read(ref _lastQueueDepthSamples),
                Volatile.Read(ref _maxQueueDepthSamples),
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
                int lastQueueDepthSamples,
                int maxQueueDepthSamples,
                long stopwatchFrequency)
            {
                CallbackCount = callbackCount;
                CallbackTicksTotal = callbackTicksTotal;
                CallbackTicksMax = callbackTicksMax;
                TransportOverruns = transportOverruns;
                TransportUnderruns = transportUnderruns;
                FramesReceived = framesReceived;
                FramesDropped = framesDropped;
                LastQueueDepthSamples = lastQueueDepthSamples;
                MaxQueueDepthSamples = maxQueueDepthSamples;
                StopwatchFrequency = stopwatchFrequency;
            }

            public long CallbackCount { get; }
            public long CallbackTicksTotal { get; }
            public long CallbackTicksMax { get; }
            public long TransportOverruns { get; }
            public long TransportUnderruns { get; }
            public long FramesReceived { get; }
            public long FramesDropped { get; }
            public int LastQueueDepthSamples { get; }
            public int MaxQueueDepthSamples { get; }
            public long StopwatchFrequency { get; }

            public double MaxCallbackMicroseconds =>
                StopwatchFrequency <= 0 ? 0.0 : CallbackTicksMax * 1000000.0 / StopwatchFrequency;

            public double AverageCallbackMicroseconds =>
                StopwatchFrequency <= 0 || CallbackCount <= 0
                    ? 0.0
                    : (CallbackTicksTotal / (double)CallbackCount) * 1000000.0 / StopwatchFrequency;
        }
    }
}
