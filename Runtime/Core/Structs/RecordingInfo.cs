namespace Eitan.EasyMic.Runtime{
    // Recording information
    public struct RecordingInfo
    {
        public readonly MicDevice Device;
        public readonly SampleRate SampleRate;
        public readonly Channel Channel;
        public readonly bool IsActive;
        public readonly int ProcessorCount;
        public readonly long NativeCallbackCount;
        public readonly long NativeInputNullCount;
        public readonly long NativeOutputNullCount;
        public readonly long NativeNonZeroCallbackCount;
        public readonly long NativeNonZeroByteCallbackCount;
        public readonly long NativeNonZeroOutputByteCallbackCount;
        public readonly float LastRawInputPeak;
        public readonly float MaxRawInputPeak;
        public readonly int LastRawInputNonZeroBytes;
        public readonly int MaxRawInputNonZeroBytes;
        public readonly int LastRawOutputNonZeroBytes;
        public readonly int MaxRawOutputNonZeroBytes;
        public readonly EasyMicTelemetrySnapshot Telemetry;

        internal RecordingInfo(
            MicDevice device,
            SampleRate sampleRate,
            Channel channel,
            bool isActive,
            int processorCount,
            long nativeCallbackCount = 0,
            long nativeInputNullCount = 0,
            long nativeOutputNullCount = 0,
            long nativeNonZeroCallbackCount = 0,
            long nativeNonZeroByteCallbackCount = 0,
            long nativeNonZeroOutputByteCallbackCount = 0,
            float lastRawInputPeak = 0f,
            float maxRawInputPeak = 0f,
            int lastRawInputNonZeroBytes = 0,
            int maxRawInputNonZeroBytes = 0,
            int lastRawOutputNonZeroBytes = 0,
            int maxRawOutputNonZeroBytes = 0,
            EasyMicTelemetrySnapshot telemetry = default)
        {
            Device = device;
            SampleRate = sampleRate;
            Channel = channel;
            IsActive = isActive;
            ProcessorCount = processorCount;
            NativeCallbackCount = nativeCallbackCount;
            NativeInputNullCount = nativeInputNullCount;
            NativeOutputNullCount = nativeOutputNullCount;
            NativeNonZeroCallbackCount = nativeNonZeroCallbackCount;
            NativeNonZeroByteCallbackCount = nativeNonZeroByteCallbackCount;
            NativeNonZeroOutputByteCallbackCount = nativeNonZeroOutputByteCallbackCount;
            LastRawInputPeak = lastRawInputPeak;
            MaxRawInputPeak = maxRawInputPeak;
            LastRawInputNonZeroBytes = lastRawInputNonZeroBytes;
            MaxRawInputNonZeroBytes = maxRawInputNonZeroBytes;
            LastRawOutputNonZeroBytes = lastRawOutputNonZeroBytes;
            MaxRawOutputNonZeroBytes = maxRawOutputNonZeroBytes;
            Telemetry = telemetry;
        }
    }
    
} 
