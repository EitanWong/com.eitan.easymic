namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal interface IOpenAIProviderAdapter
    {
        string Name { get; }
        bool SupportsResponsesApi { get; }
        string BuildChatCompletionsPayload(OpenAIChatRequest request);
        string BuildResponsesPayload(OpenAIResponseRequest request);
        string BuildTtsPayload(OpenAITtsRequest request);
        string NormalizeChatCompletionChunkJson(string json);
        string NormalizeChatCompletionResponseJson(string json);
        string NormalizeResponsesStreamEventJson(string json);
        string NormalizeResponsesResponseJson(string json);
        string SelectChatCompletionDeltaText(OpenAIChatCompletionStreamChoice choice);
        string SelectChatCompletionMessageText(OpenAIChatMessageResponse message);
    }
}
