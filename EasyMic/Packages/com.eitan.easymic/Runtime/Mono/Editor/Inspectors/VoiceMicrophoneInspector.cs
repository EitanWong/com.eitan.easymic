#if UNITY_EDITOR && EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using Eitan.EasyMic.Runtime.Mono.ASR;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomEditor(typeof(VoiceMicrophone))]
    public sealed class VoiceMicrophoneInspector : UnityEditor.Editor
    {
        private VoiceMicrophone _voiceMic;
        private SerializedProperty _microphoneOptionsProp;
        private SerializedProperty _deviceOptionsProp;
        private SerializedProperty _asrConfigProp;
#if EASYMIC_APM_INTEGRATION
        private SerializedProperty _audioProcessingOptionsProp;
#endif


        [MenuItem("GameObject/Audio/Input/Voice Microphone", false, -1)]
        public static void AddVoiceMicrophone()
        {
            var go = new UnityEngine.GameObject("Voice Microphone");
            go.AddComponent<VoiceMicrophone>();
            Undo.RegisterCreatedObjectUndo(go, "Create Voice Microphone");

            // Select the newly created GameObject and start rename
            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }

        private void OnEnable()
        {
            _voiceMic = (VoiceMicrophone)target;
            _microphoneOptionsProp = serializedObject.FindProperty("_microphoneOptions");
            _deviceOptionsProp = serializedObject.FindProperty("_deviceOptions");
            _asrConfigProp = serializedObject.FindProperty("_asrConfig");
#if EASYMIC_APM_INTEGRATION
            _audioProcessingOptionsProp = serializedObject.FindProperty("_audioProcessingOptions");
#endif
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCaptureSettings();
            DrawAsrConfiguration();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
            {
                DrawRuntimeStatus();
            }
        }

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        private void DrawCaptureSettings()
        {
            DrawSection(Styles.CaptureHeader, () =>
            {
                if (_microphoneOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_microphoneOptionsProp, Styles.MicrophoneOptionsLabel, true);
                }

                if (_deviceOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_deviceOptionsProp, Styles.DeviceOptionsLabel, true);
                }

#if EASYMIC_APM_INTEGRATION
                if (_audioProcessingOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_audioProcessingOptionsProp, Styles.AudioProcessingLabel, true);
                }
#endif
            });
        }

        private void DrawAsrConfiguration()
        {
            DrawSection(Styles.AsrHeader, () =>
            {
                if (_asrConfigProp != null)
                {
                    EditorGUILayout.PropertyField(_asrConfigProp, Styles.AsrConfigLabel, true);
                }

                if (_voiceMic != null && _voiceMic.AsrConfig != null)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(Styles.ActivePresetLabel, _voiceMic.ActivePresetId);
                        EditorGUILayout.EnumPopup(Styles.RecognitionModeLabel, _voiceMic.AsrConfig.RecognitionMode);
                    }
                }
            });
        }

        private void DrawRuntimeStatus()
        {
            if (_voiceMic == null)
            {
                return;
            }

            DrawSection(Styles.RuntimeHeader, () =>
            {
                DrawStatusRow(Styles.InitializedLabel, () => _voiceMic.Initialized);
                DrawStatusRow(Styles.RecordingLabel, () => _voiceMic.IsRecording);
                DrawStatusRow(Styles.VoiceActiveLabel, () => _voiceMic.IsVoiceActivity);
                DrawStatusRow(Styles.SpeakingLabel, () => _voiceMic.IsSpeaking);
            });
        }

        private static void DrawSection(GUIContent title, System.Action body)
        {
            using (new EditorGUILayout.VerticalScope(Styles.SectionBox))
            {
                EditorGUILayout.LabelField(title, Styles.SectionHeader);
                EditorGUI.indentLevel++;
                body?.Invoke();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
        }

        private static void DrawStatusRow(GUIContent label, Func<bool> getter)
        {
            bool value = SafeState(getter);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, Styles.StatusLabel);
                GUILayout.Label(value ? Styles.StateOnContent : Styles.StateOffContent,
                    value ? Styles.StatusValueOn : Styles.StatusValueOff,
                    GUILayout.Width(Styles.StatusValueWidth));
            }
        }

        private static bool SafeState(Func<bool> getter)
        {
            try
            {
                return getter();
            }
            catch
            {
                return false;
            }
        }

        private static class Styles
        {
            public static readonly GUIStyle SectionBox;
            public static readonly GUIStyle SectionHeader;
            public static readonly GUIStyle StatusLabel;
            public static readonly GUIStyle StatusValueOn;
            public static readonly GUIStyle StatusValueOff;
            public const float StatusValueWidth = 70f;

            public static readonly GUIContent CaptureHeader = new GUIContent("Capture Settings");
            public static readonly GUIContent MicrophoneOptionsLabel = new GUIContent("Microphone Options");
            public static readonly GUIContent DeviceOptionsLabel = new GUIContent("Device Options");
#if EASYMIC_APM_INTEGRATION
            public static readonly GUIContent AudioProcessingLabel = new GUIContent("Audio Processing");
#endif
            public static readonly GUIContent AsrHeader = new GUIContent("ASR Configuration");
            public static readonly GUIContent AsrConfigLabel = new GUIContent("Configuration");
            public static readonly GUIContent ActivePresetLabel = new GUIContent("Active Preset");
            public static readonly GUIContent RecognitionModeLabel = new GUIContent("Recognition Mode");
            public static readonly GUIContent RuntimeHeader = new GUIContent("Runtime Status");
            public static readonly GUIContent InitializedLabel = new GUIContent("Initialized");
            public static readonly GUIContent RecordingLabel = new GUIContent("Recording");
            public static readonly GUIContent VoiceActiveLabel = new GUIContent("Voice Active");
            public static readonly GUIContent SpeakingLabel = new GUIContent("Speaking");
            public static readonly GUIContent StateOnContent = new GUIContent("TRUE");
            public static readonly GUIContent StateOffContent = new GUIContent("FALSE");

            static Styles()
            {
                SectionBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 12)
                };

                SectionHeader = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 0, 8)
                };

                StatusLabel = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold
                };

                StatusValueOn = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal =
                    {
                        textColor = new Color(0.1f, 0.6f, 0.2f)
                    },
                    fontStyle = FontStyle.Bold
                };

                StatusValueOff = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    normal =
                    {
                        textColor = new Color(0.55f, 0.55f, 0.55f)
                    }
                };
            }
        }
    }
}
#endif
