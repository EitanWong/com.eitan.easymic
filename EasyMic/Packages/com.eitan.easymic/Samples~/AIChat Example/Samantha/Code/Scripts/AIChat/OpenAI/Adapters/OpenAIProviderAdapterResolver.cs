using System;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal static class OpenAIProviderAdapterResolver
    {
        public static IOpenAIProviderAdapter Resolve(string baseUrl)
        {
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            {
                string host = uri.Host ?? string.Empty;
                if (host.IndexOf("siliconflow", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new SiliconFlowProviderAdapter();
                }

                if (string.Equals(host, "api.openai.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase))
                {
                    return new OpenAIProviderAdapter();
                }
            }

            return new OpenAICompatibleProviderAdapter();
        }

        private sealed class OpenAICompatibleProviderAdapter : OpenAIProviderAdapterBase
        {
        }
    }
}
