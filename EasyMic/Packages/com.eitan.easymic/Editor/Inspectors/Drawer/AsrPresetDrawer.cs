#if UNITY_EDITOR && EASYMIC_SHERPA_ONNX_INTEGRATION
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Runtime.Mono.ASR;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(AutomaticSpeechRecognitionConfiguration.ASRPreset))]
    internal sealed class AsrPresetDrawer : PropertyDrawer
    {
        private const float BadgeWidth = 118f;
        private const float BadgeSpacing = 6f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var idProp = property.FindPropertyRelative("Id");
            var displayNameProp = property.FindPropertyRelative("DisplayName");
            var recognitionModeProp = property.FindPropertyRelative("RecognitionMode");
            var keywordOptionsProp = property.FindPropertyRelative("KeywordOptions");
            var enableKeywordProp = keywordOptionsProp?.FindPropertyRelative("Enabled");
            var enablePunctuationProp = property.FindPropertyRelative("EnablePunctuation");

            string id = idProp?.stringValue ?? string.Empty;
            string displayName = string.IsNullOrWhiteSpace(displayNameProp?.stringValue)
                ? (string.IsNullOrWhiteSpace(id) ? "ASR Preset" : id)
                : displayNameProp.stringValue;

            string modeLabel = recognitionModeProp != null
                ? recognitionModeProp.enumDisplayNames[Mathf.Clamp(recognitionModeProp.enumValueIndex, 0, recognitionModeProp.enumDisplayNames.Length - 1)]
                : "Unknown";

            bool keywordsEnabled = enableKeywordProp != null && enableKeywordProp.boolValue;
            bool punctuationEnabled = enablePunctuationProp != null && enablePunctuationProp.boolValue;

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);

            float badgesTotal = BadgeWidth * 2f + BadgeSpacing + 10f;
            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                headerRect.width - badgesTotal,
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                new GUIContent(displayName, string.Format(CultureInfo.InvariantCulture, "Preset ID: {0}", string.IsNullOrEmpty(id) ? "<default>" : id)),
                true,
                AsrInspectorStyles.Foldout);

            float badgeY = headerRect.y + (AsrInspectorStyles.HeaderHeight - AsrInspectorStyles.BadgeHeight) * 0.5f;
            Rect modeBadgeRect = new Rect(headerRect.xMax - BadgeWidth + 4f, badgeY, BadgeWidth - 10f, AsrInspectorStyles.BadgeHeight);
            AsrInspectorStyles.DrawBadge(modeBadgeRect, new GUIContent(modeLabel), AsrInspectorStyles.ModeBadgeColor);

            Rect keywordBadgeRect = new Rect(modeBadgeRect.x - (BadgeWidth + BadgeSpacing), badgeY, BadgeWidth - 10f, AsrInspectorStyles.BadgeHeight);
            AsrInspectorStyles.DrawBadge(
                keywordBadgeRect,
                keywordsEnabled ? Styles.KeywordBadgeEnabled : Styles.KeywordBadgeDisabled,
                keywordsEnabled ? AsrInspectorStyles.EnabledBadgeColor : AsrInspectorStyles.DisabledBadgeColor);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float contentHeight = GetContentHeight(property);
            Rect contentRect = new Rect(position.x, headerRect.yMax, position.width, contentHeight);
            AsrInspectorStyles.DrawContentBackground(contentRect);

            float y = contentRect.y + AsrInspectorStyles.ContentPadding;
            float x = contentRect.x + AsrInspectorStyles.ContentPadding;
            float width = contentRect.width - AsrInspectorStyles.ContentPadding * 2f;
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float sectionSpacing = AsrInspectorStyles.SectionSpacing;

            // Basics section
            Rect rowRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(rowRect, Styles.BasicsSectionLabel, AsrInspectorStyles.SectionLabel);
            y += line + spacing;

            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, idProp, Styles.IdLabel);
            y += line + spacing;

            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, displayNameProp, Styles.DisplayNameLabel);
            y += line;
            y += sectionSpacing;

            // Recognition section
            rowRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(rowRect, Styles.RecognitionSectionLabel, AsrInspectorStyles.SectionLabel);
            y += line + spacing;

            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, recognitionModeProp, Styles.ModeLabel);
            y += line + spacing;

            var streamingModelProp = property.FindPropertyRelative("StreamingModelId");
            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, streamingModelProp, Styles.StreamingModelLabel);
            y += line + spacing;

            var offlineModelProp = property.FindPropertyRelative("OfflineModelId");
            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, offlineModelProp, Styles.OfflineModelLabel);
            y += line + spacing;

            var vadModelProp = property.FindPropertyRelative("VadModelId");
            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, vadModelProp, Styles.VadModelLabel);
            y += line;
            y += sectionSpacing;

            // Punctuation section
            rowRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(rowRect, Styles.PunctuationSectionLabel, AsrInspectorStyles.SectionLabel);
            y += line + spacing;

            rowRect = new Rect(x, y, width, line);
            EditorGUI.PropertyField(rowRect, enablePunctuationProp, Styles.EnablePunctuationLabel);
            y += line + spacing;

            var punctuationModelProp = property.FindPropertyRelative("PunctuationModelId");
            if (enablePunctuationProp != null && enablePunctuationProp.boolValue)
            {
                rowRect = new Rect(x, y, width, line);
                EditorGUI.PropertyField(rowRect, punctuationModelProp, Styles.PunctuationModelLabel);
                y += line + spacing;
            }

            y += sectionSpacing;

            // Keyword options
            if (keywordOptionsProp != null)
            {
                float keywordHeight = EditorGUI.GetPropertyHeight(keywordOptionsProp, Styles.KeywordSectionLabel, true);
                rowRect = new Rect(x, y, width, keywordHeight);
                EditorGUI.PropertyField(rowRect, keywordOptionsProp, Styles.KeywordSectionLabel, true);
                y += keywordHeight + sectionSpacing;
            }

            // Turn detection
            var turnOptionsProp = property.FindPropertyRelative("TurnDetectionOptions");
            if (turnOptionsProp != null)
            {
                float turnHeight = EditorGUI.GetPropertyHeight(turnOptionsProp, Styles.TurnSectionLabel, true);
                rowRect = new Rect(x, y, width, turnHeight);
                EditorGUI.PropertyField(rowRect, turnOptionsProp, Styles.TurnSectionLabel, true);
                y += turnHeight;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = AsrInspectorStyles.HeaderHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += GetContentHeight(property);
            return height;
        }

        private static float GetContentHeight(SerializedProperty property)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float sectionSpacing = AsrInspectorStyles.SectionSpacing;
            float height = AsrInspectorStyles.ContentPadding * 2f;

            // Basics
            height += line + spacing; // section label
            height += line + spacing; // Id
            height += line; // Display name
            height += sectionSpacing;

            // Recognition
            height += line + spacing; // section label
            height += line + spacing; // mode
            height += line + spacing; // streaming model
            height += line + spacing; // offline model
            height += line; // vad model
            height += sectionSpacing;

            // Punctuation
            height += line + spacing; // section label
            height += line + spacing; // enable toggle

            var enablePunctuationProp = property.FindPropertyRelative("EnablePunctuation");
            if (enablePunctuationProp != null && enablePunctuationProp.boolValue)
            {
                height += line + spacing; // punctuation model id
            }

            height += sectionSpacing;

            // Keyword options
            var keywordOptionsProp = property.FindPropertyRelative("KeywordOptions");
            if (keywordOptionsProp != null)
            {
                height += EditorGUI.GetPropertyHeight(keywordOptionsProp, Styles.KeywordSectionLabel, true);
                height += sectionSpacing;
            }

            // Turn detection options
            var turnOptionsProp = property.FindPropertyRelative("TurnDetectionOptions");
            if (turnOptionsProp != null)
            {
                height += EditorGUI.GetPropertyHeight(turnOptionsProp, Styles.TurnSectionLabel, true);
            }

            return height;
        }

        private static class Styles
        {
            public static readonly GUIContent KeywordBadgeEnabled = new GUIContent("关键词 ON", "Keyword spotting is enabled for this preset.");
            public static readonly GUIContent KeywordBadgeDisabled = new GUIContent("关键词 OFF", "Keyword spotting is disabled for this preset.");

            public static readonly GUIContent BasicsSectionLabel = new GUIContent("基础信息 / Basics");
            public static readonly GUIContent IdLabel = new GUIContent("配置 ID / Preset ID", "用于激活预设的唯一标识 / Unique identifier used to activate this preset.");
            public static readonly GUIContent DisplayNameLabel = new GUIContent("显示名称 / Display Name", "在编辑器中展示的易读名称 / Friendly name shown in the editor.");

            public static readonly GUIContent RecognitionSectionLabel = new GUIContent("识别设置 / Recognition");
            public static readonly GUIContent ModeLabel = new GUIContent("识别模式 / Mode", "Streaming / Offline / Hybrid 模式选择 / Select between streaming, offline with VAD, or hybrid recognition.");
            public static readonly GUIContent StreamingModelLabel = new GUIContent("Streaming 模型 ID", "实时识别所使用的模型标识 / Identifier for the streaming recognition model.");
            public static readonly GUIContent OfflineModelLabel = new GUIContent("离线模型 ID", "离线识别阶段所需模型标识 / Identifier for the offline recognition model.");
            public static readonly GUIContent VadModelLabel = new GUIContent("VAD 模型 ID", "语音活动检测器模型 / Identifier for the voice activity detection model.");

            public static readonly GUIContent PunctuationSectionLabel = new GUIContent("标点服务 / Punctuation");
            public static readonly GUIContent EnablePunctuationLabel = new GUIContent("启用标点 / Enable Punctuation", "开启后会在结果中添加实时标点 / Adds punctuation to recognized text when enabled.");
            public static readonly GUIContent PunctuationModelLabel = new GUIContent("标点模型 ID", "标点服务使用的模型标识 / Identifier of the punctuation model.");

            public static readonly GUIContent KeywordSectionLabel = new GUIContent("关键词配置 / Keyword Options");
            public static readonly GUIContent TurnSectionLabel = new GUIContent("轮次检测 / Turn Detection");
        }
    }
}
#endif
