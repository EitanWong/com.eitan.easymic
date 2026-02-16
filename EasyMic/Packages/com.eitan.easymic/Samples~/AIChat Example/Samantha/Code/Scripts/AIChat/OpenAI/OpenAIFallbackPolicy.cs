namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class OpenAIFallbackPolicy
    {
        private bool _responsesApiNotSupported;

        public bool ShouldUseChatCompletions(bool forceChatCompletions, bool providerSupportsResponsesApi)
        {
            return forceChatCompletions || _responsesApiNotSupported || !providerSupportsResponsesApi;
        }

        public void MarkResponsesApiUnsupported()
        {
            _responsesApiNotSupported = true;
        }

        public void Reset()
        {
            _responsesApiNotSupported = false;
        }
    }
}
