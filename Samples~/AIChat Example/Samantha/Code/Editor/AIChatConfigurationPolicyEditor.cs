#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT

using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AIChatConfigurationPolicy))]
    internal sealed class AIChatConfigurationPolicyEditor : UnityEditor.Editor
    {
        private SerializedProperty _enabledOverrideProp;
        private SerializedProperty _presetProp;

        private SerializedProperty _apiKeyProp;
        private SerializedProperty _apiBaseUrlProp;
        private SerializedProperty _llmModelProp;
        private SerializedProperty _llmTemperatureProp;
        private SerializedProperty _maxHistoryTurnsProp;
        private SerializedProperty _useLocalTtsProp;
        private SerializedProperty _ttsModelProp;
        private SerializedProperty _ttsVoiceProp;
        private SerializedProperty _useStreamingTtsProp;
        private SerializedProperty _enableTtsDiagnosticsProp;
        private SerializedProperty _interruptAssistantOnUserSpeechProp;
        private SerializedProperty _micStartupDelayProp;
        private SerializedProperty _asrTurnDetectionDelaySecondsProp;
        private SerializedProperty _asrRecognitionModeIndexProp;
        private SerializedProperty _asrStreamingModelIdProp;
        private SerializedProperty _asrOfflineModelIdProp;
        private SerializedProperty _asrVadModelIdProp;
        private SerializedProperty _asrEnablePunctuationProp;
        private SerializedProperty _asrPunctuationModelIdProp;
        private SerializedProperty _localTtsModelIdProp;
        private SerializedProperty _localTtsVoiceIdProp;
        private SerializedProperty _localTtsSpeedProp;
        private SerializedProperty _localTtsSampleRateProp;

        private bool _advancedFoldout;
        private bool _quickActionsFoldout;
        private bool _resolvedPreviewFoldout;
        private bool _resolvedCoreFoldout;
        private bool _resolvedAsrFoldout;
        private bool _resolvedTtsFoldout;
        private bool _chatFoldout;
        private bool _remoteTtsFoldout;
        private bool _experienceFoldout;
        private bool _asrFoldout;
        private bool _localTtsFoldout;
        private string _advancedSearch = string.Empty;

        private void OnEnable()
        {
            _enabledOverrideProp = serializedObject.FindProperty("_enabledOverride");
            _presetProp = serializedObject.FindProperty("_preset");

            _apiKeyProp = serializedObject.FindProperty("_apiKey");
            _apiBaseUrlProp = serializedObject.FindProperty("_apiBaseUrl");
            _llmModelProp = serializedObject.FindProperty("_llmModel");
            _llmTemperatureProp = serializedObject.FindProperty("_llmTemperature");
            _maxHistoryTurnsProp = serializedObject.FindProperty("_maxHistoryTurns");
            _useLocalTtsProp = serializedObject.FindProperty("_useLocalTts");
            _ttsModelProp = serializedObject.FindProperty("_ttsModel");
            _ttsVoiceProp = serializedObject.FindProperty("_ttsVoice");
            _useStreamingTtsProp = serializedObject.FindProperty("_useStreamingTts");
            _enableTtsDiagnosticsProp = serializedObject.FindProperty("_enableTtsDiagnostics");
            _interruptAssistantOnUserSpeechProp = serializedObject.FindProperty("_interruptAssistantOnUserSpeech");
            _micStartupDelayProp = serializedObject.FindProperty("_micStartupDelay");
            _asrTurnDetectionDelaySecondsProp = serializedObject.FindProperty("_asrTurnDetectionDelaySeconds");
            _asrRecognitionModeIndexProp = serializedObject.FindProperty("_asrRecognitionModeIndex");
            _asrStreamingModelIdProp = serializedObject.FindProperty("_asrStreamingModelId");
            _asrOfflineModelIdProp = serializedObject.FindProperty("_asrOfflineModelId");
            _asrVadModelIdProp = serializedObject.FindProperty("_asrVadModelId");
            _asrEnablePunctuationProp = serializedObject.FindProperty("_asrEnablePunctuation");
            _asrPunctuationModelIdProp = serializedObject.FindProperty("_asrPunctuationModelId");
            _localTtsModelIdProp = serializedObject.FindProperty("_localTtsModelId");
            _localTtsVoiceIdProp = serializedObject.FindProperty("_localTtsVoiceId");
            _localTtsSpeedProp = serializedObject.FindProperty("_localTtsSpeed");
            _localTtsSampleRateProp = serializedObject.FindProperty("_localTtsSampleRate");

            _advancedFoldout = false;
            _quickActionsFoldout = false;
            _resolvedPreviewFoldout = false;
            _resolvedCoreFoldout = true;
            _resolvedAsrFoldout = false;
            _resolvedTtsFoldout = false;
            _chatFoldout = false;
            _remoteTtsFoldout = false;
            _experienceFoldout = false;
            _asrFoldout = false;
            _localTtsFoldout = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();
            DrawStatusStrip();
            DrawPolicyPanel();
            DrawQuickActions();
            DrawAdvancedOverrides();
            DrawResolvedPreview();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInspectorHeader()
        {
            var icon = EditorGUIUtility.IconContent("d_Settings");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(icon.image, GUILayout.Width(24f), GUILayout.Height(24f));
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("AI Chat Configuration Policy", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Choose a platform template first, then override only the few values you actually need.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawStatusStrip()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight * 2f + 8f);
                float spacing = 6f;
                float columnWidth = (rowRect.width - spacing * 2f) / 3f;

                DrawMiniBadge(new Rect(rowRect.x, rowRect.y, columnWidth, rowRect.height), "Preset", GetPreset().ToString());
                DrawMiniBadge(new Rect(rowRect.x + columnWidth + spacing, rowRect.y, columnWidth, rowRect.height), "Overrides", CountEnabledOverrides().ToString());
                DrawMiniBadge(new Rect(rowRect.x + (columnWidth + spacing) * 2f, rowRect.y, columnWidth, rowRect.height), "Policy",
                    _enabledOverrideProp != null && _enabledOverrideProp.boolValue ? "Active" : "Disabled");
            }
        }

        private void DrawRecommendationBox()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Recommended Setup", EditorStyles.boldLabel);
                var preset = GetPreset();
                EditorGUILayout.HelpBox(GetPresetSummary(preset), MessageType.Info);
                EditorGUILayout.LabelField("Recommendation", GetPresetRecommendation(preset), EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawPolicyPanel()
        {
            using (AIChatEditorHeaderDrawer.BeginTitledHelpBox("Policy"))
            {
                EditorGUILayout.PropertyField(_enabledOverrideProp, new GUIContent("Enable Policy"));

                using (new EditorGUI.DisabledScope(_enabledOverrideProp != null && !_enabledOverrideProp.boolValue))
                {
                    EditorGUILayout.PropertyField(_presetProp, new GUIContent("Preset"));
                }

                DrawRecommendationBox();
            }
        }

        private void DrawQuickActions()
        {
            _quickActionsFoldout = EditorGUILayout.Foldout(_quickActionsFoldout, "Quick Actions", true);
            if (!_quickActionsFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Apply Recommended Overrides"))
                    {
                        ApplyRecommendedOverrides(GetPreset());
                    }

                    if (GUILayout.Button("Clear All Overrides"))
                    {
                        ClearAllOverrides();
                    }
                }

                EditorGUILayout.HelpBox(
                    "Recommended overrides write a small set of low-risk latency defaults on top of the selected template. " +
                    "Clear All Overrides keeps the template but removes all explicit override fields.",
                    MessageType.None);
            }
        }

        private void DrawResolvedPreview()
        {
            _resolvedPreviewFoldout = EditorGUILayout.Foldout(_resolvedPreviewFoldout, "Resolved Preview", true);
            if (!_resolvedPreviewFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Resolved Preview", EditorStyles.boldLabel);
                if (serializedObject.isEditingMultipleObjects)
                {
                    EditorGUILayout.HelpBox("Resolved preview is unavailable during multi-object editing.", MessageType.Info);
                    return;
                }

                if (!(target is AIChatConfigurationPolicy policy))
                {
                    return;
                }

                var controller = policy.GetComponent<AIChatController>();
                AIChatControllerConfig sourceConfig = controller != null ? controller.CurrentConfig : null;
                AIChatResolvedConfiguration resolved = policy.PreviewResolvedConfiguration(sourceConfig);

                if (!policy.EnabledOverride)
                {
                    EditorGUILayout.HelpBox("Configuration policy is disabled. Preview reflects the current controller configuration only.", MessageType.Info);
                }

                _resolvedCoreFoldout = EditorGUILayout.Foldout(_resolvedCoreFoldout, "Core", true);
                if (_resolvedCoreFoldout)
                {
                    DrawPreviewSummary(GetEnabledOverrideCount(
                        _apiKeyProp,
                        _apiBaseUrlProp,
                        _llmModelProp,
                        _llmTemperatureProp,
                        _maxHistoryTurnsProp,
                        _interruptAssistantOnUserSpeechProp,
                        _micStartupDelayProp));
                    DrawPreviewField("Template", policy.Preset.ToString(), "Policy");
                    DrawPreviewField("API Key", MaskSecret(resolved.ApiKey), GetSourceLabel(policy, _apiKeyProp, false));
                    DrawPreviewField("API Base URL", resolved.ApiBaseUrl, GetSourceLabel(policy, _apiBaseUrlProp, true));
                    DrawPreviewField("LLM Model", resolved.LlmModel, GetSourceLabel(policy, _llmModelProp, true));
                    DrawPreviewField("LLM Temperature", resolved.LlmTemperature.ToString("0.##"), GetSourceLabel(policy, _llmTemperatureProp, false));
                    DrawPreviewField("Max History Turns", resolved.MaxHistoryTurns.ToString(), GetSourceLabel(policy, _maxHistoryTurnsProp, false));
                    DrawPreviewField("Interrupt On User Speech", BoolText(resolved.InterruptAssistantOnUserSpeech), GetSourceLabel(policy, _interruptAssistantOnUserSpeechProp, false));
                    DrawPreviewField("Mic Startup Delay", $"{resolved.MicStartupDelay:0.##} s", GetSourceLabel(policy, _micStartupDelayProp, false));
                }

                _resolvedAsrFoldout = EditorGUILayout.Foldout(_resolvedAsrFoldout, "ASR", true);
                if (_resolvedAsrFoldout)
                {
                    DrawPreviewSummary(GetEnabledOverrideCount(
                        _asrTurnDetectionDelaySecondsProp,
                        _asrRecognitionModeIndexProp,
                        _asrStreamingModelIdProp,
                        _asrOfflineModelIdProp,
                        _asrVadModelIdProp,
                        _asrEnablePunctuationProp,
                        _asrPunctuationModelIdProp));
                    DrawPreviewField("ASR Turn Delay", $"{resolved.AsrTurnDetectionDelaySeconds:0.##} s", GetSourceLabel(policy, _asrTurnDetectionDelaySecondsProp, false));
                    DrawPreviewField("ASR Recognition Mode", RecognitionModeLabel(resolved.AsrRecognitionModeIndex), GetSourceLabel(policy, _asrRecognitionModeIndexProp, false));
                    DrawPreviewField("ASR Streaming Model", resolved.AsrStreamingModelId, GetSourceLabel(policy, _asrStreamingModelIdProp, false));
                    DrawPreviewField("ASR Offline Model", resolved.AsrOfflineModelId, GetSourceLabel(policy, _asrOfflineModelIdProp, false));
                    DrawPreviewField("ASR VAD Model", resolved.AsrVadModelId, GetSourceLabel(policy, _asrVadModelIdProp, false));
                    DrawPreviewField("ASR Punctuation", BoolText(resolved.AsrEnablePunctuation), GetSourceLabel(policy, _asrEnablePunctuationProp, false));
                    DrawPreviewField("ASR Punctuation Model", resolved.AsrPunctuationModelId, GetSourceLabel(policy, _asrPunctuationModelIdProp, false));
                }

                _resolvedTtsFoldout = EditorGUILayout.Foldout(_resolvedTtsFoldout, "TTS", true);
                if (_resolvedTtsFoldout)
                {
                    DrawPreviewSummary(GetEnabledOverrideCount(
                        _useLocalTtsProp,
                        _ttsModelProp,
                        _ttsVoiceProp,
                        _useStreamingTtsProp,
                        _enableTtsDiagnosticsProp,
                        _localTtsModelIdProp,
                        _localTtsVoiceIdProp,
                        _localTtsSpeedProp,
                        _localTtsSampleRateProp));
                    DrawPreviewField("Use Local TTS", BoolText(resolved.UseLocalTts), GetSourceLabel(policy, _useLocalTtsProp, true));
                    DrawPreviewField("Remote TTS Model", resolved.TtsModel, GetSourceLabel(policy, _ttsModelProp, true));
                    DrawPreviewField("Remote TTS Voice", resolved.TtsVoice, GetSourceLabel(policy, _ttsVoiceProp, true));
                    DrawPreviewField("Streaming TTS", BoolText(resolved.UseStreamingTts), GetSourceLabel(policy, _useStreamingTtsProp, true));
                    DrawPreviewField("TTS Diagnostics", BoolText(resolved.EnableTtsDiagnostics), GetSourceLabel(policy, _enableTtsDiagnosticsProp, true));
                    DrawPreviewField("Local TTS Model", resolved.LocalTtsModelId, GetSourceLabel(policy, _localTtsModelIdProp, false));
                    DrawPreviewField("Local TTS Voice Id", resolved.LocalTtsVoiceId.ToString(), GetSourceLabel(policy, _localTtsVoiceIdProp, false));
                    DrawPreviewField("Local TTS Speed", resolved.LocalTtsSpeed.ToString("0.##"), GetSourceLabel(policy, _localTtsSpeedProp, false));
                    DrawPreviewField("Local TTS Sample Rate", resolved.LocalTtsSampleRate.ToString(), GetSourceLabel(policy, _localTtsSampleRateProp, false));
                }

                DrawRiskWarnings(policy, resolved);

                if (controller == null)
                {
                    EditorGUILayout.HelpBox("Attach this component to the same GameObject as AIChatController to preview current controller-linked ASR and Local TTS settings.", MessageType.None);
                }
            }
        }

        private void DrawAdvancedOverrides()
        {
            _advancedFoldout = EditorGUILayout.Foldout(_advancedFoldout, "Overrides", true);
            if (!_advancedFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("Only enable fields you intentionally want to pin. Disabled fields continue to follow the selected template.", MessageType.None);
                DrawSearchToolbar();

                DrawOverrideSection(ref _chatFoldout, "Chat",
                    ("API Key", _apiKeyProp),
                    ("API Base URL", _apiBaseUrlProp),
                    ("LLM Model", _llmModelProp),
                    ("LLM Temperature", _llmTemperatureProp),
                    ("Max History Turns", _maxHistoryTurnsProp));

                DrawOverrideSection(ref _remoteTtsFoldout, "Remote TTS",
                    ("Use Local TTS", _useLocalTtsProp),
                    ("TTS Model", _ttsModelProp),
                    ("TTS Voice", _ttsVoiceProp),
                    ("Use Streaming TTS", _useStreamingTtsProp),
                    ("Enable TTS Diagnostics", _enableTtsDiagnosticsProp));

                DrawOverrideSection(ref _experienceFoldout, "Experience",
                    ("Interrupt On User Speech", _interruptAssistantOnUserSpeechProp),
                    ("Mic Startup Delay", _micStartupDelayProp),
                    ("ASR Turn Delay", _asrTurnDetectionDelaySecondsProp));

                DrawOverrideSection(ref _asrFoldout, "ASR",
                    ("Recognition Mode Index", _asrRecognitionModeIndexProp),
                    ("Streaming Model Id", _asrStreamingModelIdProp),
                    ("Offline Model Id", _asrOfflineModelIdProp),
                    ("VAD Model Id", _asrVadModelIdProp),
                    ("Enable Punctuation", _asrEnablePunctuationProp),
                    ("Punctuation Model Id", _asrPunctuationModelIdProp));

                DrawOverrideSection(ref _localTtsFoldout, "Local TTS",
                    ("Local Model Id", _localTtsModelIdProp),
                    ("Voice Id", _localTtsVoiceIdProp),
                    ("Speed", _localTtsSpeedProp),
                    ("Sample Rate", _localTtsSampleRateProp));
            }
        }

        private void DrawOverrideSection(ref bool foldout, string title, params (string label, SerializedProperty property)[] properties)
        {
            if (!SectionMatchesSearch(properties))
            {
                return;
            }

            foldout = EditorGUILayout.Foldout(foldout, title, true);
            if (!foldout)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < properties.Length; i++)
                {
                    if (properties[i].property != null && MatchesSearch(properties[i].label))
                    {
                        if (IsSecretLabel(properties[i].label))
                        {
                            DrawCompactSecretOverride(properties[i].label, properties[i].property);
                        }
                        else
                        {
                            DrawCompactOverride(properties[i].label, properties[i].property);
                        }
                    }
                }
            }
        }

        private void DrawCompactOverride(string label, SerializedProperty property)
        {
            SerializedProperty enabledProp = property.FindPropertyRelative("Enabled");
            SerializedProperty valueProp = property.FindPropertyRelative("Value");
            if (enabledProp == null || valueProp == null)
            {
                EditorGUILayout.PropertyField(property, true);
                return;
            }

            Color previousColor = GUI.backgroundColor;
            if (MatchesSearch(label) && !string.IsNullOrWhiteSpace(_advancedSearch))
            {
                GUI.backgroundColor = new Color(1f, 0.96f, 0.72f, 1f);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawOverrideModeSelector(label, enabledProp);

                using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
                {
                    switch (valueProp.propertyType)
                    {
                        case SerializedPropertyType.Boolean:
                            valueProp.boolValue = EditorGUILayout.Toggle("Value", valueProp.boolValue);
                            break;
                        case SerializedPropertyType.Integer:
                            valueProp.intValue = EditorGUILayout.IntField("Value", valueProp.intValue);
                            break;
                        case SerializedPropertyType.Float:
                            valueProp.floatValue = EditorGUILayout.FloatField("Value", valueProp.floatValue);
                            break;
                        case SerializedPropertyType.String:
                            valueProp.stringValue = IsSecretLabel(label)
                                ? EditorGUILayout.PasswordField("Value", valueProp.stringValue ?? string.Empty)
                                : EditorGUILayout.TextField("Value", valueProp.stringValue ?? string.Empty);
                            break;
                        default:
                            EditorGUILayout.PropertyField(valueProp, GUIContent.none, true);
                            break;
                    }
                }
            }

            GUI.backgroundColor = previousColor;
        }

        private void DrawCompactSecretOverride(string label, SerializedProperty property)
        {
            SerializedProperty enabledProp = property.FindPropertyRelative("Enabled");
            SerializedProperty valueProp = property.FindPropertyRelative("Value");
            if (enabledProp == null || valueProp == null)
            {
                EditorGUILayout.PropertyField(property, true);
                return;
            }

            Color previousColor = GUI.backgroundColor;
            if (MatchesSearch(label) && !string.IsNullOrWhiteSpace(_advancedSearch))
            {
                GUI.backgroundColor = new Color(1f, 0.96f, 0.72f, 1f);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSecretModeSelector(label, enabledProp);

                using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
                {
                    valueProp.stringValue = EditorGUILayout.PasswordField("Value", valueProp.stringValue ?? string.Empty);
                }
            }

            GUI.backgroundColor = previousColor;
        }

        private static void DrawOverrideModeSelector(string label, SerializedProperty enabledProp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                int mode = enabledProp.boolValue ? 1 : 0;
                int next = GUILayout.Toolbar(
                    mode,
                    new[] { "Follow Template", "Pinned" },
                    GUILayout.Width(220f));

                if (next != mode)
                {
                    enabledProp.boolValue = next == 1;
                }
            }
        }

        private static void DrawSecretModeSelector(string label, SerializedProperty enabledProp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

                int mode = enabledProp.boolValue ? 1 : 0;
                int next = GUILayout.Toolbar(
                    mode,
                    new[] { "Disabled", "Use Override" },
                    GUILayout.Width(220f));

                if (next != mode)
                {
                    enabledProp.boolValue = next == 1;
                }
            }
        }

        private AIChatConfigurationPolicy.PolicyPreset GetPreset()
        {
            if (_presetProp == null || _presetProp.hasMultipleDifferentValues)
            {
                return AIChatConfigurationPolicy.PolicyPreset.Custom;
            }

            return (AIChatConfigurationPolicy.PolicyPreset)_presetProp.enumValueIndex;
        }

        private static string GetPresetSummary(AIChatConfigurationPolicy.PolicyPreset preset)
        {
            switch (preset)
            {
                case AIChatConfigurationPolicy.PolicyPreset.OpenAI:
                    return "OpenAI template: remote LLM + remote TTS, fast default setup for general cloud deployment.";
                case AIChatConfigurationPolicy.PolicyPreset.SiliconFlow:
                    return "SiliconFlow template: Qwen/Qwen3.5-9B + CosyVoice2 streaming voice, tuned for real-time Chinese digital human scenarios.";
                case AIChatConfigurationPolicy.PolicyPreset.LocalOnly:
                    return "LocalOnly template: keeps speech output local and disables remote streaming TTS. Best when you need deterministic local voice playback.";
                default:
                    return "Custom template: no platform opinion is applied. Enable only the overrides you want to hard-lock.";
            }
        }

        private static string GetPresetRecommendation(AIChatConfigurationPolicy.PolicyPreset preset)
        {
            switch (preset)
            {
                case AIChatConfigurationPolicy.PolicyPreset.OpenAI:
                    return "Use when you want the simplest hosted setup. Keep temperature moderate, keep streaming TTS on, and avoid over-constraining the model.";
                case AIChatConfigurationPolicy.PolicyPreset.SiliconFlow:
                    return "Use for low-latency Chinese voice agents. Pair with expressive CosyVoice input formatting and keep enable_thinking disabled.";
                case AIChatConfigurationPolicy.PolicyPreset.LocalOnly:
                    return "Use when playback stability matters more than remote voice quality. Pair with a validated local TTS preset and keep turn detection conservative.";
                default:
                    return "Start from one of the built-in templates unless you already know the exact API, model, TTS, and ASR settings you want to freeze.";
            }
        }

        private void ApplyRecommendedOverrides(AIChatConfigurationPolicy.PolicyPreset preset)
        {
            switch (preset)
            {
                case AIChatConfigurationPolicy.PolicyPreset.OpenAI:
                    SetFloatOverride(_llmTemperatureProp, 0.6f);
                    SetIntOverride(_maxHistoryTurnsProp, 6);
                    SetBoolOverride(_useStreamingTtsProp, true);
                    SetBoolOverride(_interruptAssistantOnUserSpeechProp, true);
                    SetFloatOverride(_micStartupDelayProp, 0.25f);
                    SetFloatOverride(_asrTurnDetectionDelaySecondsProp, 0.35f);
                    break;
                case AIChatConfigurationPolicy.PolicyPreset.SiliconFlow:
                    SetFloatOverride(_llmTemperatureProp, 0.5f);
                    SetIntOverride(_maxHistoryTurnsProp, 6);
                    SetBoolOverride(_useStreamingTtsProp, true);
                    SetBoolOverride(_interruptAssistantOnUserSpeechProp, true);
                    SetFloatOverride(_micStartupDelayProp, 0.2f);
                    SetFloatOverride(_asrTurnDetectionDelaySecondsProp, 0.3f);
                    SetBoolOverride(_enableTtsDiagnosticsProp, false);
                    break;
                case AIChatConfigurationPolicy.PolicyPreset.LocalOnly:
                    SetBoolOverride(_useLocalTtsProp, true);
                    SetBoolOverride(_useStreamingTtsProp, false);
                    SetBoolOverride(_interruptAssistantOnUserSpeechProp, true);
                    SetFloatOverride(_micStartupDelayProp, 0.15f);
                    SetFloatOverride(_asrTurnDetectionDelaySecondsProp, 0.4f);
                    break;
                default:
                    SetBoolOverride(_interruptAssistantOnUserSpeechProp, true);
                    SetFloatOverride(_micStartupDelayProp, 0.25f);
                    SetFloatOverride(_asrTurnDetectionDelaySecondsProp, 0.35f);
                    break;
            }
        }

        private void ClearAllOverrides()
        {
            ClearOverride(_apiBaseUrlProp);
            ClearOverride(_llmModelProp);
            ClearOverride(_llmTemperatureProp);
            ClearOverride(_maxHistoryTurnsProp);
            ClearOverride(_useLocalTtsProp);
            ClearOverride(_ttsModelProp);
            ClearOverride(_ttsVoiceProp);
            ClearOverride(_useStreamingTtsProp);
            ClearOverride(_enableTtsDiagnosticsProp);
            ClearOverride(_interruptAssistantOnUserSpeechProp);
            ClearOverride(_micStartupDelayProp);
            ClearOverride(_asrTurnDetectionDelaySecondsProp);
            ClearOverride(_asrRecognitionModeIndexProp);
            ClearOverride(_asrStreamingModelIdProp);
            ClearOverride(_asrOfflineModelIdProp);
            ClearOverride(_asrVadModelIdProp);
            ClearOverride(_asrEnablePunctuationProp);
            ClearOverride(_asrPunctuationModelIdProp);
            ClearOverride(_localTtsModelIdProp);
            ClearOverride(_localTtsVoiceIdProp);
            ClearOverride(_localTtsSpeedProp);
            ClearOverride(_localTtsSampleRateProp);
        }

        private static void ClearOverride(SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            var enabledProp = property.FindPropertyRelative("Enabled");
            if (enabledProp != null)
            {
                enabledProp.boolValue = false;
            }
        }

        private static void DrawPreviewField(string label, string value, string source)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                string resolved = string.IsNullOrWhiteSpace(value) ? "-" : value;
                string suffix = string.IsNullOrWhiteSpace(source) ? string.Empty : $"  [{source}]";
                EditorGUILayout.SelectableLabel(resolved + suffix, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawRiskWarnings(AIChatConfigurationPolicy policy, AIChatResolvedConfiguration resolved)
        {
            if (!policy.EnabledOverride)
            {
                return;
            }

            if (!resolved.UseLocalTts && !resolved.UseStreamingTts)
            {
                EditorGUILayout.HelpBox("Remote TTS is selected but streaming is disabled. This usually increases first-audio latency for digital human dialogue.", MessageType.Warning);
            }

            if (!resolved.InterruptAssistantOnUserSpeech)
            {
                EditorGUILayout.HelpBox("Interrupt-on-speech is disabled. This weakens barge-in behavior and usually makes real-time dialogue feel slower.", MessageType.Warning);
            }

            if (resolved.AsrTurnDetectionDelaySeconds > 0.6f)
            {
                EditorGUILayout.HelpBox("ASR turn detection delay is high. Long turn delay can noticeably increase end-of-user-speech to response latency.", MessageType.Warning);
            }

            if (resolved.EnableTtsDiagnostics)
            {
                EditorGUILayout.HelpBox("TTS diagnostics are enabled. Keep this off in performance-sensitive production dialogue unless you are actively debugging audio delivery.", MessageType.Info);
            }

            if (policy.Preset == AIChatConfigurationPolicy.PolicyPreset.SiliconFlow &&
                !string.IsNullOrWhiteSpace(resolved.LlmModel) &&
                resolved.LlmModel.IndexOf("Qwen/Qwen3.5-9B", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                EditorGUILayout.HelpBox("SiliconFlow template is active, but the resolved LLM is no longer Qwen/Qwen3.5-9B. Confirm this override is intentional for latency and cost.", MessageType.Info);
            }
        }

        private static string GetSourceLabel(AIChatConfigurationPolicy policy, SerializedProperty property, bool templateDriven)
        {
            if (property != null)
            {
                SerializedProperty enabledProp = property.FindPropertyRelative("Enabled");
                if (enabledProp != null && enabledProp.boolValue)
                {
                    return "Override";
                }
            }

            if (!policy.EnabledOverride)
            {
                return "Current";
            }

            return templateDriven && policy.Preset != AIChatConfigurationPolicy.PolicyPreset.Custom ? "Template" : "Current";
        }

        private static string BoolText(bool value)
        {
            return value ? "Enabled" : "Disabled";
        }

        private static string RecognitionModeLabel(int index)
        {
            switch (index)
            {
                case 0:
                    return "Streaming";
                case 1:
                    return "OfflineWithVad";
                case 2:
                    return "Hybrid";
                default:
                    return "-";
            }
        }

        private static bool IsSecretLabel(string label)
        {
            return string.Equals(label, "API Key", System.StringComparison.Ordinal);
        }

        private static string MaskSecret(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            string trimmed = value.Trim();
            if (trimmed.Length <= 8)
            {
                return "********";
            }

            return trimmed.Substring(0, 4) + "..." + trimmed.Substring(trimmed.Length - 4);
        }

        private int CountEnabledOverrides()
        {
            return CountEnabled(_apiKeyProp)
                   + CountEnabled(_apiBaseUrlProp)
                   + CountEnabled(_llmModelProp)
                   + CountEnabled(_llmTemperatureProp)
                   + CountEnabled(_maxHistoryTurnsProp)
                   + CountEnabled(_useLocalTtsProp)
                   + CountEnabled(_ttsModelProp)
                   + CountEnabled(_ttsVoiceProp)
                   + CountEnabled(_useStreamingTtsProp)
                   + CountEnabled(_enableTtsDiagnosticsProp)
                   + CountEnabled(_interruptAssistantOnUserSpeechProp)
                   + CountEnabled(_micStartupDelayProp)
                   + CountEnabled(_asrTurnDetectionDelaySecondsProp)
                   + CountEnabled(_asrRecognitionModeIndexProp)
                   + CountEnabled(_asrStreamingModelIdProp)
                   + CountEnabled(_asrOfflineModelIdProp)
                   + CountEnabled(_asrVadModelIdProp)
                   + CountEnabled(_asrEnablePunctuationProp)
                   + CountEnabled(_asrPunctuationModelIdProp)
                   + CountEnabled(_localTtsModelIdProp)
                   + CountEnabled(_localTtsVoiceIdProp)
                   + CountEnabled(_localTtsSpeedProp)
                   + CountEnabled(_localTtsSampleRateProp);
        }

        private static int CountEnabled(SerializedProperty property)
        {
            if (property == null)
            {
                return 0;
            }

            SerializedProperty enabledProp = property.FindPropertyRelative("Enabled");
            return enabledProp != null && enabledProp.boolValue ? 1 : 0;
        }

        private static void DrawMiniBadge(Rect rect, string label, string value)
        {
            float labelHeight = EditorGUIUtility.singleLineHeight - 2f;
            float valueHeight = EditorGUIUtility.singleLineHeight + 2f;

            Rect labelRect = new Rect(rect.x, rect.y, rect.width, labelHeight);
            Rect valueRect = new Rect(rect.x, rect.y + labelHeight + 2f, rect.width, valueHeight);

            EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
            GUI.Box(valueRect, GUIContent.none, EditorStyles.helpBox);

            Rect paddedValueRect = new Rect(
                valueRect.x + 6f,
                valueRect.y + 1f,
                valueRect.width - 12f,
                valueRect.height - 2f);

            EditorGUI.LabelField(paddedValueRect, value, EditorStyles.label);
        }

        private void DrawSearchToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent icon = EditorGUIUtility.IconContent("Search Icon");
                GUILayout.Label(icon, GUILayout.Width(20f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                _advancedSearch = EditorGUILayout.TextField(_advancedSearch ?? string.Empty);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_advancedSearch)))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(56f)))
                    {
                        _advancedSearch = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        private static void DrawPreviewSummary(int enabledOverrideCount)
        {
            string message = enabledOverrideCount > 0
                ? $"{enabledOverrideCount} override(s) active"
                : "No overrides active";
            EditorGUILayout.LabelField(message, EditorStyles.miniLabel);
        }

        private bool SectionMatchesSearch((string label, SerializedProperty property)[] properties)
        {
            if (string.IsNullOrWhiteSpace(_advancedSearch))
            {
                return true;
            }

            for (int i = 0; i < properties.Length; i++)
            {
                if (MatchesSearch(properties[i].label))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesSearch(string label)
        {
            if (string.IsNullOrWhiteSpace(_advancedSearch))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(label) &&
                   label.IndexOf(_advancedSearch.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetEnabledOverrideCount(params SerializedProperty[] properties)
        {
            int count = 0;
            for (int i = 0; i < properties.Length; i++)
            {
                count += CountEnabled(properties[i]);
            }

            return count;
        }

        private static void SetBoolOverride(SerializedProperty property, bool value)
        {
            SetOverride(property, enabledValue: true, boolValue: value);
        }

        private static void SetIntOverride(SerializedProperty property, int value)
        {
            SetOverride(property, enabledValue: true, intValue: value);
        }

        private static void SetFloatOverride(SerializedProperty property, float value)
        {
            SetOverride(property, enabledValue: true, floatValue: value);
        }

        private static void SetOverride(
            SerializedProperty property,
            bool enabledValue,
            bool? boolValue = null,
            int? intValue = null,
            float? floatValue = null)
        {
            if (property == null)
            {
                return;
            }

            var enabledProp = property.FindPropertyRelative("Enabled");
            if (enabledProp != null)
            {
                enabledProp.boolValue = enabledValue;
            }

            var valueProp = property.FindPropertyRelative("Value");
            if (valueProp == null)
            {
                return;
            }

            if (boolValue.HasValue)
            {
                valueProp.boolValue = boolValue.Value;
            }
            else if (intValue.HasValue)
            {
                valueProp.intValue = intValue.Value;
            }
            else if (floatValue.HasValue)
            {
                valueProp.floatValue = floatValue.Value;
            }
        }
    }
}

#endif
