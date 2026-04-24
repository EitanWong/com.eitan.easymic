using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal abstract class OpenAIProviderAdapterBase : IOpenAIProviderAdapter
    {
        public virtual string Name => "OpenAI-Compatible";
        public virtual bool SupportsResponsesApi => true;

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
            return request == null ? "{}" : JsonUtility.ToJson(request);
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
    }
}
