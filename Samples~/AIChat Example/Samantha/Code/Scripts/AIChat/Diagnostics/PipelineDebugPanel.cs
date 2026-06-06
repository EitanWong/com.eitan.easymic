using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class PipelineDebugPanel : MonoBehaviour
    {
        [SerializeField] private KeyCode _toggleKey = KeyCode.F12;
        [SerializeField] private int _maxDisplayRounds = 30;
        [SerializeField] private bool _visibleOnStart = false;

        private const float MinWindowWidth = 860f;
        private const float MinWindowHeight = 520f;
        private const float ToolbarHeight = 30f;
        private const float RowHeight = 22f;
        private const float OverviewHeight = 132f;
        private const float MetricCardHeight = 74f;

        private Rect _windowRect = new Rect(30, 40, 920, 620);
        private PipelineDebugTracker _tracker;
        private bool _visible;
        private bool _showHistory;
        private Vector2 _scroll;
        private Vector2 _historyScroll;

        private Texture2D _windowTex;
        private Texture2D _panelTex;
        private Texture2D _barBgTex;
        private Texture2D _lineTex;
        private readonly Dictionary<Color, Texture2D> _colorTex = new Dictionary<Color, Texture2D>();

        private GUIStyle _titleStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _rightStyle;
        private GUIStyle _monoStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _runningStyle;
        private GUIStyle _doneStyle;
        private GUIStyle _failedStyle;

        private static readonly Color WindowColor = new Color(0.08f, 0.09f, 0.11f, 0.98f);
        private static readonly Color PanelColor = new Color(0.12f, 0.13f, 0.16f, 0.96f);
        private static readonly Color LineColor = new Color(0.24f, 0.25f, 0.30f, 1f);
        private static readonly Color BarBgColor = new Color(0.20f, 0.21f, 0.25f, 1f);
        private static readonly Color TextColor = new Color(0.90f, 0.92f, 0.96f, 1f);
        private static readonly Color MutedColor = new Color(0.58f, 0.62f, 0.70f, 1f);
        private static readonly Color GoodColor = new Color(0.20f, 0.78f, 0.42f, 1f);
        private static readonly Color WarnColor = new Color(0.95f, 0.72f, 0.20f, 1f);
        private static readonly Color BadColor = new Color(0.95f, 0.30f, 0.30f, 1f);
        private static readonly Color AsrColor = new Color(0.28f, 0.68f, 1.00f, 1f);
        private static readonly Color LlmColor = new Color(1.00f, 0.58f, 0.18f, 1f);
        private static readonly Color TtsColor = new Color(0.30f, 0.86f, 0.48f, 1f);
        private static readonly Color PlaybackColor = new Color(0.62f, 0.58f, 1.00f, 1f);
        private static readonly Color TotalColor = new Color(0.86f, 0.42f, 0.86f, 1f);

        private void Awake()
        {
            _visible = _visibleOnStart;
            _windowTex = MakeTex(WindowColor);
            _panelTex = MakeTex(PanelColor);
            _barBgTex = MakeTex(BarBgColor);
            _lineTex = MakeTex(LineColor);
        }

        private void Start()
        {
            TryFindTracker();
        }

        private void OnDestroy()
        {
            DestroyIfNeeded(_windowTex);
            DestroyIfNeeded(_panelTex);
            DestroyIfNeeded(_barBgTex);
            DestroyIfNeeded(_lineTex);
            foreach (var tex in _colorTex.Values)
            {
                DestroyIfNeeded(tex);
            }
            _colorTex.Clear();
        }

        public void AssignTracker(PipelineDebugTracker tracker)
        {
            _tracker = tracker;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _visible = !_visible;
            }
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            if (_tracker == null)
            {
                TryFindTracker();
            }

            InitStyles();
            _windowRect.width = Mathf.Max(MinWindowWidth, _windowRect.width);
            _windowRect.height = Mathf.Max(MinWindowHeight, _windowRect.height);

            var windowStyle = new GUIStyle(GUI.skin.window)
            {
                normal = { background = _windowTex },
                onNormal = { background = _windowTex },
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(4, 4, 4, 4)
            };

            GUI.contentColor = TextColor;
            _windowRect = GUI.Window(GetHashCode(), _windowRect, DrawWindow, GUIContent.none, windowStyle);
            _windowRect.x = Mathf.Clamp(_windowRect.x, -_windowRect.width + 80f, Screen.width - 80f);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - 48f);
        }

        private void DrawWindow(int id)
        {
            DrawToolbar();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, ToolbarHeight));

            var contentRect = new Rect(12f, ToolbarHeight + 10f, _windowRect.width - 24f, _windowRect.height - ToolbarHeight - 22f);
            if (_tracker == null)
            {
                DrawPanel(contentRect);
                GUI.Label(new Rect(contentRect.x + 18f, contentRect.y + 18f, contentRect.width - 36f, 44f),
                    "PipelineDebug is waiting for AIChatController. Press F12 to hide.", _labelStyle);
                return;
            }

            ConversationRound current = _tracker.CurrentRound;
            ConversationRound[] history = _tracker.GetCompletedRoundsSnapshot();
            PipelineLatencyStats stats = _tracker.GetStatsSnapshot();

            if (_showHistory)
            {
                DrawHistory(contentRect, history);
                return;
            }

            _scroll = GUI.BeginScrollView(contentRect, _scroll, new Rect(0, 0, contentRect.width - 18f, 700f));
            float y = 0f;
            y = DrawOverview(new Rect(0, y, contentRect.width - 18f, OverviewHeight), current, history, stats) + 12f;
            y = DrawStageBreakdown(new Rect(0, y, contentRect.width - 18f, 190f), current, stats) + 12f;
            y = DrawHandoffBreakdown(new Rect(0, y, contentRect.width - 18f, 178f), current, stats) + 12f;
            DrawCurrentTranscript(new Rect(0, y, contentRect.width - 18f, 94f), current);
            GUI.EndScrollView();
        }

        private void DrawToolbar()
        {
            GUI.DrawTexture(new Rect(0, 0, _windowRect.width, ToolbarHeight), _panelTex);
            GUI.Label(new Rect(14, 5, 360, 22), "AI Chat Pipeline Diagnostics", _titleStyle);

            float x = _windowRect.width - 280f;
            _showHistory = GUI.Toggle(new Rect(x, 5, 78, 20), _showHistory, "History", _buttonStyle);
            x += 86f;
            if (GUI.Button(new Rect(x, 5, 72, 20), "Export", _buttonStyle))
            {
                ExportToFile();
            }
            x += 80f;
            if (GUI.Button(new Rect(x, 5, 64, 20), "Hide", _buttonStyle))
            {
                _visible = false;
            }
        }

        private float DrawOverview(Rect rect, ConversationRound current, ConversationRound[] history, PipelineLatencyStats stats)
        {
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, 220, 22), "Overview", _headerStyle);

            string state = current != null ? "Active" : "Idle";
            string ttfa = FormatMs(current != null ? current.UserWaitToFirstAudioMs : LastValid(history, r => r.UserWaitToFirstAudioMs));
            string bottleneck = current != null ? $"{current.BottleneckStage} {FormatMs(current.BottleneckMs)}" : LastBottleneck(history);

            float cardY = rect.y + 38f;
            float cardW = (rect.width - 56f) / 4f;
            DrawMetricCard(new Rect(rect.x + 14f, cardY, cardW, MetricCardHeight), "Current TTFA", ttfa, QualityLabel(current != null ? current.UserWaitToFirstAudioMs : LastValid(history, r => r.UserWaitToFirstAudioMs)));
            DrawMetricCard(new Rect(rect.x + 24f + cardW, cardY, cardW, MetricCardHeight), "P50 / P90 TTFA", $"{FormatMs(stats.P50FirstAudioMs)} / {FormatMs(stats.P90FirstAudioMs)}", $"{stats.SampleCount} samples");
            DrawMetricCard(new Rect(rect.x + 34f + cardW * 2f, cardY, cardW, MetricCardHeight), "Bottleneck", bottleneck, "largest completed stage");
            DrawMetricCard(new Rect(rect.x + 44f + cardW * 3f, cardY, cardW, MetricCardHeight), "Rounds", $"{_tracker.TotalRounds} total", $"{state}, {_tracker.CancelledRounds} interrupted");
            return rect.yMax;
        }

        private float DrawStageBreakdown(Rect rect, ConversationRound current, PipelineLatencyStats stats)
        {
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, 240, 22), "Stage Breakdown", _headerStyle);
            DrawTableHeader(rect.x + 14, rect.y + 38, rect.width - 28);

            float max = MaxPositive(
                current != null ? current.AsrMs : -1f,
                current != null ? current.LlmMs : -1f,
                current != null ? current.TtsMs : -1f,
                current != null ? current.PlaybackMs : -1f,
                stats.AverageAsrMs, stats.AverageLlmMs, stats.AverageTtsMs, stats.AveragePlaybackMs);

            float y = rect.y + 62f;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "ASR", _tracker.AsrStatus, current != null ? current.AsrMs : -1f, stats.AverageAsrMs, max, AsrColor, "speech start -> final transcript");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "LLM", _tracker.LlmStatus, current != null ? current.LlmMs : -1f, stats.AverageLlmMs, max, LlmColor, $"TTFT {FormatMs(current != null ? current.FirstTokenMs : stats.AverageFirstTokenMs)}");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "TTS", _tracker.TtsStatus, current != null ? current.TtsMs : -1f, stats.AverageTtsMs, max, TtsColor, current != null ? $"{current.CompletedSentenceCount}/{current.SentenceCount} sentences" : "sentence synthesis");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "Playback", _tracker.PlaybackStatus, current != null ? current.PlaybackMs : -1f, stats.AveragePlaybackMs, max, PlaybackColor, "first audio -> drained");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "Total", current != null ? StageStatus.Running : StageStatus.Done, current != null ? current.RunningTotalMs : LastValid(_tracker.GetCompletedRoundsSnapshot(), r => r.TotalMs), stats.AverageTotalMs, max, TotalColor, "ASR start -> playback drained");
            return rect.yMax;
        }

        private float DrawHandoffBreakdown(Rect rect, ConversationRound current, PipelineLatencyStats stats)
        {
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, 260, 22), "Low-latency Handoffs", _headerStyle);
            DrawTableHeader(rect.x + 14, rect.y + 38, rect.width - 28);

            float ttft = current != null ? current.UserWaitToFirstTokenMs : -1f;
            float firstSentence = current != null ? current.UserWaitToFirstSentenceMs : -1f;
            float ttsFirstAudio = current != null ? current.TtsQueueToFirstAudioMs : -1f;
            float ttfa = current != null ? current.UserWaitToFirstAudioMs : -1f;
            float max = MaxPositive(ttft, firstSentence, ttsFirstAudio, ttfa, stats.AverageFirstTokenMs, stats.AverageFirstSentenceMs, stats.AverageFirstAudioMs);

            float y = rect.y + 62f;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "TTFT", _tracker.LlmStatus, ttft, stats.AverageFirstTokenMs, max, LlmColor, "ASR final -> first LLM token");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "First text", _tracker.TtsStatus, firstSentence, stats.AverageFirstSentenceMs, max, TtsColor, "ASR final -> first sentence to TTS");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "TTS start", _tracker.PlaybackStatus, ttsFirstAudio, 0f, max, PlaybackColor, "first sentence -> first audio");
            y += RowHeight;
            DrawStageRow(rect.x + 14, y, rect.width - 28, "TTFA", _tracker.PlaybackStatus, ttfa, stats.AverageFirstAudioMs, max, TotalColor, "ASR final -> first audible audio");
            return rect.yMax;
        }

        private void DrawCurrentTranscript(Rect rect, ConversationRound current)
        {
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, 180, 22), "Current Transcript", _headerStyle);
            string text = current != null && !string.IsNullOrWhiteSpace(current.UserInput)
                ? current.UserInput
                : "Waiting for the next user turn.";
            GUI.Label(new Rect(rect.x + 14, rect.y + 38, rect.width - 28, 44), text, _labelStyle);
        }

        private void DrawHistory(Rect rect, ConversationRound[] history)
        {
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, 260, 22), $"History ({Mathf.Min(history.Length, _maxDisplayRounds)} rounds)", _headerStyle);
            var viewRect = new Rect(0, 0, rect.width - 34f, Mathf.Max(rect.height - 52f, history.Length * RowHeight + 30f));
            var scrollRect = new Rect(rect.x + 14, rect.y + 42, rect.width - 28, rect.height - 54);
            _historyScroll = GUI.BeginScrollView(scrollRect, _historyScroll, viewRect);

            float y = 0f;
            DrawHistoryHeader(0, y, viewRect.width);
            y += RowHeight;
            int start = Mathf.Max(0, history.Length - _maxDisplayRounds);
            for (int i = start; i < history.Length; i++)
            {
                DrawHistoryRow(0, y, viewRect.width, history[i]);
                y += RowHeight;
            }

            GUI.EndScrollView();
        }

        private void DrawTableHeader(float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, 90, RowHeight), "Metric", _mutedStyle);
            GUI.Label(new Rect(x + 96, y, 70, RowHeight), "State", _mutedStyle);
            GUI.Label(new Rect(x + 172, y, 82, RowHeight), "Current", _rightStyle);
            GUI.Label(new Rect(x + 262, y, 82, RowHeight), "Average", _rightStyle);
            GUI.Label(new Rect(x + 354, y, width - 354, RowHeight), "Details", _mutedStyle);
            GUI.DrawTexture(new Rect(x, y + RowHeight - 1, width, 1), _lineTex);
        }

        private void DrawStageRow(float x, float y, float width, string name, StageStatus status, float currentMs, float averageMs, float maxMs, Color color, string detail)
        {
            GUI.Label(new Rect(x, y, 90, RowHeight), name, _labelStyle);
            GUI.Label(new Rect(x + 96, y, 70, RowHeight), StatusText(status), StatusStyle(status));
            GUI.Label(new Rect(x + 172, y, 82, RowHeight), FormatMs(currentMs), _rightStyle);
            GUI.Label(new Rect(x + 262, y, 82, RowHeight), FormatMs(averageMs), _rightStyle);

            var barRect = new Rect(x + 356, y + 5, Mathf.Max(80, width - 560), 12);
            DrawBar(barRect, currentMs, maxMs, color);
            GUI.Label(new Rect(barRect.xMax + 10, y, width - (barRect.xMax - x) - 10, RowHeight), detail, _mutedStyle);
        }

        private void DrawHistoryHeader(float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, 58, RowHeight), "#", _mutedStyle);
            GUI.Label(new Rect(x + 58, y, 76, RowHeight), "Time", _mutedStyle);
            GUI.Label(new Rect(x + 134, y, 86, RowHeight), "State", _mutedStyle);
            GUI.Label(new Rect(x + 220, y, 76, RowHeight), "TTFA", _rightStyle);
            GUI.Label(new Rect(x + 300, y, 76, RowHeight), "TTFT", _rightStyle);
            GUI.Label(new Rect(x + 380, y, 76, RowHeight), "TTS", _rightStyle);
            GUI.Label(new Rect(x + 460, y, 76, RowHeight), "Total", _rightStyle);
            GUI.Label(new Rect(x + 546, y, 100, RowHeight), "Bottleneck", _mutedStyle);
            GUI.Label(new Rect(x + 650, y, width - 650, RowHeight), "Input", _mutedStyle);
            GUI.DrawTexture(new Rect(x, y + RowHeight - 1, width, 1), _lineTex);
        }

        private void DrawHistoryRow(float x, float y, float width, ConversationRound round)
        {
            GUI.Label(new Rect(x, y, 58, RowHeight), round.Index.ToString(), _monoStyle);
            GUI.Label(new Rect(x + 58, y, 76, RowHeight), round.WallClockTime.ToString("HH:mm:ss"), _monoStyle);
            GUI.Label(new Rect(x + 134, y, 86, RowHeight), round.WasCancelled ? "cancelled" : "done", round.WasCancelled ? _mutedStyle : _labelStyle);
            GUI.Label(new Rect(x + 220, y, 76, RowHeight), FormatMs(round.E2EMs), _rightStyle);
            GUI.Label(new Rect(x + 300, y, 76, RowHeight), FormatMs(round.FirstTokenMs), _rightStyle);
            GUI.Label(new Rect(x + 380, y, 76, RowHeight), FormatMs(round.TtsMs), _rightStyle);
            GUI.Label(new Rect(x + 460, y, 76, RowHeight), FormatMs(round.TotalMs), _rightStyle);
            GUI.Label(new Rect(x + 546, y, 100, RowHeight), round.BottleneckStage, _mutedStyle);
            GUI.Label(new Rect(x + 650, y, width - 650, RowHeight), Trim(round.UserInput, 72), _labelStyle);
        }

        private void DrawMetricCard(Rect rect, string title, string value, string subtitle)
        {
            GUI.DrawTexture(rect, _panelTex);
            GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, 18), title, _mutedStyle);
            GUI.Label(new Rect(rect.x + 10, rect.y + 26, rect.width - 20, 22), value, _valueStyle);
            GUI.Label(new Rect(rect.x + 10, rect.y + 50, rect.width - 20, 18), subtitle, _mutedStyle);
        }

        private void DrawPanel(Rect rect)
        {
            GUI.DrawTexture(rect, _panelTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), _lineTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), _lineTex);
        }

        private void DrawBar(Rect rect, float value, float max)
        {
            DrawBar(rect, value, max, TotalColor);
        }

        private void DrawBar(Rect rect, float value, float max, Color color)
        {
            GUI.DrawTexture(rect, _barBgTex);
            if (value <= 0f || max <= 0f)
            {
                return;
            }

            float fill = Mathf.Clamp01(value / max) * rect.width;
            GUI.DrawTexture(new Rect(rect.x, rect.y, fill, rect.height), GetTex(color));
        }

        private void TryFindTracker()
        {
            if (_tracker != null)
            {
                return;
            }

            var controller = FindObjectOfType<AIChatController>();
            if (controller != null)
            {
                _tracker = controller.LatencyTracker;
                return;
            }

            var controllers = FindObjectsOfType<AIChatController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != null && controllers[i].LatencyTracker != null)
                {
                    _tracker = controllers[i].LatencyTracker;
                    return;
                }
            }
        }

        private void InitStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 14,
                normal = { textColor = TextColor }
            };
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                normal = { textColor = TextColor }
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                clipping = TextClipping.Clip,
                wordWrap = false,
                normal = { textColor = TextColor }
            };
            _mutedStyle = new GUIStyle(_labelStyle)
            {
                normal = { textColor = MutedColor }
            };
            _valueStyle = new GUIStyle(_labelStyle)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 15,
                normal = { textColor = TextColor }
            };
            _rightStyle = new GUIStyle(_labelStyle)
            {
                alignment = TextAnchor.MiddleRight
            };
            _monoStyle = new GUIStyle(_labelStyle)
            {
                font = GUI.skin.label.font
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                padding = new RectOffset(4, 4, 1, 1)
            };
            _runningStyle = new GUIStyle(_labelStyle) { normal = { textColor = WarnColor } };
            _doneStyle = new GUIStyle(_labelStyle) { normal = { textColor = GoodColor } };
            _failedStyle = new GUIStyle(_labelStyle) { normal = { textColor = BadColor } };
        }

        private GUIStyle StatusStyle(StageStatus status)
        {
            switch (status)
            {
                case StageStatus.Running:
                    return _runningStyle;
                case StageStatus.Done:
                    return _doneStyle;
                case StageStatus.Failed:
                    return _failedStyle;
                default:
                    return _mutedStyle;
            }
        }

        private Texture2D GetTex(Color color)
        {
            if (!_colorTex.TryGetValue(color, out Texture2D tex))
            {
                tex = MakeTex(color);
                _colorTex[color] = tex;
            }
            return tex;
        }

        private void ExportToFile()
        {
            if (_tracker == null)
            {
                return;
            }

            string exportDir = Path.Combine(Directory.GetCurrentDirectory(), "PipelineExports");
            Directory.CreateDirectory(exportDir);
            string path = Path.Combine(exportDir, $"pipeline_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(path, _tracker.ExportToCsv());
            Debug.Log($"[PipelineDebug] Exported to {path}");
        }

        private static string StatusText(StageStatus status)
        {
            switch (status)
            {
                case StageStatus.Running: return "running";
                case StageStatus.Done: return "done";
                case StageStatus.Failed: return "failed";
                default: return "waiting";
            }
        }

        private static string QualityLabel(float ttfaMs)
        {
            if (ttfaMs <= 0f) return "waiting";
            if (ttfaMs <= 800f) return "excellent";
            if (ttfaMs <= 1500f) return "watch";
            return "slow";
        }

        private static string FormatMs(float value)
        {
            return value > 0f ? $"{value:F0} ms" : "--";
        }

        private static float LastValid(ConversationRound[] rounds, Func<ConversationRound, float> selector)
        {
            if (rounds == null || selector == null) return -1f;
            for (int i = rounds.Length - 1; i >= 0; i--)
            {
                float value = selector(rounds[i]);
                if (value > 0f) return value;
            }
            return -1f;
        }

        private static string LastBottleneck(ConversationRound[] rounds)
        {
            if (rounds == null || rounds.Length == 0) return "-";
            for (int i = rounds.Length - 1; i >= 0; i--)
            {
                if (rounds[i].BottleneckMs > 0f)
                {
                    return $"{rounds[i].BottleneckStage} {FormatMs(rounds[i].BottleneckMs)}";
                }
            }
            return "-";
        }

        private static float MaxPositive(params float[] values)
        {
            float max = 1f;
            if (values == null) return max;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max) max = values[i];
            }
            return max;
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= max) return value;
            return value.Substring(0, Mathf.Max(0, max - 3)) + "...";
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static void DestroyIfNeeded(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                Destroy(obj);
            }
        }
    }
}
