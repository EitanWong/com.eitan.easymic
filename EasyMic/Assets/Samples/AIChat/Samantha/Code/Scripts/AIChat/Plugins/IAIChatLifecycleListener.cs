namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public interface IAIChatLifecycleListener
    {
        void OnChatActivated();
        void OnConversationStarted(bool isProactive);
        void OnUserMessageSubmitted(string message, bool isProactive);
        void OnAssistantRequestStarted(string prompt, bool isProactive);
        void OnAssistantResponseFinished(string response, bool success, string errorMessage);
        void OnIdleStateChanged(bool isIdle);
    }
}
