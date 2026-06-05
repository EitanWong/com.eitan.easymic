using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// SiliconFlow Chat Completions 请求（OpenAI 兼容 API）
    /// 新增 enable_thinking 参数支持
    /// </summary>
    [Serializable]
    internal sealed class OpenAISiliconFlowChatRequest
    {
        [SerializeField] private string model;
        [SerializeField] private bool stream = true;
        [SerializeField] private float temperature = 0.7f;
        [SerializeField] private int max_tokens = 2048;
        [SerializeField] private List<OpenAIChatMessage> messages = new List<OpenAIChatMessage>();
        [SerializeField] private bool enable_thinking = false;

        public OpenAISiliconFlowChatRequest(OpenAIChatRequest request, bool enableThinking)
        {
            if (request == null)
            {
                return;
            }

            model = request.Model;
            stream = request.Stream;
            temperature = request.Temperature;
            max_tokens = request.MaxTokens;
            messages = request.Messages == null
                ? new List<OpenAIChatMessage>()
                : new List<OpenAIChatMessage>(request.Messages);
            enable_thinking = enableThinking;
        }
    }
}
