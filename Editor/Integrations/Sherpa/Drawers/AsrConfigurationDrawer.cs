#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using Eitan.EasyMic.Editor;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomPropertyDrawer(typeof(AutomaticSpeechRecognitionConfiguration))]
    internal sealed class AsrConfigurationDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 26f;
        private const float ButtonSpacing = 4f;
        private const float MinimumInlinePresetToolbarWidth = 260f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var presetsProp = property.FindPropertyRelative("_presets");
            var activePresetIdProp = property.FindPropertyRelative("_activePresetId");
            EnsurePresetArray(presetsProp, activePresetIdProp);

            Rect headerRect = new Rect(position.x, position.y, position.width, AsrInspectorStyles.HeaderHeight);
            AsrInspectorStyles.DrawHeaderBackground(headerRect);
            property.isExpanded = DrawHeader(headerRect, property, presetsProp, activePresetIdProp);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float contentHeight = GetContentHeight(property, position.width - AsrInspectorStyles.ContentPadding * 2f);
            Rect contentRect = new Rect(position.x, headerRect.yMax, position.width, contentHeight);
            AsrInspectorStyles.DrawContentBackground(contentRect);

            float x = contentRect.x + AsrInspectorStyles.ContentPadding;
            float y = contentRect.y + AsrInspectorStyles.ContentPadding;
            float width = contentRect.width - AsrInspectorStyles.ContentPadding * 2f;
            SherpaAsrEditorControls.RememberContentWidth(property, width);
            float sectionSpacing = AsrInspectorStyles.SectionSpacing;

            float activeRowHeight = ActivePresetRowHeight(width);
            Rect activeRowRect = new Rect(x, y, width, activeRowHeight);
            int activeIndex = DrawActivePresetRow(activeRowRect, presetsProp, activePresetIdProp);
            y += activeRowHeight + sectionSpacing;

            if (presetsProp != null && presetsProp.arraySize > 0)
            {
                activeIndex = Mathf.Clamp(activeIndex, 0, presetsProp.arraySize - 1);
                SerializedProperty presetProp = presetsProp.GetArrayElementAtIndex(activeIndex);
                SherpaAsrEditorControls.RememberContentWidth(presetProp, GetNestedContentWidth(width));
                float presetHeight = EditorGUI.GetPropertyHeight(presetProp, GUIContent.none, true);
                Rect presetRect = new Rect(x, y, width, presetHeight);
                EditorGUI.PropertyField(presetRect, presetProp, GUIContent.none, true);
                SetActivePresetId(activePresetIdProp, presetProp);
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var presetsProp = property.FindPropertyRelative("_presets");
            var activePresetIdProp = property.FindPropertyRelative("_activePresetId");
            EnsurePresetArray(presetsProp, activePresetIdProp);

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
            var presetsProp = property.FindPropertyRelative("_presets");
            var activePresetIdProp = property.FindPropertyRelative("_activePresetId");
            float sectionSpacing = AsrInspectorStyles.SectionSpacing;
            float height = AsrInspectorStyles.ContentPadding * 2f;
            height += ActivePresetRowHeight(width) + sectionSpacing; // active preset row

            if (presetsProp != null && presetsProp.arraySize > 0)
            {
                int activeIndex = FindActivePresetIndex(presetsProp, activePresetIdProp);
                activeIndex = Mathf.Clamp(activeIndex, 0, presetsProp.arraySize - 1);
                SerializedProperty presetProp = presetsProp.GetArrayElementAtIndex(activeIndex);
                SherpaAsrEditorControls.RememberContentWidth(presetProp, GetNestedContentWidth(width));
                height += EditorGUI.GetPropertyHeight(presetProp, GUIContent.none, true);
            }

            return height;
        }

        private static float ActivePresetRowHeight(float width)
        {
            return width >= MinimumInlinePresetToolbarWidth
                ? EditorGUIUtility.singleLineHeight
                : EditorGUIUtility.singleLineHeight * 2f + ButtonSpacing;
        }

        private static float GetNestedContentWidth(float childPositionWidth)
        {
            return Mathf.Max(0f, childPositionWidth - AsrInspectorStyles.ContentPadding * 2f);
        }

        private static bool DrawHeader(Rect headerRect, SerializedProperty property, SerializedProperty presetsProp, SerializedProperty activePresetIdProp)
        {
            Rect foldoutRect = new Rect(
                headerRect.x + 6f,
                headerRect.y + (AsrInspectorStyles.HeaderHeight - EditorGUIUtility.singleLineHeight) * 0.5f,
                Mathf.Max(40f, headerRect.width - 12f),
                EditorGUIUtility.singleLineHeight);

            return EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceAsrHeader),
                true,
                AsrInspectorStyles.Foldout);
        }

        private static int DrawActivePresetRow(Rect rect, SerializedProperty presetsProp, SerializedProperty activePresetIdProp)
        {
            int activeIndex = FindActivePresetIndex(presetsProp, activePresetIdProp);
            int presetCount = presetsProp?.arraySize ?? 0;

            float buttonsWidth = ButtonWidth * 3f + ButtonSpacing * 3f;
            bool inline = rect.width >= MinimumInlinePresetToolbarWidth;
            Rect popupRect;
            Rect addRect;
            Rect duplicateRect;
            Rect removeRect;
            if (inline)
            {
                popupRect = new Rect(rect.x, rect.y, Mathf.Max(80f, rect.width - buttonsWidth), EditorGUIUtility.singleLineHeight);
                addRect = new Rect(popupRect.xMax + ButtonSpacing, rect.y, ButtonWidth, EditorGUIUtility.singleLineHeight);
                duplicateRect = new Rect(addRect.xMax + ButtonSpacing, rect.y, ButtonWidth, EditorGUIUtility.singleLineHeight);
                removeRect = new Rect(duplicateRect.xMax + ButtonSpacing, rect.y, ButtonWidth, EditorGUIUtility.singleLineHeight);
            }
            else
            {
                popupRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
                float buttonsY = popupRect.yMax + ButtonSpacing;
                removeRect = new Rect(rect.xMax - ButtonWidth, buttonsY, ButtonWidth, EditorGUIUtility.singleLineHeight);
                duplicateRect = new Rect(removeRect.x - ButtonSpacing - ButtonWidth, buttonsY, ButtonWidth, EditorGUIUtility.singleLineHeight);
                addRect = new Rect(duplicateRect.x - ButtonSpacing - ButtonWidth, buttonsY, ButtonWidth, EditorGUIUtility.singleLineHeight);
            }

            string[] labels = BuildPresetLabels(presetsProp);
            int nextIndex = EditorGUI.Popup(popupRect, Styles.ActivePresetLabel.text, Mathf.Clamp(activeIndex, 0, Mathf.Max(0, labels.Length - 1)), labels);
            if (nextIndex >= 0 && nextIndex < presetCount)
            {
                SetActivePresetId(activePresetIdProp, presetsProp.GetArrayElementAtIndex(nextIndex));
                activeIndex = nextIndex;
            }

            if (GUI.Button(addRect, Styles.AddPresetContent, EditorStyles.miniButtonLeft))
            {
                AddPreset(presetsProp, activePresetIdProp);
                activeIndex = presetsProp.arraySize - 1;
            }

            using (new EditorGUI.DisabledScope(presetCount == 0))
            {
                if (GUI.Button(duplicateRect, Styles.DuplicatePresetContent, EditorStyles.miniButtonMid))
                {
                    DuplicatePreset(presetsProp, activePresetIdProp, activeIndex);
                    activeIndex = presetsProp.arraySize - 1;
                }

                using (new EditorGUI.DisabledScope(presetCount <= 1))
                {
                    if (GUI.Button(removeRect, Styles.RemovePresetContent, EditorStyles.miniButtonRight))
                    {
                        RemovePreset(presetsProp, activePresetIdProp, activeIndex);
                        activeIndex = FindActivePresetIndex(presetsProp, activePresetIdProp);
                    }
                }
            }

            return activeIndex;
        }

        private static void EnsurePresetArray(SerializedProperty presetsProp, SerializedProperty activePresetIdProp)
        {
            if (presetsProp == null || !presetsProp.isArray)
            {
                return;
            }

            if (presetsProp.arraySize == 0)
            {
                presetsProp.arraySize = 1;
                WriteDefaultPreset(presetsProp.GetArrayElementAtIndex(0), AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId, "Default");
            }

            if (activePresetIdProp != null && string.IsNullOrWhiteSpace(activePresetIdProp.stringValue))
            {
                SetActivePresetId(activePresetIdProp, presetsProp.GetArrayElementAtIndex(0));
            }
        }

        private static int FindActivePresetIndex(SerializedProperty presetsProp, SerializedProperty activePresetIdProp)
        {
            if (presetsProp == null || presetsProp.arraySize == 0)
            {
                return 0;
            }

            string activeId = activePresetIdProp?.stringValue;
            for (int i = 0; i < presetsProp.arraySize; i++)
            {
                string id = GetPresetId(presetsProp.GetArrayElementAtIndex(i));
                if (!string.IsNullOrWhiteSpace(activeId) && string.Equals(id, activeId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            SetActivePresetId(activePresetIdProp, presetsProp.GetArrayElementAtIndex(0));
            return 0;
        }

        private static string[] BuildPresetLabels(SerializedProperty presetsProp)
        {
            int count = presetsProp?.arraySize ?? 0;
            if (count == 0)
            {
                return new[] { "-" };
            }

            var labels = new string[count];
            for (int i = 0; i < count; i++)
            {
                SerializedProperty preset = presetsProp.GetArrayElementAtIndex(i);
                string displayName = preset.FindPropertyRelative("DisplayName")?.stringValue;
                string id = GetPresetId(preset);
                labels[i] = string.IsNullOrWhiteSpace(displayName)
                    ? (string.IsNullOrWhiteSpace(id) ? EasyMicEditorLocalization.SherpaAsrText(SherpaAsrEditorTextKey.PresetFallbackLabel) : id)
                    : displayName;
            }

            return labels;
        }

        private static void AddPreset(SerializedProperty presetsProp, SerializedProperty activePresetIdProp)
        {
            int index = presetsProp.arraySize;
            presetsProp.arraySize++;
            string id = CreateUniquePresetId(presetsProp, "preset");
            WriteDefaultPreset(presetsProp.GetArrayElementAtIndex(index), id, id);
            SetActivePresetId(activePresetIdProp, presetsProp.GetArrayElementAtIndex(index));
        }

        private static void DuplicatePreset(SerializedProperty presetsProp, SerializedProperty activePresetIdProp, int activeIndex)
        {
            if (presetsProp == null || presetsProp.arraySize == 0)
            {
                return;
            }

            activeIndex = Mathf.Clamp(activeIndex, 0, presetsProp.arraySize - 1);
            int newIndex = presetsProp.arraySize;
            presetsProp.InsertArrayElementAtIndex(activeIndex);
            presetsProp.MoveArrayElement(activeIndex + 1, newIndex);
            SerializedProperty duplicate = presetsProp.GetArrayElementAtIndex(newIndex);
            string id = CreateUniquePresetId(presetsProp, GetPresetId(duplicate));
            duplicate.FindPropertyRelative("Id").stringValue = id;
            duplicate.FindPropertyRelative("DisplayName").stringValue = id;
            SetActivePresetId(activePresetIdProp, duplicate);
        }

        private static void RemovePreset(SerializedProperty presetsProp, SerializedProperty activePresetIdProp, int activeIndex)
        {
            if (presetsProp == null || presetsProp.arraySize <= 1)
            {
                return;
            }

            activeIndex = Mathf.Clamp(activeIndex, 0, presetsProp.arraySize - 1);
            presetsProp.DeleteArrayElementAtIndex(activeIndex);
            int nextIndex = Mathf.Clamp(activeIndex, 0, presetsProp.arraySize - 1);
            SetActivePresetId(activePresetIdProp, presetsProp.GetArrayElementAtIndex(nextIndex));
        }

        private static void WriteDefaultPreset(SerializedProperty presetProp, string id, string displayName)
        {
            presetProp.FindPropertyRelative("Id").stringValue = id;
            presetProp.FindPropertyRelative("DisplayName").stringValue = displayName;
            presetProp.FindPropertyRelative("RecognitionMode").enumValueIndex = (int)RecognitionMode.Streaming;
            presetProp.FindPropertyRelative("StreamingModelId").stringValue = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
            presetProp.FindPropertyRelative("OfflineModelId").stringValue = "sherpa-onnx-zipformer-zh-en-2023-11-22";
            presetProp.FindPropertyRelative("VadModelId").stringValue = "silero-vad-v5";
            presetProp.FindPropertyRelative("EnablePunctuation").boolValue = true;
            presetProp.FindPropertyRelative("PunctuationModelId").stringValue = "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8";

            SerializedProperty keywordOptions = presetProp.FindPropertyRelative("KeywordOptions");
            if (keywordOptions != null)
            {
                keywordOptions.FindPropertyRelative("Enabled").boolValue = false;
                keywordOptions.FindPropertyRelative("ModelId").stringValue = string.Empty;
            }

            SerializedProperty turnOptions = presetProp.FindPropertyRelative("TurnDetectionOptions");
            if (turnOptions != null)
            {
                turnOptions.FindPropertyRelative("MinDelaySeconds").floatValue = 0.3f;
                turnOptions.FindPropertyRelative("MaxDelaySeconds").floatValue = 1.2f;
            }
        }

        private static string CreateUniquePresetId(SerializedProperty presetsProp, string baseId)
        {
            baseId = string.IsNullOrWhiteSpace(baseId) ? "preset" : baseId.Trim();
            string candidate = baseId;
            int suffix = 2;
            while (PresetIdExists(presetsProp, candidate))
            {
                candidate = baseId + "-" + suffix.ToString();
                suffix++;
            }

            return candidate;
        }

        private static bool PresetIdExists(SerializedProperty presetsProp, string id)
        {
            for (int i = 0; i < presetsProp.arraySize; i++)
            {
                if (string.Equals(GetPresetId(presetsProp.GetArrayElementAtIndex(i)), id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetPresetId(SerializedProperty presetProp)
        {
            return presetProp?.FindPropertyRelative("Id")?.stringValue ?? string.Empty;
        }

        private static void SetActivePresetId(SerializedProperty activePresetIdProp, SerializedProperty presetProp)
        {
            if (activePresetIdProp == null || presetProp == null)
            {
                return;
            }

            string id = GetPresetId(presetProp);
            activePresetIdProp.stringValue = string.IsNullOrWhiteSpace(id)
                ? AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId
                : id;
        }

        private static class Styles
        {
            public static GUIContent ActivePresetLabel => EasyMicEditorLocalization.SherpaAsrContent(SherpaAsrEditorTextKey.ActivePresetLabel);
            public static GUIContent AddPresetContent => CreateIconContent("Toolbar Plus", "+", SherpaAsrEditorTextKey.AddPresetLabel);
            public static GUIContent DuplicatePresetContent => CreateIconContent("TreeEditor.Duplicate", "D", SherpaAsrEditorTextKey.DuplicatePresetLabel);
            public static GUIContent RemovePresetContent => CreateIconContent("Toolbar Minus", "-", SherpaAsrEditorTextKey.RemovePresetLabel);

            private static GUIContent CreateIconContent(string iconName, string fallbackText, SherpaAsrEditorTextKey tooltipKey)
            {
                GUIContent content = EditorGUIUtility.IconContent(iconName);
                string tooltip = EasyMicEditorLocalization.SherpaAsrText(tooltipKey);
                if (content == null || content.image == null)
                {
                    return new GUIContent(fallbackText, tooltip);
                }

                return new GUIContent(content.image, tooltip);
            }
        }
    }
}
#endif
