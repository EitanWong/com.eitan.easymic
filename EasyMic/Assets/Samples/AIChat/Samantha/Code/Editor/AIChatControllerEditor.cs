using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CustomEditor(typeof(AIChatController))]
    internal class AIChatControllerEditor : Editor
    {
        private SerializedProperty _configProp;

        private void OnEnable()
        {
            _configProp = serializedObject.FindProperty("_config");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_configProp != null)
            {
                EditorGUILayout.PropertyField(_configProp, new GUIContent("Chat Configuration"), true);
                DrawConfigDiagnostics();
            }

            EditorGUILayout.Space();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConfigDiagnostics()
        {
            if (serializedObject.isEditingMultipleObjects || _configProp == null)
            {
                return;
            }

            if (GetConfigObject(nameof(AIChatControllerConfig.Microphone)) == null)
            {
                EditorGUILayout.HelpBox("Assign a VoiceMicrophone component or the controller cannot capture input.", MessageType.Error);
            }

            bool? useLocalTts = GetConfigBool(nameof(AIChatControllerConfig.UseLocalTts));
            if (useLocalTts == true && GetConfigObject(nameof(AIChatControllerConfig.SpeechSynthesizer)) == null)
            {
                EditorGUILayout.HelpBox("Local TTS is enabled but no SpeechSynthesizer is assigned.", MessageType.Warning);
            }

            if (useLocalTts == false)
            {
                string remoteModel = GetConfigString(nameof(AIChatControllerConfig.TtsModel));
                string remoteVoice = GetConfigString(nameof(AIChatControllerConfig.TtsVoice));
                if (string.IsNullOrWhiteSpace(remoteModel) || string.IsNullOrWhiteSpace(remoteVoice))
                {
                    EditorGUILayout.HelpBox("Remote playback requires both Model and Voice names.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Remote TTS will use the configured OpenAI-compatible endpoint.", MessageType.Info);
                }
            }

            string apiBase = GetConfigString(nameof(AIChatControllerConfig.ApiBaseUrl));
            if (string.IsNullOrWhiteSpace(apiBase))
            {
                EditorGUILayout.HelpBox("API Base URL is empty. LLM and remote TTS calls will fail.", MessageType.Error);
            }
        }

        private SerializedProperty FindConfigProperty(string relativePath)
            => _configProp?.FindPropertyRelative(relativePath);

        private bool? GetConfigBool(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }
            return prop.boolValue;
        }

        private string GetConfigString(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }
            return prop.stringValue;
        }

        private UnityEngine.Object GetConfigObject(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }

            return prop.objectReferenceValue;
        }
    }
}
