#if EITAN_SHERPA_ONNX_UNITY_PRESENT
namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR
{
    /// <summary>
    /// Tracks elapsed silence while allowing resets from voice activity.
    /// </summary>
    public sealed class SilenceTimer
    {
        private float _elapsed;

        public float Elapsed => _elapsed;

        public void Reset()
        {
            _elapsed = 0f;
        }

        public void Update(float deltaTime)
        {
            _elapsed += deltaTime;
            if (_elapsed < 0f)
            {
                _elapsed = 0f;
            }
        }
    }
}
#endif
