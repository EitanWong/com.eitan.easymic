#if UNITY_EDITOR && EASYMIC_SHERPA_ONNX_INTEGRATION
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Runtime.Mono.ASR;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(TurnDetectionOptions))]
    internal sealed class TurnDetectionOptionsDrawer : PropertyDrawer
    {
        private const float BadgeWidth = 150f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var minProp = property.FindPropertyRelative("MinDelaySeconds");
            var maxProp = property.FindPropertyRelative("MaxDelaySeconds");

            float minValue = Mathf.Max(0f, minProp != null ? minProp.floatValue : 0f);
            float maxValue = Mathf.Max(minValue, maxProp != null ? maxProp.floatValue : 0f);

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);

            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                headerRect.width - BadgeWidth - 12f,
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true, AsrInspectorStyles.Foldout);

            Rect badgeRect = new Rect(
                headerRect.xMax - BadgeWidth + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - AsrInspectorStyles.BadgeHeight) * 0.5f,
                BadgeWidth - 12f,
                AsrInspectorStyles.BadgeHeight);

            string badgeText = string.Format(CultureInfo.InvariantCulture, "{0:0.0}s – {1:0.0}s", minValue, maxValue);
            AsrInspectorStyles.DrawBadge(badgeRect, new GUIContent(badgeText), AsrInspectorStyles.ModeBadgeColor);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float contentHeight = GetContentHeight();
            Rect contentRect = new Rect(position.x, headerRect.yMax, position.width, contentHeight);
            AsrInspectorStyles.DrawContentBackground(contentRect);

            float y = contentRect.y + AsrInspectorStyles.ContentPadding;
            float x = contentRect.x + AsrInspectorStyles.ContentPadding;
            float width = contentRect.width - AsrInspectorStyles.ContentPadding * 2f;
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;

            Rect rowRect = new Rect(x, y, width, line);
            float halfWidth = (rowRect.width - spacing) * 0.5f;
            var minRect = new Rect(rowRect.x, rowRect.y, halfWidth, line);
            var maxRect = new Rect(rowRect.x + halfWidth + spacing, rowRect.y, halfWidth, line);

            if (minProp != null)
            {
                EditorGUI.PropertyField(minRect, minProp, Styles.MinLabel);
                minProp.floatValue = Mathf.Max(0f, minProp.floatValue);
                minValue = minProp.floatValue;
            }

            if (maxProp != null)
            {
                EditorGUI.PropertyField(maxRect, maxProp, Styles.MaxLabel);
                maxProp.floatValue = Mathf.Max(minValue, maxProp.floatValue);
                maxValue = maxProp.floatValue;
            }

            y += line + spacing;

            Rect infoRect = new Rect(x, y, width, line);
            EditorGUI.LabelField(infoRect, Styles.InfoContent, AsrInspectorStyles.SectionLabel);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = AsrInspectorStyles.HeaderHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += GetContentHeight();
            return height;
        }

        private static float GetContentHeight()
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float height = AsrInspectorStyles.ContentPadding * 2f;

            height += line + spacing; // Fields row
            height += line; // Info label

            return height;
        }

        private static class Styles
        {
            public static readonly GUIContent MinLabel = new GUIContent("最短静音 / Min Silence", "识别结束前需要等待的最短静音时长 / Minimum silence before a turn can close.");
            public static readonly GUIContent MaxLabel = new GUIContent("最长静音 / Max Silence", "超过该静音时长将强制结束当前说话轮次 / Maximum silence before forcing a turn completion.");
            public static readonly GUIContent InfoContent = new GUIContent("保持合理的静音时长有助于更准确地切分语音段落 / Balanced silence windows help segment speech accurately.");
        }
    }
}
#endif
