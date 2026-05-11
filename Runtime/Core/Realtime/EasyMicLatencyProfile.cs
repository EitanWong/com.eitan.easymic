namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Policy-level latency target. The backend may relax these hints when a platform
    /// or device cannot provide the requested buffer geometry.
    /// </summary>
    public enum EasyMicLatencyProfile
    {
        UltraLowLatency = 0,
        LowLatency = 1,
        Balanced = 2,
        SafeStreaming = 3,
        Stable = 3
    }
}
