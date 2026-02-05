#if EASYMIC_APM_INTEGRATION
using Eitan.EasyMic.Runtime.Mono;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Inspectors
{
    [CustomPropertyDrawer(typeof(AudioProcessingOptions))]
    internal sealed class AudioProcessingOptionsDrawer : PropertyDrawer
    {
        private static readonly (string property, GUIContent content)[] ToggleMap =
        {
            (
                "EnableAEC",
                new GUIContent(
                    "AEC",
                    "声学回声消除 · Acoustic Echo Cancellation\n移除扬声器回馈到麦克风的回声，让语音更加清晰。\nRemoves speaker feedback from the microphone feed for clearer speech.")),
            (
                "EnableANS",
                new GUIContent(
                    "ANS",
                    "自适应噪声抑制 · Adaptive Noise Suppression\n动态削减持续的背景噪声，保持语音干净专注。\nDynamically attenuates persistent background noise to keep the voice track clean.")),
            (
                "EnableAGC",
                new GUIContent(
                    "AGC",
                    "自动增益控制 · Automatic Gain Control\n自动调节输入音量，避免过大或过小的波动。\nAutomatically evens out input gain so recordings stay consistently audible.")),
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float headerHeight = EditorGUIUtility.singleLineHeight;
            var headerRect = new Rect(position.x, position.y, position.width, headerHeight);
            EditorGUI.LabelField(headerRect, label, Styles.TitleLabel);

            Rect frameRect = new Rect(
                position.x,
                headerRect.yMax + Styles.FrameSpacing,
                position.width,
                Styles.FrameHeight);

            Styles.DrawFrame(frameRect);

            Rect buttonsRect = new Rect(
                frameRect.x + Styles.FramePadding,
                frameRect.y + Styles.FramePadding,
                frameRect.width - Styles.FramePadding * 2f,
                Styles.ButtonHeight);

            float totalSpacing = Styles.ButtonSpacing * (ToggleMap.Length - 1);
            float buttonWidth = Mathf.Max(Styles.MinButtonWidth, (buttonsRect.width - totalSpacing) / ToggleMap.Length);
            Rect buttonRect = new Rect(buttonsRect.x, buttonsRect.y, buttonWidth, Styles.ButtonHeight);

            for (int i = 0; i < ToggleMap.Length; i++)
            {
                SerializedProperty flagProperty = property.FindPropertyRelative(ToggleMap[i].property);
                DrawToggleButton(buttonRect, flagProperty, ToggleMap[i].content);
                buttonRect.x += buttonWidth + Styles.ButtonSpacing;
            }

            EditorGUI.indentLevel = previousIndent;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float headerHeight = EditorGUIUtility.singleLineHeight;
            return headerHeight + Styles.FrameSpacing + Styles.FrameHeight;
        }

        private static void DrawToggleButton(Rect rect, SerializedProperty flagProperty, GUIContent content)
        {
            if (flagProperty == null)
            {
                return;
            }

            bool current = flagProperty.boolValue;
            bool hovered = rect.Contains(Event.current.mousePosition);

            Color background = current
                ? (hovered ? Styles.ButtonOnHover : Styles.ButtonOn)
                : (hovered ? Styles.ButtonOffHover : Styles.ButtonOff);

            EditorGUI.DrawRect(rect, background);
            Styles.DrawBorder(rect, Styles.ButtonBorder);

            Rect indicatorRect = new Rect(
                rect.x + Styles.IndicatorPadding,
                rect.y + rect.height * 0.5f - Styles.IndicatorSize * 0.5f,
                Styles.IndicatorSize,
                Styles.IndicatorSize);

            DrawIndicator(indicatorRect, current);

            Rect labelRect = new Rect(
                indicatorRect.xMax + Styles.LabelSpacing,
                rect.y,
                rect.width - (indicatorRect.width + Styles.IndicatorPadding + Styles.LabelSpacing * 2f),
                rect.height);
            GUI.Label(labelRect, new GUIContent(content.text, content.tooltip), Styles.ToggleLabel);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                flagProperty.boolValue = !current;
            }
        }

        private static void DrawIndicator(Rect rect, bool enabled)
        {
            Color fill = enabled ? Styles.IndicatorOn : Styles.IndicatorOff;
            Color border = enabled ? Styles.IndicatorOnBorder : Styles.IndicatorOffBorder;
            EditorGUI.DrawRect(rect, fill);
            Styles.DrawBorder(rect, border);
        }

        private static class Styles
        {
            public static readonly GUIStyle TitleLabel;
            public static readonly GUIStyle ToggleLabel;

            public static readonly Color FrameBackground;
            public static readonly Color FrameBorder;
            public static readonly Color ButtonOn;
            public static readonly Color ButtonOnHover;
            public static readonly Color ButtonOff;
            public static readonly Color ButtonOffHover;
            public static readonly Color ButtonBorder;
            public static readonly Color IndicatorOn;
            public static readonly Color IndicatorOff;
            public static readonly Color IndicatorOnBorder;
            public static readonly Color IndicatorOffBorder;

            public static float FramePadding => 8f;
            public static float FrameSpacing => 6f;
            public static float ButtonSpacing => 6f;
            public static float ButtonHeight => EditorGUIUtility.singleLineHeight + 6f;
            public static float FrameHeight => ButtonHeight + FramePadding * 2f;
            public static float MinButtonWidth => 64f;
            public static float IndicatorSize => 8f;
            public static float IndicatorPadding => 8f;
            public static float LabelSpacing => 6f;

            static Styles()
            {
                bool proSkin = EditorGUIUtility.isProSkin;

                FrameBackground = proSkin ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.94f, 0.94f, 0.94f, 1f);
                FrameBorder = proSkin ? new Color(0.28f, 0.28f, 0.28f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);

                ButtonOn = proSkin ? new Color(0.20f, 0.45f, 0.32f, 1f) : new Color(0.28f, 0.62f, 0.38f, 1f);
                ButtonOnHover = proSkin ? new Color(0.24f, 0.52f, 0.38f, 1f) : new Color(0.32f, 0.68f, 0.44f, 1f);
                ButtonOff = proSkin ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.89f, 0.89f, 0.89f, 1f);
                ButtonOffHover = proSkin ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.94f, 0.94f, 0.94f, 1f);
                ButtonBorder = proSkin ? new Color(0.32f, 0.32f, 0.32f, 1f) : new Color(0.76f, 0.76f, 0.76f, 1f);

                IndicatorOn = proSkin ? new Color(0.18f, 0.86f, 0.45f, 1f) : new Color(0.10f, 0.65f, 0.24f, 1f);
                IndicatorOff = proSkin ? new Color(0.35f, 0.35f, 0.35f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
                IndicatorOnBorder = proSkin ? new Color(0.24f, 0.95f, 0.55f, 1f) : new Color(0.28f, 0.82f, 0.38f, 1f);
                IndicatorOffBorder = proSkin ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.66f, 0.66f, 0.66f, 1f);

                TitleLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12
                };

                ToggleLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    normal =
                    {
                        textColor = proSkin ? new Color(0.92f, 0.92f, 0.92f, 1f) : new Color(0.14f, 0.14f, 0.14f, 1f)
                    }
                };
            }

            public static void DrawFrame(Rect rect)
            {
                EditorGUI.DrawRect(rect, FrameBackground);
                DrawBorder(rect, FrameBorder);
            }

            public static void DrawBorder(Rect rect, Color color)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
            }
        }
    }
}
#endif
