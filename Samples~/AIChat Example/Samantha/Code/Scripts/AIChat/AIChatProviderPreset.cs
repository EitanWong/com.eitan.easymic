using System;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Identifies the provider defaults offered by the AI Chat setup tools.
    /// </summary>
    public enum AIChatProviderPresetKind
    {
        SiliconFlow = 0,
        OpenAICompatible = 1
    }

    /// <summary>
    /// Immutable provider defaults for the shared LLM and remote-TTS endpoint.
    /// </summary>
    public sealed class AIChatProviderPreset
    {
        public AIChatProviderPreset(
            string displayName,
            string apiBaseUrl,
            string llmModel,
            string ttsModel,
            string ttsVoice,
            bool supportsRemoteTts)
        {
            DisplayName = displayName ?? string.Empty;
            ApiBaseUrl = AIChatProviderPresets.NormalizeApiBaseUrl(apiBaseUrl);
            LlmModel = llmModel ?? string.Empty;
            TtsModel = ttsModel ?? string.Empty;
            TtsVoice = ttsVoice ?? string.Empty;
            SupportsRemoteTts = supportsRemoteTts;
        }

        public string DisplayName { get; }
        public string ApiBaseUrl { get; }
        public string LlmModel { get; }
        public string TtsModel { get; }
        public string TtsVoice { get; }
        public bool SupportsRemoteTts { get; }

        public void ApplyTo(AIChatControllerConfig configuration, bool useLocalTts)
        {
            if (configuration == null)
            {
                return;
            }

            configuration.ApiBaseUrl = ApiBaseUrl;
            configuration.LlmModel = LlmModel;
            configuration.UseLocalTts = useLocalTts;
            configuration.TtsModel = TtsModel;
            configuration.TtsVoice = TtsVoice;
            configuration.UseStreamingTts = !useLocalTts && SupportsRemoteTts;
        }
    }

    /// <summary>
    /// Centralizes the defaults used by the sample and its setup tooling.
    /// </summary>
    public static class AIChatProviderPresets
    {
        public const string SiliconFlowApiBaseUrl = "https://api.siliconflow.cn/v1/";
        public const string SiliconFlowLlmModel = "Qwen/Qwen3.5-9B";
        public const string SiliconFlowTtsModel = "FunAudioLLM/CosyVoice2-0.5B";
        public const string SiliconFlowTtsVoice = "FunAudioLLM/CosyVoice2-0.5B:alex";

        public const string OpenAiApiBaseUrl = "https://api.openai.com/v1/";
        public const string OpenAiLlmModel = "gpt-5.4";
        public const string OpenAiTtsModel = "gpt-4o-mini-tts";
        public const string OpenAiTtsVoice = "marin";

        public static AIChatProviderPreset SiliconFlow { get; } = new AIChatProviderPreset(
            "SiliconFlow",
            SiliconFlowApiBaseUrl,
            SiliconFlowLlmModel,
            SiliconFlowTtsModel,
            SiliconFlowTtsVoice,
            supportsRemoteTts: true);

        public static AIChatProviderPreset OpenAICompatible { get; } = new AIChatProviderPreset(
            "OpenAI Compatible",
            OpenAiApiBaseUrl,
            OpenAiLlmModel,
            OpenAiTtsModel,
            OpenAiTtsVoice,
            supportsRemoteTts: true);

        public static AIChatProviderPreset Get(AIChatProviderPresetKind kind)
        {
            return kind == AIChatProviderPresetKind.SiliconFlow
                ? SiliconFlow
                : OpenAICompatible;
        }

        public static AIChatProviderPresetKind ResolveKind(string apiBaseUrl)
        {
            if (!Uri.TryCreate(NormalizeApiBaseUrl(apiBaseUrl), UriKind.Absolute, out Uri uri))
            {
                return AIChatProviderPresetKind.OpenAICompatible;
            }

            string host = uri.Host;
            bool siliconFlowHost =
                host.Equals("siliconflow.cn", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".siliconflow.cn", StringComparison.OrdinalIgnoreCase);
            return siliconFlowHost
                ? AIChatProviderPresetKind.SiliconFlow
                : AIChatProviderPresetKind.OpenAICompatible;
        }

        public static string NormalizeApiBaseUrl(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            return normalized.EndsWith("/", StringComparison.Ordinal)
                ? normalized
                : normalized + "/";
        }
    }
}
