#if UNITY_EDITOR && EASYMIC_SHERPA_ONNX_INTEGRATION
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    /// <summary>
    /// Shared styling helpers for Sherpa ASR property drawers. Provide a consistent, modern look
    /// across keyword, preset, and turn detection inspectors.
    /// </summary>
    internal static class AsrInspectorStyles
    {
        public const float HeaderHeight = 24f;
        public const float ContentPadding = 10f;
        public const float ContentSpacing = 4f;
        public const float SectionSpacing = 8f;
        public const float BadgeHeight = 18f;

        public static readonly GUIStyle Foldout;
        public static readonly GUIStyle SectionLabel;
        public static readonly GUIStyle BadgeLabel;

        public static readonly Color HeaderBackground;
        public static readonly Color ContentBackground;
        public static readonly Color BorderColor;
        public static readonly Color EnabledBadgeColor;
        public static readonly Color DisabledBadgeColor;
        public static readonly Color ModeBadgeColor;

        static AsrInspectorStyles()
        {
            bool pro = EditorGUIUtility.isProSkin;

            HeaderBackground = pro ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.86f, 0.86f, 0.86f, 1f);
            ContentBackground = pro ? new Color(0.13f, 0.13f, 0.13f, 1f) : new Color(0.96f, 0.96f, 0.96f, 1f);
            BorderColor = pro ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.14f);
            EnabledBadgeColor = pro ? new Color(0.21f, 0.58f, 0.33f, 1f) : new Color(0.16f, 0.53f, 0.3f, 1f);
            DisabledBadgeColor = pro ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.65f, 0.65f, 0.65f, 1f);
            ModeBadgeColor = pro ? new Color(0.27f, 0.42f, 0.78f, 1f) : new Color(0.26f, 0.46f, 0.82f, 1f);

            Foldout = new GUIStyle("Foldout")
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 0, 0, 0)
            };

            SectionLabel = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal =
                {
                    textColor = pro ? new Color(0.78f, 0.78f, 0.78f, 1f) : new Color(0.28f, 0.28f, 0.28f, 1f)
                }
            };

            BadgeLabel = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                padding = new RectOffset(8, 8, 2, 2),
                normal = { textColor = Color.white }
            };
        }

        public static void DrawHeaderBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, HeaderBackground);
            DrawHorizontalBorder(rect.x, rect.yMax - 1f, rect.width);
        }

        public static void DrawContentBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, ContentBackground);
            DrawBorders(rect);
        }

        public static void DrawBadge(Rect rect, GUIContent content, Color background)
        {
            EditorGUI.DrawRect(rect, background);
            EditorGUI.LabelField(rect, content, BadgeLabel);
        }

        private static void DrawBorders(Rect rect)
        {
            DrawHorizontalBorder(rect.x, rect.y, rect.width);
            DrawHorizontalBorder(rect.x, rect.yMax - 1f, rect.width);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), BorderColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), BorderColor);
        }

        private static void DrawHorizontalBorder(float x, float y, float width)
        {
            EditorGUI.DrawRect(new Rect(x, y, width, 1f), BorderColor);
        }
    }
}
#endif
