#if EASYMIC_SHERPA_ONNX_INTEGRATION
namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
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
