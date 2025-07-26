public class AudioState
{
    public int ChannelCount { get; set; }
    public int SampleRate { get; set; }
    public int Length { get; set; }

    public AudioState(int channelCount, int sampleRate, int length)
    {
        this.ChannelCount = channelCount;
        this.SampleRate = sampleRate;
        this.Length=length;
    }

    public override string ToString()
    {
        return $"SampleRate: {SampleRate}, ChannelCount: {ChannelCount}";
    }
}