namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// 最小化的音频帧状态（引用类型）。
    /// - ChannelCount: 当前通道数（Writer 可修改）
    /// - SampleRate: 采样率（通常不变）
    /// - Length: 当前帧有效总样本数（所有通道合计，Writer 可修改）
    /// 使用引用类型以便在同一回调链路中下游能观察到上游 Writer 的修改。
    /// </summary>
    public sealed class AudioState
    {
        public int ChannelCount { get; set; }
        public int SampleRate { get; set; }
        public int Length { get; set; }

        public AudioState(int channelCount, int sampleRate, int length)
        {
            ChannelCount = channelCount;
            SampleRate = sampleRate;
            Length = length;
        }

        public override string ToString()
        {
            return $"SampleRate: {SampleRate}, ChannelCount: {ChannelCount}";
        }
    }
}

