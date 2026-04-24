using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Auto-formats SiliconFlow CosyVoice input:
    /// 1) builds/refreshes a short instruction,
    /// 2) injects end marker,
    /// 3) inserts expressive pitch markers.
    /// </summary>
    public sealed class SiliconFlowExpressiveTtsInputPlugin : MonoBehaviour
    {
        public const string EndOfPromptMarker = "<|endofprompt|>";
        private const string BreathMarker = "[breath]";
        private const string LaughterMarker = "[laughter]";

        private const int MinInstructionChars = 24;
        private const int MaxInstructionChars = 256;
        private const int DefaultInstructionChars = 96;
        private const int MaxContextCharacters = 1200;
        private const int InstructionRequestMaxTokens = 96;
        private const float InstructionTemperature = 0.2f;
        private const string DefaultInstruction =
            "Please read with vivid emotion, natural rhythm, and clear expressiveness.";
        private const string InstructionSystemPrompt =
            "You are a speech style planner for expressive TTS. " +
            "From user context, output exactly one concise speaking instruction sentence describing emotion, pace, role style, and optional dialect. " +
            "Return plain text only.";

        private static readonly string[] PositiveKeywords =
        {
            "happy", "glad", "excited", "joy", "great", "awesome", "wonderful", "amazing", "fun", "haha", "lol",
            "happy", "smile", "cheerful", "celebrate",
            "kaixin", "gaoxing", "xinfu",
            "hahaha", "hehe",
            "开心", "高兴", "快乐", "兴奋", "太棒", "真好", "哈哈", "嘿嘿", "笑", "庆祝"
        };

        private static readonly string[] SadKeywords =
        {
            "sad", "upset", "sorry", "regret", "depressed", "hurt", "pain",
            "难过", "伤心", "抱歉", "遗憾", "失落", "痛苦"
        };

        private static readonly string[] QuestionKeywords =
        {
            "why", "how", "what", "when", "where", "which",
            "吗", "呢", "为什么", "怎么", "如何", "是不是", "是否", "?", "？"
        };

        private static readonly string[] UrgentKeywords =
        {
            "urgent", "quick", "hurry", "immediately", "asap", "now",
            "马上", "立刻", "赶紧", "紧急", "尽快", "快点"
        };

        [Serializable]
        public struct RuntimeProfile
        {
            public bool Enabled;
            public bool RequireSiliconFlowHost;
            public bool RequireCosyVoiceModel;
            public bool KeepExistingPromptWhenMarkerPresent;
            public bool AppendPitchMarkers;
            public string Instruction;
            public string PitchMarkers;
        }

        public sealed class RuntimeBinding
        {
            private readonly RuntimeProfile _baseProfile;
            private string _instruction;

            internal RuntimeBinding(RuntimeProfile baseProfile, string initialInstruction)
            {
                _baseProfile = baseProfile;
                _instruction = initialInstruction ?? string.Empty;
            }

            public RuntimeProfile CreateCurrentProfile()
            {
                var profile = _baseProfile;
                profile.Instruction = GetInstruction();
                return profile;
            }

            public string GetInstruction()
            {
                return Interlocked.CompareExchange(ref _instruction, null, null) ?? string.Empty;
            }

            public void SetInstruction(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                Interlocked.Exchange(ref _instruction, value.Trim());
            }
        }

        [Header("General")]
        [SerializeField] private bool _enabled = true;
        [Header("LLM Instruction")]
        [SerializeField] private bool _useLlmInstructionFromContext = true;
        [SerializeField] private string _instructionModelOverride;

        public bool IsEnabled => _enabled && isActiveAndEnabled;

        public RuntimeProfile CreateRuntimeProfile()
        {
            return new RuntimeProfile
            {
                Enabled = IsEnabled,
                RequireSiliconFlowHost = true,
                RequireCosyVoiceModel = true,
                KeepExistingPromptWhenMarkerPresent = true,
                AppendPitchMarkers = true,
                Instruction = DefaultInstruction,
                PitchMarkers = string.Empty
            };
        }

        public RuntimeBinding CreateRuntimeBinding()
        {
            RuntimeProfile profile = CreateRuntimeProfile();
            string initialInstruction = NormalizeInstruction(profile.Instruction, DefaultInstructionChars);
            return new RuntimeBinding(profile, initialInstruction);
        }

        internal async Task RefreshInstructionFromContextAsync(
            OpenAICompatibleClient client,
            string llmModel,
            string userContext,
            RuntimeBinding binding,
            CancellationToken cancellationToken)
        {
            if (!_useLlmInstructionFromContext || binding == null || client == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(userContext))
            {
                return;
            }

            binding.SetInstruction(InferInstructionFromContext(userContext));

            string model = string.IsNullOrWhiteSpace(_instructionModelOverride)
                ? llmModel
                : _instructionModelOverride.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            var request = new OpenAIChatRequest
            {
                Model = model,
                Stream = false,
                Temperature = InstructionTemperature,
                MaxTokens = InstructionRequestMaxTokens,
                EnableThinkingOverride = false,
                Messages = BuildInstructionMessages(userContext)
            };

            try
            {
                OpenAIChatResult result = await client.ChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);
                if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
                {
                    return;
                }

                string instruction = NormalizeInstruction(result.Content, DefaultInstructionChars);
                if (instruction.Length == 0)
                {
                    return;
                }

                binding.SetInstruction(instruction);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }

        public static bool ShouldApply(string apiBaseUrl, string model, RuntimeProfile profile)
        {
            if (!profile.Enabled)
            {
                return false;
            }

            if (profile.RequireSiliconFlowHost && !IsSiliconFlowApiBaseUrl(apiBaseUrl))
            {
                return false;
            }

            if (profile.RequireCosyVoiceModel && !IsCosyVoiceModel(model))
            {
                return false;
            }

            return true;
        }

        public static string FormatInput(string input, RuntimeProfile profile)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return input ?? string.Empty;
            }

            string normalizedInput = CollapseWhitespace(input);
            bool hasMarker = TrySplitPromptAndContent(normalizedInput, out string existingPrompt, out string content);
            content = string.IsNullOrWhiteSpace(content) ? normalizedInput : content;

            if (profile.AppendPitchMarkers)
            {
                content = AutoInsertExpressiveMarkers(content);
            }

            if (hasMarker && profile.KeepExistingPromptWhenMarkerPresent)
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return existingPrompt;
                }

                return $"{existingPrompt} {content}".Trim();
            }

            string instruction = NormalizeInstruction(profile.Instruction, DefaultInstructionChars);
            if (instruction.Length == 0)
            {
                instruction = DefaultInstruction;
            }

            return $"{instruction} {EndOfPromptMarker} {content}".Trim();
        }

        internal static string NormalizeInstruction(string value, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            int clampedMax = Mathf.Clamp(maxCharacters, MinInstructionChars, MaxInstructionChars);
            string collapsed = CollapseWhitespace(value);
            collapsed = TrimSurroundingQuotes(collapsed);
            if (collapsed.Length <= clampedMax)
            {
                return collapsed;
            }

            int cut = collapsed.LastIndexOf(' ', clampedMax);
            if (cut < MinInstructionChars)
            {
                cut = clampedMax;
            }

            return collapsed.Substring(0, cut).Trim();
        }

        internal static string AutoInsertExpressiveMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text ?? string.Empty;
            }

            string output = CollapseWhitespace(text);

            if (!ContainsMarker(output, BreathMarker))
            {
                output = InsertBreathMarkers(output);
            }

            if (!ContainsMarker(output, LaughterMarker) && ShouldInjectLaughter(output))
            {
                output = AppendMarker(output, LaughterMarker);
            }

            return CollapseWhitespace(output);
        }

        public static bool IsSiliconFlowApiBaseUrl(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                return false;
            }

            string normalized = apiBaseUrl.Trim();
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                return uri.Host.IndexOf("siliconflow", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return normalized.IndexOf("siliconflow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsCosyVoiceModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return false;
            }

            return model.IndexOf("cosyvoice", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TrySplitPromptAndContent(string value, out string prompt, out string content)
        {
            prompt = string.Empty;
            content = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int markerIndex = value.IndexOf(EndOfPromptMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return false;
            }

            string left = value.Substring(0, markerIndex).Trim();
            string right = value.Substring(markerIndex + EndOfPromptMarker.Length).Trim();
            prompt = left.Length == 0
                ? EndOfPromptMarker
                : $"{left} {EndOfPromptMarker}";
            content = right;
            return true;
        }

        private List<OpenAIChatMessage> BuildInstructionMessages(string userContext)
        {
            int maxChars = Mathf.Clamp(DefaultInstructionChars, MinInstructionChars, MaxInstructionChars);
            string compressedContext = NormalizeUserContext(userContext, MaxContextCharacters);
            string userPrompt =
                $"User context:\n{compressedContext}\n\n" +
                $"Write one instruction sentence (<= {maxChars} chars). " +
                "No quotes, no markdown, no explanation, no line breaks.";

            return new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.System(InstructionSystemPrompt),
                OpenAIChatMessage.User(userPrompt)
            };
        }

        private static string InferInstructionFromContext(string userContext)
        {
            if (string.IsNullOrWhiteSpace(userContext))
            {
                return DefaultInstruction;
            }

            string normalized = CollapseWhitespace(userContext);

            if (ContainsAny(normalized, SadKeywords))
            {
                return "Please read softly with a gentle, comforting emotion and slower pace.";
            }

            if (ContainsAny(normalized, UrgentKeywords))
            {
                return "Please read with energetic urgency, slightly faster pace, and firm emphasis.";
            }

            if (ContainsAny(normalized, QuestionKeywords))
            {
                return "Please read with a curious conversational tone, clear pauses, and natural rhythm.";
            }

            if (ContainsAny(normalized, PositiveKeywords))
            {
                return "Please read with a bright happy emotion, lively pace, and warm expressive tone.";
            }

            return DefaultInstruction;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null || keywords.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < keywords.Length; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMarker(string text, string marker)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker))
            {
                return false;
            }

            return text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string InsertBreathMarkers(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 18)
            {
                return text ?? string.Empty;
            }

            string output = text;
            int firstPause = FindPauseIndex(output, 10);
            if (firstPause < 0)
            {
                if (output.Length > 40)
                {
                    output = AppendMarker(output, BreathMarker);
                }

                return output;
            }

            output = InsertMarkerAfterIndex(output, firstPause, BreathMarker);

            if (output.Length < 52)
            {
                return output;
            }

            int secondSearchStart = Clamp(firstPause + 18, 0, output.Length - 1);
            int secondPause = FindPauseIndex(output, secondSearchStart);
            if (secondPause >= 0)
            {
                output = InsertMarkerAfterIndex(output, secondPause, BreathMarker);
            }

            return output;
        }

        private static int FindPauseIndex(string text, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return -1;
            }

            int start = Clamp(startIndex, 0, text.Length - 1);
            for (int i = start; i < text.Length; i++)
            {
                if (IsPauseCharacter(text[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsPauseCharacter(char ch)
        {
            return ch == ',' || ch == '，' || ch == ';' || ch == '；' || ch == '、' ||
                   ch == '.' || ch == '!' || ch == '?' || ch == '。' || ch == '！' || ch == '？';
        }

        private static string InsertMarkerAfterIndex(string text, int index, string marker)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker))
            {
                return text ?? string.Empty;
            }

            if (index < 0 || index >= text.Length)
            {
                return text;
            }

            int insertPos = index + 1;
            while (insertPos < text.Length && char.IsWhiteSpace(text[insertPos]))
            {
                insertPos++;
            }

            if (insertPos < text.Length)
            {
                int markerLength = marker.Length;
                if (insertPos + markerLength <= text.Length)
                {
                    string existing = text.Substring(insertPos, markerLength);
                    if (existing.Equals(marker, StringComparison.OrdinalIgnoreCase))
                    {
                        return text;
                    }
                }
            }

            return text.Insert(insertPos, $" {marker} ");
        }

        private static bool ShouldInjectLaughter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return ContainsAny(text, PositiveKeywords);
        }

        private static string AppendMarker(string text, string marker)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return marker ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(marker))
            {
                return text;
            }

            if (ContainsMarker(text, marker))
            {
                return text;
            }

            return $"{text.Trim()} {marker}";
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static string NormalizeUserContext(string value, int maxCharacters)
        {
            string normalized = CollapseWhitespace(value);
            if (normalized.Length <= maxCharacters)
            {
                return normalized;
            }

            return normalized.Substring(0, maxCharacters).Trim();
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            bool inWhitespace = false;

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (char.IsWhiteSpace(ch))
                {
                    if (inWhitespace)
                    {
                        continue;
                    }

                    builder.Append(' ');
                    inWhitespace = true;
                    continue;
                }

                builder.Append(ch);
                inWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        private static string TrimSurroundingQuotes(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.Length >= 2)
            {
                bool hasQuotePair =
                    (trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                    (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') ||
                    (trimmed[0] == '`' && trimmed[trimmed.Length - 1] == '`');

                if (hasQuotePair)
                {
                    return trimmed.Substring(1, trimmed.Length - 2).Trim();
                }
            }

            return trimmed;
        }
    }
}
