using System.Text.RegularExpressions;

namespace Eitan.EasyMic.Runtime.Mono.Components.TTS.Internal
{
    internal static class SpeechTextPreprocessor
    {
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex CodeBlockRegex = new Regex(@"```[\s\S]*?```", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(@"`[^`]*`", RegexOptions.Compiled);
        private static readonly Regex MdImageRegex = new Regex(@"!\[([^\\]*)\]\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex MdLinkRegex = new Regex(@"\[([^\\]*)\]\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex MdAutoLinkRegex = new Regex(@"<\s*https?://[^>]+>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BareUrlRegex = new Regex(@"(?:https?://|www\.)[^\s<>\]\}\}，。！？；：】》）]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StrongEmRegex = new Regex(@"(\$\*\*|__)(.*?)\1", RegexOptions.Compiled);
        private static readonly Regex EmRegex = new Regex(@"(\*|_)(.*?)\1", RegexOptions.Compiled);
        private static readonly Regex StrikeRegex = new Regex(@"~~(.*?)~~", RegexOptions.Compiled);
        private static readonly Regex HeadingRegex = new Regex(@"^\s*#{1,6}\s*", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex ListMarkerRegex = new Regex(@"^(\s*[-*+]|\s*\d+\.)\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex SentenceSplitRegex = new Regex(@"(?<=[.!?\n。！？])(?=\s|\S)", RegexOptions.Compiled);

        public static string[] SplitSentences(string text) => SentenceSplitRegex.Split(text);

        public static string CleanForTts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string cleaned = text;
            cleaned = cleaned.Replace("{{ ", string.Empty).Replace(" }}", string.Empty);
            cleaned = CodeBlockRegex.Replace(cleaned, string.Empty);
            cleaned = InlineCodeRegex.Replace(cleaned, string.Empty);
            cleaned = HtmlTagRegex.Replace(cleaned, string.Empty);
            cleaned = MdImageRegex.Replace(cleaned, m => m.Groups[1].Value);
            cleaned = MdLinkRegex.Replace(cleaned, m => m.Groups[1].Value);
            cleaned = MdAutoLinkRegex.Replace(cleaned, string.Empty);
            cleaned = BareUrlRegex.Replace(cleaned, string.Empty);
            cleaned = StrongEmRegex.Replace(cleaned, m => m.Groups[2].Value);
            cleaned = EmRegex.Replace(cleaned, m => m.Groups[2].Value);
            cleaned = StrikeRegex.Replace(cleaned, m => m.Groups[1].Value);
            cleaned = HeadingRegex.Replace(cleaned, string.Empty);
            cleaned = ListMarkerRegex.Replace(cleaned, string.Empty);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        public static string ApplyPronunciationRules(string sentence) => sentence.Replace("海晟", "海胜");
    }
}
