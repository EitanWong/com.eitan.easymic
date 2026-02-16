using System;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Token 使用统计
    /// </summary>
    [Serializable]
    internal sealed class OpenAIUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
        public int input_tokens;
        public int output_tokens;
    }

    /// <summary>
    /// 聊天结果
    /// </summary>
    internal sealed class OpenAIChatResult
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string ReasoningContent { get; set; }
        public string ErrorMessage { get; set; }
        public bool FallbackRequired { get; set; }
    }
}
