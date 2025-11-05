#if EASYMIC_APM_INTEGRATION
using Eitan.EasyMic.Runtime.Mono;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Inspectors
{
    [CustomPropertyDrawer(typeof(EasyMicrophone.AudioProcessingOptions))]
    internal sealed class AudioProcessingOptionsDrawer : PropertyDrawer
    {
        private static float ToggleHeight => EditorGUIUtility.singleLineHeight + 8f;

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

            position.height = ToggleHeight;
            var fieldRect = EditorGUI.PrefixLabel(position, label, Styles.TitleLabel);

            int previousIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float spacing = 10f;
            float width = (fieldRect.width - spacing * (ToggleMap.Length - 1)) / ToggleMap.Length;
            Rect toggleRect = new Rect(fieldRect.x, fieldRect.y, width, ToggleHeight);

            for (int i = 0; i < ToggleMap.Length; i++)
            {
                if (i > 0)
                {
                    toggleRect.x += width + spacing;
                }

                DrawToggle(toggleRect, property.FindPropertyRelative(ToggleMap[i].property), ToggleMap[i].content);
            }

            EditorGUI.indentLevel = previousIndent;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return ToggleHeight;
        }

        private static void DrawToggle(Rect rect, SerializedProperty flagProperty, GUIContent content)
        {
            if (flagProperty == null)
            {
                return;
            }

            bool current = flagProperty.boolValue;
            bool hovered = rect.Contains(Event.current.mousePosition);

            Color background = current
                ? (hovered ? Styles.ActiveBackgroundHover : Styles.ActiveBackground)
                : (hovered ? Styles.InactiveBackgroundHover : Styles.InactiveBackground);
            Color border = current ? Styles.ActiveBorder : Styles.InactiveBorder;

            EditorGUI.DrawRect(rect, background);
            Styles.DrawBorder(rect, border);

            Rect titleRect = new Rect(rect.x + 12f, rect.y + 4f, rect.width - 24f, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(titleRect, content.text, current ? Styles.ToggleTitleActive : Styles.ToggleTitleInactive);

            Rect stateRect = new Rect(titleRect.x + titleRect.width * .5f, titleRect.y, titleRect.width * .5f, EditorGUIUtility.singleLineHeight - 2f);
            EditorGUI.LabelField(stateRect,
                current ? Styles.StateOnContent : Styles.StateOffContent,
                current ? Styles.StateOnLabel : Styles.StateOffLabel);

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (GUI.Button(rect, Styles.TooltipContent(content.tooltip), GUIStyle.none))
            {
                flagProperty.boolValue = !current;
            }
        }

        private static class Styles
        {
            public static readonly GUIStyle TitleLabel;
            public static readonly GUIStyle ToggleTitleActive;
            public static readonly GUIStyle ToggleTitleInactive;
            public static readonly GUIStyle StateOnLabel;
            public static readonly GUIStyle StateOffLabel;

            public static readonly GUIContent StateOnContent = new GUIContent("STATE · ON");
            public static readonly GUIContent StateOffContent = new GUIContent("STATE · OFF");

            public static readonly Color ActiveBackground;
            public static readonly Color ActiveBackgroundHover;
            public static readonly Color ActiveBorder;
            public static readonly Color InactiveBackground;
            public static readonly Color InactiveBackgroundHover;
            public static readonly Color InactiveBorder;

            private static readonly GUIContent TooltipProxy = new GUIContent(string.Empty);

            static Styles()
            {
                bool proSkin = EditorGUIUtility.isProSkin;

                ActiveBackground = proSkin
                    ? new Color(0.20f, 0.40f, 0.82f, 1f)
                    : new Color(0.18f, 0.48f, 0.86f, 1f);
                ActiveBackgroundHover = proSkin
                    ? new Color(0.24f, 0.48f, 0.90f, 1f)
                    : new Color(0.24f, 0.56f, 0.94f, 1f);
                ActiveBorder = proSkin
                    ? new Color(0.36f, 0.62f, 1f, 1f)
                    : new Color(0.28f, 0.54f, 0.95f, 1f);

                InactiveBackground = proSkin
                    ? new Color(0.16f, 0.16f, 0.16f, 1f)
                    : new Color(0.88f, 0.88f, 0.88f, 1f);
                InactiveBackgroundHover = proSkin
                    ? new Color(0.21f, 0.21f, 0.21f, 1f)
                    : new Color(0.93f, 0.93f, 0.93f, 1f);
                InactiveBorder = proSkin
                    ? new Color(0.32f, 0.32f, 0.32f, 1f)
                    : new Color(0.66f, 0.66f, 0.66f, 1f);

                TitleLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12
                };

                ToggleTitleActive = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12,
                    normal = { textColor = Color.white }
                };

                ToggleTitleInactive = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12,
                    normal = { textColor = proSkin ? new Color(0.75f, 0.75f, 0.75f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f) }
                };

                StateOnLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 10,
                    normal = { textColor = new Color(0.88f, 1f, 0.93f, 1f) }
                };

                StateOffLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 10,
                    normal = { textColor = proSkin ? new Color(0.65f, 0.65f, 0.65f, 1f) : new Color(0.38f, 0.38f, 0.38f, 1f) }
                };
            }

            public static GUIContent TooltipContent(string tooltip)
            {
                TooltipProxy.text = string.Empty;
                TooltipProxy.tooltip = tooltip;
                return TooltipProxy;
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
