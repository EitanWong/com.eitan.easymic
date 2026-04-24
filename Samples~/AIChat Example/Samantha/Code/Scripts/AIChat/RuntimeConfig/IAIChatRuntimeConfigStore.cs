#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal interface IAIChatRuntimeConfigStore
    {
        AIChatRuntimeConfig CreateDefault(AIChatControllerConfig controllerConfig);
        AIChatRuntimeConfig Capture(AIChatControllerConfig controllerConfig);
        bool TryLoad(string path, out AIChatRuntimeConfig runtimeConfig);
        AIChatRuntimeConfig LoadOrCreate(string path, AIChatControllerConfig controllerConfig, out bool createdDefault);
        bool TrySave(string path, AIChatRuntimeConfig runtimeConfig, out string errorMessage);
        void Apply(AIChatRuntimeConfig runtimeConfig, AIChatControllerConfig controllerConfig);
    }
}
#endif
