#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CustomPropertyDrawer(typeof(AIChatControllerConfig))]
    internal class AIChatControllerConfigDrawer : PropertyDrawer
    {
        private const float SectionSpacing = 6f;
        private const float SectionPadding = 6f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return line;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = line + spacing;

            height += GetSectionBlockHeight(property, Section.Components);
            height += SectionSpacing;
            height += GetSectionBlockHeight(property, Section.Llm);
            height += SectionSpacing;
            height += GetSectionBlockHeight(property, Section.Conversation);
            height += SectionSpacing;
            height += GetSectionBlockHeight(property, Section.Speech);
            height += SectionSpacing;
            height += GetSectionBlockHeight(property, Section.Runtime);

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

            y = DrawSection(position, y, property, Section.Components);
            y = DrawSection(position, y, property, Section.Llm);
            y = DrawSection(position, y, property, Section.Conversation);
            y = DrawSection(position, y, property, Section.Speech);
            y = DrawSection(position, y, property, Section.Runtime, addBottomSpacing: false);

            EditorGUI.EndProperty();
        }

        private enum Section
        {
            Components,
            Llm,
            Conversation,
            Speech,
            Runtime
        }

        private static float DrawSection(Rect root, float currentY, SerializedProperty property, Section section, bool addBottomSpacing = true)
        {
            float sectionHeight = GetSectionBlockHeight(property, section);
            var boxRect = new Rect(root.x, currentY, root.width, sectionHeight);
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            var contentRoot = new Rect(
                boxRect.x + SectionPadding,
                boxRect.y + SectionPadding,
                boxRect.width - (SectionPadding * 2f),
                boxRect.height - (SectionPadding * 2f));

            float y = contentRoot.y;

            switch (section)
            {
                case Section.Components:
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.Microphone)), "Microphone");
                    break;

                case Section.Llm:
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.ApiBaseUrl)), "API Base URL");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.LlmModel)), "Model");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.LlmTemperature)), "Temperature");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.SystemPromptProfile)), "System Prompt Profile");
                    break;

                case Section.Conversation:
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.MaxHistoryTurns)), "Max History Turns");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.AsrTurnDetectionDelaySeconds)), "Turn Detection Delay (s)");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.InterruptAssistantOnUserSpeech)), "Interrupt On User Speech");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.AutoHideMouseCursorWhenIdle)), "Auto Hide Cursor");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.MouseCursorHideDelaySeconds)), "Cursor Hide Delay (s)");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.CursorMoveThresholdPixels)), "Cursor Move Threshold (px)");
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.LogStreamingChunks)), "Verbose Streaming Log");
                    break;

                case Section.Speech:
                {
                    var useLocalProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.UseLocalTts));
                    bool showLocalFields = useLocalProp == null ||
                                           useLocalProp.hasMultipleDifferentValues ||
                                           useLocalProp.boolValue;
                    bool showRemoteFields = useLocalProp == null ||
                                            useLocalProp.hasMultipleDifferentValues ||
                                            !useLocalProp.boolValue;

                    y = DrawProperty(contentRoot, y, useLocalProp, "Use Local TTS");

                    if (showLocalFields)
                    {
                        y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.SpeechSynthesizer)), "Speech Synthesizer");
                    }

                    if (showRemoteFields)
                    {
                        y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.TtsModel)), "Remote Model");
                        y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.TtsVoice)), "Remote Voice");
                        var useStreamingProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.UseStreamingTts));
                        y = DrawProperty(contentRoot, y, useStreamingProp, "Stream TTS");
                        if (useStreamingProp == null || useStreamingProp.hasMultipleDifferentValues || useStreamingProp.boolValue)
                        {
                            var infoRect = new Rect(contentRoot.x, y, contentRoot.width, EditorGUIUtility.singleLineHeight * 1.6f);
                            EditorGUI.HelpBox(infoRect, "Streaming buffer is adaptive and configured automatically.", MessageType.Info);
                            y = infoRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                        }

                        y = DrawProperty(contentRoot, y,
                            property.FindPropertyRelative(nameof(AIChatControllerConfig.EnableTtsDiagnostics)),
                            "TTS Diagnostics");
                    }

                    break;
                }

                case Section.Runtime:
                {
                    y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.MicStartupDelay)), "Mic Startup Delay (s)");
                    var loadRuntimeConfigProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.LoadRuntimeConfigOnAwake));
                    y = DrawProperty(contentRoot, y, loadRuntimeConfigProp, "Load Runtime Config");

                    if (loadRuntimeConfigProp == null ||
                        loadRuntimeConfigProp.hasMultipleDifferentValues ||
                        loadRuntimeConfigProp.boolValue)
                    {
                        y = DrawProperty(contentRoot, y, property.FindPropertyRelative(nameof(AIChatControllerConfig.RuntimeConfigFileName)), "Runtime Config File");
                    }

                    break;
                }
            }

            if (!addBottomSpacing)
            {
                return boxRect.yMax;
            }

            return boxRect.yMax + SectionSpacing;
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

        private static float GetSectionBlockHeight(SerializedProperty property, Section section)
        {
            float contentHeight = GetSectionContentHeight(property, section);
            return (SectionPadding * 2f) + contentHeight;
        }

        private static float GetSectionContentHeight(SerializedProperty property, Section section)
        {
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float height = 0f;

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
                    height += AddProp(nameof(AIChatControllerConfig.LlmModel));
                    height += AddProp(nameof(AIChatControllerConfig.LlmTemperature));
                    height += AddProp(nameof(AIChatControllerConfig.SystemPromptProfile));
                    break;

                case Section.Conversation:
                    height += AddProp(nameof(AIChatControllerConfig.MaxHistoryTurns));
                    height += AddProp(nameof(AIChatControllerConfig.AsrTurnDetectionDelaySeconds));
                    height += AddProp(nameof(AIChatControllerConfig.InterruptAssistantOnUserSpeech));
                    height += AddProp(nameof(AIChatControllerConfig.AutoHideMouseCursorWhenIdle));
                    height += AddProp(nameof(AIChatControllerConfig.MouseCursorHideDelaySeconds));
                    height += AddProp(nameof(AIChatControllerConfig.CursorMoveThresholdPixels));
                    height += AddProp(nameof(AIChatControllerConfig.LogStreamingChunks));
                    break;

                case Section.Speech:
                {
                    var useLocalProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.UseLocalTts));
                    height += AddProp(nameof(AIChatControllerConfig.UseLocalTts));

                    bool showLocalFields = useLocalProp == null ||
                                           useLocalProp.hasMultipleDifferentValues ||
                                           useLocalProp.boolValue;
                    bool showRemoteFields = useLocalProp == null ||
                                            useLocalProp.hasMultipleDifferentValues ||
                                            !useLocalProp.boolValue;

                    if (showLocalFields)
                    {
                        height += AddProp(nameof(AIChatControllerConfig.SpeechSynthesizer));
                    }

                    if (showRemoteFields)
                    {
                        height += AddProp(nameof(AIChatControllerConfig.TtsModel));
                        height += AddProp(nameof(AIChatControllerConfig.TtsVoice));
                        height += AddProp(nameof(AIChatControllerConfig.UseStreamingTts));
                        var useStreamingProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.UseStreamingTts));
                        if (useStreamingProp == null || useStreamingProp.hasMultipleDifferentValues || useStreamingProp.boolValue)
                        {
                            height += (EditorGUIUtility.singleLineHeight * 1.6f) + spacing;
                        }
                        height += AddProp(nameof(AIChatControllerConfig.EnableTtsDiagnostics));
                    }

                    break;
                }

                case Section.Runtime:
                {
                    height += AddProp(nameof(AIChatControllerConfig.MicStartupDelay));
                    var loadRuntimeConfigProp = property.FindPropertyRelative(nameof(AIChatControllerConfig.LoadRuntimeConfigOnAwake));
                    height += AddProp(nameof(AIChatControllerConfig.LoadRuntimeConfigOnAwake));
                    if (loadRuntimeConfigProp == null ||
                        loadRuntimeConfigProp.hasMultipleDifferentValues ||
                        loadRuntimeConfigProp.boolValue)
                    {
                        height += AddProp(nameof(AIChatControllerConfig.RuntimeConfigFileName));
                    }

                    break;
                }
            }

            if (height > 0f)
            {
                height -= spacing;
            }

            return height;
        }

    }
}
#endif
