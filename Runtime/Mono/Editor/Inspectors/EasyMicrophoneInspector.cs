#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{

    [CustomEditor(typeof(EasyMicrophone))]
    public class EasyMicrophoneInspector : UnityEditor.Editor
    {

        private EasyMicrophone _mic;

        private SerializedProperty _microphoneOptionsProp;
        private SerializedProperty _deviceOptionsProp;
        private SerializedProperty _maxDurationProp;
#if EASYMIC_APM_INTEGRATION
        private SerializedProperty _audioProcessingOptionsProp;
#endif

        private AudioClip _cachedClip;
        private UnityEditor.Editor _clipPreviewEditor;

        private void OnEnable()
        {
            _mic = (EasyMicrophone)target;

            _microphoneOptionsProp = serializedObject.FindProperty("_microphoneOptions");
            _deviceOptionsProp = serializedObject.FindProperty("_deviceOptions");
            _maxDurationProp = serializedObject.FindProperty("_maxRecordingDurationSeconds");
#if EASYMIC_APM_INTEGRATION
            _audioProcessingOptionsProp = serializedObject.FindProperty("_audioProcessingOptions");
#endif

            SubscribeToRuntimeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromRuntimeEvents();
            DisposeClipPreview();
        }

        [MenuItem("GameObject/Audio/Input/Easy Microphone", false, -1)]
        public static void AddEasyMicrophone()
        {
            var go = new GameObject("Easy Microphone");
            go.AddComponent<EasyMicrophone>();
            Undo.RegisterCreatedObjectUndo(go, "Create Easy Microphone");

            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawConfigurationSection();

            EditorGUILayout.Space(Styles.SectionSpacing);

            if (!Application.isPlaying)
            {
                DrawPlayModeNotice();
            }
            else
            {
                var mic = _mic;
                RefreshClipPreview(mic);

                DrawStatusSection(mic);
                EditorGUILayout.Space(Styles.SectionSpacing);

                DrawRecordingControls(mic);
                EditorGUILayout.Space(Styles.SectionSpacing);

                DrawRecordingPreview(mic);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPlayModeNotice()
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Runtime Controls", Styles.SectionHeader);
                EditorGUILayout.HelpBox("Enter Play Mode to monitor microphone status, control recording, and preview captured clips.", MessageType.Info);
            }
        }

        private void DrawStatusSection(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Status", Styles.SectionHeader);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusBadge(mic.Initialized ? "Initialized" : "Not Initialized", mic.Initialized);
                    DrawStatusBadge(mic.IsRecording ? "Recording" : "Idle", mic.IsRecording);
                }

                EditorGUILayout.Space(Styles.HeaderBodySpacing);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField("Available Devices", mic.AvailableDevices.Length);
                    if (_maxDurationProp != null)
                    {
                        EditorGUILayout.IntField("Max Recording Duration (s)", _maxDurationProp.intValue);
                    }
                }
            }
        }

        private void DrawConfigurationSection()
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Configuration", Styles.SectionHeader);

                if (_microphoneOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_microphoneOptionsProp);
                }

                if (_deviceOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_deviceOptionsProp);
                }

                if (_maxDurationProp != null)
                {
                    EditorGUILayout.PropertyField(_maxDurationProp, new GUIContent("Recording Duration (s)"));
                }
#if EASYMIC_APM_INTEGRATION
                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                EditorGUILayout.LabelField("Audio Processing", Styles.SectionHeader);
                if (_audioProcessingOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_audioProcessingOptionsProp);
                }
