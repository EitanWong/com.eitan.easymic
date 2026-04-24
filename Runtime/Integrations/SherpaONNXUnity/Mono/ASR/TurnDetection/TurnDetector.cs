#if EITAN_SHERPA_ONNX_UNITY_PRESENT
namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR
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
