#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using Eitan.EasyMic.Editor;
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomPropertyDrawer(typeof(AutomaticSpeechRecognitionConfiguration.ASRPreset))]
    internal sealed class AsrPresetDrawer : PropertyDrawer
    {
        private const float ModeBadgeWidth = 168f;
        private const float KeywordBadgeWidth = 104f;
        private const float BadgeSpacing = 6f;
        private const float MinimumBadgeLayoutWidth = 410f;

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
                ? (string.IsNullOrWhiteSpace(id) ? EasyMicEditorLocalization.SherpaAsrText(SherpaAsrEditorTextKey.PresetFallbackLabel) : id)
                : displayNameProp.stringValue;

            string modeLabel = recognitionModeProp != null
                ? EasyMicEditorLocalization.SherpaAsrRecognitionModeLabel(recognitionModeProp.enumNames[Mathf.Clamp(recognitionModeProp.enumValueIndex, 0, recognitionModeProp.enumNames.Length - 1)])
                : EasyMicEditorLocalization.SherpaAsrText(SherpaAsrEditorTextKey.UnknownLabel);

            bool keywordsEnabled = enableKeywordProp != null && enableKeywordProp.boolValue;
            bool punctuationEnabled = enablePunctuationProp != null && enablePunctuationProp.boolValue;

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);

            bool drawBadges = headerRect.width >= MinimumBadgeLayoutWidth;
            float badgesTotal = drawBadges ? ModeBadgeWidth + KeywordBadgeWidth + BadgeSpacing + 10f : 8f;
            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                Mathf.Max(40f, headerRect.width - badgesTotal),
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                new GUIContent(displayName),
                true,
                AsrInspectorStyles.Foldout);

            if (drawBadges)
            {
                float badgeY = headerRect.y + (AsrInspectorStyles.HeaderHeight - AsrInspectorStyles.BadgeHeight) * 0.5f;
                Rect modeBadgeRect = new Rect(headerRect.xMax - ModeBadgeWidth + 4f, badgeY, ModeBadgeWidth - 10f, AsrInspectorStyles.BadgeHeight);
                AsrInspectorStyles.DrawBadge(modeBadgeRect, new GUIContent(modeLabel), AsrInspectorStyles.ModeBadgeColor);

                Rect keywordBadgeRect = new Rect(modeBadgeRect.x - (KeywordBadgeWidth + BadgeSpacing), badgeY, KeywordBadgeWidth - 10f, AsrInspectorStyles.BadgeHeight);
                AsrInspectorStyles.DrawBadge(
                    keywordBadgeRect,
                    keywordsEnabled ? Styles.KeywordBadgeEnabled : Styles.KeywordBadgeDisabled,
                    keywordsEnabled ? AsrInspectorStyles.EnabledBadgeColor : AsrInspectorStyles.DisabledBadgeColor);
            }

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float contentHeight = GetContentHeight(property, position.width - AsrInspectorStyles.ContentPadding * 2f);
            Rect contentRect = new Rect(position.x, headerRect.yMax, position.width, contentHeight);
            AsrInspectorStyles.DrawContentBackground(contentRect);

            float y = contentRect.y + AsrInspectorStyles.ContentPadding;
            float x = contentRect.x + AsrInspectorStyles.ContentPadding;
            float width = contentRect.width - AsrInspectorStyles.ContentPadding * 2f;
            SherpaAsrEditorControls.RememberContentWidth(property, width);
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float sectionSpacing = AsrInspectorStyles.SectionSpacing;

            // Basics section
            Rect rowRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(rowRect, Styles.BasicsSectionLabel, AsrInspectorStyles.SectionLabel);
            y += line + spacing;

            float idHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
            rowRect = new Rect(x, y, width, idHeight);
            SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, idProp, Styles.IdLabel);
            y += idHeight + spacing;

            float displayNameHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
            rowRect = new Rect(x, y, width, displayNameHeight);
            SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, displayNameProp, Styles.DisplayNameLabel);
            y += displayNameHeight;
            y += sectionSpacing;

            // Recognition section
            rowRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(rowRect, Styles.RecognitionSectionLabel, AsrInspectorStyles.SectionLabel);
            y += line + spacing;

            float modeHeight = EnumPopupHeight(width);
            rowRect = new Rect(x, y, width, modeHeight);
            DrawLocalizedEnumPopup(rowRect, recognitionModeProp, Styles.ModeLabel);
            y += modeHeight + spacing;

            var streamingModelProp = property.FindPropertyRelative("StreamingModelId");
            float modelFieldHeight = SherpaAsrEditorControls.ModelFieldHeight(width);
            rowRect = new Rect(x, y, width, modelFieldHeight);
            SherpaAsrEditorControls.DrawModelIdField(rowRect, streamingModelProp, Styles.StreamingModelLabel, SherpaAsrModelList.StreamingAsr);
            y += modelFieldHeight + spacing;

            var offlineModelProp = property.FindPropertyRelative("OfflineModelId");
            rowRect = new Rect(x, y, width, modelFieldHeight);
            SherpaAsrEditorControls.DrawModelIdField(rowRect, offlineModelProp, Styles.OfflineModelLabel, SherpaAsrModelList.OfflineAsr);
            y += modelFieldHeight + spacing;

            var vadModelProp = property.FindPropertyRelative("VadModelId");
            rowRect = new Rect(x, y, width, modelFieldHeight);
            SherpaAsrEditorControls.DrawModelIdField(rowRect, vadModelProp, Styles.VadModelLabel, SherpaAsrModelList.Vad);
            y += modelFieldHeight;
            y += sectionSpacing;

            // Punctuation section
            rowRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(rowRect, Styles.PunctuationSectionLabel, AsrInspectorStyles.SectionLabel);
            y += line + spacing;

            float punctuationToggleHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
            rowRect = new Rect(x, y, width, punctuationToggleHeight);
            SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, enablePunctuationProp, Styles.EnablePunctuationLabel);
            y += punctuationToggleHeight + spacing;

            var punctuationModelProp = property.FindPropertyRelative("PunctuationModelId");
            if (enablePunctuationProp != null && enablePunctuationProp.boolValue)
            {
                rowRect = new Rect(x, y, width, modelFieldHeight);
                SherpaAsrEditorControls.DrawModelIdField(rowRect, punctuationModelProp, Styles.PunctuationModelLabel, SherpaAsrModelList.Punctuation);
                y += modelFieldHeight + spacing;
            }

            y += sectionSpacing;

            // Keyword options
            if (keywordOptionsProp != null)
            {
                SherpaAsrEditorControls.RememberContentWidth(keywordOptionsProp, GetNestedContentWidth(width));
                float keywordHeight = EditorGUI.GetPropertyHeight(keywordOptionsProp, Styles.KeywordSectionLabel, true);
                rowRect = new Rect(x, y, width, keywordHeight);
                EditorGUI.PropertyField(rowRect, keywordOptionsProp, Styles.KeywordSectionLabel, true);
                y += keywordHeight + sectionSpacing;
            }

            // Turn detection
            var turnOptionsProp = property.FindPropertyRelative("TurnDetectionOptions");
            if (turnOptionsProp != null)
            {
                SherpaAsrEditorControls.RememberContentWidth(turnOptionsProp, GetNestedContentWidth(width));
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

            height += GetContentHeight(property, SherpaAsrEditorControls.GetRememberedContentWidth(property));
            return height;
        }

        private static float GetContentHeight(SerializedProperty property, float width)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float sectionSpacing = AsrInspectorStyles.SectionSpacing;
            float height = AsrInspectorStyles.ContentPadding * 2f;
            float modelFieldHeight = SherpaAsrEditorControls.ModelFieldHeight(width);

            // Basics
            height += line + spacing; // section label
            height += SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width) + spacing; // Id
            height += SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width); // Display name
            height += sectionSpacing;

            // Recognition
            height += line + spacing; // section label
            height += EnumPopupHeight(width) + spacing; // mode
            height += modelFieldHeight + spacing; // streaming model
            height += modelFieldHeight + spacing; // offline model
            height += modelFieldHeight; // vad model
            height += sectionSpacing;

            // Punctuation
            height += line + spacing; // section label
            height += SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width) + spacing; // enable toggle

            var enablePunctuationProp = property.FindPropertyRelative("EnablePunctuation");
            if (enablePunctuationProp != null && enablePunctuationProp.boolValue)
            {
                height += modelFieldHeight + spacing; // punctuation model id
            }

            height += sectionSpacing;

            // Keyword options
            var keywordOptionsProp = property.FindPropertyRelative("KeywordOptions");
            if (keywordOptionsProp != null)
            {
                SherpaAsrEditorControls.RememberContentWidth(keywordOptionsProp, GetNestedContentWidth(width));
                height += EditorGUI.GetPropertyHeight(keywordOptionsProp, Styles.KeywordSectionLabel, true);
                height += sectionSpacing;
            }

            // Turn detection options
            var turnOptionsProp = property.FindPropertyRelative("TurnDetectionOptions");
            if (turnOptionsProp != null)
            {
                SherpaAsrEditorControls.RememberContentWidth(turnOptionsProp, GetNestedContentWidth(width));
                height += EditorGUI.GetPropertyHeight(turnOptionsProp, Styles.TurnSectionLabel, true);
            }

            return height;
        }

        private static void DrawLocalizedEnumPopup(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                if (property != null)
                {
                    EditorGUI.PropertyField(rect, property, label);
                }

                return;
            }

            string[] displayOptions = EasyMicEditorLocalization.SherpaAsrRecognitionModeLabels(property.enumNames);
            if (rect.width >= 300f)
            {
                property.enumValueIndex = EditorGUI.Popup(rect, label.text, property.enumValueIndex, displayOptions);
                return;
            }

            Rect labelRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            Rect popupRect = new Rect(rect.x, labelRect.yMax + AsrInspectorStyles.ContentSpacing, rect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);
            property.enumValueIndex = EditorGUI.Popup(popupRect, property.enumValueIndex, displayOptions);
        }

        private static float EnumPopupHeight(float width)
        {
            return width >= 300f
                ? EditorGUIUtility.singleLineHeight
                : EditorGUIUtility.singleLineHeight * 2f + AsrInspectorStyles.ContentSpacing;
        }

        private static float GetNestedContentWidth(float childPositionWidth)
        {
            return Mathf.Max(0f, childPositionWidth - AsrInspectorStyles.ContentPadding * 2f);
        }

        private static class Styles
        {
            public static GUIContent KeywordBadgeEnabled => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordOnBadge);
            public static GUIContent KeywordBadgeDisabled => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordOffBadge);
            public static GUIContent BasicsSectionLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.SectionBasics);
            public static GUIContent IdLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.PresetIdLabel);
            public static GUIContent DisplayNameLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.DisplayNameLabel);
            public static GUIContent RecognitionSectionLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.SectionRecognition);
            public static GUIContent ModeLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.ModeLabel);
            public static GUIContent StreamingModelLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.StreamingModelLabel);
            public static GUIContent OfflineModelLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.OfflineModelLabel);
            public static GUIContent VadModelLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.VadModelLabel);
            public static GUIContent PunctuationSectionLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.SectionPunctuation);
            public static GUIContent EnablePunctuationLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.EnablePunctuationLabel);
            public static GUIContent PunctuationModelLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.PunctuationModelLabel);
            public static GUIContent KeywordSectionLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.SectionKeywordOptions);
            public static GUIContent TurnSectionLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.SectionTurnDetection);
        }
    }
}
#endif
