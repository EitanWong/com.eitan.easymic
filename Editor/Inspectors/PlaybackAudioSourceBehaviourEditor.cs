#if UNITY_EDITOR
using System.Globalization;
using Eitan.EasyMic.Editor.Icons;
using Eitan.EasyMic.Runtime.Mono.Components;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
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
        private GUIStyle _controlsPanelStyle;
        private GUIStyle _controlButtonStyle;
        private GUIStyle _diagnosticsButtonStyle;

        [MenuItem("GameObject/Audio/Playback Audio Source", false, -1)]
        public static void AddPlaybackAudioSource()
        {
            var go = new UnityEngine.GameObject("Playback Audio Source");
            var source = go.AddComponent<PlaybackAudioSourceBehaviour>();
            EasyMicComponentIconInstaller.ApplyTemporaryIcon(source);
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
                bool runtime = Application.isPlaying;
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

                DrawRuntimeControls(behaviour, runtime, playing);
            }
        }

        private void DrawRuntimeControls(PlaybackAudioSourceBehaviour behaviour, bool runtime, bool playing)
        {
            using (new EditorGUILayout.VerticalScope(_controlsPanelStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Controls", EditorStyles.miniBoldLabel, GUILayout.Width(58));
                    GUILayout.FlexibleSpace();
                    if (!runtime)
                    {
                        GUILayout.Label("Enter Play Mode", EditorStyles.miniLabel);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!runtime))
                    {
                        if (playing)
                        {
                            if (ControlButton("Pause", new Color(1f, 0.86f, 0.42f)))
                            {
                                try { behaviour.Pause(); } catch { }
                            }
                            if (ControlButton("Stop", new Color(1f, 0.55f, 0.5f)))
                            {
                                try { behaviour.Stop(); } catch { }
                            }
                        }
                        else
                        {
                            if (ControlButton("Play", new Color(0.5f, 0.88f, 0.58f)))
                            {
                                try { behaviour.Play(); } catch { }
                            }
                            if (ControlButton("Resume", new Color(0.52f, 0.72f, 1f)))
                            {
                                try { behaviour.Resume(); } catch { }
                            }
                            if (ControlButton("Stop", new Color(1f, 0.55f, 0.5f)))
                            {
                                try { behaviour.Stop(); } catch { }
                            }
                        }
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Diagnostics", _diagnosticsButtonStyle, GUILayout.Height(24), GUILayout.MinWidth(92)))
                    {
                        Runtime.Editor.AudioSystemDiagnosticsWindow.ShowWindow();
                    }
                }
            }
        }

        private bool ControlButton(string label, Color tint)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            bool clicked = GUILayout.Button(label, _controlButtonStyle, GUILayout.Height(26), GUILayout.MinWidth(72));
            GUI.backgroundColor = previous;
            return clicked;
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

            if (_controlsPanelStyle == null)
            {
                _controlsPanelStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(8, 8, 6, 7),
                    margin = new RectOffset(0, 0, 4, 2)
                };
            }

            if (_controlButtonStyle == null)
            {
                _controlButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 26,
                    margin = new RectOffset(2, 2, 2, 2),
                    padding = new RectOffset(10, 10, 3, 4)
                };
            }

            if (_diagnosticsButtonStyle == null)
            {
                _diagnosticsButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 24,
                    margin = new RectOffset(8, 0, 3, 2),
                    padding = new RectOffset(10, 10, 3, 4)
                };
            }
        }
    }
}
#endif
