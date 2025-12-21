using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CustomPropertyDrawer(typeof(AIChatControllerConfig))]
    internal class AIChatControllerConfigDrawer : PropertyDrawer
    {
        private const float SectionSpacing = 6f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return line;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = line + spacing;

            height += GetSectionHeight(property, Section.Components);
            height += SectionSpacing;
            height += GetSectionHeight(property, Section.Llm);
            height += SectionSpacing;
            height += GetSectionHeight(property, Section.Speech);
            height += SectionSpacing;
            height += GetSectionHeight(property, Section.Interaction);
            height += SectionSpacing;
            height += GetSectionHeight(property, Section.Runtime);

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            float y = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            EditorGUI.indentLevel++;

            y = DrawSectionLabel(position, y, "Components");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.Microphone)), "Microphone");
            y += SectionSpacing;

            y = DrawSectionLabel(position, y, "LLM Settings");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.ApiBaseUrl)), "API Base URL");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.ApiKey)), "API Key");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.LlmModel)), "Model");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.LlmTemperature)), "Temperature");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.SystemPromptProfile)), "System Prompt Profile");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.LogStreamingChunks)), "Verbose Streaming Log");
            y += SectionSpacing;

            var useLocalProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.UseLocalTts));
            bool showRemoteFields = useLocalProp == null ||
                                    useLocalProp.hasMultipleDifferentValues ||
                                    !useLocalProp.boolValue;

            y = DrawSectionLabel(position, y, "Speech Output");
            y = DrawProperty(position, y, useLocalProp, "Use Local TTS");
            if (useLocalProp != null && useLocalProp.boolValue)
            {
                y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.SpeechSynthesizer)), "Speech Synthesizer");
            }
            if (showRemoteFields)
            {
                y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.TtsModel)), "Remote Model");
                y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.TtsVoice)), "Remote Voice");
            }
            y += SectionSpacing;

            y = DrawSectionLabel(position, y, "Interaction");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.InterruptAssistantOnUserSpeech)), "Interrupt On User Speech");
            y += SectionSpacing;

            y = DrawSectionLabel(position, y, "Runtime");
            y = DrawProperty(position, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.MicStartupDelay)), "Mic Startup Delay (s)");

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private enum Section
        {
            Components,
            Llm,
            Speech,
            Interaction,
            Runtime
        }

        private static float DrawProperty(Rect root, float currentY, SerializedProperty property, string label, bool includeChildren = false)
        {
            if (property == null)
            {
                return currentY;
            }

            float height = EditorGUI.GetPropertyHeight(property, includeChildren);
            var rect = new Rect(root.x, currentY, root.width, height);
            EditorGUI.PropertyField(rect, property, new GUIContent(label), includeChildren);
            return rect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float DrawSectionLabel(Rect root, float currentY, string title)
        {
            var labelRect = new Rect(root.x, currentY, root.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, title, EditorStyles.boldLabel);
            return labelRect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float GetSectionHeight(SerializedProperty property, Section section)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = EditorGUIUtility.singleLineHeight + spacing;

            float AddProp(string relative, bool includeChildren = false)
            {
                var prop = property.FindPropertyRelative(relative);
                if (prop == null)
                {
                    return 0f;
                }
                return EditorGUI.GetPropertyHeight(prop, includeChildren) + spacing;
            }

            switch (section)
            {
                case Section.Components:
                    height += AddProp(nameof(AIChatControllerConfig.Microphone));
                    break;

                case Section.Llm:
                    height += AddProp(nameof(AIChatControllerConfig.ApiBaseUrl));
                    height += AddProp(nameof(AIChatControllerConfig.ApiKey));
                    height += AddProp(nameof(AIChatControllerConfig.LlmModel));
                    height += AddProp(nameof(AIChatControllerConfig.LlmTemperature));
                    height += AddProp(nameof(AIChatControllerConfig.SystemPromptProfile));
                    height += AddProp(nameof(AIChatControllerConfig.LogStreamingChunks));
                    break;

                case Section.Speech:
                    var useLocalProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.UseLocalTts));
                    height += AddProp(nameof(AIChatControllerConfig.UseLocalTts));

                    bool showRemoteFields = useLocalProp == null ||
                                            useLocalProp.hasMultipleDifferentValues ||
                                            !useLocalProp.boolValue;

                    if (!showRemoteFields)
                    {
                        // Local TTS enabled – show local Speech Synthesizer field
                        height += AddProp(nameof(AIChatControllerConfig.SpeechSynthesizer));
                    }
                    else
                    {
                        // Remote TTS – show remote fields
                        height += AddProp(nameof(AIChatControllerConfig.TtsModel));
                        height += AddProp(nameof(AIChatControllerConfig.TtsVoice));
                    }
                    break;

                case Section.Interaction:
                    height += AddProp(nameof(AIChatControllerConfig.InterruptAssistantOnUserSpeech));
                    break;

                case Section.Runtime:
                    height += AddProp(nameof(AIChatControllerConfig.MicStartupDelay));
                    break;
            }

            return height;
        }
    }
}
