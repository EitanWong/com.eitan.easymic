#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;

namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
{
    /// <summary>
    /// Lightweight voice activity monitor that surfaces speaking transitions without silence heuristics.
    /// </summary>
    public sealed class VoiceActivityMonitor
    {
        #region Fields

        private bool _isVoiceActive;

        #endregion

        #region Events

        public event Action<bool> VoiceActivityChanged;

        #endregion

        #region Properties

        public bool IsVoiceActive => _isVoiceActive;

        #endregion

        #region Public API

        public void Reset()
        {
            _isVoiceActive = false;
        }

        public void SetVoiceActivity(bool isActive)
        {
            if (_isVoiceActive == isActive)
            {
                return;
            }

            _isVoiceActive = isActive;
            VoiceActivityChanged?.Invoke(_isVoiceActive);
        }

        #endregion
    }
}
#endif
