#if EASYMIC_SHERPA_ONNX_INTEGRATION
namespace Eitan.EasyMic.Runtime.Mono.ASR
{
  
/// <summary>
/// Recognition strategy for hybrid setups leveraging both streaming and offline recognizers.
/// </summary>
public sealed class HybridRecognitionStrategy : IRecognitionStrategy
{
    /// <inheritdoc />
    public RecognitionMode Mode => RecognitionMode.Hybrid;

    /// <inheritdoc />
    public bool RequiresStreaming => true;

    /// <inheritdoc />
    public bool RequiresOffline => true;

    /// <inheritdoc />
    public bool RequiresVoiceActivity => true;

    /// <inheritdoc />
    public bool AllowsStreamingFinalCommit => false;

    /// <inheritdoc />
    public bool AppliesStreamingVoiceActivity => false;

    /// <inheritdoc />
    public bool SubmitWhenSpeakingEndsWithoutOffline => false;
}
}
#endif