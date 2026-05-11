#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using Eitan.EasyMic;
using Eitan.EasyMic.Editor.Icons;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input;
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomEditor(typeof(EasyMicSherpaAudioInputSource))]
    public sealed class EasyMicSherpaAudioInputSourceInspector : UnityEditor.Editor
    {
        private SerializedProperty _preferredDeviceName;
        private SerializedProperty _outputSampleRate;
        private SerializedProperty _channel;
        private SerializedProperty _latencyProfile;
        private SerializedProperty _chunkDurationSeconds;
        private SerializedProperty _autoStartOnEnable;
        private SerializedProperty _stopOnDisable;
        private SerializedProperty _maxPendingChunks;
        private SerializedProperty _maxChunksPerUpdate;
        private SerializedProperty _onChunkReady;
        private SerializedProperty _onCaptureStateChanged;

        private void OnEnable()
        {
            _preferredDeviceName = serializedObject.FindProperty("preferredDeviceName");
            _outputSampleRate = serializedObject.FindProperty("outputSampleRate");
            _channel = serializedObject.FindProperty("channel");
            _latencyProfile = serializedObject.FindProperty("latencyProfile");
            _chunkDurationSeconds = serializedObject.FindProperty("chunkDurationSeconds");
            _autoStartOnEnable = serializedObject.FindProperty("autoStartOnEnable");
            _stopOnDisable = serializedObject.FindProperty("stopOnDisable");
            _maxPendingChunks = serializedObject.FindProperty("maxPendingChunks");
            _maxChunksPerUpdate = serializedObject.FindProperty("maxChunksPerUpdate");
            _onChunkReady = serializedObject.FindProperty("onChunkReady");
            _onCaptureStateChanged = serializedObject.FindProperty("onCaptureStateChanged");

            EasyMicComponentIconInstaller.ApplyTemporaryIcon((EasyMicSherpaAudioInputSource)target);
        }

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawConfigurationSettings();
            EditorGUILayout.Space(Styles.SectionSpacing);

            DrawSetupActions();
            EditorGUILayout.Space(Styles.SectionSpacing);

            DrawRuntimeDiagnostics();
            EditorGUILayout.Space(Styles.SectionSpacing);

            DrawEvents();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConfigurationSettings()
        {
            DrawSection(EasyMicEditorLocalization.Content(EasyMicEditorTextKey.EasyMicSectionConfiguration), () =>
            {
                DrawSubHeader(Styles.CaptureHeader);
                DrawPreferredDevicePopup();
                EditorGUILayout.PropertyField(_outputSampleRate, T(SherpaInputSourceEditorTextKey.OutputSampleRate));
                EditorGUILayout.PropertyField(_channel, T(SherpaInputSourceEditorTextKey.Channel));
                EditorGUILayout.PropertyField(_latencyProfile, T(SherpaInputSourceEditorTextKey.LatencyProfile));
                EditorGUILayout.PropertyField(_chunkDurationSeconds, T(SherpaInputSourceEditorTextKey.ChunkDuration));

                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                DrawSubHeader(Styles.LifecycleHeader);
                EditorGUILayout.PropertyField(_autoStartOnEnable, T(SherpaInputSourceEditorTextKey.AutoStartOnEnable));
                EditorGUILayout.PropertyField(_stopOnDisable, T(SherpaInputSourceEditorTextKey.StopOnDisable));

                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                DrawSubHeader(Styles.BackpressureHeader);
                EditorGUILayout.PropertyField(_maxPendingChunks, T(SherpaInputSourceEditorTextKey.MaxPendingChunks));
                EditorGUILayout.PropertyField(_maxChunksPerUpdate, T(SherpaInputSourceEditorTextKey.MaxChunksPerUpdate));
            });
        }

        private void DrawRuntimeDiagnostics()
        {
            DrawSection(Styles.RuntimeHeader, () =>
            {
                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox(Txt(SherpaInputSourceEditorTextKey.PlayModeNotice), MessageType.Info);
                    return;
                }

                var source = (EasyMicSherpaAudioInputSource)target;
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusBadge(
                        $"{Txt(SherpaInputSourceEditorTextKey.Capturing)}: {(source.IsCapturing ? Txt(SherpaInputSourceEditorTextKey.StateOn) : Txt(SherpaInputSourceEditorTextKey.StateOff))}",
                        source.IsCapturing);
                }

                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                DrawReadonlyInt(T(SherpaInputSourceEditorTextKey.Listeners), source.ListenerCount);
                DrawReadonlyInt(T(SherpaInputSourceEditorTextKey.OutputSampleRate), source.OutputSampleRate);
                DrawReadonlyFloat(T(SherpaInputSourceEditorTextKey.ChunkDuration), source.ChunkDurationSeconds);
                DrawReadonlyInt(T(SherpaInputSourceEditorTextKey.PendingChunks), source.PendingChunkCount);
                DrawReadonlyInt(T(SherpaInputSourceEditorTextKey.DroppedChunks), source.DroppedChunkCount);
                DrawReadonlyInt(T(SherpaInputSourceEditorTextKey.FormatMismatches), source.FormatMismatchCount);
                DrawReadonlyText(
                    T(SherpaInputSourceEditorTextKey.LastFormatMismatch),
                    string.IsNullOrEmpty(source.LastFormatMismatch)
                        ? Txt(SherpaInputSourceEditorTextKey.NoFormatMismatch)
                        : source.LastFormatMismatch);

                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(source.IsCapturing))
                    {
                        if (GUILayout.Button(T(SherpaInputSourceEditorTextKey.StartCapture), Styles.PrimaryButton))
                        {
                            source.TryStartCapture();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!source.IsCapturing))
                    {
                        if (GUILayout.Button(T(SherpaInputSourceEditorTextKey.ForceStop), Styles.Button))
                        {
                            source.ForceStopCapture();
                        }
                    }
                }
            });
        }

        private void DrawEvents()
        {
            DrawSection(Styles.EventsHeader, () =>
            {
                EditorGUILayout.PropertyField(_onChunkReady, T(SherpaInputSourceEditorTextKey.OnChunkReady));
                EditorGUILayout.PropertyField(_onCaptureStateChanged, T(SherpaInputSourceEditorTextKey.OnCaptureStateChanged));
            });
        }

        private void DrawSetupActions()
        {
            var source = (EasyMicSherpaAudioInputSource)target;
            DrawSection(Styles.SetupHeader, () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(T(SherpaInputSourceEditorTextKey.AddSherpaComponent), Styles.PrimaryButton))
                    {
                        ShowSherpaComponentMenu(source);
                    }

                    GUILayout.FlexibleSpace();
                }
            });
        }

        private void DrawPreferredDevicePopup()
        {
            EasyMicAPI.Refresh();

            if (!EasyMicAPI.IsAvailable)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrWhiteSpace(EasyMicAPI.UnavailabilityReason)
                        ? Txt(SherpaInputSourceEditorTextKey.BackendUnavailable)
                        : EasyMicAPI.UnavailabilityReason,
                    MessageType.Warning);
                return;
            }

            MicDevice[] devices = EasyMicAPI.Devices ?? Array.Empty<MicDevice>();
            if (devices.Length == 0)
            {
                EditorGUILayout.HelpBox(Txt(SherpaInputSourceEditorTextKey.NoDevicesDetected), MessageType.Warning);
                return;
            }

            string current = _preferredDeviceName.stringValue ?? string.Empty;
            int currentIndex = FindPreferredDevicePopupIndex(devices, current);
            string[] options = BuildDevicePopupOptions(devices);

            EditorGUI.BeginChangeCheck();
            int selectedIndex = EditorGUILayout.Popup(T(SherpaInputSourceEditorTextKey.PreferredDeviceName), currentIndex, options);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            string selectedDeviceName = selectedIndex <= 0 ? string.Empty : devices[selectedIndex - 1].Name ?? string.Empty;
            var source = (EasyMicSherpaAudioInputSource)target;
            bool restartCapture = Application.isPlaying && source.IsCapturing;

            Undo.RecordObject(source, "Change EasyMic Sherpa Device");
            _preferredDeviceName.stringValue = selectedDeviceName;
            serializedObject.ApplyModifiedProperties();

            source.SetPreferredDevice(selectedDeviceName);
            if (restartCapture)
            {
                source.ForceStopCapture();
                source.TryStartCapture();
            }

            EditorUtility.SetDirty(source);
        }

        private static int FindPreferredDevicePopupIndex(MicDevice[] devices, string preferredDeviceName)
        {
            if (string.IsNullOrWhiteSpace(preferredDeviceName))
            {
                return 0;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (string.Equals(devices[i].Name, preferredDeviceName, StringComparison.Ordinal) ||
                    string.Equals(devices[i].Name, preferredDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private static string[] BuildDevicePopupOptions(MicDevice[] devices)
        {
            var options = new string[devices.Length + 1];
            options[0] = Txt(SherpaInputSourceEditorTextKey.DefaultDevice);
            for (int i = 0; i < devices.Length; i++)
            {
                string name = string.IsNullOrWhiteSpace(devices[i].Name)
                    ? Txt(SherpaInputSourceEditorTextKey.UnnamedDevice)
                    : devices[i].Name;
                options[i + 1] = devices[i].IsDefault
                    ? $"{name} ({Txt(SherpaInputSourceEditorTextKey.DefaultDevice)})"
                    : name;
            }

            return options;
        }

        private static void ShowSherpaComponentMenu(EasyMicSherpaAudioInputSource source)
        {
            var menu = new GenericMenu();
            AddSherpaComponentMenuItem(
                menu,
                SherpaInputSourceEditorTextKey.CreateRealtimeAsr,
                () => SherpaSceneRecipeBuilder.AddOrBindStreamingComponent<RealtimeSpeechRecognizerComponent>(source));
            AddSherpaComponentMenuItem(
                menu,
                SherpaInputSourceEditorTextKey.CreateOfflineAsrFromVad,
                () => SherpaSceneRecipeBuilder.AddOrBindOfflineRecognizerWithVad(source));
            AddSherpaComponentMenuItem(
                menu,
                SherpaInputSourceEditorTextKey.CreateKeywordSpotter,
                () => SherpaSceneRecipeBuilder.AddOrBindStreamingComponent<KeywordSpottingComponent>(source));
            AddSherpaComponentMenuItem(
                menu,
                SherpaInputSourceEditorTextKey.CreateVad,
                () => SherpaSceneRecipeBuilder.AddOrBindStreamingComponent<VoiceActivityDetectionComponent>(source));
            AddSherpaComponentMenuItem(
                menu,
                SherpaInputSourceEditorTextKey.CreateAudioTagging,
                () => SherpaSceneRecipeBuilder.AddOrBindAudioTagging(source));
            menu.ShowAsContext();
        }

        private static void AddSherpaComponentMenuItem(GenericMenu menu, SherpaInputSourceEditorTextKey label, GenericMenu.MenuFunction action)
        {
            menu.AddItem(T(label), false, action);
        }

        private static void DrawSection(GUIContent title, System.Action body)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField(title, Styles.SectionHeader);
                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                body?.Invoke();
            }
        }

        private static void DrawSubHeader(GUIContent label)
        {
            EditorGUILayout.LabelField(label, Styles.SubHeader);
        }

        private static void DrawStatusBadge(GUIContent content, bool active)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = active ? Styles.BadgeActiveColor : Styles.BadgeInactiveColor;
            GUILayout.Label(content, Styles.StatusBadge, GUILayout.Width(108f));
            GUI.backgroundColor = previous;
        }

        private static void DrawStatusBadge(string text, bool active)
        {
            DrawStatusBadge(new GUIContent(text), active);
        }

        private static void DrawReadonlyInt(GUIContent label, int value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(label, value);
            }
        }

        private static void DrawReadonlyFloat(GUIContent label, float value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField(label, value);
            }
        }

        private static void DrawReadonlyText(GUIContent label, string value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, value);
            }
        }

        private static void DrawReadonlyText(GUIContent label, GUIContent value)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(label, value.text);
            }
        }

        private static GUIContent T(SherpaInputSourceEditorTextKey key)
        {
            return EasyMicEditorLocalization.SherpaInputSourceContent(key);
        }

        private static string Txt(SherpaInputSourceEditorTextKey key)
        {
            return EasyMicEditorLocalization.SherpaInputSourceText(key);
        }

        private static string Txt(SherpaInputSourceEditorTextKey key, params object[] args)
        {
            return EasyMicEditorLocalization.SherpaInputSourceText(key, args);
        }

        private static class Styles
        {
            public static readonly GUIStyle Section;
            public static readonly GUIStyle SectionHeader;
            public static readonly GUIStyle SubHeader;
            public static readonly GUIStyle Button;
            public static readonly GUIStyle PrimaryButton;
            public static readonly GUIStyle StatusBadge;

            public static GUIContent CaptureHeader => T(SherpaInputSourceEditorTextKey.SectionCapture);
            public static GUIContent LifecycleHeader => T(SherpaInputSourceEditorTextKey.SectionLifecycle);
            public static GUIContent BackpressureHeader => T(SherpaInputSourceEditorTextKey.SectionBackpressure);
            public static GUIContent RuntimeHeader => T(SherpaInputSourceEditorTextKey.SectionRuntime);
            public static GUIContent EventsHeader => T(SherpaInputSourceEditorTextKey.SectionEvents);
            public static GUIContent SetupHeader => T(SherpaInputSourceEditorTextKey.SectionSetupActions);

            static Styles()
            {
                Section = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 12),
                    margin = new RectOffset(0, 0, 0, 4)
                };

                SectionHeader = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };

                SubHeader = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    fontSize = 10,
                    normal =
                    {
                        textColor = EditorGUIUtility.isProSkin
                            ? new Color(0.72f, 0.72f, 0.72f)
                            : new Color(0.35f, 0.35f, 0.35f)
                    }
                };

                Button = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 22,
                    padding = new RectOffset(12, 12, 2, 2)
                };

                PrimaryButton = new GUIStyle(GUI.skin.button)
                {
                    fixedHeight = 26,
                    fontStyle = FontStyle.Bold
                };

                StatusBadge = new GUIStyle(EditorStyles.miniButtonMid)
                {
                    padding = new RectOffset(10, 10, 3, 3),
                    fontSize = 10,
                    alignment = TextAnchor.MiddleCenter,
                };
            }

            public static float SectionSpacing => 8f;
            public static float HeaderBodySpacing => 4f;
            public static Color BadgeActiveColor => EditorGUIUtility.isProSkin ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.25f, 0.7f, 0.4f);
            public static Color BadgeInactiveColor => EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.75f, 0.75f, 0.75f);
        }
    }
}
#endif
