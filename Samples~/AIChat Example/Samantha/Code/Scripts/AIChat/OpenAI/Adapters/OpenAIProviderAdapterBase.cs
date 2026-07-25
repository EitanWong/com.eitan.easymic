using System;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal abstract class OpenAIProviderAdapterBase : IOpenAIProviderAdapter
    {
        public virtual string Name => "OpenAI-Compatible";
        public virtual bool SupportsResponsesApi => false;

        public virtual string BuildChatCompletionsPayload(OpenAIChatRequest request)
        {
            return request == null ? "{}" : JsonUtility.ToJson(request);
        }

        public virtual string BuildResponsesPayload(OpenAIResponseRequest request)
        {
            return request == null ? "{}" : JsonUtility.ToJson(request);
        }

        public virtual string BuildTtsPayload(OpenAITtsRequest request)
        {
            return request == null
                ? "{}"
                : JsonUtility.ToJson(new StandardTtsPayload(request));
        }

        public virtual string NormalizeChatCompletionChunkJson(string json) => json;
        public virtual string NormalizeChatCompletionResponseJson(string json) => json;
        public virtual string NormalizeResponsesStreamEventJson(string json) => json;
        public virtual string NormalizeResponsesResponseJson(string json) => json;

        public virtual string SelectChatCompletionDeltaText(OpenAIChatCompletionStreamChoice choice)
        {
            return choice?.delta?.content;
        }

        public virtual string SelectChatCompletionMessageText(OpenAIChatMessageResponse message)
        {
            return message?.content;
        }

        [Serializable]
        private sealed class StandardTtsPayload
        {
            public string model;
            public string input;
            public string voice;
            public string response_format;
            public float speed;

            public StandardTtsPayload(OpenAITtsRequest request)
            {
                model = request.Model;
                input = request.Input;
                voice = request.Voice;
                response_format = request.ResponseFormat;
                speed = request.Speed;
            }
        }
    }
}
