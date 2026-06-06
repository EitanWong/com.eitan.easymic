using System;
using UnityEngine;
using System.Collections.Generic;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class PipelineDebugPanel : MonoBehaviour
    {
        [SerializeField] private KeyCode _toggleKey = KeyCode.F12;
        [SerializeField] private int _maxDisplayRounds = 20;
        private Rect _windowRect = new Rect(30, 40, 620, 340);

        private PipelineDebugTracker _tracker;
        private bool _visible;
        private bool _showHistory;
        private Vector2 _historyScroll;

        // Cached textures
        private Texture2D _bgTex;
        private Texture2D _barBg;
        private Dictionary<Color, Texture2D> _barColorCache;

        private GUIStyle _headerStyle;
        private GUIStyle _bgStyle;

        // ── Color scheme (VS Code dark theme) ──
        private static readonly Color BgColor    = new Color(0.15f, 0.15f, 0.18f, 0.97f);
        private static readonly Color BarBgColor = new Color(0.28f, 0.28f, 0.35f);
        private static readonly Color AsrColor   = new Color(0.30f, 0.70f, 1.00f);
        private static readonly Color LlmColor   = new Color(1.00f, 0.60f, 0.10f);
        private static readonly Color TtsColor   = new Color(0.20f, 0.90f, 0.40f);
        private static readonly Color TotalColor = new Color(0.55f, 0.55f, 0.65f);
        private static readonly Color E2eColor   = new Color(0.85f, 0.40f, 0.85f);
        private static readonly Color Green      = new Color(0.20f, 0.85f, 0.35f);
        private static readonly Color Yellow     = new Color(1.00f, 0.85f, 0.20f);
        private static readonly Color Gray       = new Color(0.60f, 0.60f, 0.65f);
        private static readonly Color RedN       = new Color(1.00f, 0.30f, 0.30f);
        private static readonly Color HeaderLabelColor = new Color(0.6f, 0.6f, 0.7f);

        private void Awake()
        {
            _bgTex = MakeTex(BgColor);
            _barBg = MakeTex(BarBgColor);
            _barColorCache = new Dictionary<Color, Texture2D>();
        }

        private void Start()
        {
            TryFindTracker();
        }

        private void OnDestroy()
        {
            if (_bgTex != null) Destroy(_bgTex);
            if (_barBg != null) Destroy(_barBg);
            if (_barColorCache != null)
            {
                foreach (var tex in _barColorCache.Values)
                {
                    if (tex != null) Destroy(tex);
                }
                _barColorCache.Clear();
            }
        }

        private void TryFindTracker()
        {
            if (_tracker != null) return;
            var controller = FindObjectOfType<AIChatController>();
            if (controller != null)
                _tracker = controller.LatencyTracker;
            if (_tracker == null)
            {
                var allControllers = FindObjectsByType<AIChatController>(FindObjectsSortMode.None);
                foreach (var c in allControllers)
                {
                    if (c.gameObject.scene.name == "DontDestroyOnLoad" || c.gameObject.scene.name == null)
                    {
                        _tracker = c.LatencyTracker;
                        if (_tracker != null) break;
                    }
                }
            }
        }

        public void AssignTracker(PipelineDebugTracker tracker)
        {
            _tracker = tracker;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
                _visible = !_visible;
        }

        private void OnGUI()
        {
            if (!_visible) return;

            if (_tracker == null)
            {
                TryFindTracker();
                if (_tracker == null)
                {
                    GUI.Box(new Rect(30, 40, 400, 50),
                        "PipelineDebug: searching for AIChatController...\n(Press F12 to toggle)");
                    return;
                }
            }

            // Use a custom window style that matches our dark theme
            var windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = _bgTex;
            windowStyle.onNormal.background = _bgTex;
            windowStyle.border = new RectOffset(4, 4, 4, 4);
            windowStyle.padding = new RectOffset(0, 0, 22, 0);  // room for our custom drag bar
            GUI.contentColor = Color.white;

            _windowRect = GUI.Window(GetHashCode(), _windowRect, DrawWindow, "", windowStyle);

            // Keep window on screen
            _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 60, Screen.width - 60);
            _windowRect.y = Mathf.Clamp(_windowRect.y, -20, Screen.height - 40);
        }

        private void DrawWindow(int id)
        {
            if (_headerStyle == null) InitStyles();

            // Draw dark background below toolbar (20px offset for drag bar)
            GUI.Box(new Rect(0, 20, _windowRect.width, _windowRect.height - 20), "", _bgStyle);

            // Full-width drag area at top
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));

            var cur = _tracker.CurrentRound;
            var history = _tracker.CompletedRounds;
            int histCount = Mathf.Min(history.Count, _maxDisplayRounds);
            var asrStatus = _tracker.AsrStatus;
            var llmStatus = _tracker.LlmStatus;
            var ttsStatus = _tracker.TtsStatus;

            GUILayout.Space(4);

            // ── Toolbar ──
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("✕ Hide", GUILayout.Width(65), GUILayout.Height(20)))
                _visible = false;
            _showHistory = GUILayout.Toggle(
                _showHistory, " History", GUILayout.Width(72), GUILayout.Height(20));
            GUILayout.Label(
                $"Rounds: {_tracker.TotalRounds}  |  {(cur != null ? "Active" : "Idle")}",
                GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("CSV", GUILayout.Width(40), GUILayout.Height(20)))
                ExportToFile();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (_showHistory)
            {
                DrawHistoryList(histCount, history);
                return;
            }

            // ── Stages section ──
            DrawSectionHeader("Stages");
            GUILayout.Space(2);

            float maxCur = 1f;
            if (cur != null)
                maxCur = Mathf.Max(1f, cur.AsrMs, cur.LlmMs, cur.TtsMs, cur.E2EMs, cur.TotalMs);

            DrawStageRow("ASR", asrStatus, cur?.AsrMs ?? -1, maxCur, AsrColor,
                cur != null ? $"\"{Str(cur.UserInput, 40)}\"" : "-");
            DrawStageRow("LLM", llmStatus, cur?.LlmMs ?? -1, maxCur, LlmColor,
                cur != null
                    ? (cur.FirstTokenMs > 0
                        ? $"TTFT:{cur.FirstTokenMs:F0}ms  {cur.SentenceCount} chunks"
                        : "-")
                    : "-");
            DrawStageRow("TTS", ttsStatus, cur?.TtsMs ?? -1, maxCur, TtsColor,
                cur != null
                    ? $"{cur.CompletedSentenceCount}/{cur.SentenceCount} sentences"
                    : "-");
            if (cur != null && cur.AsrEndTime >= 0f && cur.E2EMs > 0f)
                DrawStageRow("E2E ", StageStatus.Done, cur.E2EMs, maxCur, E2eColor,
                    $"{cur.E2EMs:F0}ms");

            GUILayout.Space(8);

            // ── Timeline section ──
            bool hasAverages = histCount > 0;

            DrawSectionHeader("Timeline");
            GUILayout.Space(2);

            if (cur != null)
            {
                DrawTimelineBar("ASR  ", cur.AsrMs, maxCur, AsrColor,
                    hasAverages ? _tracker.AverageAsrMs : -1f);
                DrawTimelineBar("LLM  ", cur.LlmMs, maxCur, LlmColor,
                    hasAverages ? _tracker.AverageLlmMs : -1f,
                    cur.FirstTokenMs > 0f ? $"TTFT:{cur.FirstTokenMs:F0}ms" : null);
                DrawTimelineBar("TTS  ", cur.TtsMs, maxCur, TtsColor,
                    hasAverages ? _tracker.AverageTtsMs : -1f);
                if (cur.AsrEndTime >= 0f && cur.E2EMs > 0f)
                    DrawTimelineBar("E2E  ", cur.E2EMs, maxCur, E2eColor,
                        hasAverages ? _tracker.AverageE2EMs : -1f);
                DrawTimelineBar("TOTAL", cur.TotalMs, maxCur, TotalColor,
                    hasAverages ? _tracker.AverageTotalMs : -1f);
            }
            else
            {
                var waitingStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUILayout.Label("  (waiting for conversation...)", waitingStyle, GUILayout.Height(40));
                GUILayout.Space(4);
            }

            GUILayout.Space(4);
        }

        private void InitStyles()
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = HeaderLabelColor },
                padding = new RectOffset(2, 0, 1, 0)
            };
            _bgStyle = new GUIStyle();
            _bgStyle.normal.background = _bgTex;
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"── {title} ──", _headerStyle, GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawHistoryList(int histCount, IReadOnlyList<ConversationRound> history)
        {
            GUILayout.Label($"── History ({histCount} rounds) ──", _headerStyle, GUILayout.Height(20));

            // Fixed scroll area height within 440px window (80px accounts for toolbar + padding)
            float scrollH = _windowRect.height - 80;
            _historyScroll = GUILayout.BeginScrollView(
                _historyScroll, GUILayout.Height(Mathf.Max(50, scrollH)));

            int startIdx = Mathf.Max(0, history.Count - _maxDisplayRounds);
            for (int i = startIdx; i < history.Count; i++)
            {
                var r = history[i];
                string h = $"{r.WallClockTime:HH:mm:ss}  "
                    + $"ASR:{(r.AsrMs > 0 ? r.AsrMs.ToString("F0") : "--"),5}  "
                    + $"LLM:{(r.LlmMs > 0 ? r.LlmMs.ToString("F0") : "--"),5}  "
                    + $"TTS:{(r.TtsMs > 0 ? r.TtsMs.ToString("F0") : "--"),5}  "
                    + (r.AsrEndTime >= 0f && r.E2EMs > 0f
                        ? $"E2E:{r.E2EMs,5:F0}  "
                        : "")
                    + $"∑{(r.TotalMs > 0 ? r.TotalMs.ToString("F0") : "--"),5}";
                GUILayout.Label(h, GUILayout.Height(20));
            }
            GUILayout.EndScrollView();
        }

        private void DrawStageRow(
            string name, StageStatus status, float durationMs,
            float maxMs, Color barColor, string detail)
        {
            GUILayout.BeginHorizontal();

            // Status icon — 14w, colored by status
            string icon = status == StageStatus.Done ? "●"
                : status == StageStatus.Running ? "◐"
                : status == StageStatus.Failed ? "✕"
                : "○";
            Color iconColor = status == StageStatus.Done ? Green
                : status == StageStatus.Running ? Yellow
                : status == StageStatus.Failed ? RedN
                : Gray;

            var saved = GUI.color;
            GUI.color = iconColor;
            GUILayout.Label(icon, GUILayout.Width(14), GUILayout.Height(20));
            GUI.color = saved;

            GUILayout.Label(name, GUILayout.Width(45), GUILayout.Height(20));

            // Bar — 80px wide, 20px tall, visual ONLY (no text overlay)
            int barW = 80;
            var r = GUILayoutUtility.GetRect(barW, 20);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(r.x, r.y, barW, 20), _barBg);
                if (durationMs >= 0 && maxMs > 0)
                {
                    float norm = Mathf.Clamp01(durationMs / maxMs);
                    int fill = (int)(barW * norm);
                    if (fill > 0)
                    {
                        Texture2D barTex = GetOrCreateBarTex(barColor);
                        GUI.DrawTexture(new Rect(r.x, r.y, fill, 20), barTex);
                    }
                }
            }

            // Duration label after bar — fixed width 55w
            GUILayout.Label(durationMs >= 0 ? $"{durationMs:F0}ms" : "—",
                GUILayout.Width(55), GUILayout.Height(20));

            // Detail label — flexible
            GUILayout.Label(detail, GUILayout.MinWidth(0), GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawTimelineBar(
            string label, float duration, float maxMs, Color color,
            float avgMs, string extraInfo = null)
        {
            if (duration < 0) return;
            float norm = maxMs > 0 ? duration / maxMs : 0f;
            int barW = Math.Max(50, (int)(_windowRect.width - 320));
            int fillW = Math.Max(0, (int)(barW * Mathf.Clamp01(norm)));

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(45), GUILayout.Height(20));

            // Bar — visual ONLY (no text overlay), sized by fixed barW
            var r = GUILayoutUtility.GetRect(barW, 20);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(r.x, r.y, barW, 20), _barBg);
                if (fillW > 0)
                {
                    Texture2D barTex = GetOrCreateBarTex(color);
                    GUI.DrawTexture(new Rect(r.x, r.y, fillW, 20), barTex);
                }
            }

            // Duration label after bar — fixed width 55w
            GUILayout.Label($"{duration:F0}ms", GUILayout.Width(55), GUILayout.Height(20));

            // Average label — fixed width 85w. ALWAYS emitted to avoid IMGUI control count mismatch.
            GUILayout.Label(avgMs > 0f ? $"(avg {avgMs:F0}ms)" : "", GUILayout.Width(100), GUILayout.Height(20));

            // Extra info (e.g. TTFT) — flexible width. ALWAYS emitted to avoid IMGUI control count mismatch.
            GUILayout.Label(extraInfo ?? "", GUILayout.Width(120), GUILayout.Height(20));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void ExportToFile()
        {
            string csv = _tracker.ExportToCsv();
            string exportDir = System.IO.Path.Combine(
                System.IO.Directory.GetCurrentDirectory(), "PipelineExports");
            System.IO.Directory.CreateDirectory(exportDir);
            string path = System.IO.Path.Combine(exportDir,
                $"pipeline_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
            System.IO.File.WriteAllText(path, csv);
            Debug.Log($"[PipelineDebug] Exported to {path}");
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private Texture2D GetOrCreateBarTex(Color color)
        {
            if (!_barColorCache.TryGetValue(color, out Texture2D tex))
            {
                tex = MakeTex(color);
                _barColorCache[color] = tex;
            }
            return tex;
        }

        private static string Str(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) + "…" : s);
    }
}
