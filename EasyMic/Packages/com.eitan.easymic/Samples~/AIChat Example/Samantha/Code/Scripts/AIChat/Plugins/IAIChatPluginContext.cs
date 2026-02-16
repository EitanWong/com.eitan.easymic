namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public interface IAIChatPluginContext
    {
        bool IsIdle { get; }
        bool IsChatActive { get; }
        bool IsInitialized { get; }
        bool IsUserSpeaking { get; }
        bool HasConversationHistory { get; }
        float TimeSinceLastUserActivity { get; }
        float TimeSinceLastAssistantResponse { get; }
        float MicStartupDelaySeconds { get; }
        bool TrySendProactiveMessage(string prompt, bool recordUserMessage);
    }
}
