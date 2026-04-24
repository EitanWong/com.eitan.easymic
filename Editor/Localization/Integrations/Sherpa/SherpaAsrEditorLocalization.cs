#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Editor
{
    internal enum SherpaAsrEditorTextKey
    {
        PresetFallbackLabel,
        UnknownLabel,
        KeywordOnBadge,
        KeywordOffBadge,
        KeywordEnabledBadge,
        KeywordDisabledBadge,
        SectionBasics,
        PresetIdLabel,
        DisplayNameLabel,
        SectionRecognition,
        ModeLabel,
        StreamingModelLabel,
        OfflineModelLabel,
        VadModelLabel,
        SectionPunctuation,
        EnablePunctuationLabel,
        PunctuationModelLabel,
        SectionKeywordOptions,
        SectionTurnDetection,
        KeywordModelIdLabel,
        CustomKeywordsLabel,
        KeywordScoreLabel,
        KeywordThresholdLabel,
        ContinuousConversationLabel,
        ConversationTimeoutLabel,
        TriggerSoundLabel,
        TriggerClipLabel,
        MinSilenceLabel,
        MaxSilenceLabel,
        ActivePresetLabel,
        PresetsLabel,
        AddPresetLabel,
        DuplicatePresetLabel,
        RemovePresetLabel
    }

    internal static partial class EasyMicEditorLocalization
    {
        private static readonly IReadOnlyDictionary<SherpaAsrEditorTextKey, string> SherpaAsrEnglish =
            new Dictionary<SherpaAsrEditorTextKey, string>
            {
                { SherpaAsrEditorTextKey.PresetFallbackLabel, "ASR Preset" },
                { SherpaAsrEditorTextKey.UnknownLabel, "Unknown" },
                { SherpaAsrEditorTextKey.KeywordOnBadge, "Keyword ON" },
                { SherpaAsrEditorTextKey.KeywordOffBadge, "Keyword OFF" },
                { SherpaAsrEditorTextKey.KeywordEnabledBadge, "Enabled" },
                { SherpaAsrEditorTextKey.KeywordDisabledBadge, "Disabled" },
                { SherpaAsrEditorTextKey.SectionBasics, "Basics" },
                { SherpaAsrEditorTextKey.PresetIdLabel, "Preset ID" },
                { SherpaAsrEditorTextKey.DisplayNameLabel, "Display Name" },
                { SherpaAsrEditorTextKey.SectionRecognition, "Recognition" },
                { SherpaAsrEditorTextKey.ModeLabel, "Mode" },
                { SherpaAsrEditorTextKey.StreamingModelLabel, "Streaming Model ID" },
                { SherpaAsrEditorTextKey.OfflineModelLabel, "Offline Model ID" },
                { SherpaAsrEditorTextKey.VadModelLabel, "VAD Model ID" },
                { SherpaAsrEditorTextKey.SectionPunctuation, "Punctuation" },
                { SherpaAsrEditorTextKey.EnablePunctuationLabel, "Enable Punctuation" },
                { SherpaAsrEditorTextKey.PunctuationModelLabel, "Punctuation Model ID" },
                { SherpaAsrEditorTextKey.SectionKeywordOptions, "Keyword Options" },
                { SherpaAsrEditorTextKey.SectionTurnDetection, "Turn Detection" },
                { SherpaAsrEditorTextKey.KeywordModelIdLabel, "Model ID" },
                { SherpaAsrEditorTextKey.CustomKeywordsLabel, "Custom Keywords" },
                { SherpaAsrEditorTextKey.KeywordScoreLabel, "Score Boost" },
                { SherpaAsrEditorTextKey.KeywordThresholdLabel, "Trigger Threshold" },
                { SherpaAsrEditorTextKey.ContinuousConversationLabel, "Keep Conversation" },
                { SherpaAsrEditorTextKey.ConversationTimeoutLabel, "Timeout (s)" },
                { SherpaAsrEditorTextKey.TriggerSoundLabel, "Trigger Sound" },
                { SherpaAsrEditorTextKey.TriggerClipLabel, "Audio Clip" },
                { SherpaAsrEditorTextKey.MinSilenceLabel, "Min Silence" },
                { SherpaAsrEditorTextKey.MaxSilenceLabel, "Max Silence" },
                { SherpaAsrEditorTextKey.ActivePresetLabel, "Active Preset" },
                { SherpaAsrEditorTextKey.PresetsLabel, "Presets" },
                { SherpaAsrEditorTextKey.AddPresetLabel, "Add Preset" },
                { SherpaAsrEditorTextKey.DuplicatePresetLabel, "Duplicate Preset" },
                { SherpaAsrEditorTextKey.RemovePresetLabel, "Remove Preset" },
            };

        private static readonly IReadOnlyDictionary<SherpaAsrEditorTextKey, string> SherpaAsrChineseSimplified =
            new Dictionary<SherpaAsrEditorTextKey, string>
            {
                { SherpaAsrEditorTextKey.PresetFallbackLabel, "ASR 预设" },
                { SherpaAsrEditorTextKey.UnknownLabel, "未知" },
                { SherpaAsrEditorTextKey.KeywordOnBadge, "关键词开" },
                { SherpaAsrEditorTextKey.KeywordOffBadge, "关键词关" },
                { SherpaAsrEditorTextKey.KeywordEnabledBadge, "启用" },
                { SherpaAsrEditorTextKey.KeywordDisabledBadge, "禁用" },
                { SherpaAsrEditorTextKey.SectionBasics, "基础信息" },
                { SherpaAsrEditorTextKey.PresetIdLabel, "预设 ID" },
                { SherpaAsrEditorTextKey.DisplayNameLabel, "显示名称" },
                { SherpaAsrEditorTextKey.SectionRecognition, "识别设置" },
                { SherpaAsrEditorTextKey.ModeLabel, "识别模式" },
                { SherpaAsrEditorTextKey.StreamingModelLabel, "实时模型 ID" },
                { SherpaAsrEditorTextKey.OfflineModelLabel, "离线模型 ID" },
                { SherpaAsrEditorTextKey.VadModelLabel, "VAD 模型 ID" },
                { SherpaAsrEditorTextKey.SectionPunctuation, "标点服务" },
                { SherpaAsrEditorTextKey.EnablePunctuationLabel, "启用标点" },
                { SherpaAsrEditorTextKey.PunctuationModelLabel, "标点模型 ID" },
                { SherpaAsrEditorTextKey.SectionKeywordOptions, "关键词配置" },
                { SherpaAsrEditorTextKey.SectionTurnDetection, "轮次检测" },
                { SherpaAsrEditorTextKey.KeywordModelIdLabel, "模型 ID" },
                { SherpaAsrEditorTextKey.CustomKeywordsLabel, "自定义关键词" },
                { SherpaAsrEditorTextKey.KeywordScoreLabel, "加权得分" },
                { SherpaAsrEditorTextKey.KeywordThresholdLabel, "触发阈值" },
                { SherpaAsrEditorTextKey.ContinuousConversationLabel, "连续对话" },
                { SherpaAsrEditorTextKey.ConversationTimeoutLabel, "超时(秒)" },
                { SherpaAsrEditorTextKey.TriggerSoundLabel, "触发音效" },
                { SherpaAsrEditorTextKey.TriggerClipLabel, "音效剪辑" },
                { SherpaAsrEditorTextKey.MinSilenceLabel, "最短静音" },
                { SherpaAsrEditorTextKey.MaxSilenceLabel, "最长静音" },
                { SherpaAsrEditorTextKey.ActivePresetLabel, "当前预设" },
                { SherpaAsrEditorTextKey.PresetsLabel, "预设" },
                { SherpaAsrEditorTextKey.AddPresetLabel, "添加预设" },
                { SherpaAsrEditorTextKey.DuplicatePresetLabel, "复制预设" },
                { SherpaAsrEditorTextKey.RemovePresetLabel, "移除预设" },
            };

        public static string SherpaAsrText(SherpaAsrEditorTextKey key)
        {
            var table = IsChineseSimplified ? SherpaAsrChineseSimplified : SherpaAsrEnglish;
            return table.TryGetValue(key, out string value) ? value : key.ToString();
        }

        public static GUIContent SherpaAsrContent(SherpaAsrEditorTextKey key)
        {
            return new GUIContent(SherpaAsrText(key));
        }

        public static string SherpaAsrRecognitionModeLabel(string enumName)
        {
            if (!IsChineseSimplified)
            {
                return enumName;
            }

            switch (enumName)
            {
                case "Streaming":
                    return "实时识别";
                case "OfflineWithVad":
                    return "离线识别 + VAD";
                case "Hybrid":
                    return "混合识别";
                case "KeywordSpottingOnly":
                    return "仅关键词";
                default:
                    return enumName;
            }
        }

        public static string[] SherpaAsrRecognitionModeLabels(string[] enumNames)
        {
            if (enumNames == null)
            {
                return System.Array.Empty<string>();
            }

            var labels = new string[enumNames.Length];
            for (int i = 0; i < enumNames.Length; i++)
            {
                labels[i] = SherpaAsrRecognitionModeLabel(enumNames[i]);
            }

            return labels;
        }
    }
}
#endif
