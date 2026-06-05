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
        /// <summary>OpenAI GPT-4o</summary>
        public const string GPT4o = "gpt-4o";
        /// <summary>OpenAI GPT-4o Mini</summary>
        public const string GPT4o_Mini = "gpt-4o-mini";
        /// <summary>OpenAI GPT-4.1</summary>
        public const string GPT4_1 = "gpt-4.1";
        /// <summary>OpenAI GPT-4.1 Mini</summary>
        public const string GPT4_1_Mini = "gpt-4.1-mini";
        /// <summary>OpenAI GPT-4.1 Nano</summary>
        public const string GPT4_1_Nano = "gpt-4.1-nano";
        /// <summary>OpenAI o3</summary>
        public const string O3 = "o3";
        /// <summary>OpenAI o3 Mini</summary>
        public const string O3_Mini = "o3-mini";
        /// <summary>OpenAI o1</summary>
        public const string O1 = "o1";
        /// <summary>OpenAI o1 Mini</summary>
        public const string O1_Mini = "o1-mini";

        /// <summary>OpenAI TTS-1</summary>
        public const string TTS1 = "tts-1";
        /// <summary>OpenAI TTS-1 HD</summary>
        public const string TTS1_HD = "tts-1-hd";
    }
}
