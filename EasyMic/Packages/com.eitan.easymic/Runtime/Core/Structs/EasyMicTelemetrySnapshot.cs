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
            LastQueueDepthSamples = source.LastQueueDepthSamples;
            MaxQueueDepthSamples = source.MaxQueueDepthSamples;
        }

        public long CallbackCount { get; }
        public double CallbackMaxMicroseconds { get; }
        public double CallbackAverageMicroseconds { get; }
        public long TransportOverruns { get; }
        public long TransportUnderruns { get; }
        public long FramesReceived { get; }
        public long FramesDropped { get; }
        public int LastQueueDepthSamples { get; }
        public int MaxQueueDepthSamples { get; }
    }
}
