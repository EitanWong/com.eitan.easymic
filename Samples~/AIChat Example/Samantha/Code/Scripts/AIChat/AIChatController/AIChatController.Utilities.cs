#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private const string DefaultLlmModel = "gpt-5.4";
        private const string LegacyDefaultLlmModel = "gpt-5.2";
        private const string DefaultSiliconFlowLlmModel = "Qwen/Qwen3.5-9B";

        private string GetSystemPrompt()
        {
            return _systemPromptCache;
        }

        private void RefreshSystemPromptCache()
        {
            var profile = Config.SystemPromptProfile;
            if (ReferenceEquals(profile, _cachedSystemPromptProfile))
            {
                return;
            }

            _cachedSystemPromptProfile = profile;
            _systemPromptCache = ReadSystemPromptFromProfile();
        }

        private string ReadSystemPromptFromProfile()
        {
            var profile = Config.SystemPromptProfile;
            if (profile == null)
            {
                return string.Empty;
            }

            return profile.GetCombinedText("\n");
        }

        private string GetRawResponse()
        {
            return _requestOrchestrator?.GetRawResponse() ?? string.Empty;
        }

        private string GetCleanedResponse()
        {
            return _requestOrchestrator?.GetCleanedResponse() ?? string.Empty;
        }

        private static string CleanText(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string cleaned = input;

            cleaned = Regex.Replace(cleaned, @"```[\s\S]*?```", string.Empty, RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"`[^`]*`", string.Empty);

            cleaned = Regex.Replace(cleaned, @"!?\[([^\]]*)\]\([^)]*\)", "$1");

            cleaned = Regex.Replace(cleaned,
                @"\b(?:https?|ftp|file):\/\/\S+[\/\w]|(?:\bwww\.|\b[a-zA-Z0-9\.\-]+\.(?:com|org|net|io|ai|cn|dev|gov|edu))\S*[\/\w]?",
                string.Empty, RegexOptions.IgnoreCase);

            cleaned = Regex.Replace(cleaned, @"(\*{1,3}|_{1,3}|~~)(.+?)\1", "$2");
            cleaned = Regex.Replace(cleaned, @"^\s*#{1,6}\s*|\s*[\*\-]\s*|^>\s*|^\s*\d+\.\s*", string.Empty, RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"^\s*[-*_]{3,}\s*$", string.Empty, RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"[\[\]\(\)*_~#>`!]", string.Empty);

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        private void ResetResponseLatencyTracking()
        {
            lock (_stateLock)
            {
                _activeResponseStopwatch = Stopwatch.StartNew();
                _lastFirstTokenLatencyMs = 0f;
                _lastFirstSentenceLatencyMs = 0f;
                _lastFirstAudioLatencyMs = 0f;
                _lastPlaybackBufferedSeconds = 0f;
            }
        }

        private void EndResponseLatencyTracking(long generation)
        {
            lock (_stateLock)
            {
                if (!IsCurrentResponseGeneration(generation))
                {
                    return;
                }

                _activeResponseStopwatch = null;
            }
        }

        private void TryCaptureLatencyMilestone(ref float targetField, long generation)
        {
            lock (_stateLock)
            {
                if (!IsCurrentResponseGeneration(generation) || targetField > 0f || _activeResponseStopwatch == null)
                {
                    return;
                }

                targetField = (float)_activeResponseStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        private bool IsCurrentResponseGeneration(long generation)
        {
            return Interlocked.Read(ref _responseGeneration) == generation;
        }

        private string ResolveLlmModel(string requestedModel = null)
        {
            string model = string.IsNullOrWhiteSpace(requestedModel) ? Config.LlmModel : requestedModel;
            model = string.IsNullOrWhiteSpace(model) ? DefaultLlmModel : model.Trim();

            if (!SiliconFlowExpressiveTtsInputPlugin.IsSiliconFlowApiBaseUrl(Config.ApiBaseUrl))
            {
                return model;
            }

            if (IsOpenAIDefaultModelPlaceholder(model))
            {
                return DefaultSiliconFlowLlmModel;
            }

            return model;
        }

        private static bool IsOpenAIDefaultModelPlaceholder(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return true;
            }

            return string.Equals(model, DefaultLlmModel, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(model, LegacyDefaultLlmModel, StringComparison.OrdinalIgnoreCase);
        }

        private void NotifyPluginHost(Action<AIChatPluginHost> notify)
        {
            if (notify == null || _pluginHost == null)
            {
                return;
            }

            if (IsOnUnityThread)
            {
                notify(_pluginHost);
                return;
            }

            PostToUnityThread(() =>
            {
                if (_pluginHost != null)
                {
                    notify(_pluginHost);
                }
            });
        }
    }
}
#endif
