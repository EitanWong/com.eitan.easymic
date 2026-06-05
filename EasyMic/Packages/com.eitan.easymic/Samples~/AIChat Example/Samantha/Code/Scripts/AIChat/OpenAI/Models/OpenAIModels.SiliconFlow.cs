namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// SiliconFlow 平台模型（OpenAI 兼容 API）
    /// 新增供应商时按此模式创建 OpenAIModels.YourProvider.cs
    /// </summary>
    internal static partial class OpenAIModels
    {
        /// <summary>Qwen QwQ 32B</summary>
        public const string Qwen_QwQ_32B = "Qwen/QwQ-32B";
        /// <summary>Qwen2.5 72B</summary>
        public const string Qwen2_5_72B = "Qwen/Qwen2.5-72B-Instruct";
        /// <summary>Qwen2.5 32B</summary>
        public const string Qwen2_5_32B = "Qwen/Qwen2.5-32B-Instruct";
        /// <summary>Qwen2.5 14B</summary>
        public const string Qwen2_5_14B = "Qwen/Qwen2.5-14B-Instruct";
        /// <summary>Qwen2.5 7B</summary>
        public const string Qwen2_5_7B = "Qwen/Qwen2.5-7B-Instruct";
        /// <summary>DeepSeek V3</summary>
        public const string DeepSeek_V3 = "deepseek-ai/DeepSeek-V3";
        /// <summary>DeepSeek R1</summary>
        public const string DeepSeek_R1 = "deepseek-ai/DeepSeek-R1";

        /// <summary>CosyVoice2 TTS</summary>
        public const string CosyVoice2 = "FunAudioLLM/CosyVoice2-0.5B";
        /// <summary>MOSS TTS</summary>
        public const string MOSS_TTS = "fnlp/MOSS-TTSD-v0.5";
    }
}
