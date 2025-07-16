namespace Eitan.EasyMic.Runtime{
    // Recording information
    public struct RecordingInfo
    {
        public readonly MicDevice Device;
        public readonly SampleRate SampleRate;
        public readonly Channel Channel;
        public readonly bool IsActive;
        public readonly int ProcessorCount;

        internal RecordingInfo(MicDevice device, SampleRate sampleRate, Channel channel, bool isActive, int processorCount)
        {
            Device = device;
            SampleRate = sampleRate;
            Channel = channel;
            IsActive = isActive;
            ProcessorCount = processorCount;
        }
    }
    
} 