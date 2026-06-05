#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class AIChatControllerState
    {
        private readonly object _sync = new object();
        private bool _llmInFlight;
        private bool _isAssistantSpeaking;
        private bool _isChatActive;
        private bool _initialized;
        private bool _initializationFailed;
        private bool _isIdle;
        private bool _isShuttingDown;
        private float _lastLoadingProgress;

        public bool LlmInFlight
        {
            get { lock (_sync) { return _llmInFlight; } }
            set { lock (_sync) { _llmInFlight = value; } }
        }

        public bool IsAssistantSpeaking
        {
            get { lock (_sync) { return _isAssistantSpeaking; } }
            set { lock (_sync) { _isAssistantSpeaking = value; } }
        }

        public bool IsChatActive
        {
            get { lock (_sync) { return _isChatActive; } }
            set { lock (_sync) { _isChatActive = value; } }
        }

        public bool IsInitialized
        {
            get { lock (_sync) { return _initialized; } }
            set { lock (_sync) { _initialized = value; } }
        }

        public bool InitializationFailed
        {
            get { lock (_sync) { return _initializationFailed; } }
            set { lock (_sync) { _initializationFailed = value; } }
        }

        public bool IsIdle
        {
            get { lock (_sync) { return _isIdle; } }
            set { lock (_sync) { _isIdle = value; } }
        }

        public float LastLoadingProgress
        {
            get { lock (_sync) { return _lastLoadingProgress; } }
            set { lock (_sync) { _lastLoadingProgress = value; } }
        }

        public bool IsShuttingDown
        {
            get { lock (_sync) { return _isShuttingDown; } }
            set { lock (_sync) { _isShuttingDown = value; } }
        }
        public struct StateSnapshot
        {
            public bool LlmInFlight;
            public bool IsAssistantSpeaking;
        }

        public StateSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new StateSnapshot
                {
                    LlmInFlight = _llmInFlight,
                    IsAssistantSpeaking = _isAssistantSpeaking
                };
            }
        }
    }
}
#endif
