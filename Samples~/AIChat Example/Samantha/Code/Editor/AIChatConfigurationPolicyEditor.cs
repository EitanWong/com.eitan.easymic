#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CustomEditor(typeof(AIChatConfigurationPolicy))]
    internal sealed class AIChatConfigurationPolicyEditor : UnityEditor.Editor
    {
        private const string ProviderSetupMenuPath = "Tools/EasyMic/AI Chat/Provider Setup";

        private SerializedProperty _enabledOverride;
        private SerializedProperty _preset;
        private SerializedProperty _apiBaseUrl;
        private SerializedProperty _llmModel;
        private SerializedProperty _llmTemperature;
        private SerializedProperty _maxHistoryTurns;
        private SerializedProperty _useLocalTts;
        private SerializedProperty _ttsModel;
        private SerializedProperty _ttsVoice;
        private SerializedProperty _useStreamingTts;
        private SerializedProperty _enableTtsDiagnostics;
        private SerializedProperty _interruptAssistantOnUserSpeech;
        private SerializedProperty _micStartupDelay;
        private SerializedProperty _asrTurnDetectionDelaySeconds;
        private SerializedProperty _asrRecognitionModeIndex;
        private SerializedProperty _asrStreamingModelId;
        private SerializedProperty _asrOfflineModelId;
        private SerializedProperty _asrVadModelId;
        private SerializedProperty _asrEnablePunctuation;
        private SerializedProperty _asrPunctuationModelId;
        private SerializedProperty _localTtsModelId;
        private SerializedProperty _localTtsVoiceId;
        private SerializedProperty _localTtsSpeed;
        private SerializedProperty _localTtsSampleRate;

        private bool _chatFoldout = true;
        private bool _speechFoldout = true;
        private bool _experienceFoldout;
        private bool _asrFoldout;
        private bool _localTtsFoldout;
        private bool _previewFoldout;
        private AIChatSetupLanguage _language;
        private SerializedProperty[] _allOverrides = Array.Empty<SerializedProperty>();

        private void OnEnable()
        {
            _enabledOverride = serializedObject.FindProperty("_enabledOverride");
            _preset = serializedObject.FindProperty("_preset");
            _apiBaseUrl = serializedObject.FindProperty("_apiBaseUrl");
            _llmModel = serializedObject.FindProperty("_llmModel");
            _llmTemperature = serializedObject.FindProperty("_llmTemperature");
            _maxHistoryTurns = serializedObject.FindProperty("_maxHistoryTurns");
            _useLocalTts = serializedObject.FindProperty("_useLocalTts");
            _ttsModel = serializedObject.FindProperty("_ttsModel");
            _ttsVoice = serializedObject.FindProperty("_ttsVoice");
            _useStreamingTts = serializedObject.FindProperty("_useStreamingTts");
            _enableTtsDiagnostics = serializedObject.FindProperty("_enableTtsDiagnostics");
            _interruptAssistantOnUserSpeech = serializedObject.FindProperty("_interruptAssistantOnUserSpeech");
            _micStartupDelay = serializedObject.FindProperty("_micStartupDelay");
            _asrTurnDetectionDelaySeconds = serializedObject.FindProperty("_asrTurnDetectionDelaySeconds");
            _asrRecognitionModeIndex = serializedObject.FindProperty("_asrRecognitionModeIndex");
            _asrStreamingModelId = serializedObject.FindProperty("_asrStreamingModelId");
            _asrOfflineModelId = serializedObject.FindProperty("_asrOfflineModelId");
            _asrVadModelId = serializedObject.FindProperty("_asrVadModelId");
            _asrEnablePunctuation = serializedObject.FindProperty("_asrEnablePunctuation");
            _asrPunctuationModelId = serializedObject.FindProperty("_asrPunctuationModelId");
            _localTtsModelId = serializedObject.FindProperty("_localTtsModelId");
            _localTtsVoiceId = serializedObject.FindProperty("_localTtsVoiceId");
            _localTtsSpeed = serializedObject.FindProperty("_localTtsSpeed");
            _localTtsSampleRate = serializedObject.FindProperty("_localTtsSampleRate");

            _allOverrides = new[]
            {
                _apiBaseUrl,
                _llmModel,
                _llmTemperature,
                _maxHistoryTurns,
                _useLocalTts,
                _ttsModel,
                _ttsVoice,
                _useStreamingTts,
                _enableTtsDiagnostics,
                _interruptAssistantOnUserSpeech,
                _micStartupDelay,
                _asrTurnDetectionDelaySeconds,
                _asrRecognitionModeIndex,
                _asrStreamingModelId,
                _asrOfflineModelId,
                _asrVadModelId,
                _asrEnablePunctuation,
                _asrPunctuationModelId,
                _localTtsModelId,
                _localTtsVoiceId,
                _localTtsSpeed,
                _localTtsSampleRate
            };
        }

        public override void OnInspectorGUI()
        {
            _language = AIChatSetupLocalization.CurrentLanguage;
            serializedObject.Update();

            AIChatSetupGui.DrawHero(
                "d_Settings",
                Text(AIChatSetupTextKey.PolicyHeader),
                Text(AIChatSetupTextKey.PolicyDescription),
                Text(AIChatSetupTextKey.Latest));

            if (AIChatSetupGui.PrimaryButton(Text(AIChatSetupTextKey.OpenProviderSetup), 30f))
            {
                EditorApplication.ExecuteMenuItem(ProviderSetupMenuPath);
            }

            DrawPolicy();
            DrawOverrides();
            DrawScenePolicyPreview();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPolicy()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(1, Text(AIChatSetupTextKey.Preset));
                EditorGUILayout.PropertyField(
                    _enabledOverride,
                    new GUIContent(Text(AIChatSetupTextKey.EnablePolicy)));

                using (new EditorGUI.DisabledScope(_enabledOverride != null && !_enabledOverride.boolValue))
                {
                    int selected = EditorGUILayout.Popup(
                        Text(AIChatSetupTextKey.Preset),
                        _preset.enumValueIndex,
                        new[]
                        {
                            Text(AIChatSetupTextKey.Custom),
                            Text(AIChatSetupTextKey.OpenAI),
                            Text(AIChatSetupTextKey.SiliconFlow),
                            Text(AIChatSetupTextKey.LocalOnly)
                        });
                    _preset.enumValueIndex = selected;
                }

                string state = _enabledOverride != null && _enabledOverride.boolValue
                    ? Text(AIChatSetupTextKey.Active)
                    : Text(AIChatSetupTextKey.Inactive);
                EditorGUILayout.LabelField(
                    $"{state} · {Text(AIChatSetupTextKey.Overrides)}: {CountEnabledOverrides()}",
                    AIChatSetupGui.BodyStyle);
            }
        }

        private void DrawOverrides()
        {
            using (new EditorGUI.DisabledScope(_enabledOverride != null && !_enabledOverride.boolValue))
            {
                DrawChatOverrides();
                DrawSpeechOverrides();
                DrawExperienceOverrides();
                DrawAsrOverrides();
                DrawLocalTtsOverrides();
            }

            using (new EditorGUI.DisabledScope(CountEnabledOverrides() == 0))
            {
                if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.ClearOverrides)))
                {
                    ClearAllOverrides();
                }
            }
        }

        private void DrawChatOverrides()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                _chatFoldout = EditorGUILayout.Foldout(
                    _chatFoldout,
                    Text(AIChatSetupTextKey.ChatSection),
                    true);
                if (!_chatFoldout)
                {
                    return;
                }

                DrawOverride(_apiBaseUrl, AIChatSetupTextKey.ApiBaseUrl);
                DrawOverride(_llmModel, AIChatSetupTextKey.LlmModel);
                DrawOverride(_llmTemperature, AIChatSetupTextKey.LlmTemperature);
                DrawOverride(_maxHistoryTurns, AIChatSetupTextKey.MaxHistoryTurns);
            }
        }

        private void DrawSpeechOverrides()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                _speechFoldout = EditorGUILayout.Foldout(
                    _speechFoldout,
                    Text(AIChatSetupTextKey.SpeechSection),
                    true);
                if (!_speechFoldout)
                {
                    return;
                }

                DrawOverride(_useLocalTts, AIChatSetupTextKey.UseLocalTts);
                DrawOverride(_ttsModel, AIChatSetupTextKey.RemoteTtsModel);
                DrawOverride(_ttsVoice, AIChatSetupTextKey.RemoteTtsVoice);
                DrawOverride(_useStreamingTts, AIChatSetupTextKey.StreamRemoteTts);
                DrawOverride(_enableTtsDiagnostics, AIChatSetupTextKey.EnableTtsDiagnostics);
            }
        }

        private void DrawExperienceOverrides()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                _experienceFoldout = EditorGUILayout.Foldout(
                    _experienceFoldout,
                    Text(AIChatSetupTextKey.ExperienceSection),
                    true);
                if (!_experienceFoldout)
                {
                    return;
                }

                DrawOverride(_interruptAssistantOnUserSpeech, AIChatSetupTextKey.InterruptAssistant);
                DrawOverride(_micStartupDelay, AIChatSetupTextKey.MicStartupDelay);
                DrawOverride(_asrTurnDetectionDelaySeconds, AIChatSetupTextKey.AsrTurnDelay);
            }
        }

        private void DrawAsrOverrides()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                _asrFoldout = EditorGUILayout.Foldout(
                    _asrFoldout,
                    Text(AIChatSetupTextKey.AsrSection),
                    true);
                if (!_asrFoldout)
                {
                    return;
                }

                DrawOverride(_asrRecognitionModeIndex, AIChatSetupTextKey.AsrRecognitionMode);
                DrawOverride(_asrStreamingModelId, AIChatSetupTextKey.AsrStreamingModel);
                DrawOverride(_asrOfflineModelId, AIChatSetupTextKey.AsrOfflineModel);
                DrawOverride(_asrVadModelId, AIChatSetupTextKey.AsrVadModel);
                DrawOverride(_asrEnablePunctuation, AIChatSetupTextKey.EnablePunctuation);
                DrawOverride(_asrPunctuationModelId, AIChatSetupTextKey.PunctuationModel);
            }
        }

        private void DrawLocalTtsOverrides()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                _localTtsFoldout = EditorGUILayout.Foldout(
                    _localTtsFoldout,
                    Text(AIChatSetupTextKey.LocalTtsSection),
                    true);
                if (!_localTtsFoldout)
                {
                    return;
                }

                DrawOverride(_localTtsModelId, AIChatSetupTextKey.LocalTtsModel);
                DrawOverride(_localTtsVoiceId, AIChatSetupTextKey.LocalTtsVoiceId);
                DrawOverride(_localTtsSpeed, AIChatSetupTextKey.LocalTtsSpeed);
                DrawOverride(_localTtsSampleRate, AIChatSetupTextKey.LocalTtsSampleRate);
            }
        }

        private void DrawOverride(SerializedProperty property, AIChatSetupTextKey labelKey)
        {
            if (property == null)
            {
                return;
            }

            SerializedProperty enabled = property.FindPropertyRelative("Enabled");
            SerializedProperty value = property.FindPropertyRelative("Value");
            if (enabled == null || value == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                enabled.boolValue = EditorGUILayout.Toggle(enabled.boolValue, GUILayout.Width(18f));
                using (new EditorGUI.DisabledScope(!enabled.boolValue))
                {
                    EditorGUILayout.PropertyField(value, new GUIContent(Text(labelKey)));
                }
            }
        }

        private void DrawScenePolicyPreview()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                _previewFoldout = EditorGUILayout.Foldout(
                    _previewFoldout,
                    Text(AIChatSetupTextKey.ScenePolicyPreview),
                    true);
                if (!_previewFoldout || targets.Length != 1)
                {
                    return;
                }

                var policy = target as AIChatConfigurationPolicy;
                var controller = policy != null ? policy.GetComponent<AIChatController>() : null;
                if (policy == null || controller == null)
                {
                    EditorGUILayout.HelpBox(Text(AIChatSetupTextKey.ControllerMissingHelp), MessageType.Info);
                    return;
                }

                AIChatResolvedConfiguration resolved = policy.PreviewResolvedConfiguration(controller.CurrentConfig);
                EditorGUILayout.HelpBox(
                    Text(AIChatSetupTextKey.DeviceConfigAfterPolicy),
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(Text(AIChatSetupTextKey.ApiBaseUrl), resolved.ApiBaseUrl ?? string.Empty);
                    EditorGUILayout.TextField(Text(AIChatSetupTextKey.LlmModel), resolved.LlmModel ?? string.Empty);
                    EditorGUILayout.Toggle(Text(AIChatSetupTextKey.UseLocalTts), resolved.UseLocalTts);
                    EditorGUILayout.TextField(Text(AIChatSetupTextKey.RemoteTtsModel), resolved.TtsModel ?? string.Empty);
                    EditorGUILayout.TextField(Text(AIChatSetupTextKey.RemoteTtsVoice), resolved.TtsVoice ?? string.Empty);
                }
            }
        }

        private int CountEnabledOverrides()
        {
            int count = 0;
            foreach (SerializedProperty property in _allOverrides)
            {
                SerializedProperty enabled = property?.FindPropertyRelative("Enabled");
                if (enabled != null && enabled.boolValue)
                {
                    count++;
                }
            }

            return count;
        }

        private void ClearAllOverrides()
        {
            Undo.RecordObjects(targets, Text(AIChatSetupTextKey.ClearPolicyOverridesUndo));
            foreach (SerializedProperty property in _allOverrides)
            {
                SerializedProperty enabled = property?.FindPropertyRelative("Enabled");
                if (enabled != null)
                {
                    enabled.boolValue = false;
                }
            }
        }

        private string Text(AIChatSetupTextKey key, params object[] args)
        {
            return AIChatSetupLocalization.Text(key, _language, args);
        }
    }
}

#endif
