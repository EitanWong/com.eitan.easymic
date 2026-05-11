using System;

namespace Eitan.EasyMic.Runtime
{
    public enum EasyMicPipelineNodeKind
    {
        CaptureDevice,
        PlaybackDevice,
        Transport,
        Queue,
        Mixer,
        PlaybackSource,
        Processor,
        Reader,
        Writer,
        Group,
        Output
    }

    public enum EasyMicPipelineThreadKind
    {
        Unknown,
        AudioThread,
        WorkerThread,
        MainThread,
        TelemetryThread,
        NativeThread
    }

    public readonly struct EasyMicProcessorSnapshot
    {
        internal EasyMicProcessorSnapshot(int order, IAudioWorker worker)
        {
            Order = order;
            TypeName = worker != null ? worker.GetType().Name : "Missing Processor";
            FullTypeName = worker != null ? worker.GetType().FullName : string.Empty;
            IsReader = worker is AudioReader;
            IsWriter = worker is AudioWriter;
            IsEnabled = worker is AudioWorkerBase workerBase && workerBase.Enabled;
            NodeKind = IsReader ? EasyMicPipelineNodeKind.Reader : EasyMicPipelineNodeKind.Writer;
            ThreadKind = IsReader ? EasyMicPipelineThreadKind.WorkerThread : EasyMicPipelineThreadKind.AudioThread;
        }

        public int Order { get; }
        public string TypeName { get; }
        public string FullTypeName { get; }
        public bool IsReader { get; }
        public bool IsWriter { get; }
        public bool IsEnabled { get; }
        public EasyMicPipelineNodeKind NodeKind { get; }
        public EasyMicPipelineThreadKind ThreadKind { get; }
    }

    public readonly struct EasyMicRecordingPipelineSnapshot
    {
        internal EasyMicRecordingPipelineSnapshot(
            RecordingHandle handle,
            RecordingInfo info,
            EasyMicLatencyProfile latencyProfile,
            bool isUsingFallback,
            EasyMicProcessorSnapshot[] processors)
        {
            Handle = handle;
            Info = info;
            LatencyProfile = latencyProfile;
            IsUsingFallback = isUsingFallback;
            Processors = processors ?? Array.Empty<EasyMicProcessorSnapshot>();
        }

        public RecordingHandle Handle { get; }
        public RecordingInfo Info { get; }
        public EasyMicLatencyProfile LatencyProfile { get; }
        public bool IsUsingFallback { get; }
        public EasyMicProcessorSnapshot[] Processors { get; }
    }

    public readonly struct EasyMicPlaybackSourceSnapshot
    {
        internal EasyMicPlaybackSourceSnapshot(
            string name,
            int channels,
            int sampleRate,
            int queuedSamples,
            int freeSamples,
            double bufferedSeconds,
            float volume,
            bool mute,
            bool solo,
            bool isPlaying,
            EasyMicProcessorSnapshot[] processors)
        {
            Name = string.IsNullOrEmpty(name) ? "Playback Source" : name;
            Channels = channels;
            SampleRate = sampleRate;
            QueuedSamples = queuedSamples;
            FreeSamples = freeSamples;
            BufferedSeconds = bufferedSeconds;
            Volume = volume;
            Mute = mute;
            Solo = solo;
            IsPlaying = isPlaying;
            Processors = processors ?? Array.Empty<EasyMicProcessorSnapshot>();
        }

        public string Name { get; }
        public int Channels { get; }
        public int SampleRate { get; }
        public int QueuedSamples { get; }
        public int FreeSamples { get; }
        public double BufferedSeconds { get; }
        public float Volume { get; }
        public bool Mute { get; }
        public bool Solo { get; }
        public bool IsPlaying { get; }
        public EasyMicProcessorSnapshot[] Processors { get; }
    }

    public readonly struct EasyMicMixerSnapshot
    {
        internal EasyMicMixerSnapshot(
            string name,
            float volume,
            bool mute,
            bool solo,
            EasyMicProcessorSnapshot[] processors,
            EasyMicPlaybackSourceSnapshot[] sources,
            EasyMicMixerSnapshot[] children)
        {
            Name = string.IsNullOrEmpty(name) ? "Mixer" : name;
            Volume = volume;
            Mute = mute;
            Solo = solo;
            Processors = processors ?? Array.Empty<EasyMicProcessorSnapshot>();
            Sources = sources ?? Array.Empty<EasyMicPlaybackSourceSnapshot>();
            Children = children ?? Array.Empty<EasyMicMixerSnapshot>();
        }

        public string Name { get; }
        public float Volume { get; }
        public bool Mute { get; }
        public bool Solo { get; }
        public EasyMicProcessorSnapshot[] Processors { get; }
        public EasyMicPlaybackSourceSnapshot[] Sources { get; }
        public EasyMicMixerSnapshot[] Children { get; }
    }

    public readonly struct EasyMicPlaybackPipelineSnapshot
    {
        internal EasyMicPlaybackPipelineSnapshot(
            bool isRunning,
            string backendName,
            string deviceName,
            uint channels,
            uint sampleRate,
            EasyMicLatencyProfile latencyProfile,
            EasyMicTelemetrySnapshot telemetry,
            EasyMicMixerSnapshot masterMixer)
        {
            IsRunning = isRunning;
            BackendName = backendName ?? string.Empty;
            DeviceName = deviceName ?? string.Empty;
            Channels = channels;
            SampleRate = sampleRate;
            LatencyProfile = latencyProfile;
            Telemetry = telemetry;
            MasterMixer = masterMixer;
        }

        public bool IsRunning { get; }
        public string BackendName { get; }
        public string DeviceName { get; }
        public uint Channels { get; }
        public uint SampleRate { get; }
        public EasyMicLatencyProfile LatencyProfile { get; }
        public EasyMicTelemetrySnapshot Telemetry { get; }
        public EasyMicMixerSnapshot MasterMixer { get; }
    }
}
