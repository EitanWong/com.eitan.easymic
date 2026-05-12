namespace Eitan.EasyMic.Runtime
{
    public enum EasyMicQueueStatus
    {
        Ok = 0,
        Partial = 1,
        Full = 2,
        Stopped = 3,
        Paused = 4,
        DeviceLost = 5,
        Disposed = 6,
        InvalidFormat = 7,
        Timeout = 8
    }

    public readonly struct EasyMicEnqueueResult
    {
        public EasyMicEnqueueResult(
            EasyMicQueueStatus status,
            int samplesRequested,
            int samplesWritten)
        {
            Status = status;
            SamplesRequested = samplesRequested;
            SamplesWritten = samplesWritten;
        }

        public EasyMicQueueStatus Status { get; }
        public int SamplesRequested { get; }
        public int SamplesWritten { get; }
        public bool Success => Status == EasyMicQueueStatus.Ok;
        public bool WroteAnySamples => SamplesWritten > 0;

        public static EasyMicEnqueueResult InvalidFormat(int samplesRequested = 0)
            => new EasyMicEnqueueResult(EasyMicQueueStatus.InvalidFormat, samplesRequested, 0);

        public static EasyMicEnqueueResult Disposed(int samplesRequested = 0)
            => new EasyMicEnqueueResult(EasyMicQueueStatus.Disposed, samplesRequested, 0);

        public static EasyMicEnqueueResult FromWrite(int samplesRequested, int samplesWritten)
        {
            if (samplesRequested <= 0)
            {
                return new EasyMicEnqueueResult(EasyMicQueueStatus.InvalidFormat, samplesRequested, 0);
            }

            if (samplesWritten <= 0)
            {
                return new EasyMicEnqueueResult(EasyMicQueueStatus.Full, samplesRequested, 0);
            }

            return samplesWritten >= samplesRequested
                ? new EasyMicEnqueueResult(EasyMicQueueStatus.Ok, samplesRequested, samplesWritten)
                : new EasyMicEnqueueResult(EasyMicQueueStatus.Partial, samplesRequested, samplesWritten);
        }
    }
}
