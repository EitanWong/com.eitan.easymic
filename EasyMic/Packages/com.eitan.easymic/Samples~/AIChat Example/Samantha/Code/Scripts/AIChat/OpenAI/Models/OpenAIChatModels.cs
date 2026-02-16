using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Chat Completions API 请求
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatRequest
    {
        [SerializeField] private string model;
        [SerializeField] private bool stream = true;
        [SerializeField] private float temperature = 0.7f;
        [SerializeField] private int max_tokens = 2048;
        [SerializeField] private List<OpenAIChatMessage> messages = new List<OpenAIChatMessage>();
        [NonSerialized] private bool? enableThinkingOverride;

        public string Model
        {
            get => model;
            set => model = value;
        }

        public bool Stream
        {
            get => stream;
            set => stream = value;
        }

        public float Temperature
        {
            get => temperature;
            set => temperature = value;
        }

        public int MaxTokens
        {
            get => max_tokens;
            set => max_tokens = value;
        }

        public List<OpenAIChatMessage> Messages
        {
            get => messages;
            set => messages = value ?? new List<OpenAIChatMessage>();
        }

        public bool? EnableThinkingOverride
        {
            get => enableThinkingOverride;
            set => enableThinkingOverride = value;
        }
    }

    /// <summary>
    /// SiliconFlow Chat Completions 请求
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

    /// <summary>
    /// 聊天消息
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatMessage
    {
        [SerializeField] private string role;
        [SerializeField] private string content;

        public OpenAIChatMessage() { }

        public OpenAIChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public string Role
        {
            get => role;
            set => role = value;
        }

        public string Content
        {
            get => content;
            set => content = value;
        }

        public static OpenAIChatMessage System(string content) => new OpenAIChatMessage("system", content);
        public static OpenAIChatMessage User(string content) => new OpenAIChatMessage("user", content);
        public static OpenAIChatMessage Assistant(string content) => new OpenAIChatMessage("assistant", content);
    }

    /// <summary>
    /// Chat Completions API 响应
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatCompletionResponse
    {
        public string id;
        public List<OpenAIChatCompletionChoice> choices;
        public OpenAIUsage usage;
        public long created;
        public string model;
        [SerializeField] private string @object;
        public string Object => @object;
    }

    [Serializable]
    internal sealed class OpenAIChatCompletionChoice
    {
        public OpenAIChatMessageResponse message;
        public string finish_reason;
        public int index;
    }

    [Serializable]
    internal sealed class OpenAIChatMessageResponse
    {
        public string role;
        public string content;
        public string reasoning_content;
    }

    /// <summary>
    /// Chat Completions 流式响应块
    /// </summary>
    [Serializable]
    internal sealed class OpenAIChatCompletionChunk
    {
        public string id;
        public List<OpenAIChatCompletionStreamChoice> choices;
        public long created;
        public string model;
    }

    [Serializable]
    internal sealed class OpenAIChatCompletionStreamChoice
    {
        public OpenAIChatDelta delta;
        public string finish_reason;
        public int index;
    }

    [Serializable]
    internal sealed class OpenAIChatDelta
    {
        public string role;
        public string content;
        public string reasoning_content;
    }
}
