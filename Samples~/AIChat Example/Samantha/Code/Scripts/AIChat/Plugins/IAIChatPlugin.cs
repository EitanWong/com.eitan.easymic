namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public interface IAIChatPlugin
    {
        bool IsEnabled { get; }
        void Initialize(IAIChatPluginContext context);
        void Tick(float deltaTime);
        void Shutdown();
    }
}
