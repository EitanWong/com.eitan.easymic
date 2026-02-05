#if UNITY_EDITOR && EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Runtime.Mono.ASR;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(KeywordOptions))]
    internal sealed class KeywordOptionsDrawer : PropertyDrawer
    {
        private const float BadgeWidth = 112f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var enabledProp = property.FindPropertyRelative("Enabled");
            bool isEnabled = enabledProp?.boolValue ?? false;

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);

            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                headerRect.width - BadgeWidth - 12f,
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true, AsrInspectorStyles.Foldout);

            Rect badgeRect = new Rect(
                headerRect.xMax - BadgeWidth + 4f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - AsrInspectorStyles.BadgeHeight) * 0.5f,
                BadgeWidth - 10f,
                AsrInspectorStyles.BadgeHeight);

            if (enabledProp != null)
            {
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

            float contentHeight = GetContentHeight(property);
            Rect contentRect = new Rect(position.x, headerRect.yMax, position.width, contentHeight);
            AsrInspectorStyles.DrawContentBackground(contentRect);

            float y = contentRect.y + AsrInspectorStyles.ContentPadding;
            float x = contentRect.x + AsrInspectorStyles.ContentPadding;
            float width = contentRect.width - AsrInspectorStyles.ContentPadding * 2f;
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;

            Rect rowRect = new Rect(x, y, width, line);
            var modelProp = property.FindPropertyRelative("ModelId");
            EditorGUI.PropertyField(rowRect, modelProp, Styles.ModelIdLabel);
            y += line + spacing;

            var customKeywordsProp = property.FindPropertyRelative("CustomKeywords");
            if (customKeywordsProp != null)
            {
                float keywordsHeight = EditorGUI.GetPropertyHeight(customKeywordsProp, Styles.CustomKeywordsLabel, true);
                rowRect = new Rect(x, y, width, keywordsHeight);
                EditorGUI.PropertyField(rowRect, customKeywordsProp, Styles.CustomKeywordsLabel, true);
                y += keywordsHeight + spacing;
            }

            var scoreProp = property.FindPropertyRelative("KeywordsScore");
            var thresholdProp = property.FindPropertyRelative("KeywordsThreshold");
            rowRect = new Rect(x, y, width, line);
            float halfWidth = (rowRect.width - spacing) * 0.5f;
            var leftRect = new Rect(rowRect.x, rowRect.y, halfWidth, line);
            var rightRect = new Rect(rowRect.x + halfWidth + spacing, rowRect.y, halfWidth, line);
            EditorGUI.PropertyField(leftRect, scoreProp, Styles.ScoreLabel);
            EditorGUI.PropertyField(rightRect, thresholdProp, Styles.ThresholdLabel);
            y += line + spacing;

            var continuousProp = property.FindPropertyRelative("ContinuousConversation");
            if (continuousProp != null)
            {
                rowRect = new Rect(x, y, width, line);
                EditorGUI.PropertyField(rowRect, continuousProp, Styles.ContinuousLabel);
                y += line + spacing;

                if (continuousProp.boolValue)
                {
                    var durationProp = property.FindPropertyRelative("ContinuousConversationTimeoutSeconds");
                    rowRect = new Rect(x, y, width, line);
                    EditorGUI.PropertyField(rowRect, durationProp, Styles.TimeoutLabel);
                    y += line + spacing;
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
                    rowRect = new Rect(x, y, width, line);
                    EditorGUI.PropertyField(rowRect, clipProp, Styles.TriggerClipLabel);
                    y += line + spacing;
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

            height += GetContentHeight(property);
            return height;
        }

        private static float GetContentHeight(SerializedProperty property)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float height = AsrInspectorStyles.ContentPadding * 2f;

            height += line + spacing; // Model ID

            var customKeywordsProp = property.FindPropertyRelative("CustomKeywords");
            if (customKeywordsProp != null)
            {
                height += EditorGUI.GetPropertyHeight(customKeywordsProp, Styles.CustomKeywordsLabel, true) + spacing;
            }

            height += line + spacing; // Score / Threshold row

            height += line + spacing; // Continuous conversation toggle
            var continuousProp = property.FindPropertyRelative("ContinuousConversation");
            if (continuousProp != null && continuousProp.boolValue)
            {
                height += line + spacing; // Timeout field
            }

            height += line + spacing; // Trigger sound toggle
            var triggerProp = property.FindPropertyRelative("UseTriggerSound");
            if (triggerProp != null && triggerProp.boolValue)
            {
                height += line + spacing; // Trigger clip field
            }

            height -= spacing; // Remove trailing spacing
            return Mathf.Max(height, AsrInspectorStyles.ContentPadding * 2f + line);
        }

        private static class Styles
        {
            public static readonly GUIContent EnabledBadgeContent = new GUIContent("启用 Enabled");
            public static readonly GUIContent DisabledBadgeContent = new GUIContent("禁用 Disabled");
            public static readonly GUIContent ModelIdLabel = new GUIContent("模型 ID / Model ID", "Sherpa 模型标识符，用于决定加载的关键词模型 / Identifier of the Sherpa keyword model to load.");
            public static readonly GUIContent CustomKeywordsLabel = new GUIContent("自定义关键词 / Custom Keywords", "在这里配置额外的关键词及加权参数 / Configure additional keywords with their boost and trigger thresholds.");
            public static readonly GUIContent ScoreLabel = new GUIContent("加权得分 / Score Boost", "提高关键词得分可以让命中更容易 / Boost applied to keyword matches to make activation easier.");
            public static readonly GUIContent ThresholdLabel = new GUIContent("触发阈值 / Trigger Threshold", "识别分数超过该阈值才会触发 / Minimum score required for a keyword to fire.");
            public static readonly GUIContent ContinuousLabel = new GUIContent("连续对话 / Keep Conversation", "启用后在关键词触发后维持会话可持续识别 / Keeps the gate open after activation to continue recognition.");
            public static readonly GUIContent TimeoutLabel = new GUIContent("会话超时(秒) / Timeout (s)", "若在该时间内没有声音则自动关闭连续对话 / Closes the conversation if no activity is detected within this timeout.");
            public static readonly GUIContent TriggerToggleLabel = new GUIContent("触发音效 / Trigger Sound", "在关键词触发时播放提示音效 / Play an audio cue when a keyword is detected.");
            public static readonly GUIContent TriggerClipLabel = new GUIContent("音效剪辑 / Audio Clip", "要播放的音效剪辑 / Audio clip that will be played upon activation.");
        }
    }
}
#endif
