#if UNITY_EDITOR
using Eitan.EasyMic.Runtime.Mono;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(MicrophoneOptions))]
    internal sealed class MicrophoneOptionsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float height = Styles.Padding * 2f;
            height += line; // header
            height += Styles.RowSpacing;
            height += line; // Record On Awake toggle
            height += Styles.InnerSpacing;
            height += line; // Auto Fallback toggle
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect frameRect = EditorGUI.IndentedRect(position);
            Styles.DrawFrame(frameRect);

            Rect contentRect = new Rect(
                frameRect.x + Styles.Padding,
                frameRect.y + Styles.Padding,
                frameRect.width - Styles.Padding * 2f,
                frameRect.height - Styles.Padding * 2f);

            float line = EditorGUIUtility.singleLineHeight;
            float cursorY = contentRect.y;

            // Header
            Rect headerRect = new Rect(contentRect.x, cursorY, contentRect.width, line);
            EditorGUI.LabelField(headerRect, label, Styles.SectionHeader);
            cursorY = headerRect.yMax + Styles.RowSpacing;

            // Fields
            var recordOnAwakeProp = property.FindPropertyRelative("recordOnAwake");
            var autoFallbackProp = property.FindPropertyRelative("autoFallback");

            EditorGUIUtility.labelWidth = Styles.FieldLabelWidth;

            var recordOnAwakeContent = Styles.MakeLabel("Record On Awake", "EditorSettings Icon");
            recordOnAwakeContent.tooltip = "Start recording automatically on startup.";
            Rect toggleRect = new Rect(contentRect.x, cursorY, contentRect.width, line);
            EditorGUI.BeginChangeCheck();
            bool newRecordOnAwake = EditorGUI.Toggle(toggleRect,
                recordOnAwakeContent,
                recordOnAwakeProp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                recordOnAwakeProp.boolValue = newRecordOnAwake;
            }

            toggleRect.y += line + Styles.InnerSpacing;
            var autoFallbackContent = Styles.MakeLabel("Auto Fallback", "EditorSettings Icon");
            autoFallbackContent.tooltip = "If the current input device is unavailable, automatically switch to the default fallback device.";
            EditorGUI.BeginChangeCheck();
            bool newAutoFallback = EditorGUI.Toggle(toggleRect,
                autoFallbackContent,
                autoFallbackProp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                autoFallbackProp.boolValue = newAutoFallback;
            }

            EditorGUI.EndProperty();
        }


        private static class Styles
        {
            public static readonly GUIStyle SectionHeader;

            public static readonly Color FrameBackground;
            public static readonly Color FrameBorder;
            public static readonly Color SeparatorColor;


            public static float Padding => 10f;
            public static float RowSpacing => 10f;
            public static float InnerSpacing => 4f;
            public static float FieldLabelWidth => 150f;

            static Styles()
            {
                bool proSkin = EditorGUIUtility.isProSkin;

                FrameBackground = proSkin ? new Color(0.16f, 0.16f, 0.16f, 1f) : new Color(0.94f, 0.94f, 0.94f, 1f);
                FrameBorder = proSkin ? new Color(0.28f, 0.28f, 0.28f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
                SeparatorColor = proSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.08f);

                SectionHeader = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
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

            public static GUIContent MakeLabel(string text, string iconName)
            {
                var content = EditorGUIUtility.IconContent(iconName);
                if (content == null || content.image == null)
                {
                    return new GUIContent(text);

                }
                return new GUIContent($" {text}", content.image);
            }
        }
    }
}
#endif
