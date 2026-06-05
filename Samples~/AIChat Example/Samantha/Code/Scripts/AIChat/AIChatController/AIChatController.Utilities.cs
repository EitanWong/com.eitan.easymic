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

        // Compiled regexes for CleanText hot path — merged into fewer passes to reduce GC pressure
        private static readonly Regex _codeRegex = new Regex(@"```[\s\S]*?```|`[^`]*`", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex _markdownLinkRegex = new Regex(@"!?\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex _urlRegex = new Regex(
            @"\b(?:https?|ftp|file):\/\/\S+[\/\w]|(?:\bwww\.|\b[a-zA-Z0-9\.\-]+\.(?:com|org|net|io|ai|cn|dev|gov|edu))\S*[\/\w]?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _emphasisRegex = new Regex(@"(\*{1,3}|_{1,3}|~~)(.+?)\1", RegexOptions.Compiled);
        private static readonly Regex _markdownAndSpecialRegex = new Regex(
            @"^\s*#{1,6}\s*|\s*[\*\-]\s*|^>\s*|^\s*\d+\.\s*|^\s*[-*_]{3,}\s*$|[\[\]\(\)*_~#>`!]",
            RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex _whitespaceCollapseRegex = new Regex(@"\s+", RegexOptions.Compiled);

        private static string CleanText(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            string cleaned = input;

            cleaned = _codeRegex.Replace(cleaned, string.Empty);

            cleaned = _markdownLinkRegex.Replace(cleaned, "$1");

            cleaned = _urlRegex.Replace(cleaned, string.Empty);

            cleaned = _emphasisRegex.Replace(cleaned, "$2");
            cleaned = _markdownAndSpecialRegex.Replace(cleaned, string.Empty);

            // Manual whitespace collapse + trim — avoids a Regex.Replace + Trim allocation
            cleaned = CollapseAndTrim(cleaned);

            return cleaned;
        }

        /// <summary>
        /// Single-pass whitespace collapse and trim. Avoids Regex.Replace + Trim allocations.
        /// </summary>
        private static string CollapseAndTrim(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            int start = 0;
            int end = input.Length - 1;

            // TrimStart
            while (start <= end && char.IsWhiteSpace(input[start]))
            {
                start++;
            }

            if (start > end)
            {
                return string.Empty;
            }

            // TrimEnd
            while (end >= start && char.IsWhiteSpace(input[end]))
            {
                end--;
            }

            int len = end - start + 1;
            if (len <= 0)
            {
                return string.Empty;
            }

            // Single pass: collapse inner whitespace runs into single spaces
            var result = new char[len];
            int writePos = 0;
            bool inSpace = false;

            for (int i = start; i <= end; i++)
            {
                char c = input[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!inSpace)
                    {
                        result[writePos++] = ' ';
                        inSpace = true;
                    }
                }
                else
                {
                    result[writePos++] = c;
                    inSpace = false;
                }
            }

            return new string(result, 0, writePos);
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
