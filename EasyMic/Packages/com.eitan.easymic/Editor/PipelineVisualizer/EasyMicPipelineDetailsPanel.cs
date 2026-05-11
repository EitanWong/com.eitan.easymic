#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Eitan.EasyMic.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eitan.EasyMic.Editor
{
    internal sealed class EasyMicPipelineDetailsPanel : VisualElement
    {
        private readonly Dictionary<string, DetailRow> _rows = new Dictionary<string, DetailRow>(32);
        private readonly VisualElement _accent;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Button _dockButton;
        private readonly VisualElement _selectedSection;
        private readonly VisualElement _overviewSection;
        private readonly VisualElement _timingSection;
        private readonly VisualElement _processingSection;
        private readonly List<DetailRow> _detailRows = new List<DetailRow>(32);
        private readonly VisualElement _header;
        private readonly VisualElement _headerText;
        private bool _draggingDock;
        private string _selectedNodeId;

        public event Action<EasyMicInspectorDock> DockChanged;
        public event Action RuntimeOverviewRequested;

        public EasyMicPipelineDetailsPanel()
        {
            style.flexGrow = 1;
            style.backgroundColor = EasyMicPipelineStyles.PanelBackground;
            style.borderRightColor = EasyMicPipelineStyles.Separator;
            style.borderRightWidth = 1;
            style.minWidth = 240;

            _header = new VisualElement();
            _header.style.flexDirection = FlexDirection.Row;
            _header.style.paddingLeft = 16;
            _header.style.paddingRight = 16;
            _header.style.paddingTop = 14;
            _header.style.paddingBottom = 12;
            _header.style.minHeight = 86;
            _header.style.flexShrink = 0;
            _header.style.borderBottomColor = EasyMicPipelineStyles.Separator;
            _header.style.borderBottomWidth = 1;
            _header.RegisterCallback<PointerDownEvent>(OnDockPointerDown);
            _header.RegisterCallback<PointerMoveEvent>(OnDockPointerMove);
            _header.RegisterCallback<PointerUpEvent>(OnDockPointerUp);
            Add(_header);

            _accent = new VisualElement();
            _accent.style.width = 4;
            _accent.style.marginRight = 10;
            _accent.style.marginTop = 2;
            _accent.style.marginBottom = 2;
            _accent.style.backgroundColor = EasyMicPipelineStyles.SecondaryText;
            _header.Add(_accent);

            _headerText = new VisualElement();
            _headerText.style.flexGrow = 1;
            _headerText.style.minWidth = 0;
            _header.Add(_headerText);

            var topLine = new VisualElement();
            topLine.style.flexDirection = FlexDirection.Row;
            topLine.style.alignItems = Align.Center;
            _headerText.Add(topLine);

            var eyebrow = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.InspectorEyebrow));
            eyebrow.style.color = EasyMicPipelineStyles.SecondaryText;
            eyebrow.style.fontSize = 10;
            eyebrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            eyebrow.style.flexGrow = 1;
            topLine.Add(eyebrow);

            _dockButton = new Button(() => DockChanged?.Invoke(_currentDock == EasyMicInspectorDock.Left ? EasyMicInspectorDock.Right : EasyMicInspectorDock.Left));
            _dockButton.text = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.InspectorDockInitial);
            _dockButton.tooltip = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.InspectorDockTooltip);
            _dockButton.style.height = 20;
            _dockButton.style.width = 26;
            _dockButton.style.minWidth = 26;
            _dockButton.style.flexShrink = 0;
            _dockButton.style.paddingLeft = 6;
            _dockButton.style.paddingRight = 6;
            _dockButton.style.fontSize = 10;
            topLine.Add(_dockButton);

            _title = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RuntimeOverview));
            _title.style.color = EasyMicPipelineStyles.PrimaryText;
            _title.style.fontSize = 17;
            _title.style.marginTop = 7;
            _title.style.whiteSpace = WhiteSpace.Normal;
            _headerText.Add(_title);

            _subtitle = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RuntimeOverviewSubtitle));
            _subtitle.style.color = EasyMicPipelineStyles.SecondaryText;
            _subtitle.style.fontSize = 11;
            _subtitle.style.marginTop = 3;
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            _headerText.Add(_subtitle);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 14;
            scroll.style.paddingRight = 14;
            scroll.style.paddingTop = 12;
            scroll.style.paddingBottom = 16;
            scroll.contentContainer.style.flexGrow = 1;
            scroll.contentContainer.style.flexDirection = FlexDirection.Column;
            Add(scroll);

            _selectedSection = AddSection(scroll, EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.SelectedNode));
            AddRow(_selectedSection, "node.name", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Name));
            AddRow(_selectedSection, "node.type", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Type));
            AddRow(_selectedSection, "node.pipeline", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Pipeline));
            AddRow(_selectedSection, "node.state", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.State));
            AddRow(_selectedSection, "node.thread", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Thread));
            AddRow(_selectedSection, "node.metrics", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Metrics));

            _overviewSection = AddSection(scroll, EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RuntimeOverview));
            AddRow(_overviewSection, "overview.playback", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Playback));
            AddRow(_overviewSection, "overview.recording", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Recording));
            AddRow(_overviewSection, "overview.backend", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Backend));
            AddRow(_overviewSection, "overview.output", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Output));
            AddRow(_overviewSection, "overview.capture", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Capture));
            AddRow(_overviewSection, "overview.graph", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Graph));

            _timingSection = AddSection(scroll, EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.TimingAndQueues));
            AddRow(_timingSection, "timing.callback", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Callback));
            AddRow(_timingSection, "timing.playbackQueue", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlaybackQueue));
            AddRow(_timingSection, "timing.captureQueue", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureQueue));
            AddRow(_timingSection, "timing.xruns", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Xruns));
            AddRow(_timingSection, "timing.latency", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Latency));

            _processingSection = AddSection(scroll, EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Processing));
            AddRow(_processingSection, "processing.playback", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlaybackDsp));
            AddRow(_processingSection, "processing.capture", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureDsp));
            AddRow(_processingSection, "processing.readers", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.AsyncReaders));
            AddRow(_processingSection, "processing.threading", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Threading));

            var emptyInspectorArea = new VisualElement();
            emptyInspectorArea.style.flexGrow = 1;
            emptyInspectorArea.style.minHeight = 96;
            emptyInspectorArea.pickingMode = PickingMode.Position;
            emptyInspectorArea.RegisterCallback<PointerDownEvent>(OnEmptyInspectorAreaPointerDown);
            scroll.Add(emptyInspectorArea);

            RegisterCallback<GeometryChangedEvent>(_ => ApplyResponsiveLayout());
            SetSelection(null);
        }

        private EasyMicInspectorDock _currentDock;

        public void SetDockSide(EasyMicInspectorDock dock)
        {
            _currentDock = dock;
            _dockButton.text = dock == EasyMicInspectorDock.Left ? "\u21E5" : "\u21E4";
            _dockButton.tooltip = dock == EasyMicInspectorDock.Left
                ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.InspectorDockRightTooltip)
                : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.InspectorDockLeftTooltip);
        }

        public void SetSelection(EasyMicPipelineGraphNode node)
        {
            _selectedNodeId = node?.Id;
            ApplySelection(node);
        }

        public void ClearSelection()
        {
            SetSelection(null);
        }

        public void UpdateTelemetry(EasyMicPipelineGraphModel model)
        {
            if (model == null)
            {
                return;
            }

            EasyMicPipelineGraphNode selected = null;
            if (!string.IsNullOrEmpty(_selectedNodeId))
            {
                model.TryGetNode(_selectedNodeId, out selected);
            }

            ApplySelection(selected);
            ApplyOverview(model);
            ApplyTiming(model);
            ApplyProcessing(model);
        }

        private void ApplySelection(EasyMicPipelineGraphNode node)
        {
            bool hasSelection = node != null;
            _selectedSection.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasSelection)
            {
                SetHeader(
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RuntimeOverview),
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RuntimeOverviewSelectionHint));
                _accent.style.backgroundColor = EasyMicPipelineStyles.SecondaryText;
                return;
            }

            SetHeader(node.Title, node.Subtitle);
            _accent.style.backgroundColor = EasyMicPipelineStyles.Accent(node.Kind);
            SetRow("node.name", node.Title);
            SetRow("node.type", EasyMicEditorLocalization.PipelineNodeKindLabel(node.Kind));
            SetRow("node.pipeline", ResolvePipeline(node.Id));
            SetRow("node.state", node.Activity > 0.25f
                ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Active)
                : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Idle));
            SetRow("node.thread", EasyMicPipelineFormatting.ThreadLabel(node.Thread));
            SetRow("node.metrics", string.IsNullOrEmpty(node.Metrics) ? "-" : node.Metrics);
        }

        private void ApplyOverview(EasyMicPipelineGraphModel model)
        {
            var playback = model.Playback;
            var recordings = model.Recordings ?? Array.Empty<EasyMicRecordingPipelineSnapshot>();
            string captureDevice = recordings.Length > 0 ? SafeDeviceName(recordings[0]) : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.None);
            SetRow("overview.playback", playback.IsRunning
                ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Running)
                : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Stopped));
            SetRow("overview.recording", recordings.Length == 0 ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.NoActiveSessions) : recordings.Length.ToString());
            SetRow("overview.backend", string.IsNullOrEmpty(playback.BackendName) ? "miniaudio" : playback.BackendName);
            SetRow("overview.output", string.Format("{0} | {1} Hz / {2} ch", string.IsNullOrEmpty(playback.DeviceName) ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DefaultOutput) : playback.DeviceName, playback.SampleRate, playback.Channels));
            SetRow("overview.capture", captureDevice);
            SetRow("overview.graph", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.GraphCountsFormat, model.Nodes.Count, model.Groups.Count, model.Edges.Count));
        }

        private void ApplyTiming(EasyMicPipelineGraphModel model)
        {
            var telemetry = model.Playback.Telemetry;
            int captureQueue = 0;
            long captureUnderruns = 0;
            long captureOverruns = 0;
            var recordings = model.Recordings ?? Array.Empty<EasyMicRecordingPipelineSnapshot>();
            for (int i = 0; i < recordings.Length; i++)
            {
                var rt = recordings[i].Info.Telemetry;
                captureQueue += rt.LastQueueDepthSamples;
                captureUnderruns += rt.TransportUnderruns;
                captureOverruns += rt.TransportOverruns;
            }

            SetRow("timing.callback", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CallbackFormat, telemetry.CallbackAverageMicroseconds, telemetry.CallbackMaxMicroseconds));
            SetRow("timing.playbackQueue", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.QueueSamplesPeakFormat, telemetry.LastQueueDepthSamples, telemetry.MaxQueueDepthSamples));
            SetRow("timing.captureQueue", captureQueue == 0 ? "-" : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.QueueSamplesPeakFormat, captureQueue, captureQueue));
            SetRow("timing.xruns", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.XrunsFormat, telemetry.TransportUnderruns, telemetry.TransportOverruns, captureUnderruns, captureOverruns));
            SetRow("timing.latency", model.Playback.LatencyProfile.ToString());
        }

        private void ApplyProcessing(EasyMicPipelineGraphModel model)
        {
            int captureStages = 0;
            int readers = 0;
            var recordings = model.Recordings ?? Array.Empty<EasyMicRecordingPipelineSnapshot>();
            for (int i = 0; i < recordings.Length; i++)
            {
                var processors = recordings[i].Processors ?? Array.Empty<EasyMicProcessorSnapshot>();
                captureStages += processors.Length;
                for (int p = 0; p < processors.Length; p++)
                {
                    if (processors[p].IsReader)
                    {
                        readers++;
                    }
                }
            }

            var playbackProcessors = model.Playback.MasterMixer.Processors ?? Array.Empty<EasyMicProcessorSnapshot>();
            SetRow("processing.playback", playbackProcessors.Length.ToString());
            SetRow("processing.capture", captureStages.ToString());
            SetRow("processing.readers", readers.ToString());
            SetRow("processing.threading", EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ThreadingPath));
        }

        private static string SafeDeviceName(EasyMicRecordingPipelineSnapshot recording)
        {
            string name = recording.Info.Device.Name;
            return string.IsNullOrEmpty(name) ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.None) : name;
        }

        private static string ResolvePipeline(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return "-";
            }

            if (nodeId.StartsWith("capture-", StringComparison.Ordinal))
            {
                return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Recording);
            }

            if (nodeId.StartsWith("playback-", StringComparison.Ordinal))
            {
                return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Playback);
            }

            return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.Runtime);
        }

        private void SetHeader(string title, string subtitle)
        {
            if (_title.text != title)
            {
                _title.text = title;
                _title.tooltip = title;
            }

            if (_subtitle.text != subtitle)
            {
                _subtitle.text = subtitle;
                _subtitle.tooltip = subtitle;
            }
        }

        private VisualElement AddSection(VisualElement parent, string title)
        {
            var section = new VisualElement();
            section.style.flexShrink = 0;
            section.style.marginBottom = 16;
            section.style.paddingLeft = 2;
            section.style.paddingRight = 2;
            section.style.paddingTop = 2;
            section.style.paddingBottom = 0;
            parent.Add(section);

            var label = new Label(title);
            label.style.color = EasyMicPipelineStyles.PrimaryText;
            label.style.fontSize = 11;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 0;
            label.style.marginBottom = 7;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            section.Add(label);

            var rule = new VisualElement();
            rule.style.height = 1;
            rule.style.backgroundColor = new Color(0.24f, 0.25f, 0.27f, 0.55f);
            rule.style.marginBottom = 6;
            section.Add(rule);
            return section;
        }

        private void AddRow(VisualElement parent, string key, string label)
        {
            var row = new DetailRow(label);
            _rows[key] = row;
            _detailRows.Add(row);
            parent.Add(row.Root);
        }

        private void SetRow(string key, string value)
        {
            if (_rows.TryGetValue(key, out var row))
            {
                row.SetValue(value);
            }
        }

        private void OnDockPointerDown(PointerDownEvent evt)
        {
            _draggingDock = true;
            this.CapturePointer(evt.pointerId);
        }

        private void OnDockPointerMove(PointerMoveEvent evt)
        {
            if (!_draggingDock || parent == null)
            {
                return;
            }

            Vector2 local = parent.WorldToLocal(evt.position);
            if (local.x < 28f)
            {
                DockChanged?.Invoke(EasyMicInspectorDock.Left);
            }
            else if (local.x > parent.layout.width - 28f)
            {
                DockChanged?.Invoke(EasyMicInspectorDock.Right);
            }
        }

        private void OnDockPointerUp(PointerUpEvent evt)
        {
            _draggingDock = false;
            this.ReleasePointer(evt.pointerId);
        }

        private void OnEmptyInspectorAreaPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            ClearSelection();
            RuntimeOverviewRequested?.Invoke();
            evt.StopPropagation();
        }

        private void ApplyResponsiveLayout()
        {
            float width = resolvedStyle.width;
            if (width <= 1f)
            {
                return;
            }

            bool compact = width < 300f;

            _header.style.paddingLeft = compact ? 12 : 16;
            _header.style.paddingRight = compact ? 12 : 16;
            _title.style.fontSize = compact ? 15 : 17;
            _subtitle.style.fontSize = compact ? 10 : 11;

            for (int i = 0; i < _detailRows.Count; i++)
            {
                _detailRows[i].SetPanelWidth(width);
            }
        }

        private sealed class DetailRow
        {
            private const int LongSegmentWrapLength = 42;

            public readonly VisualElement Root;
            private readonly Label _name;
            private readonly VisualElement _valueRoot;
            private readonly List<Label> _valueLines = new List<Label>(4);
            private string _lastValue = string.Empty;

            public DetailRow(string label)
            {
                Root = new VisualElement();
                Root.style.flexDirection = FlexDirection.Column;
                Root.style.alignItems = Align.Stretch;
                Root.style.marginBottom = 10;
                Root.style.flexShrink = 0;
                Root.style.paddingLeft = 9;
                Root.style.paddingRight = 9;
                Root.style.paddingTop = 8;
                Root.style.paddingBottom = 8;
                Root.style.backgroundColor = new Color(0.135f, 0.14f, 0.15f, 0.42f);
                Root.style.borderTopColor = new Color(0.25f, 0.26f, 0.28f, 0.42f);
                Root.style.borderRightColor = new Color(0.25f, 0.26f, 0.28f, 0.42f);
                Root.style.borderBottomColor = new Color(0.25f, 0.26f, 0.28f, 0.42f);
                Root.style.borderLeftColor = new Color(0.25f, 0.26f, 0.28f, 0.42f);
                Root.style.borderTopWidth = 1;
                Root.style.borderRightWidth = 1;
                Root.style.borderBottomWidth = 1;
                Root.style.borderLeftWidth = 1;
                Root.style.borderTopLeftRadius = 5;
                Root.style.borderTopRightRadius = 5;
                Root.style.borderBottomLeftRadius = 5;
                Root.style.borderBottomRightRadius = 5;

                _name = new Label(label);
                _name.tooltip = label;
                _name.style.flexShrink = 0;
                _name.style.color = EasyMicPipelineStyles.SecondaryText;
                _name.style.fontSize = 10;
                _name.style.unityFontStyleAndWeight = FontStyle.Bold;
                _name.style.unityTextAlign = TextAnchor.UpperLeft;
                _name.style.whiteSpace = WhiteSpace.Normal;
                _name.style.marginBottom = 4;
                Root.Add(_name);

                _valueRoot = new VisualElement();
                _valueRoot.style.flexDirection = FlexDirection.Column;
                _valueRoot.style.alignItems = Align.Stretch;
                _valueRoot.style.flexGrow = 1;
                _valueRoot.style.flexShrink = 0;
                _valueRoot.style.minWidth = 0;
                Root.Add(_valueRoot);
            }

            public void SetPanelWidth(float panelWidth)
            {
                bool compact = panelWidth < 300f;
                Root.style.paddingLeft = compact ? 7 : 9;
                Root.style.paddingRight = compact ? 7 : 9;
                _name.style.fontSize = compact ? 9 : 10;
                for (int i = 0; i < _valueLines.Count; i++)
                {
                    _valueLines[i].style.fontSize = compact ? 10 : 11;
                }
            }

            public void SetValue(string value)
            {
                value = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
                if (_lastValue == value)
                {
                    return;
                }

                _lastValue = value;
                string displayValue = InsertLineBreaksForLongSegments(value);
                string[] lines = displayValue.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                EnsureValueLineCount(lines.Length);
                Root.tooltip = _name.text + "\n" + value;
                for (int i = 0; i < _valueLines.Count; i++)
                {
                    bool active = i < lines.Length;
                    var line = _valueLines[i];
                    line.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                    if (active)
                    {
                        string lineValue = string.IsNullOrWhiteSpace(lines[i]) ? "-" : lines[i].Trim();
                        if (line.text != lineValue)
                        {
                            line.text = lineValue;
                        }

                        line.tooltip = value;
                    }
                }
            }

            private void EnsureValueLineCount(int count)
            {
                while (_valueLines.Count < count)
                {
                    var line = new Label("-");
                    line.style.flexShrink = 0;
                    line.style.minWidth = 0;
                    line.style.width = Length.Percent(100);
                    line.style.color = EasyMicPipelineStyles.PrimaryText;
                    line.style.fontSize = 11;
                    line.style.whiteSpace = WhiteSpace.Normal;
                    line.style.marginBottom = 2;
                    line.style.unityTextAlign = TextAnchor.UpperLeft;
                    _valueLines.Add(line);
                    _valueRoot.Add(line);
                }
            }

            private static string InsertLineBreaksForLongSegments(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return "-";
                }

                var builder = new StringBuilder(value.Length + value.Length / LongSegmentWrapLength);
                int segmentLength = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    builder.Append(c);

                    if (char.IsWhiteSpace(c) || c == '/' || c == '\\' || c == '|' || c == ',' || c == ':' || c == ';')
                    {
                        segmentLength = 0;
                        continue;
                    }

                    segmentLength++;
                    if (segmentLength >= LongSegmentWrapLength && i < value.Length - 1)
                    {
                        builder.Append('\n');
                        segmentLength = 0;
                    }
                }

                return builder.ToString();
            }
        }
    }
}
#endif
