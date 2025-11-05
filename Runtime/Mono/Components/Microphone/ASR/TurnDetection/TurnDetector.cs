#if EASYMIC_SHERPA_ONNX_INTEGRATION
namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Produces adaptive delays before committing buffered recognition turns.
    /// </summary>
    public abstract class TurnDetector
    {
        public abstract float EvaluateDelay(in TurnDetectionContext context);
    }
}
#endif
