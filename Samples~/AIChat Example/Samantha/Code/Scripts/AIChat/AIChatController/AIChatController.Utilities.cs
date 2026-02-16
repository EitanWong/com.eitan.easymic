#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System.Text.RegularExpressions;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
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
    }
}
#endif
