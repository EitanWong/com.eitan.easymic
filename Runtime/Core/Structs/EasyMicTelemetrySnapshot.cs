namespace Eitan.EasyMic.Runtime
{
    public readonly struct EasyMicTelemetrySnapshot
    {
        internal EasyMicTelemetrySnapshot(RealtimeAudioTelemetry.Snapshot source)
        {
            CallbackCount = source.CallbackCount;
            CallbackMaxMicroseconds = source.MaxCallbackMicroseconds;
            CallbackAverageMicroseconds = source.AverageCallbackMicroseconds;
            TransportOverruns = source.TransportOverruns;
            TransportUnderruns = source.TransportUnderruns;
            FramesReceived = source.FramesReceived;
            FramesDropped = source.FramesDropped;
            ZeroFilledFrames = source.ZeroFilledFrames;
            WorkerLateCount = source.WorkerLateCount;
            WorkerMaxMicroseconds = source.MaxWorkerMicroseconds;
            ProcessorExceptions = source.ProcessorExceptions;
            EventQueueDrops = source.EventQueueDrops;
            LastQueueDepthSamples = source.LastQueueDepthSamples;
            MinQueueDepthSamples = source.MinQueueDepthSamples;
            MaxQueueDepthSamples = source.MaxQueueDepthSamples;
            ActiveCallbacks = source.ActiveCallbacks;
            LastCallbackThreadId = source.LastCallbackThreadId;
            CallbackExceptions = source.CallbackExceptions;
        }

        public long CallbackCount { get; }
        public double CallbackMaxMicroseconds { get; }
        public double CallbackAverageMicroseconds { get; }
        public long TransportOverruns { get; }
        public long TransportUnderruns { get; }
        public long FramesReceived { get; }
        public long FramesDropped { get; }
        public long ZeroFilledFrames { get; }
        public long WorkerLateCount { get; }
        public double WorkerMaxMicroseconds { get; }
        public long ProcessorExceptions { get; }
        public long EventQueueDrops { get; }
        public int LastQueueDepthSamples { get; }
        public int MinQueueDepthSamples { get; }
        public int MaxQueueDepthSamples { get; }
        public int ActiveCallbacks { get; }
        public int LastCallbackThreadId { get; }
        public long CallbackExceptions { get; }
    }

    public readonly struct EasyMicRealtimeStats
    {
        public EasyMicRealtimeStats(EasyMicTelemetrySnapshot telemetry)
        {
            CallbackCount = telemetry.CallbackCount;
            CallbackExceptions = telemetry.CallbackExceptions;
            CallbackMaxMicroseconds = telemetry.CallbackMaxMicroseconds;
            CallbackAverageMicroseconds = telemetry.CallbackAverageMicroseconds;
            ActiveCallbacks = telemetry.ActiveCallbacks;
            LastCallbackThreadId = telemetry.LastCallbackThreadId;
            TransportOverruns = telemetry.TransportOverruns;
            TransportUnderruns = telemetry.TransportUnderruns;
            FramesDropped = telemetry.FramesDropped;
            ZeroFilledFrames = telemetry.ZeroFilledFrames;
            ProcessorExceptions = telemetry.ProcessorExceptions;
        }

        public long CallbackCount { get; }
        public long CallbackExceptions { get; }
        public double CallbackMaxMicroseconds { get; }
        public double CallbackAverageMicroseconds { get; }
        public int ActiveCallbacks { get; }
        public int LastCallbackThreadId { get; }
        public long TransportOverruns { get; }
        public long TransportUnderruns { get; }
        public long FramesDropped { get; }
        public long ZeroFilledFrames { get; }
        public long ProcessorExceptions { get; }
    }

    public readonly struct EasyMicLatencyStats
    {
        public EasyMicLatencyStats(
            EasyMicLatencyProfile profile,
            uint actualSampleRate,
            uint actualChannelCount,
            EasyMicTelemetrySnapshot telemetry)
        {
            Profile = profile;
            ActualSampleRate = actualSampleRate;
            ActualChannelCount = actualChannelCount;
            LastQueueDepthSamples = telemetry.LastQueueDepthSamples;
            MinQueueDepthSamples = telemetry.MinQueueDepthSamples;
            MaxQueueDepthSamples = telemetry.MaxQueueDepthSamples;
        }

        public EasyMicLatencyProfile Profile { get; }
        public uint ActualSampleRate { get; }
        public uint ActualChannelCount { get; }
        public int LastQueueDepthSamples { get; }
        public int MinQueueDepthSamples { get; }
        public int MaxQueueDepthSamples { get; }

        public double LastQueueDepthMilliseconds =>
            ActualSampleRate == 0 || ActualChannelCount == 0
                ? 0.0
                : LastQueueDepthSamples * 1000.0 / (ActualSampleRate * ActualChannelCount);
    }
}
