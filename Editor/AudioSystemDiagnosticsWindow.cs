using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Editor
{
    public class AudioSystemDiagnosticsWindow : EditorWindow
    {
        [MenuItem("Tools/EasyMic/AudioSystem Diagnostics")] 
        public static void ShowWindow()
        {
            GetWindow<AudioSystemDiagnosticsWindow>(false, "AudioSystem Diagnostics", true);
        }

        private Vector2 _scroll;

        private void OnGUI()
        {
            var sys = AudioSystem.Instance;
            GUILayout.Label($"Device: {_Safe(sys)}  SR={sys.SampleRate}  CH={sys.Channels}", EditorStyles.boldLabel);
            GUILayout.Label($"Backend: {sys.BackendName}   Device: {sys.DeviceName}");
            DrawMeters(sys);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawMixer(sys.MasterMixer, 0);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMeters(AudioSystem sys)
        {
            sys.GetMeters(out var peak, out var rms);
            if (peak.Length == 0) return;
            GUILayout.Label("Levels (Peak / RMS)");
            for (int ch = 0; ch < peak.Length; ch++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"Ch {ch}", GUILayout.Width(40));
                DrawBar(rms[ch], Color.green);
                DrawBar(peak[ch], Color.yellow);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawBar(float v, Color c)
        {
            Rect r = GUILayoutUtility.GetRect(100, 12, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.15f,0.15f,0.15f));
            var rw = new Rect(r.x, r.y, Mathf.Clamp01(v) * r.width, r.height);
            EditorGUI.DrawRect(rw, c);
        }

        private void DrawMixer(AudioMixer mixer, int indent)
        {
            if (mixer == null) { GUILayout.Label("<null mixer>"); return; }
            var pad = new string(' ', indent * 2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{pad}- Mixer {mixer.name}  Vol={mixer.MasterVolume:0.00}  Pipeline={mixer.Pipeline.WorkerCount} workers");
            mixer.Mute = EditorGUILayout.ToggleLeft("Mute", mixer.Mute, GUILayout.Width(60));
            mixer.Solo = EditorGUILayout.ToggleLeft("Solo", mixer.Solo, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            var children = mixer.GetChildren();
            for (int i = 0; i < children.Length; i++)
            {
                DrawMixer(children[i], indent + 1);
            }
            var sources = mixer.GetSources();
            for (int i = 0; i < sources.Length; i++)
            {
                var s = sources[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"{pad}- Source {s.name}: SR={s.SampleRate}, CH={s.Channels}, Q={s.QueuedSamples}");
                s.Mute = EditorGUILayout.ToggleLeft("Mute", s.Mute, GUILayout.Width(60));
                s.Solo = EditorGUILayout.ToggleLeft("Solo", s.Solo, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
            }
        }

        private static string _Safe(AudioSystem s) => (s != null).ToString();
    }
}
