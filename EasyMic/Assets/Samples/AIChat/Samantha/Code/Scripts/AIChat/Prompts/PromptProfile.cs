using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CreateAssetMenu(menuName = "AIChat/Prompt Profile", fileName = "PromptProfile")]
    public sealed class PromptProfile : ScriptableObject
    {
        // [Header("Prompts")]
        public TextAsset[] Prompts;

        public string GetRandomText()
        {
            if (Prompts == null || Prompts.Length == 0)
            {
                return string.Empty;
            }

            int index = Random.Range(0, Prompts.Length);
            return GetText(Prompts[index]);
        }

        public string GetCombinedText(string separator = "\n")
        {
            if (Prompts == null || Prompts.Length == 0)
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < Prompts.Length; i++)
            {
                string text = GetText(Prompts[i]);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(separator);
                }

                builder.Append(text);
            }

            return builder.ToString();
        }

        public string GetTextAt(int index)
        {
            if (Prompts == null || index < 0 || index >= Prompts.Length)
            {
                return string.Empty;
            }

            return GetText(Prompts[index]);
        }

        private static string GetText(TextAsset asset)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.text))
            {
                return string.Empty;
            }

            return asset.text.Trim();
        }
    }
}
