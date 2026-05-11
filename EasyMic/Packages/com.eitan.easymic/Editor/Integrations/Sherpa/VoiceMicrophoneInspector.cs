#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using Eitan.EasyMic.Editor;
using Eitan.EasyMic.Editor.Icons;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Mono.Editor;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomEditor(typeof(VoiceMicrophone))]
    public sealed class VoiceMicrophoneInspector : EasyMicrophoneInspector
    {
        private VoiceMicrophone _voiceMic;
        private SerializedProperty _asrConfigProp;

        [MenuItem("GameObject/Audio/Input/Voice Microphone", false, -1)]
        public static void AddVoiceMicrophone()
        {
            var go = new UnityEngine.GameObject(EasyMicEditorLocalization.Text(EasyMicEditorTextKey.VoiceMenuGameObjectName));
            var voiceMic = go.AddComponent<VoiceMicrophone>();
            EasyMicComponentIconInstaller.ApplyTemporaryIcon(voiceMic);
            Undo.RegisterCreatedObjectUndo(go, EasyMicEditorLocalization.Text(EasyMicEditorTextKey.VoiceMenuCreate));

            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _voiceMic = (VoiceMicrophone)target;
            _asrConfigProp = serializedObject.FindProperty("_asrConfig");
        }

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        protected override void DrawAdditionalConfigurationSections()
        {
            if (_asrConfigProp != null)
            {
                EditorGUILayout.PropertyField(_asrConfigProp, GUIContent.none, true);
                EditorGUILayout.Space();
            }
        }

        protected override void DrawAdditionalRuntimeSections(EasyMicrophone mic)
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

            public static GUIContent RuntimeHeader => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceRuntimeHeader);
            public static GUIContent InitializedLabel => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceInitializedLabel);
            public static GUIContent RecordingLabel => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceRecordingLabel);
            public static GUIContent VoiceActiveLabel => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceVoiceActiveLabel);
            public static GUIContent SpeakingLabel => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceSpeakingLabel);
            public static GUIContent StateOnContent => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceTrueLabel);
            public static GUIContent StateOffContent => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.VoiceFalseLabel);

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
