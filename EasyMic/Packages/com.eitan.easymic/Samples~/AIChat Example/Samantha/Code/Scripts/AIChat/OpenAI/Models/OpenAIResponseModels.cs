using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Responses API 请求
    /// </summary>
    [Serializable]
    internal sealed class OpenAIResponseRequest
    {
        [SerializeField] private string model;
        [SerializeField] private bool stream = true;
        [SerializeField] private float temperature = 1f;
        [SerializeField] private string instructions;
        [SerializeField] private OpenAIResponseInputItem[] input;
        [SerializeField] private int max_output_tokens;

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

        public string Instructions
        {
            get => instructions;
            set => instructions = value;
        }

        public OpenAIResponseInputItem[] Input
        {
            get => input;
            set => input = value;
        }

        public int MaxOutputTokens
        {
            get => max_output_tokens;
            set => max_output_tokens = value;
        }

        public static OpenAIResponseRequest FromChatRequest(OpenAIChatRequest chatRequest)
        {
            if (chatRequest == null)
            {
                return null;
            }

            string systemInstruction = null;
            var inputItems = new List<OpenAIResponseInputItem>();

            foreach (var msg in chatRequest.Messages)
            {
                if (msg == null)
                {
                    continue;
                }

                string role = string.IsNullOrWhiteSpace(msg.Role) ? "user" : msg.Role.Trim().ToLower();
                string content = msg.Content ?? string.Empty;

                if (role == "system" || role == "developer")
                {
                    systemInstruction = content;
                    continue;
                }

                inputItems.Add(new OpenAIResponseInputItem
                {
                    type = "message",
                    role = role,
                    content = new[]
                    {
                        new OpenAIResponseInputContent
                        {
                            type = "input_text",
                            text = content
                        }
                    }
                });
            }

            return new OpenAIResponseRequest
            {
                Model = chatRequest.Model,
                Stream = chatRequest.Stream,
                Temperature = chatRequest.Temperature,
                Instructions = systemInstruction,
                Input = inputItems.ToArray(),
                MaxOutputTokens = chatRequest.MaxTokens > 0 ? chatRequest.MaxTokens : 0
            };
        }
    }

    [Serializable]
    internal sealed class OpenAIResponseInputItem
    {
        public string type = "message";
        public string role;
        public OpenAIResponseInputContent[] content;
    }

    [Serializable]
    internal sealed class OpenAIResponseInputContent
    {
        public string type = "input_text";
        public string text;
    }

    /// <summary>
    /// Responses API 完整响应对象
    /// </summary>
    [Serializable]
    internal sealed class OpenAIResponseObject
    {
        public string id;
        [SerializeField] private string @object;
        public string Object => @object;
        public long created_at;
        public string status;
        public string model;
        public OpenAIResponseOutputItem[] output;
        public OpenAIResponseError error;
        public OpenAIUsage usage;
        public string instructions;
    }

    /// <summary>
    /// Responses API 流式事件
    /// </summary>
    [Serializable]
    internal sealed class OpenAIResponseStreamEvent
    {
        public string type;
        public OpenAIResponseObject response;
        public string delta;
        public OpenAIResponseError error;
        public string item_id;
        public int output_index;
        public int content_index;
    }

    [Serializable]
    internal sealed class OpenAIResponseOutputItem
    {
        public string id;
        public string type;
        public string status;
        public string role;
        public OpenAIResponseOutputContent[] content;
    }

    [Serializable]
    internal sealed class OpenAIResponseOutputContent
    {
        public string type;
        public string text;
        public string[] annotations;
    }

    [Serializable]
    internal sealed class OpenAIResponseError
    {
        public string message;
        public string code;
        public string type;
    }
}
