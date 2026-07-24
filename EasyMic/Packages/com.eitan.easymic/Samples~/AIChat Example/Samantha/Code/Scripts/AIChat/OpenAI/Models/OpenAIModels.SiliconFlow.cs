namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// SiliconFlow 平台模型（OpenAI 兼容 API）
    /// 新增供应商时按此模式创建 OpenAIModels.YourProvider.cs
    /// </summary>
    internal static partial class OpenAIModels
    {
        /// <summary>Current low-latency SiliconFlow LLM used by this sample.</summary>
        public const string Qwen3_5_9B = AIChatProviderPresets.SiliconFlowLlmModel;

        /// <summary>CosyVoice2 TTS</summary>
        public const string CosyVoice2 = AIChatProviderPresets.SiliconFlowTtsModel;
        /// <summary>MOSS TTS</summary>
        public const string MOSS_TTS = "fnlp/MOSS-TTSD-v0.5";
    }
}
