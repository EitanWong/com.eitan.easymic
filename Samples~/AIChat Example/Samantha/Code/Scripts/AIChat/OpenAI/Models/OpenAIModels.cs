namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// 常用模型名称
    /// 用 partial class 拆分，每个供应商一个文件：
    /// - OpenAIModels.cs — OpenAI 官方模型
    /// - OpenAIModels.SiliconFlow.cs — SiliconFlow 平台模型
    /// </summary>
    internal static partial class OpenAIModels
    {
        /// <summary>Current OpenAI text model used by this sample.</summary>
        public const string GPT5_4 = AIChatProviderPresets.OpenAiLlmModel;

        /// <summary>Current OpenAI speech model used by this sample.</summary>
        public const string GPT4O_MINI_TTS = AIChatProviderPresets.OpenAiTtsModel;
    }
}
