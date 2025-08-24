using System.Globalization;
using Eitan.EasyMic.Runtime;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Editor
{
    [CustomEditor(typeof(PlaybackAudioSourceBehaviour))]
    public class PlaybackAudioSourceBehaviourEditor : UnityEditor.Editor
    {
        // Serialized fields
        private SerializedProperty _clipProp;
        private SerializedProperty _playOnAwakeProp;
        private SerializedProperty _loopProp;
        private SerializedProperty _volumeProp;
        private SerializedProperty _muteProp;

        private GUIStyle _headerStyle;

        [MenuItem("GameObject/Audio/Playback Audio Source", false, -1)]
        public static void AddPlaybackAudioSource()
        {
            var go = new UnityEngine.GameObject("Playback Audio Source");
            go.AddComponent<PlaybackAudioSourceBehaviour>();
            Undo.RegisterCreatedObjectUndo(go, "Create Playback Audio Source");

            // Select the newly created GameObject and start rename
            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }

        private void OnEnable()
        {
            _clipProp = serializedObject.FindProperty("_clip");
            _playOnAwakeProp = serializedObject.FindProperty("_playOnAwake");
            _loopProp = serializedObject.FindProperty("_loop");
            _volumeProp = serializedObject.FindProperty("_volume");
            _muteProp = serializedObject.FindProperty("_mute");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            var behaviour = (PlaybackAudioSourceBehaviour)target;

            serializedObject.Update();

            // Clip + playback options
            EditorGUILayout.PropertyField(_clipProp);
            EditorGUILayout.PropertyField(_playOnAwakeProp, new GUIContent("Play On Awake"));
            EditorGUILayout.PropertyField(_loopProp, new GUIContent("Loop"));
            EditorGUILayout.Space(4);

            // Level
            EditorGUILayout.LabelField("Level", _headerStyle);
            EditorGUILayout.Slider(_volumeProp, 0f, 2f, new GUIContent("Volume"));
            EditorGUILayout.PropertyField(_muteProp, new GUIContent("Mute"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawRuntimeSection(behaviour);
        }

        // Ensure live updates while playing without requiring mouse hover.
        public override bool RequiresConstantRepaint()
        {
            if (!Application.isPlaying)
            {
                return false;
            }


            var behaviour = target as PlaybackAudioSourceBehaviour;
            if (behaviour == null)
            {
                return false;
            }

            // Only repaint when this object is selected in the editor to save resources.
            // Unity will only repaint visible Inspector windows, so hidden inspectors won't update.

            var sel = Selection.gameObjects;
            if (sel == null || sel.Length == 0)
            {
                return false;
            }


            for (int i = 0; i < sel.Length; i++)
            {
                if (sel[i] == behaviour.gameObject)
                {
                    return true;
                }

            }
            return false;
        }

        // Gizmo drawing moved to a [DrawGizmo] class so it shows for selected and non-selected objects.

        private void DrawRuntimeSection(PlaybackAudioSourceBehaviour behaviour)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Runtime", _headerStyle);

                var src = behaviour.Source;
                bool hasSrc = src != null;
                bool playing = hasSrc && src.IsPlaying;
                float progress = behaviour.ProgressNormalized;
                double buffered = hasSrc ? src.BufferedSeconds : 0.0;

                // Status row
                using (new EditorGUILayout.HorizontalScope())
                {
                    StatusDot(playing ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.7f, 0.7f, 0.2f));
                    GUILayout.Label(playing ? "Playing" : "Paused/Idle", GUILayout.Width(90));
                    GUILayout.FlexibleSpace();
                    GUILayout.Label(hasSrc ? $"SR={src.SampleRate}" : "SR=-", GUILayout.Width(60));
                    GUILayout.Label(hasSrc ? $"CH={src.Channels}" : "CH=-", GUILayout.Width(60));
                    if (hasSrc)
                    {
                        GUILayout.Label($"Queue={src.QueuedSamples}", GUILayout.Width(100));
                        GUILayout.Label($"Free={src.FreeSamples}", GUILayout.Width(100));
                    }
                }

                // Progress bar
                Rect r = GUILayoutUtility.GetRect(18, 18);
                EditorGUI.ProgressBar(r, Mathf.Clamp01(progress), $"Progress  {(progress * 100f).ToString("0", CultureInfo.InvariantCulture)}%   Buffered ~{buffered:F3}s");
                // Time readout (elapsed / total if known)
                if (hasSrc)
                {
                    double elapsed = src.PlayedSourceFrames / (double)Mathf.Max(1, src.SampleRate);
                    double total = -1;
                    if (behaviour.Clip != null && behaviour.Clip.frequency > 0)
                    {
                        total = (double)behaviour.Clip.samples / System.Math.Max(1, behaviour.Clip.frequency);
                    }
                    GUILayout.Space(2);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"Time: {FormatTime(elapsed)}" + (total > 0 ? $" / {FormatTime(total)}" : string.Empty), EditorStyles.miniLabel);
                    }
                }
                GUILayout.Space(4);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    {
                        if (GUILayout.Button("Play"))
                        {
                            try { behaviour.Play(); } catch { }
                        }
                        if (GUILayout.Button("Pause"))
                        {
                            try { behaviour.Pause(); } catch { }
                        }
                        if (GUILayout.Button("Stop"))
                        {
                            try { behaviour.Stop(); } catch { }
                        }
                    }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Diagnostics"))
                    {
                        AudioSystemDiagnosticsWindow.ShowWindow();
                    }
                }
            }
        }

        private void StatusDot(Color c)
        {
            Rect rr = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            rr.y += 2;
            rr.height -= 4;
            rr.width = rr.height;
            EditorGUI.DrawRect(rr, c);
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0)
            {
                return "-";
            }


            int mins = (int)(seconds / 60.0);
            double rem = seconds - mins * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00.000}", mins, rem);
        }

        private void EnsureStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel);
            }
        }
    }
}