#endif
            }
        }

        private void DrawRecordingControls(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Recording Control", Styles.SectionHeader);

                if (!mic.Initialized && GUILayout.Button("Initialize", Styles.PrimaryButton))
                {
                    mic.Init();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(mic.IsRecording))
                    {
                        if (GUILayout.Button("Start Recording", Styles.PrimaryButton))
                        {
                            mic.StartRecording();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!mic.IsRecording))
                    {
                        if (GUILayout.Button("Stop Recording", Styles.PrimaryButton))
                        {
                            mic.StopRecording();
                        }
                    }
                }
            }
        }

        private void DrawRecordingPreview(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Recording Result", Styles.SectionHeader);

                var clip = mic.LatestRecordingClip;

                if (clip == null)
                {
                    EditorGUILayout.HelpBox("Stop recording to generate a preview clip.", MessageType.Info);
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Audio Clip", clip, typeof(AudioClip), false);
                }

                EditorGUILayout.LabelField("Length", $"{clip.length:F2} s", Styles.MutedLabel);
                EditorGUILayout.LabelField("Sample Rate", $"{clip.frequency} Hz", Styles.MutedLabel);
                EditorGUILayout.LabelField("Channels", clip.channels.ToString(), Styles.MutedLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Save As...", Styles.SecondaryButton))
                    {
                        SaveClipToDisk(mic, clip);
                    }
                }
            }
        }

        private void SaveClipToDisk(EasyMicrophone mic, AudioClip clip)
        {
            string directory = mic.LastSavedPath;
            if (string.IsNullOrEmpty(directory))
            {
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EasyMic");
            }
            else
            {
                directory = Path.GetDirectoryName(directory);
            }

            string defaultName = clip != null && !string.IsNullOrEmpty(clip.name) ? clip.name : $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}";
            string path = EditorUtility.SaveFilePanel("Save Recording", directory, defaultName, "wav");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (mic.TrySaveLatestRecording(path))
            {
                EditorUtility.DisplayDialog("EasyMic", "Recording saved successfully.", "OK");
                if (path.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                {
                    AssetDatabase.Refresh();
                }
            }
            else
            {
                EditorUtility.DisplayDialog("EasyMic", "Failed to save the recording. See the console for details.", "OK");
            }
        }

        public override bool HasPreviewGUI()
        {
            return Application.isPlaying && _cachedClip != null && _clipPreviewEditor != null;
        }

        public override GUIContent GetPreviewTitle()
        {
            return new GUIContent("Recording Preview");
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RefreshClipPreview(_mic);

            if (_clipPreviewEditor != null && _cachedClip != null)
            {
                _clipPreviewEditor.OnPreviewGUI(r, background);
            }
        }

        public override void OnPreviewSettings()
        {
            if (_clipPreviewEditor != null && _cachedClip != null)
            {
                _clipPreviewEditor.OnPreviewSettings();
            }
        }

        private void RefreshClipPreview(EasyMicrophone mic)
        {
            if (mic == null)
            {
                return;
            }

            var latestClip = mic.LatestRecordingClip;
            if (latestClip == _cachedClip)
            {
                return;
            }

            DisposeClipPreview();

            _cachedClip = latestClip;
            if (_cachedClip != null)
            {
                _clipPreviewEditor = UnityEditor.Editor.CreateEditor(_cachedClip);
            }
        }

        private void DisposeClipPreview()
        {
            _cachedClip = null;
            if (_clipPreviewEditor != null)
            {
                DestroyImmediate(_clipPreviewEditor);
                _clipPreviewEditor = null;
            }
        }

        private void SubscribeToRuntimeEvents()
        {
            if (_mic == null)
            {
                return;
            }

            _mic.OnRecordingStateChanged -= HandleRecordingStateChanged;
            _mic.OnRecordingStateChanged += HandleRecordingStateChanged;

            _mic.OnMicrophoneInitialized -= HandleMicrophoneInitialized;
            _mic.OnMicrophoneInitialized += HandleMicrophoneInitialized;
        }

        private void UnsubscribeFromRuntimeEvents()
        {
            if (_mic == null)
            {
                return;
            }

            _mic.OnRecordingStateChanged -= HandleRecordingStateChanged;
            _mic.OnMicrophoneInitialized -= HandleMicrophoneInitialized;
        }

        private void HandleRecordingStateChanged(bool _)
        {
            RefreshClipPreview(_mic);
            Repaint();
        }

        private void HandleMicrophoneInitialized(bool _)
        {
            Repaint();
        }

        private static void DrawStatusBadge(string label, bool active)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = active ? Styles.BadgeActiveColor : Styles.BadgeInactiveColor;
            GUILayout.Label(label, Styles.StatusBadge, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = previousColor;
        }

        private static class Styles
        {
            private static GUIStyle _section;
            private static GUIStyle _sectionHeader;
            private static GUIStyle _statusBadge;
            private static GUIStyle _mutedLabel;
            private static GUIStyle _secondaryButton;
            private static GUIStyle _primaryButton;

            public static GUIStyle Section
            {
                get
                {
                    if (_section == null)
                    {
                        _section = new GUIStyle("HelpBox")
                        {
                            padding = new RectOffset(12, 12, 10, 12),
                            margin = new RectOffset(0, 0, 0, 4)
                        };
                    }

                    return _section;
                }
            }

            public static GUIStyle SectionHeader
            {
                get
                {
                    if (_sectionHeader == null)
                    {
                        _sectionHeader = new GUIStyle(EditorStyles.boldLabel)
                        {
                            fontSize = 11,
                            fontStyle = FontStyle.Bold
                        };
                    }

                    return _sectionHeader;
                }
            }

            public static GUIStyle StatusBadge
            {
                get
                {
                    if (_statusBadge == null)
                    {
                        _statusBadge = new GUIStyle(EditorStyles.miniButtonMid)
                        {
                            padding = new RectOffset(10, 10, 3, 3),
                            fontSize = 10,
                            alignment = TextAnchor.MiddleCenter
                        };
                    }

                    return _statusBadge;
                }
            }

            public static GUIStyle MutedLabel
            {
                get
                {
                    if (_mutedLabel == null)
                    {
                        _mutedLabel = new GUIStyle(EditorStyles.label)
                        {
                            fontSize = 10,
                            normal =
                            {
                                textColor = EditorGUIUtility.isProSkin
                                    ? new Color(0.7f, 0.7f, 0.7f)
                                    : new Color(0.35f, 0.35f, 0.35f)
                            }
                        };
                    }

                    return _mutedLabel;
                }
            }

            public static GUIStyle SecondaryButton
            {
                get
                {
                    if (_secondaryButton == null)
                    {
                        _secondaryButton = new GUIStyle(EditorStyles.miniButton)
                        {
                            fixedHeight = 22,
                            padding = new RectOffset(12, 12, 2, 2)
                        };
                    }

                    return _secondaryButton;
                }
            }

            public static GUIStyle PrimaryButton
            {
                get
                {
                    if (_primaryButton == null)
                    {
                        _primaryButton = new GUIStyle(GUI.skin.button)
                        {
                            fixedHeight = 26,
                            fontStyle = FontStyle.Bold
                        };
                    }

                    return _primaryButton;
                }
            }

            public static float SectionSpacing => 8f;
            public static float HeaderBodySpacing => 4f;

            public static Color BadgeActiveColor => EditorGUIUtility.isProSkin ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.25f, 0.7f, 0.4f);
            public static Color BadgeInactiveColor => EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.75f, 0.75f, 0.75f);
        }
    }
}
#endif
