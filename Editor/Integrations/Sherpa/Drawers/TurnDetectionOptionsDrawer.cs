#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Editor;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomPropertyDrawer(typeof(TurnDetectionOptions))]
    internal sealed class TurnDetectionOptionsDrawer : PropertyDrawer
    {
        private const float BadgeWidth = 150f;
        private const float MinimumBadgeLayoutWidth = 300f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var minProp = property.FindPropertyRelative("MinDelaySeconds");
            var maxProp = property.FindPropertyRelative("MaxDelaySeconds");

            float minValue = Mathf.Max(0f, minProp != null ? minProp.floatValue : 0f);
            float maxValue = Mathf.Max(minValue, maxProp != null ? maxProp.floatValue : 0f);

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);

            bool drawBadge = headerRect.width >= MinimumBadgeLayoutWidth;
            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                Mathf.Max(40f, headerRect.width - (drawBadge ? BadgeWidth + 12f : 8f)),
                EditorGUIUtility.singleLineHeight);

            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true, AsrInspectorStyles.Foldout);

            if (drawBadge)
            {
                Rect badgeRect = new Rect(
                    headerRect.xMax - BadgeWidth + 6f,
                    headerRect.y + (AsrInspectorStyles.HeaderHeight - AsrInspectorStyles.BadgeHeight) * 0.5f,
                    BadgeWidth - 12f,
                    AsrInspectorStyles.BadgeHeight);

                string badgeText = string.Format(CultureInfo.InvariantCulture, "{0:0.0}s - {1:0.0}s", minValue, maxValue);
                AsrInspectorStyles.DrawBadge(badgeRect, new GUIContent(badgeText), AsrInspectorStyles.ModeBadgeColor);
            }

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float contentHeight = GetContentHeight(position.width - AsrInspectorStyles.ContentPadding * 2f);
            Rect contentRect = new Rect(position.x, headerRect.yMax, position.width, contentHeight);
            AsrInspectorStyles.DrawContentBackground(contentRect);

            float y = contentRect.y + AsrInspectorStyles.ContentPadding;
            float x = contentRect.x + AsrInspectorStyles.ContentPadding;
            float width = contentRect.width - AsrInspectorStyles.ContentPadding * 2f;
            SherpaAsrEditorControls.RememberContentWidth(property, width);
            Rect rowRect = new Rect(x, y, width, SherpaAsrEditorControls.TurnRangeHeight(width));
            SherpaAsrEditorControls.DrawTurnRange(rowRect, minProp, maxProp, Styles.MinLabel, Styles.MaxLabel);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = AsrInspectorStyles.HeaderHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += GetContentHeight(SherpaAsrEditorControls.GetRememberedContentWidth(property));
            return height;
        }

        private static float GetContentHeight(float width)
        {
            float height = AsrInspectorStyles.ContentPadding * 2f;
            height += SherpaAsrEditorControls.TurnRangeHeight(width);

            return height;
        }

        private static class Styles
        {
            public static GUIContent MinLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.MinSilenceLabel);
            public static GUIContent MaxLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.MaxSilenceLabel);
        }
    }
}
#endif
