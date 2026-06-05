namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Information about current network quality.
    /// </summary>
    public struct NetworkQualityInfo
    {
        public string Quality;
        public float AverageLatencyMs;
        public float JitterMs;
        public bool ShouldUsePreemptiveStreaming;
        public int RecommendedBufferSize;
        public float RecommendedTimeoutMs;

        public static NetworkQualityInfo Default => new NetworkQualityInfo
        {
            Quality = "Unknown",
            AverageLatencyMs = 0,
            JitterMs = 0,
            ShouldUsePreemptiveStreaming = false,
            RecommendedBufferSize = 2,
            RecommendedTimeoutMs = 30000
        };
    }
}
