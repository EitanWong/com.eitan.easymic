#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Editor;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomPropertyDrawer(typeof(KeywordOptions))]
    internal sealed class KeywordOptionsDrawer : PropertyDrawer
    {
        private const float BadgeWidth = 112f;
        private const float MinimumBadgeLayoutWidth = 260f;
        private const float MinimumTwoColumnWidth = 360f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var enabledProp = property.FindPropertyRelative("Enabled");
            bool isEnabled = enabledProp?.boolValue ?? false;

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);

            bool drawBadge = headerRect.width >= MinimumBadgeLayoutWidth;
            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                Mathf.Max(40f, headerRect.width - (drawBadge ? BadgeWidth + 12f : 8f)),
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true, AsrInspectorStyles.Foldout);

            if (enabledProp != null && drawBadge)
            {
                Rect badgeRect = new Rect(
                    headerRect.xMax - BadgeWidth + 4f,
                    headerRect.y + (AsrInspectorStyles.HeaderHeight - AsrInspectorStyles.BadgeHeight) * 0.5f,
                    BadgeWidth - 10f,
                    AsrInspectorStyles.BadgeHeight);

                if (GUI.Button(badgeRect, GUIContent.none, GUIStyle.none))
                {
                    enabledProp.boolValue = !enabledProp.boolValue;
                    isEnabled = enabledProp.boolValue;
                }

                AsrInspectorStyles.DrawBadge(
                    badgeRect,
                    isEnabled ? Styles.EnabledBadgeContent : Styles.DisabledBadgeContent,
                    isEnabled ? AsrInspectorStyles.EnabledBadgeColor : AsrInspectorStyles.DisabledBadgeColor);
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

            Rect rowRect = new Rect(x, y, width, line);
            var modelProp = property.FindPropertyRelative("ModelId");
            float modelFieldHeight = SherpaAsrEditorControls.ModelFieldHeight(width);
            rowRect = new Rect(x, y, width, modelFieldHeight);
            SherpaAsrEditorControls.DrawModelIdField(rowRect, modelProp, Styles.ModelIdLabel, SherpaAsrModelList.Keyword);
            y += modelFieldHeight + spacing;

            var customKeywordsProp = property.FindPropertyRelative("CustomKeywords");
            if (customKeywordsProp != null)
            {
                float keywordsHeight = SherpaAsrEditorControls.CustomKeywordsHeight(customKeywordsProp, width);
                rowRect = new Rect(x, y, width, keywordsHeight);
                SherpaAsrEditorControls.DrawCustomKeywords(rowRect, customKeywordsProp, Styles.CustomKeywordsLabel);
                y += keywordsHeight + spacing;
            }

            var scoreProp = property.FindPropertyRelative("KeywordsScore");
            var thresholdProp = property.FindPropertyRelative("KeywordsThreshold");
            if (width >= MinimumTwoColumnWidth)
            {
                rowRect = new Rect(x, y, width, line);
                float halfWidth = (rowRect.width - spacing) * 0.5f;
                var leftRect = new Rect(rowRect.x, rowRect.y, halfWidth, line);
                var rightRect = new Rect(rowRect.x + halfWidth + spacing, rowRect.y, halfWidth, line);
                EditorGUI.PropertyField(leftRect, scoreProp, Styles.ScoreLabel);
                EditorGUI.PropertyField(rightRect, thresholdProp, Styles.ThresholdLabel);
                y += line + spacing;
            }
            else
            {
                float scoreHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
                rowRect = new Rect(x, y, width, scoreHeight);
                SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, scoreProp, Styles.ScoreLabel);
                y += scoreHeight + spacing;

                float thresholdHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
                rowRect = new Rect(x, y, width, thresholdHeight);
                SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, thresholdProp, Styles.ThresholdLabel);
                y += thresholdHeight + spacing;
            }

            var continuousProp = property.FindPropertyRelative("ContinuousConversation");
            if (continuousProp != null)
            {
                rowRect = new Rect(x, y, width, line);
                EditorGUI.PropertyField(rowRect, continuousProp, Styles.ContinuousLabel);
                y += line + spacing;

                if (continuousProp.boolValue)
                {
                    var durationProp = property.FindPropertyRelative("ContinuousConversationTimeoutSeconds");
                    float durationHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
                    rowRect = new Rect(x, y, width, durationHeight);
                    SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, durationProp, Styles.TimeoutLabel);
                    y += durationHeight + spacing;
                }
            }

            var triggerProp = property.FindPropertyRelative("UseTriggerSound");
            if (triggerProp != null)
            {
                rowRect = new Rect(x, y, width, line);
                EditorGUI.PropertyField(rowRect, triggerProp, Styles.TriggerToggleLabel);
                y += line + spacing;

                if (triggerProp.boolValue)
                {
                    var clipProp = property.FindPropertyRelative("TriggerSoundClip");
                    float clipHeight = SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width);
                    rowRect = new Rect(x, y, width, clipHeight);
                    SherpaAsrEditorControls.DrawResponsivePropertyField(rowRect, clipProp, Styles.TriggerClipLabel);
                    y += clipHeight + spacing;
                }
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
            float height = AsrInspectorStyles.ContentPadding * 2f;

            height += SherpaAsrEditorControls.ModelFieldHeight(width) + spacing; // Model ID

            var customKeywordsProp = property.FindPropertyRelative("CustomKeywords");
            if (customKeywordsProp != null)
            {
                height += SherpaAsrEditorControls.CustomKeywordsHeight(customKeywordsProp, width) + spacing;
            }

            height += width >= MinimumTwoColumnWidth
                ? line + spacing
                : (SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width) * 2f + spacing * 2f);

            height += line + spacing; // Continuous conversation toggle
            var continuousProp = property.FindPropertyRelative("ContinuousConversation");
            if (continuousProp != null && continuousProp.boolValue)
            {
                height += SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width) + spacing; // Timeout field
            }

            height += line + spacing; // Trigger sound toggle
            var triggerProp = property.FindPropertyRelative("UseTriggerSound");
            if (triggerProp != null && triggerProp.boolValue)
            {
                height += SherpaAsrEditorControls.ResponsivePropertyFieldHeight(width) + spacing; // Trigger clip field
            }

            height -= spacing; // Remove trailing spacing
            return Mathf.Max(height, AsrInspectorStyles.ContentPadding * 2f + line);
        }

        private static class Styles
        {
            public static GUIContent EnabledBadgeContent => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordEnabledBadge);
            public static GUIContent DisabledBadgeContent => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordDisabledBadge);
            public static GUIContent ModelIdLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordModelIdLabel);
            public static GUIContent CustomKeywordsLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.CustomKeywordsLabel);
            public static GUIContent ScoreLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordScoreLabel);
            public static GUIContent ThresholdLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.KeywordThresholdLabel);
            public static GUIContent ContinuousLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.ContinuousConversationLabel);
            public static GUIContent TimeoutLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.ConversationTimeoutLabel);
            public static GUIContent TriggerToggleLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.TriggerSoundLabel);
            public static GUIContent TriggerClipLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.TriggerClipLabel);
        }
    }
}
#endif
