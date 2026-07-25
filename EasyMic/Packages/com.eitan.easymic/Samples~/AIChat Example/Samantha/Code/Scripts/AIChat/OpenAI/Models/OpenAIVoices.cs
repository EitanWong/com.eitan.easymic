namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// 常用音色
    /// 用 partial class 拆分，每个供应商一个文件：
    /// - OpenAIVoices.cs — OpenAI 官方音色
    /// - OpenAIVoices.SiliconFlow.cs — SiliconFlow 平台音色
    /// </summary>
    internal static partial class OpenAIVoices
    {
        /// <summary>OpenAI Marin, recommended for best quality.</summary>
        public const string Marin = AIChatProviderPresets.OpenAiTtsVoice;
        /// <summary>OpenAI Cedar, recommended for best quality.</summary>
        public const string Cedar = "cedar";
        /// <summary>OpenAI Alloy</summary>
        public const string Alloy = "alloy";
        /// <summary>OpenAI Ash</summary>
        public const string Ash = "ash";
        /// <summary>OpenAI Ballad</summary>
        public const string Ballad = "ballad";
        /// <summary>OpenAI Coral</summary>
        public const string Coral = "coral";
        /// <summary>OpenAI Echo</summary>
        public const string Echo = "echo";
        /// <summary>OpenAI Fable</summary>
        public const string Fable = "fable";
        /// <summary>OpenAI Onyx</summary>
        public const string Onyx = "onyx";
        /// <summary>OpenAI Nova</summary>
        public const string Nova = "nova";
        /// <summary>OpenAI Shimmer</summary>
        public const string Shimmer = "shimmer";
        /// <summary>OpenAI Sage</summary>
        public const string Sage = "sage";
        /// <summary>OpenAI Verse</summary>
        public const string Verse = "verse";
    }
}
