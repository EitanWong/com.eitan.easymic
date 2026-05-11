#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Linq;
using System.Text;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    public sealed class SherpaIntegrationDiagnosticsWindow : EditorWindow
    {
        private const double RefreshIntervalSeconds = 1.0;

        private Vector2 _scroll;
        private double _nextRefresh;
        private SherpaRuntimeDiagnosticsSnapshot _snapshot;

        [MenuItem("Window/EasyMic/SherpaONNXUnity Diagnostics")]
        public static void ShowWindow()
        {
            GetWindow<SherpaIntegrationDiagnosticsWindow>(false, "SherpaONNXUnity Diagnostics", true);
        }

        private void OnEnable()
        {
            _nextRefresh = 0;
            RefreshSnapshot();
            EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
        }

        private void EditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRefresh)
            {
                return;
            }

            RefreshSnapshot();
            Repaint();
            _nextRefresh = now + RefreshIntervalSeconds;
        }

        private void OnGUI()
        {
            DrawToolbar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawInputs();
            DrawVoiceMicrophones();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    RefreshSnapshot();
                    Repaint();
                }

                if (GUILayout.Button("Copy Diagnostics", EditorStyles.toolbarButton, GUILayout.Width(130)))
                {
                    EditorGUIUtility.systemCopyBuffer = _snapshot.ToReport();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label("Refresh: 1 Hz", EditorStyles.miniLabel);
            }
        }

        private void DrawInputs()
        {
            EditorGUILayout.LabelField("EasyMic Sherpa Input Sources", EditorStyles.boldLabel);
            if (_snapshot.Inputs == null || _snapshot.Inputs.Length == 0)
            {
                EditorGUILayout.HelpBox("No EasyMicSherpaAudioInputSource was found in the loaded scenes.", MessageType.None);
                return;
            }

            for (int i = 0; i < _snapshot.Inputs.Length; i++)
            {
                var row = _snapshot.Inputs[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField("Component", row.Component, typeof(EasyMicSherpaAudioInputSource), true);
                    EditorGUILayout.LabelField("Capturing", row.IsCapturing ? "Yes" : "No");
                    EditorGUILayout.LabelField("Output Sample Rate", row.OutputSampleRate.ToString());
                    EditorGUILayout.LabelField("Chunk Duration", row.ChunkDurationSeconds.ToString("0.###"));
                    EditorGUILayout.LabelField("Listeners", row.ListenerCount.ToString());
                    EditorGUILayout.LabelField("Pending / Dropped", $"{row.PendingChunkCount} / {row.DroppedChunkCount}");
                    EditorGUILayout.LabelField("Format Mismatches", row.FormatMismatchCount.ToString());
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawVoiceMicrophones()
        {
            EditorGUILayout.LabelField("VoiceMicrophone", EditorStyles.boldLabel);
            if (_snapshot.VoiceMicrophones == null || _snapshot.VoiceMicrophones.Length == 0)
            {
                EditorGUILayout.HelpBox("No VoiceMicrophone was found in the loaded scenes.", MessageType.None);
                return;
            }

            for (int i = 0; i < _snapshot.VoiceMicrophones.Length; i++)
            {
                var row = _snapshot.VoiceMicrophones[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ObjectField("Component", row.Component, typeof(VoiceMicrophone), true);
                    EditorGUILayout.LabelField("Initialized", row.Initialized ? "Yes" : "No");
                    EditorGUILayout.LabelField("Recording", row.IsRecording ? "Yes" : "No");
                    EditorGUILayout.LabelField("Voice Active", row.IsVoiceActivity ? "Yes" : "No");
                    EditorGUILayout.LabelField("Speaking", row.IsSpeaking ? "Yes" : "No");
                }
            }
        }

        private void RefreshSnapshot()
        {
            _snapshot = SherpaRuntimeDiagnosticsSnapshot.Capture();
        }
    }

    internal readonly struct SherpaRuntimeDiagnosticsSnapshot
    {
        public readonly SherpaInputDiagnosticsRow[] Inputs;
        public readonly VoiceMicrophoneDiagnosticsRow[] VoiceMicrophones;

        private SherpaRuntimeDiagnosticsSnapshot(
            SherpaInputDiagnosticsRow[] inputs,
            VoiceMicrophoneDiagnosticsRow[] voiceMicrophones)
        {
            Inputs = inputs ?? Array.Empty<SherpaInputDiagnosticsRow>();
            VoiceMicrophones = voiceMicrophones ?? Array.Empty<VoiceMicrophoneDiagnosticsRow>();
        }

        public static SherpaRuntimeDiagnosticsSnapshot Capture()
        {
            var inputs = FindSceneObjects<EasyMicSherpaAudioInputSource>();
            var voiceMicrophones = FindSceneObjects<VoiceMicrophone>();
            var inputRows = new SherpaInputDiagnosticsRow[inputs.Length];
            var voiceRows = new VoiceMicrophoneDiagnosticsRow[voiceMicrophones.Length];

            for (int i = 0; i < inputs.Length; i++)
            {
                inputRows[i] = SherpaInputDiagnosticsRow.From(inputs[i]);
            }

            for (int i = 0; i < voiceMicrophones.Length; i++)
            {
                voiceRows[i] = VoiceMicrophoneDiagnosticsRow.From(voiceMicrophones[i]);
            }

            return new SherpaRuntimeDiagnosticsSnapshot(inputRows, voiceRows);
        }

        public string ToReport()
        {
            var builder = new StringBuilder(1024);
            builder.AppendLine("EasyMic SherpaONNXUnity Diagnostics");
            builder.AppendLine();
            builder.AppendLine("Input Sources:");
            for (int i = 0; i < Inputs.Length; i++)
            {
                builder.AppendLine(Inputs[i].ToReportLine());
            }

            builder.AppendLine();
            builder.AppendLine("VoiceMicrophones:");
            for (int i = 0; i < VoiceMicrophones.Length; i++)
            {
                builder.AppendLine(VoiceMicrophones[i].ToReportLine());
            }

            return builder.ToString();
        }

        private static T[] FindSceneObjects<T>()
            where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .Where(IsSceneObject)
                .ToArray();
        }

        private static bool IsSceneObject(Component component)
        {
            return component != null &&
                   component.gameObject.scene.IsValid() &&
                   !EditorUtility.IsPersistent(component) &&
                   (component.hideFlags & HideFlags.HideInHierarchy) == 0;
        }
    }

    internal readonly struct SherpaInputDiagnosticsRow
    {
        public readonly EasyMicSherpaAudioInputSource Component;
        public readonly bool IsCapturing;
        public readonly int OutputSampleRate;
        public readonly float ChunkDurationSeconds;
        public readonly int ListenerCount;
        public readonly int PendingChunkCount;
        public readonly int DroppedChunkCount;
        public readonly int FormatMismatchCount;

        private SherpaInputDiagnosticsRow(EasyMicSherpaAudioInputSource component)
        {
            Component = component;
            IsCapturing = component != null && component.IsCapturing;
            OutputSampleRate = component != null ? component.OutputSampleRate : 0;
            ChunkDurationSeconds = component != null ? component.ChunkDurationSeconds : 0f;
            ListenerCount = component != null ? component.ListenerCount : 0;
            PendingChunkCount = component != null ? component.PendingChunkCount : 0;
            DroppedChunkCount = component != null ? component.DroppedChunkCount : 0;
            FormatMismatchCount = component != null ? component.FormatMismatchCount : 0;
        }

        public static SherpaInputDiagnosticsRow From(EasyMicSherpaAudioInputSource component)
        {
            return new SherpaInputDiagnosticsRow(component);
        }

        public string ToReportLine()
        {
            string name = Component != null ? Component.name : "<missing>";
            return $"- {name}: capturing={IsCapturing}, listeners={ListenerCount}, pending={PendingChunkCount}, dropped={DroppedChunkCount}, mismatches={FormatMismatchCount}";
        }
    }

    internal readonly struct VoiceMicrophoneDiagnosticsRow
    {
        public readonly VoiceMicrophone Component;
        public readonly bool Initialized;
        public readonly bool IsRecording;
        public readonly bool IsVoiceActivity;
        public readonly bool IsSpeaking;

        private VoiceMicrophoneDiagnosticsRow(VoiceMicrophone component)
        {
            Component = component;
            Initialized = SafeRead(component, mic => mic.Initialized);
            IsRecording = SafeRead(component, mic => mic.IsRecording);
            IsVoiceActivity = SafeRead(component, mic => mic.IsVoiceActivity);
            IsSpeaking = SafeRead(component, mic => mic.IsSpeaking);
        }

        public static VoiceMicrophoneDiagnosticsRow From(VoiceMicrophone component)
        {
            return new VoiceMicrophoneDiagnosticsRow(component);
        }

        public string ToReportLine()
        {
            string name = Component != null ? Component.name : "<missing>";
            return $"- {name}: initialized={Initialized}, recording={IsRecording}, voiceActive={IsVoiceActivity}, speaking={IsSpeaking}";
        }

        private static bool SafeRead(VoiceMicrophone component, Func<VoiceMicrophone, bool> getter)
        {
            try
            {
                return component != null && getter(component);
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
