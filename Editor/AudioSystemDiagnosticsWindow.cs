using System;
using System.Collections.Generic;
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

        // State/UI
        private Vector2 _mainScroll;
        private bool _showLevels = true;
        private bool _showSceneSources = true;
        private bool _showMixersGroup = true;
        private bool _showSceneGroup = true;
        private bool _autoRefresh = true;
        private float _refreshFps = 30f;
        private double _nextRepaint;

        private readonly Dictionary<object, bool> _foldouts = new Dictionary<object, bool>(ReferenceEqualityComparer<object>.Instance);

        // Styles/colors
        private GUIStyle _headerStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _colHeaderStyle;
        private GUIStyle _barTextStyle;
        private Color _bgBar = new Color(0.15f, 0.15f, 0.15f);
        private Color _rmsColor = new Color(0.3f, 0.8f, 0.3f);
        private Color _peakColor = new Color(1.0f, 0.9f, 0.2f);

        // Search/Filter
        private string _search = string.Empty;
        private UnityEditor.IMGUI.Controls.SearchField _searchField;
        private bool _filterShowMutedOnly = false;
        private bool _filterShowSoloOnly = false;
        private bool _filterShowActiveOnly = false; // active = has queued samples or not muted
        private bool _filterSoloPathOnly = false; // show only solo paths in tree/list
        private bool _muteNonSolo = false; // toolbar: mute all non-solo
        private readonly HashSet<object> _autoMuted = new HashSet<object>(ReferenceEqualityComparer<object>.Instance);

        // Scene sources cache for performance
        private PlaybackAudioSourceBehaviour[] _sceneSources = Array.Empty<PlaybackAudioSourceBehaviour>();
        private string[] _sceneSourcePaths = Array.Empty<string>();
        private bool _sceneDirty = true;
        // Stable snapshot for Layout/Repaint consistency
        private struct SceneRow
        {
            public PlaybackAudioSourceBehaviour behaviour;
            public PlaybackAudioSource source;
            public string path;
            public bool hasSrc;
            public int sr, ch, queue, free;
            public bool muted, solo, active;
        }
        private List<SceneRow> _sceneSnapshot = new List<SceneRow>(64);

        private void OnEnable()
        {
            EditorApplication.update += EditorUpdate;
            _nextRepaint = EditorApplication.timeSinceStartup;
            EditorApplication.hierarchyChanged += MarkSceneDirty;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            EditorApplication.hierarchyChanged -= MarkSceneDirty;
        }

        private void EditorUpdate()
        {
            if (!_autoRefresh) return;
            if (_refreshFps <= 1f) _refreshFps = 1f;
            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextRepaint)
            {
                Repaint();
                _nextRepaint = now + (1.0 / Math.Max(1.0, _refreshFps));
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            var sys = AudioSystem.Instance;
            DrawToolbar(sys);

            if (_showLevels)
            {
                DrawSectionHeader("Master Output");
                DrawMeters(sys);
            }

            DrawSectionHeader("Hierarchy");
            DrawSearchBar();
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);
            // Mixers group
            using (new EditorGUILayout.VerticalScope("box"))
            {
                _showMixersGroup = EditorGUILayout.Foldout(_showMixersGroup, "Audio Mixers", true);
                if (_showMixersGroup)
                {
                    DrawMixerTree(sys);
                }
            }
            // Scene group
            if (_showSceneSources)
            using (new EditorGUILayout.VerticalScope("box"))
            {
                _showSceneGroup = EditorGUILayout.Foldout(_showSceneGroup, "Scene Playback Sources", true);
                if (_showSceneGroup)
                {
                    DrawSceneColumnsHeader();
                    DrawSceneSources();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar(AudioSystem sys)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var running = sys != null && sys.IsRunning;
                GUILayout.Label(running ? "Running" : "Stopped", EditorStyles.toolbarButton, GUILayout.Width(70));
                GUILayout.Space(8);
                GUILayout.Label($"SR={(running ? sys.SampleRate.ToString() : "-")}", EditorStyles.miniLabel, GUILayout.Width(70));
                GUILayout.Label($"CH={(running ? sys.Channels.ToString() : "-")}", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.FlexibleSpace();
                _showLevels = GUILayout.Toggle(_showLevels, "Levels", EditorStyles.toolbarButton);
                _showSceneSources = GUILayout.Toggle(_showSceneSources, "Scene Sources", EditorStyles.toolbarButton);
                GUILayout.Space(8);
                _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto Refresh", EditorStyles.toolbarButton);
                using (new EditorGUI.DisabledScope(!_autoRefresh))
                {
                    GUILayout.Label("FPS", EditorStyles.miniLabel, GUILayout.Width(28));
                    _refreshFps = Mathf.Round(GUILayout.HorizontalSlider(_refreshFps, 5f, 60f, GUILayout.Width(80)));
                    GUILayout.Label(((int)_refreshFps).ToString(), EditorStyles.miniLabel, GUILayout.Width(24));
                }
            }

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                // AudioSystem control
                var sysRef = sys ?? AudioSystem.Instance;
                using (new EditorGUI.DisabledScope(sysRef == null))
                {
                    if (GUILayout.Button("Start", EditorStyles.toolbarButton, GUILayout.Width(60))) { try { sysRef.Start(); } catch { } }
                    if (GUILayout.Button("Stop", EditorStyles.toolbarButton, GUILayout.Width(60))) { try { sysRef.Stop(); } catch { } }
                    if (GUILayout.Button("PreferNative", EditorStyles.toolbarButton, GUILayout.Width(100))) { try { sysRef.PreferNativeFormat(); } catch { } }
                    GUILayout.Space(6);
                    GUILayout.Label("SR", GUILayout.Width(20));
                    int sr = (int)(sysRef?.SampleRate ?? 0);
                    string srStr = GUILayout.TextField(sr.ToString(), GUILayout.Width(60));
                    GUILayout.Label("CH", GUILayout.Width(20));
                    int ch = (int)(sysRef?.Channels ?? 0);
                    string chStr = GUILayout.TextField(ch.ToString(), GUILayout.Width(40));
                    if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(60)))
                    {
                        if (int.TryParse(srStr, out var nsr) && nsr > 0 && int.TryParse(chStr, out var nch) && nch > 0)
                        {
                            try { sysRef.Configure((uint)nsr, (uint)nch); } catch { }
                        }
                    }
                    GUILayout.Space(6);
                    bool nmns = GUILayout.Toggle(_muteNonSolo, "Mute Non-Solo", EditorStyles.toolbarButton, GUILayout.Width(120));
                    if (nmns != _muteNonSolo)
                    {
                        _muteNonSolo = nmns;
                        ApplyMuteAllNonSolo(_muteNonSolo);
                    }
                }
                GUILayout.FlexibleSpace();
                // Tree actions
                if (GUILayout.Button("Expand All", EditorStyles.toolbarButton, GUILayout.Width(90))) ExpandCollapseAll(true);
                if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton, GUILayout.Width(100))) ExpandCollapseAll(false);
                _filterSoloPathOnly = GUILayout.Toggle(_filterSoloPathOnly, "Solo Path Only", EditorStyles.toolbarButton, GUILayout.Width(120));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var backend = sys?.BackendName ?? "-";
                var device = sys?.DeviceName ?? "-";
                EditorGUILayout.LabelField($"Backend: {backend}", _headerStyle);
                EditorGUILayout.LabelField($"Device: {device}", _headerStyle);
            }
        }

        private void DrawMixerTree(AudioSystem sys)
        {
            var mix = sys?.MasterMixer;
            if (mix == null)
            {
                EditorGUILayout.HelpBox("AudioSystem not running or Master mixer unavailable.", MessageType.Info);
                return;
            }
            DrawMixer(mix, 0);
        }

        private void DrawMixer(AudioMixer mixer, int indent)
        {
            if (mixer == null) { GUILayout.Label("<null mixer>"); return; }
            // If search/filter hides this mixer and all its descendants, skip drawing entirely
            if (!MixerMatchesOrHasMatches(mixer)) return;
            using (new EditorGUI.IndentLevelScope(indent))
            using (new EditorGUILayout.VerticalScope("box"))
            {
                var headerRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
                bool open = GetFoldout(mixer);
                open = EditorGUI.Foldout(new Rect(headerRect.x, headerRect.y, 14, headerRect.height), open, GUIContent.none);
                SetFoldout(mixer, open);

                string title = $"Mixer  {mixer.name}    Vol={mixer.MasterVolume:0.00}    Pipeline={mixer.Pipeline.WorkerCount}";
                GUI.Label(new Rect(headerRect.x + 14, headerRect.y, headerRect.width - 160, headerRect.height), title, _sectionTitleStyle);

                var rectRight = new Rect(headerRect.xMax - 150, headerRect.y, 150, headerRect.height);
                using (new GUILayout.AreaScope(rectRight))
                using (new EditorGUILayout.HorizontalScope())
                {
                    mixer.Mute = GUILayout.Toggle(mixer.Mute, "Mute", "Button", GUILayout.Width(60));
                    mixer.Solo = GUILayout.Toggle(mixer.Solo, "Solo", "Button", GUILayout.Width(60));
                }

                GUILayout.Space(2);

                if (open)
                {
                    var children = mixer.GetChildren();
                    for (int i = 0; i < children.Length; i++) DrawMixer(children[i], indent + 1);

                    var sources = mixer.GetSources();
                    for (int i = 0; i < sources.Length; i++)
                    {
                        var s = sources[i];
                        if (s == null) continue;
                        bool active = s.QueuedSamples > 0 || (!s.Mute && s.Volume > 0f);
                        if (PassesFilter(s.name, false, s.Mute, s.Solo, active))
                            DrawSourceRow(s, indent + 1);
                    }
                }
            }
        }

        private void DrawSourceRow(PlaybackAudioSource s, int indent)
        {
            if (s == null) return;
            using (new EditorGUI.IndentLevelScope(indent))
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                GUILayout.Label($"Source  {s.name}", GUILayout.Width(160));
                GUILayout.Label($"SR={s.SampleRate}", GUILayout.Width(70));
                GUILayout.Label($"CH={s.Channels}", GUILayout.Width(60));
                GUILayout.Label($"Queue={s.QueuedSamples}", GUILayout.Width(100));
                // Per-source audio meters (RMS/Peak)
                s.GetMeters(out var spk, out var srms);
                DrawCompactMeters(srms, spk, 2);
                GUILayout.Space(8);
                // Volume column
                GUILayout.Label("Vol", GUILayout.Width(26));
                float nv = GUILayout.HorizontalSlider(s.Volume, 0f, 2f, GUILayout.Width(80));
                if (Mathf.Abs(nv - s.Volume) > 1e-4f) s.Volume = nv;
                GUILayout.Space(4);
                GUILayout.Label($"Stages={s.Pipeline.WorkerCount}", GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                s.Mute = GUILayout.Toggle(s.Mute, "Mute", "Button", GUILayout.Width(60));
                s.Solo = GUILayout.Toggle(s.Solo, "Solo", "Button", GUILayout.Width(60));
            }
        }

        private void DrawSceneSources()
        {
            if (_sceneDirty || _sceneSources.Length == 0)
            {
                RebuildSceneCache();
            }

            // Stable snapshot for this OnGUI pass: build only during Layout, reuse during Repaint
            if (Event.current.type == EventType.Layout)
            {
                _sceneSnapshot.Clear();
                var behaviours = _sceneSources;
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (b == null) { _sceneDirty = true; continue; }
                    var src = b.Source;
                    var row = new SceneRow
                    {
                        behaviour = b,
                        source = src,
                        path = (i < _sceneSourcePaths.Length) ? _sceneSourcePaths[i] : GetHierarchyPath(b.transform),
                        hasSrc = src != null,
                        sr = src?.SampleRate ?? 0,
                        ch = src?.Channels ?? 0,
                        queue = src?.QueuedSamples ?? 0,
                        free = src?.FreeSamples ?? 0,
                        muted = src?.Mute ?? false,
                        solo = src?.Solo ?? false,
                        active = src != null && (src.QueuedSamples > 0 || (!src.Mute && src.Volume > 0f))
                    };
                    // Apply filters to snapshot selection
                    if (PassesFilter(row.path, false, row.muted, row.solo, row.active))
                    {
                        if (!_filterSoloPathOnly || row.solo) // solo path filter for sources: require solo
                            _sceneSnapshot.Add(row);
                    }
                }
            }

            if (_sceneSnapshot.Count == 0)
            {
                EditorGUILayout.HelpBox("No PlaybackAudioSourceBehaviour found (or filtered).", MessageType.None);
                return;
            }

            const float rowH = 22f;
            for (int i = 0; i < _sceneSnapshot.Count; i++)
            {
                var row = _sceneSnapshot[i];
                Rect rr = GUILayoutUtility.GetRect(0, rowH, GUILayout.ExpandWidth(true));
                DrawSceneRow(rr, row, i);
            }
        }

        private void DrawSceneRow(Rect r, SceneRow row, int index)
        {
            // background (zebra)
            if ((index & 1) == 0) EditorGUI.DrawRect(r, new Color(1,1,1,0.03f));
            // column layout
            float x = r.x + 4f;
            const float pingW = 24f, statusW = 10f, gap = 6f, srW = 56f, chW = 40f, queueW = 80f, meterW = 120f, volLabelW = 24f, volSliderW = 80f, muteW = 50f, soloW = 50f;
            // Ping button
            var go = row.behaviour ? row.behaviour.gameObject : null;
            var pingRect = new Rect(x, r.y + 2, pingW, r.height - 4);
            if (GUI.Button(pingRect, EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow")))
            {
                if (go != null) { EditorGUIUtility.PingObject(go); Selection.activeObject = go; }
            }
            x += pingW + 4f;
            // status dot
            var dot = new Rect(x, r.y + (r.height-8f)*0.5f, statusW, 8f);
            Color dc = row.active ? new Color(0.2f,0.9f,0.2f) : (row.muted ? new Color(0.5f,0.5f,0.5f) : new Color(0.8f,0.8f,0.2f));
            EditorGUI.DrawRect(new Rect(dot.x, dot.y, 8f, 8f), dc);
            x += statusW + gap;
            // name/path
            float fixedTail = srW + chW + queueW + meterW + volLabelW + volSliderW + muteW + soloW + (gap*8) + 6f;
            float nameW = Mathf.Max(60f, r.width - (x - r.x) - fixedTail);
            var nameRect = new Rect(x, r.y + 2, nameW, r.height - 4);
            GUI.Label(nameRect, row.path);
            x += nameW + gap;
            // SR / CH / Queue
            GUI.Label(new Rect(x, r.y + 2, srW, r.height - 4), row.hasSrc ? $"SR={row.sr}" : "SR=-"); x += srW + gap;
            GUI.Label(new Rect(x, r.y + 2, chW, r.height - 4), row.hasSrc ? $"CH={row.ch}" : "CH=-"); x += chW + gap;
            GUI.Label(new Rect(x, r.y + 2, queueW, r.height - 4), row.hasSrc ? $"Queue={row.queue}" : "Queue=-"); x += queueW + gap;
            // meters
            var meterRect = new Rect(x, r.y + 5, meterW, r.height - 10);
            if (row.hasSrc && row.source != null)
            {
                row.source.GetMeters(out var pk, out var rm);
                float p = 0f, m = 0f;
                if (pk != null) for (int i = 0; i < pk.Length; i++) if (pk[i] > p) p = pk[i];
                if (rm != null) for (int i = 0; i < rm.Length; i++) if (rm[i] > m) m = rm[i];
                DrawStyledMeter(meterRect, m, p, -60f);
            }
            else
            {
                EditorGUI.DrawRect(meterRect, new Color(0.1f,0.1f,0.1f));
            }
            x += meterW + gap;
            // Volume slider
            GUI.Label(new Rect(x, r.y + 2, volLabelW, r.height - 4), "Vol"); x += volLabelW + 2f;
            if (row.hasSrc && row.source != null)
            {
                float nv = GUI.HorizontalSlider(new Rect(x, r.y + (r.height-12f)*0.5f, volSliderW, 12f), row.source.Volume, 0f, 2f);
                if (Mathf.Abs(nv - row.source.Volume) > 1e-4f) row.source.Volume = nv;
            }
            x += volSliderW + gap;
            // Mute / Solo
            if (row.hasSrc && row.source != null)
            {
                bool nm = GUI.Toggle(new Rect(x, r.y + 2, muteW, r.height - 4), row.muted, "Mute", "Button");
                if (nm != row.muted) { row.source.Mute = nm; }
                x += muteW + gap;
                bool ns = GUI.Toggle(new Rect(x, r.y + 2, soloW, r.height - 4), row.solo, "Solo", "Button");
                if (ns != row.solo) { row.source.Solo = ns; }
            }

            // Context menu
            var e = Event.current;
            if (e.type == EventType.ContextClick && r.Contains(e.mousePosition))
            {
                e.Use();
                ShowSceneRowContextMenu(row);
            }
        }

        private void ShowSceneRowContextMenu(SceneRow row)
        {
            var menu = new GenericMenu();
            var go = row.behaviour ? row.behaviour.gameObject : null;
            menu.AddItem(new GUIContent("Ping"), false, () => { if (go) EditorGUIUtility.PingObject(go); });
            menu.AddItem(new GUIContent("Select"), false, () => { if (go) Selection.activeObject = go; });
            menu.AddItem(new GUIContent("Reveal in Hierarchy"), false, () => {
                EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
                if (go) { EditorGUIUtility.PingObject(go); Selection.activeObject = go; }
            });
            if (row.hasSrc && row.source != null)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Isolate Solo"), false, () => IsolateSolo(row.source));
                menu.AddItem(new GUIContent("Mute Others"), false, () => MuteOthers(row.source));
            }
            menu.ShowAsContext();
        }

        private void IsolateSolo(PlaybackAudioSource target)
        {
            var master = AudioSystem.Instance?.MasterMixer;
            if (master == null || target == null) return;
            TraverseMixer(master, m => m.Solo = false, s => s.Solo = ReferenceEquals(s, target));
        }

        private void MuteOthers(PlaybackAudioSource target)
        {
            var master = AudioSystem.Instance?.MasterMixer;
            if (master == null || target == null) return;
            TraverseMixer(master, m => { }, s => { if (!ReferenceEquals(s, target)) s.Mute = true; });
        }

        private void DrawSearchBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_searchField == null) _searchField = new UnityEditor.IMGUI.Controls.SearchField();
                _search = _searchField.OnToolbarGUI(_search);
                GUILayout.Space(6);
                _filterShowActiveOnly = GUILayout.Toggle(_filterShowActiveOnly, new GUIContent("Active"), EditorStyles.miniButtonLeft, GUILayout.Width(60));
                _filterShowMutedOnly = GUILayout.Toggle(_filterShowMutedOnly, new GUIContent("Muted"), EditorStyles.miniButtonMid, GUILayout.Width(60));
                _filterShowSoloOnly = GUILayout.Toggle(_filterShowSoloOnly, new GUIContent("Solo"), EditorStyles.miniButtonRight, GUILayout.Width(60));
            }
        }

        private void DrawSceneColumnsHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Object", _colHeaderStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label("SR", _colHeaderStyle, GUILayout.Width(70));
                GUILayout.Label("CH", _colHeaderStyle, GUILayout.Width(60));
                GUILayout.Label("Queue", _colHeaderStyle, GUILayout.Width(100));
            }
        }

        private bool PassesFilter(string name, bool isMixer, bool muted, bool solo, bool active = false)
        {
            if (!PassesText(name)) return false;
            if (_filterShowMutedOnly && !muted) return false;
            if (_filterShowSoloOnly && !solo) return false;
            if (_filterShowActiveOnly && !active) return false;
            return true;
        }

        private bool PassesText(string name)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            return name != null && name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MixerMatchesOrHasMatches(AudioMixer m)
        {
            if (m == null) return false;
            // If text matches name, it's a match regardless of other toggles (toggles apply to sources)
            if (PassesText(m.name)) return true;
            if (_filterSoloPathOnly && !m.HasSoloRecursive()) return false;
            // Check sources with full filters
            var sources = m.GetSources();
            for (int i = 0; i < sources.Length; i++)
            {
                var s = sources[i];
                if (s == null) continue;
                bool active = s.QueuedSamples > 0 || (!s.Mute && s.Volume > 0f);
                if (PassesFilter(s.name, false, s.Mute, s.Solo, active)) return true;
            }
            // Check children recursively
            var children = m.GetChildren();
            for (int i = 0; i < children.Length; i++) if (MixerMatchesOrHasMatches(children[i])) return true;
            return false;
        }

        private void ExpandCollapseAll(bool expand)
        {
            var sys = AudioSystem.Instance;
            var master = sys?.MasterMixer;
            if (master == null) return;
            SetAllFoldouts(master, expand);
        }

        private void SetAllFoldouts(AudioMixer m, bool value)
        {
            if (m == null) return;
            SetFoldout(m, value);
            var children = m.GetChildren();
            for (int i = 0; i < children.Length; i++) SetAllFoldouts(children[i], value);
        }

        private void ApplyMuteAllNonSolo(bool enable)
        {
            var sys = AudioSystem.Instance;
            var master = sys?.MasterMixer;
            if (master == null) return;
            if (enable)
            {
                // Mute every source/mixer that is not Solo
                TraverseMixer(master, m =>
                {
                    if (!m.Solo && !m.Mute)
                    {
                        m.Mute = true; _autoMuted.Add(m);
                    }
                }, s =>
                {
                    if (!s.Solo && !s.Mute)
                    {
                        s.Mute = true; _autoMuted.Add(s);
                    }
                });
            }
            else
            {
                // Unmute only those we auto-muted previously
                foreach (var o in _autoMuted)
                {
                    if (o is AudioMixer mm) mm.Mute = false;
                    else if (o is PlaybackAudioSource ss) ss.Mute = false;
                }
                _autoMuted.Clear();
            }
        }

        private void TraverseMixer(AudioMixer m, Action<AudioMixer> onMixer, Action<PlaybackAudioSource> onSource)
        {
            if (m == null) return;
            onMixer?.Invoke(m);
            var sources = m.GetSources();
            for (int i = 0; i < sources.Length; i++) if (sources[i] != null) onSource?.Invoke(sources[i]);
            var children = m.GetChildren();
            for (int i = 0; i < children.Length; i++) TraverseMixer(children[i], onMixer, onSource);
        }

        private void MarkSceneDirty()
        {
            _sceneDirty = true;
        }

        private void RebuildSceneCache()
        {
            var list = UnityEngine.Object.FindObjectsOfType<PlaybackAudioSourceBehaviour>(true);
            if (list == null) { _sceneSources = Array.Empty<PlaybackAudioSourceBehaviour>(); _sceneSourcePaths = Array.Empty<string>(); _sceneDirty = false; return; }
            Array.Sort(list, (a, b) => string.Compare(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform), StringComparison.Ordinal));
            _sceneSources = list;
            _sceneSourcePaths = new string[list.Length];
            for (int i = 0; i < list.Length; i++) _sceneSourcePaths[i] = GetHierarchyPath(list[i].transform);
            _sceneDirty = false;
        }

        private void DrawMeters(AudioSystem sys)
        {
            sys.GetMeters(out var peak, out var rms);
            if (peak.Length == 0) return;
            DrawOutputMeters(rms, peak, minDb: -60f);
        }

        private void DrawBar(float v, Color c, string label)
        {
            Rect r = GUILayoutUtility.GetRect(100, 14, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, _bgBar);
            float clamped = Mathf.Clamp01(v);
            var rw = new Rect(r.x, r.y, clamped * r.width, r.height);
            EditorGUI.DrawRect(rw, c);
            var text = $"{label} {LinearToDb(v):0.0} dB";
            if (_barTextStyle == null) _barTextStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUI.DropShadowLabel(r, text, _barTextStyle);
        }

        private void DrawMiniBar(float v, Color c)
        {
            Rect r = GUILayoutUtility.GetRect(80, 10, GUILayout.Width(80));
            EditorGUI.DrawRect(r, _bgBar);
            float clamped = Mathf.Clamp01(v);
            var rw = new Rect(r.x, r.y, clamped * r.width, r.height);
            EditorGUI.DrawRect(rw, c);
        }

        // Render compact per-source meters, up to maxChannels bars
        private void DrawCompactMeters(float[] rms, float[] peak, int maxChannels)
        {
            if (rms == null || peak == null) return;
            int n = Mathf.Min(Mathf.Min(rms.Length, peak.Length), Mathf.Max(1, maxChannels));
            for (int ch = 0; ch < n; ch++)
            {
                float rv = ch < rms.Length ? rms[ch] : 0f;
                float pv = ch < peak.Length ? peak[ch] : 0f;
                var rect = GUILayoutUtility.GetRect(90, 10, GUILayout.Width(90));
                DrawStyledMeter(rect, rv, pv, -60f);
                GUILayout.Space(2);
            }
        }

        private void DrawOutputMeters(float[] rms, float[] peak, float minDb)
        {
            int count = Mathf.Min(rms.Length, peak.Length);
            const float barHeight = 16f;
            for (int ch = 0; ch < count; ch++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label((count == 2 ? (ch == 0 ? "L" : "R") : $"Ch {ch}"), GUILayout.Width(26));
                    var rect = GUILayoutUtility.GetRect(0, barHeight, GUILayout.ExpandWidth(true));
                    DrawStyledMeter(rect, rms[ch], peak[ch], minDb);
                }
            }
            // scale legend
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("-60", EditorStyles.miniLabel, GUILayout.Width(30));
                GUILayout.Label("-12", EditorStyles.miniLabel, GUILayout.Width(30));
                GUILayout.Label("-6", EditorStyles.miniLabel, GUILayout.Width(30));
                GUILayout.Label("0 dB", EditorStyles.miniLabel, GUILayout.Width(40));
            }
        }

        private void DrawStyledMeter(Rect r, float rmsLin, float peakLin, float minDb)
        {
            // Background
            EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.12f));
            // Grid/ticks
            DrawTicks(r, minDb);

            float rmsDb = LinearToDb(rmsLin);
            float peakDb = LinearToDb(peakLin);
            float rmsN = DbToNorm(rmsDb, minDb);
            float peakN = DbToNorm(peakDb, minDb);

            // RMS fill with color zones
            var rmsRect = new Rect(r.x, r.y, Mathf.Clamp01(rmsN) * r.width, r.height);
            EditorGUI.DrawRect(rmsRect, MeterColor(rmsDb));

            // Peak marker line
            float px = r.x + Mathf.Clamp01(peakN) * r.width;
            var peakRect = new Rect(px - 1, r.y, 2, r.height);
            EditorGUI.DrawRect(peakRect, _peakColor);

            // Clip indicator
            if (peakDb >= -0.1f)
            {
                var clipRect = new Rect(r.xMax - 6, r.y, 6, r.height);
                EditorGUI.DrawRect(clipRect, Color.red);
            }
        }

        private void DrawTicks(Rect r, float minDb)
        {
            // Draw ticks at -12, -6, 0 dB
            float[] marks = new float[] { -12f, -6f, 0f };
            var col = new Color(1, 1, 1, 0.08f);
            for (int i = 0; i < marks.Length; i++)
            {
                float n = DbToNorm(marks[i], minDb);
                float x = r.x + Mathf.Clamp01(n) * r.width;
                EditorGUI.DrawRect(new Rect(x, r.y, 1, r.height), col);
            }
        }

        private void EnsureStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            if (_sectionTitleStyle == null)
            {
                _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }
            if (_colHeaderStyle == null)
            {
                _colHeaderStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField(title, _sectionTitleStyle);
            GUILayout.Space(2);
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "<null>";
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack);
        }

        private static string _Safe(AudioSystem s) => (s != null).ToString();

        private static float LinearToDb(float lin)
        {
            lin = Mathf.Max(lin, 1e-9f);
            return 20f * Mathf.Log10(lin);
        }

        private static float DbToNorm(float db, float minDb)
        {
            if (float.IsNaN(db) || float.IsInfinity(db)) return 0f;
            if (db <= minDb) return 0f;
            if (db >= 0f) return 1f;
            return (db - minDb) / (0f - minDb);
        }

        private static Color MeterColor(float db)
        {
            if (db >= -3f) return new Color(0.9f, 0.2f, 0.2f); // red near clip
            if (db >= -9f) return new Color(0.95f, 0.8f, 0.2f); // yellow
            return new Color(0.3f, 0.8f, 0.3f); // green
        }

        private bool GetFoldout(object key)
        {
            if (key == null) return true;
            if (_foldouts.TryGetValue(key, out var v)) return v;
            _foldouts[key] = true;
            return true;
        }

        private void SetFoldout(object key, bool open)
        {
            if (key == null) return;
            _foldouts[key] = open;
        }
    }

    // Reference equality comparer for foldout dictionary keys (mixers/sources)
    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
