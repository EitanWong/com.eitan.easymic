#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(EasyMicrophone.MicrophoneOptions))]
    internal sealed class MicrophoneOptionsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return line * 3f + spacing * 2f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var headerRect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.LabelField(headerRect, label, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            var toggleRect = new Rect(position.x, headerRect.yMax + spacing, position.width, line);
            var recordOnAwake = property.FindPropertyRelative("recordOnAwake");
            recordOnAwake.boolValue = EditorGUI.Toggle(toggleRect, new GUIContent("Record On Awake", "Automatically begin recording when the component awakens."), recordOnAwake.boolValue);

            toggleRect.y += line + spacing;
            var autoFallback = property.FindPropertyRelative("autoFallback");
            autoFallback.boolValue = EditorGUI.Toggle(toggleRect, new GUIContent("Auto Fallback", "Switch to another available device if the preferred capture device is unavailable."), autoFallback.boolValue);
            EditorGUI.indentLevel--;

            EditorGUI.EndProperty();
        }
    }
}
#endif
