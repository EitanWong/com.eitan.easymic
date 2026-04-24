#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class AIChatPluginContext : IAIChatPluginContext
    {
        private readonly AIChatController _controller;

        public AIChatPluginContext(AIChatController controller)
        {
            _controller = controller;
        }

        public bool IsIdle => _controller != null && _controller.IsIdle;
        public bool IsChatActive => _controller != null && _controller.IsChatActive;
        public bool IsInitialized => _controller != null && _controller.IsInitialized;
        public bool HasConfigurationPolicy => _controller != null && _controller.HasConfigurationPolicy;
        public AIChatConfigurationPolicy.PolicyPreset ConfigurationPolicyPreset =>
            _controller != null ? _controller.ConfigurationPolicyPreset : AIChatConfigurationPolicy.PolicyPreset.Custom;
        public bool IsUserSpeaking => _controller != null && _controller.IsUserSpeaking;
        public bool HasConversationHistory => _controller != null && _controller.HasConversationHistory;
        public float TimeSinceLastUserActivity => _controller != null ? _controller.TimeSinceLastUserActivity : 0f;
        public float TimeSinceLastAssistantResponse => _controller != null ? _controller.TimeSinceLastAssistantResponse : 0f;
        public float MicStartupDelaySeconds => _controller != null ? _controller.MicStartupDelaySeconds : 0f;
        public AIChatResolvedConfiguration ResolvedConfiguration => _controller != null ? _controller.GetResolvedConfiguration() : new AIChatResolvedConfiguration();

        public bool TrySendProactiveMessage(string prompt, bool recordUserMessage)
        {
            return _controller != null && _controller.TrySendProactiveMessage(prompt, recordUserMessage);
        }
    }
}
#endif
