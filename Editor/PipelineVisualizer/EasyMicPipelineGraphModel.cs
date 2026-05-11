#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Eitan.EasyMic.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Editor
{
    internal enum EasyMicPipelineViewMode
    {
        Playback = 0,
        Recording = 1
    }

    internal sealed class EasyMicPipelineGraphModel
    {
        private const float GroupPaddingX = 36f;
        private const float GroupPaddingTop = 54f;
        private const float GroupPaddingBottom = 32f;
        private const float NodeHeight = 118f;
        private const float GroupMinWidth = 520f;
        private const float GroupMinHeight = 190f;

        private readonly List<EasyMicPipelineGraphNode> _nodes = new List<EasyMicPipelineGraphNode>(128);
        private readonly List<EasyMicPipelineGraphEdge> _edges = new List<EasyMicPipelineGraphEdge>(160);
        private readonly List<EasyMicPipelineGraphGroup> _groups = new List<EasyMicPipelineGraphGroup>(16);
        private readonly Dictionary<string, EasyMicPipelineGraphNode> _nodeIndex = new Dictionary<string, EasyMicPipelineGraphNode>(128);
        private int _nextLaneSlot;

        public IReadOnlyList<EasyMicPipelineGraphNode> Nodes => _nodes;
        public IReadOnlyList<EasyMicPipelineGraphEdge> Edges => _edges;
        public IReadOnlyList<EasyMicPipelineGraphGroup> Groups => _groups;
        public int TopologyHash { get; private set; }
        public string StatusText { get; private set; } = "No snapshot";
        public EasyMicPlaybackPipelineSnapshot Playback { get; private set; }
        public EasyMicRecordingPipelineSnapshot[] Recordings { get; private set; } = Array.Empty<EasyMicRecordingPipelineSnapshot>();
        public EasyMicPipelineViewMode ViewMode { get; private set; }

        public void Capture(EasyMicPipelineViewMode viewMode)
        {
            _nodes.Clear();
            _edges.Clear();
            _groups.Clear();
            _nodeIndex.Clear();
            _nextLaneSlot = 0;
            ViewMode = viewMode;

            var playback = AudioSystem.Instance.PipelineSnapshot;
            var recordings = Eitan.EasyMic.EasyMicAPI.GetRecordingPipelineSnapshots();
            recordings = recordings ?? Array.Empty<EasyMicRecordingPipelineSnapshot>();
            Playback = playback;
            Recordings = recordings;

            if (viewMode == EasyMicPipelineViewMode.Playback)
            {
                BuildPlayback(playback);
            }

            if (viewMode == EasyMicPipelineViewMode.Recording)
            {
                BuildRecordings(Recordings);
            }

            NormalizeLayoutToOrigin();
            TopologyHash = ComputeTopologyHash();
            StatusText = EasyMicEditorLocalization.PipelineText(
                EasyMicPipelineTextKey.StatusFormat,
                EasyMicEditorLocalization.PipelineViewLabel(viewMode),
                playback.IsRunning
                    ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RunningLower)
                    : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.StoppedLower),
                recordings.Length,
                recordings.Length == 1
                    ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureSessionSingular)
                    : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureSessionPlural),
                _nodes.Count,
                _edges.Count);
        }

        public bool TryGetNode(string id, out EasyMicPipelineGraphNode node)
        {
            return _nodeIndex.TryGetValue(id, out node);
        }

        public Rect GetContentBounds()
        {
            if (_nodes.Count == 0)
            {
                return new Rect(0f, 0f, 600f, 260f);
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                float height = 118f;
                minX = Mathf.Min(minX, node.Position.x);
                minY = Mathf.Min(minY, node.Position.y);
                maxX = Mathf.Max(maxX, node.Position.x + node.Width);
                maxY = Mathf.Max(maxY, node.Position.y + height);
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                minX = Mathf.Min(minX, group.Bounds.xMin);
                minY = Mathf.Min(minY, group.Bounds.yMin);
                maxX = Mathf.Max(maxX, group.Bounds.xMax);
                maxY = Mathf.Max(maxY, group.Bounds.yMax);
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public string CreateDiagnosticsReport()
        {
            var builder = new StringBuilder(4096);
            builder.AppendLine(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DiagnosticsTitle));
            builder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine(StatusText);
            builder.AppendLine();
            builder.AppendLine(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DiagnosticsNodes));
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                builder.Append(node.Id).Append(" | ")
                    .Append(node.Kind).Append(" | ")
                    .Append(node.Thread).Append(" | ")
                    .Append(node.Title).Append(" | ")
                    .Append(node.Subtitle).AppendLine();
                if (!string.IsNullOrEmpty(node.Metrics))
                {
                    builder.Append("  ").AppendLine(node.Metrics.Replace("\n", "\n  "));
                }
            }

            builder.AppendLine();
            builder.AppendLine(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DiagnosticsEdges));
            for (int i = 0; i < _edges.Count; i++)
            {
                var edge = _edges[i];
                builder.Append(edge.FromId).Append(" -> ").Append(edge.ToId);
                if (edge.IsBoundary)
                {
                    builder.Append(" [").Append(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DiagnosticsBoundary)).Append("]");
                }
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private sealed class PlaybackMixerLayout
        {
            private PlaybackMixerLayout(int rows, int outputColumn, PlaybackMixerLayout[] children)
            {
                Rows = rows;
                OutputColumn = outputColumn;
                Children = children;
            }

            public int Rows { get; }
            public int OutputColumn { get; }
            public PlaybackMixerLayout[] Children { get; }

            public float CenterLane(float startLane)
            {
                return startLane + Mathf.Max(0, Rows - 1) * 0.5f;
            }

            public static PlaybackMixerLayout Plan(EasyMicMixerSnapshot mixer)
            {
                var children = mixer.Children.Length == 0
                    ? Array.Empty<PlaybackMixerLayout>()
                    : new PlaybackMixerLayout[mixer.Children.Length];
                int rows = mixer.Sources.Length;
                int deepestInputColumn = 1;

                for (int i = 0; i < mixer.Sources.Length; i++)
                {
                    deepestInputColumn = Mathf.Max(deepestInputColumn, 1 + mixer.Sources[i].Processors.Length);
                }

                for (int i = 0; i < mixer.Children.Length; i++)
                {
                    var child = Plan(mixer.Children[i]);
                    children[i] = child;
                    rows += child.Rows;
                    deepestInputColumn = Mathf.Max(deepestInputColumn, child.OutputColumn);
                }

                if (rows == 0)
                {
                    rows = 1;
                }

                int mixerColumn = deepestInputColumn + mixer.Processors.Length + 1;
                return new PlaybackMixerLayout(rows, mixerColumn, children);
            }
        }

        private void BuildPlayback(EasyMicPlaybackPipelineSnapshot playback)
        {
            int firstNode = _nodes.Count;
            var mixerPlan = PlaybackMixerLayout.Plan(playback.MasterMixer);
            float centerLane = mixerPlan.CenterLane(0f);
            int mixerIdColumn = mixerPlan.OutputColumn;

            string deviceId = "playback-device";
            string renderQueueId = "playback-render-queue";
            string transportId = "playback-render-transport";
            string mixerId = "playback-master";

            AddNode(new EasyMicPipelineGraphNode(
                deviceId,
                playback.DeviceName.Length > 0 ? playback.DeviceName : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlaybackDeviceFallback),
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.MiniaudioOutput),
                EasyMicPipelineNodeKind.PlaybackDevice,
                EasyMicPipelineThreadKind.NativeThread,
                FormatDevice(playback.SampleRate, playback.Channels, playback.LatencyProfile),
                playback.IsRunning ? 1f : 0.15f,
                Pos(mixerIdColumn + 3, centerLane),
                230));

            AddNode(new EasyMicPipelineGraphNode(
                renderQueueId,
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RenderQueue),
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WorkerToNativeBoundary),
                EasyMicPipelineNodeKind.Queue,
                EasyMicPipelineThreadKind.AudioThread,
                FormatQueue(playback.Telemetry.LastQueueDepthSamples, playback.Telemetry.MaxQueueDepthSamples, playback.SampleRate, playback.Channels),
                QueueActivity(playback.Telemetry.LastQueueDepthSamples, playback.Telemetry.MaxQueueDepthSamples),
                Pos(mixerIdColumn + 2, centerLane),
                190));

            AddNode(new EasyMicPipelineGraphNode(
                transportId,
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlaybackTransport),
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PreRenderWorker),
                EasyMicPipelineNodeKind.Transport,
                EasyMicPipelineThreadKind.WorkerThread,
                FormatTelemetry(playback.Telemetry),
                playback.IsRunning ? 0.85f : 0.1f,
                Pos(mixerIdColumn + 1, centerLane),
                215));

            AddMixerTree(playback.MasterMixer, mixerId, EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.MasterMixer), mixerPlan, 0f, transportId);
            AddEdge(transportId, renderQueueId, true);
            AddEdge(renderQueueId, deviceId, true);
            AddGroupAroundNodes(
                "group-playback",
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlaybackPipeline),
                playback.IsRunning
                    ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ActiveOutputGraph)
                    : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.OutputGraphIdle),
                firstNode,
                _nodes.Count);
        }

        private void AddMixerTree(EasyMicMixerSnapshot mixer, string mixerId, string title, PlaybackMixerLayout layout, float startLane, string downstreamId)
        {
            float mixerLane = layout.CenterLane(startLane);
            AddNode(new EasyMicPipelineGraphNode(
                mixerId,
                title,
                EasyMicEditorLocalization.PipelineText(
                    EasyMicPipelineTextKey.MixerSubtitleFormat,
                    mixer.Sources.Length,
                    mixer.Sources.Length == 1
                        ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.SourceSingular)
                        : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.SourcePlural),
                    mixer.Children.Length,
                    mixer.Children.Length == 1
                        ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ChildMixerSingular)
                        : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ChildMixerPlural)),
                EasyMicPipelineNodeKind.Mixer,
                EasyMicPipelineThreadKind.WorkerThread,
                FormatMixer(mixer),
                mixer.Mute ? 0.15f : 0.75f,
                Pos(layout.OutputColumn, mixerLane),
                210));
            AddEdge(mixerId, downstreamId, false);

            string previous = mixerId;
            for (int i = mixer.Processors.Length - 1; i >= 0; i--)
            {
                string id = mixerId + "-processor-" + i;
                AddProcessorNode(id, mixer.Processors[i], layout.OutputColumn - (mixer.Processors.Length - i), mixerLane, EasyMicPipelineThreadKind.WorkerThread);
                AddEdge(id, previous, false);
                previous = id;
            }

            float lane = startLane;
            for (int i = 0; i < mixer.Sources.Length; i++)
            {
                AddPlaybackSource(mixer.Sources[i], mixerId + "-source-" + i, 0, lane, previous);
                lane += 1f;
            }

            for (int i = 0; i < mixer.Children.Length; i++)
            {
                var childLayout = layout.Children[i];
                AddMixerTree(mixer.Children[i], mixerId + "-mixer-" + i, mixer.Children[i].Name, childLayout, lane, previous);
                lane += childLayout.Rows;
            }
        }

        private void AddPlaybackSource(EasyMicPlaybackSourceSnapshot source, string sourceId, int x, float y, string downstreamId)
        {
            string sourceNodeId = sourceId + "-node";
            string queueNodeId = sourceId + "-queue";

            AddNode(new EasyMicPipelineGraphNode(
                sourceNodeId,
                source.Name,
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlaybackSource),
                EasyMicPipelineNodeKind.PlaybackSource,
                EasyMicPipelineThreadKind.MainThread,
                FormatSource(source),
                source.IsPlaying && !source.Mute ? 0.75f : 0.2f,
                Pos(x, y),
                210));

            AddNode(new EasyMicPipelineGraphNode(
                queueNodeId,
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.SourceQueue),
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.StreamBuffer),
                EasyMicPipelineNodeKind.Queue,
                EasyMicPipelineThreadKind.WorkerThread,
                FormatQueue(source.QueuedSamples, source.QueuedSamples + source.FreeSamples, (uint)source.SampleRate, (uint)source.Channels),
                QueueActivity(source.QueuedSamples, source.QueuedSamples + source.FreeSamples),
                Pos(x + 1, y),
                180));

            AddEdge(sourceNodeId, queueNodeId, true);

            string previous = queueNodeId;
            for (int i = 0; i < source.Processors.Length; i++)
            {
                string processorId = sourceId + "-processor-" + i;
                AddProcessorNode(processorId, source.Processors[i], x + 2 + i, y, EasyMicPipelineThreadKind.WorkerThread);
                AddEdge(previous, processorId, source.Processors[i].ThreadKind != EasyMicPipelineThreadKind.WorkerThread);
                previous = processorId;
            }

            AddEdge(previous, downstreamId, false);
        }

        private void BuildRecordings(EasyMicRecordingPipelineSnapshot[] recordings)
        {
            if (recordings.Length == 0)
            {
                int firstNode = _nodes.Count;
                int idleY = AllocateLane();
                AddNode(new EasyMicPipelineGraphNode(
                    "capture-idle",
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureIdle),
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WaitingForRecording),
                    EasyMicPipelineNodeKind.CaptureDevice,
                    EasyMicPipelineThreadKind.MainThread,
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.StartRecordingToInspect),
                    0.1f,
                    Pos(0, idleY),
                    230));
                AddGroupAroundNodes(
                    "group-capture-idle",
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RecordingPipelines),
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.NoActiveCaptureSessions),
                    firstNode,
                    _nodes.Count);
                return;
            }

            int firstRecordingNode = _nodes.Count;
            int outputColumn = 4;
            for (int i = 0; i < recordings.Length; i++)
            {
                outputColumn = Mathf.Max(outputColumn, 4 + recordings[i].Processors.Length);
            }

            for (int i = 0; i < recordings.Length; i++)
            {
                int y = AllocateLane();
                var recording = recordings[i];
                string prefix = "capture-" + recording.Handle.Id;
                string deviceId = prefix + "-device";
                string queueId = prefix + "-queue";
                string transportId = prefix + "-transport";
                string outputId = prefix + "-output";

                AddNode(new EasyMicPipelineGraphNode(
                    deviceId,
                    recording.Info.Device.Name,
                    recording.IsUsingFallback
                        ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureDeviceFallback)
                        : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureDevice),
                    EasyMicPipelineNodeKind.CaptureDevice,
                    EasyMicPipelineThreadKind.NativeThread,
                    FormatRecording(recording),
                    recording.Info.IsActive ? 1f : 0.1f,
                    Pos(0, y),
                    230));

                AddNode(new EasyMicPipelineGraphNode(
                    queueId,
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureQueueNode),
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.NativeToManagedBoundary),
                    EasyMicPipelineNodeKind.Queue,
                    EasyMicPipelineThreadKind.WorkerThread,
                    FormatQueue(recording.Info.Telemetry.LastQueueDepthSamples, recording.Info.Telemetry.MaxQueueDepthSamples, (uint)recording.Info.SampleRate, (uint)recording.Info.Channel),
                    QueueActivity(recording.Info.Telemetry.LastQueueDepthSamples, recording.Info.Telemetry.MaxQueueDepthSamples),
                    Pos(1, y),
                    185));

                AddNode(new EasyMicPipelineGraphNode(
                    transportId,
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureTransport),
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ManagedDrainWorker),
                    EasyMicPipelineNodeKind.Transport,
                    EasyMicPipelineThreadKind.WorkerThread,
                    FormatTelemetry(recording.Info.Telemetry),
                    recording.Info.IsActive ? 0.85f : 0.1f,
                    Pos(2, y),
                    215));

                AddEdge(deviceId, queueId, true);
                AddEdge(queueId, transportId, true);

                string previous = transportId;
                for (int p = 0; p < recording.Processors.Length; p++)
                {
                    string processorId = prefix + "-processor-" + p;
                    AddProcessorNode(processorId, recording.Processors[p], 3 + p, y, EasyMicPipelineThreadKind.WorkerThread);
                    AddEdge(previous, processorId, recording.Processors[p].ThreadKind != EasyMicPipelineThreadKind.WorkerThread);
                    previous = processorId;
                }

                AddNode(new EasyMicPipelineGraphNode(
                    outputId,
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureConsumers),
                    EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureConsumersSubtitle),
                    EasyMicPipelineNodeKind.Output,
                    EasyMicPipelineThreadKind.MainThread,
                    EasyMicEditorLocalization.PipelineText(
                        EasyMicPipelineTextKey.ProcessorCountFormat,
                        recording.Info.ProcessorCount,
                        recording.Info.ProcessorCount == 1
                            ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ProcessorSingular)
                            : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ProcessorPlural)),
                    recording.Info.IsActive ? 0.55f : 0.1f,
                    Pos(outputColumn, y),
                    210));
                AddEdge(previous, outputId, false);
            }

            AddGroupAroundNodes(
                "group-capture",
                EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.RecordingPipelines),
                EasyMicEditorLocalization.PipelineText(
                    EasyMicPipelineTextKey.ActiveCaptureSessionsFormat,
                    recordings.Length,
                    recordings.Length == 1
                        ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureSessionSingular)
                        : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.CaptureSessionPlural)),
                firstRecordingNode,
                _nodes.Count);
        }

        private void AddGroupAroundNodes(string id, string title, string subtitle, int startInclusive, int endExclusive)
        {
            Rect bounds = ComputeNodeRangeBounds(startInclusive, endExclusive);
            _groups.Add(new EasyMicPipelineGraphGroup(
                id,
                title,
                subtitle,
                bounds));
        }

        private Rect ComputeNodeRangeBounds(int startInclusive, int endExclusive)
        {
            if (startInclusive < 0 || endExclusive <= startInclusive || startInclusive >= _nodes.Count)
            {
                return new Rect(44f, 44f, GroupMinWidth, GroupMinHeight);
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            int end = Mathf.Min(endExclusive, _nodes.Count);
            for (int i = startInclusive; i < end; i++)
            {
                var node = _nodes[i];
                minX = Mathf.Min(minX, node.Position.x);
                minY = Mathf.Min(minY, node.Position.y);
                maxX = Mathf.Max(maxX, node.Position.x + node.Width);
                maxY = Mathf.Max(maxY, node.Position.y + NodeHeight);
            }

            float x = minX - GroupPaddingX;
            float y = minY - GroupPaddingTop;
            float width = Mathf.Max(GroupMinWidth, maxX - minX + GroupPaddingX * 2f);
            float height = Mathf.Max(GroupMinHeight, maxY - minY + GroupPaddingTop + GroupPaddingBottom);
            return new Rect(x, y, width, height);
        }

        private void AddProcessorNode(string id, EasyMicProcessorSnapshot processor, int x, float y, EasyMicPipelineThreadKind fallbackThread)
        {
            var thread = processor.ThreadKind == EasyMicPipelineThreadKind.Unknown ? fallbackThread : processor.ThreadKind;
            AddNode(new EasyMicPipelineGraphNode(
                id,
                processor.TypeName,
                processor.IsReader
                    ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.AsyncReader)
                    : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DspStage),
                processor.NodeKind,
                thread,
                EasyMicEditorLocalization.PipelineText(
                    EasyMicPipelineTextKey.ProcessorStateFormat,
                    processor.Order + 1,
                    processor.IsEnabled
                        ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.EnabledLower)
                        : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.BypassedLower)),
                processor.IsEnabled ? 0.7f : 0.15f,
                Pos(x, y),
                190));
        }

        private void AddNode(EasyMicPipelineGraphNode node)
        {
            _nodes.Add(node);
            _nodeIndex[node.Id] = node;
        }

        private void AddEdge(string from, string to, bool boundary)
        {
            _edges.Add(new EasyMicPipelineGraphEdge(from, to, boundary));
        }

        private void NormalizeLayoutToOrigin()
        {
            Rect bounds = GetContentBounds();
            if (Mathf.Abs(bounds.xMin) < 0.001f && Mathf.Abs(bounds.yMin) < 0.001f)
            {
                return;
            }

            Vector2 offset = new Vector2(bounds.xMin, bounds.yMin);
            _nodeIndex.Clear();
            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                var shifted = new EasyMicPipelineGraphNode(
                    node.Id,
                    node.Title,
                    node.Subtitle,
                    node.Kind,
                    node.Thread,
                    node.Metrics,
                    node.Activity,
                    node.Position - offset,
                    node.Width);
                _nodes[i] = shifted;
                _nodeIndex[shifted.Id] = shifted;
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                Rect shiftedBounds = group.Bounds;
                shiftedBounds.position -= offset;
                _groups[i] = new EasyMicPipelineGraphGroup(group.Id, group.Title, group.Subtitle, shiftedBounds);
            }
        }

        private int AllocateLane()
        {
            return _nextLaneSlot++;
        }

        private static Vector2 Pos(int x, float lane)
        {
            return new Vector2(GroupPaddingX + x * 245, GroupPaddingTop + lane * 170);
        }

        private int ComputeTopologyHash()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < _nodes.Count; i++)
                {
                    hash = hash * 31 + ViewMode.GetHashCode();
                    hash = hash * 31 + _nodes[i].Id.GetHashCode();
                    hash = hash * 31 + _nodes[i].Kind.GetHashCode();
                }
                for (int i = 0; i < _edges.Count; i++)
                {
                    hash = hash * 31 + _edges[i].FromId.GetHashCode();
                    hash = hash * 31 + _edges[i].ToId.GetHashCode();
                }
                return hash;
            }
        }

        private static string FormatDevice(uint sampleRate, uint channels, EasyMicLatencyProfile profile)
        {
            return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.DeviceMetricsFormat, sampleRate, channels, profile);
        }

        private static string FormatRecording(EasyMicRecordingPipelineSnapshot recording)
        {
            return EasyMicEditorLocalization.PipelineText(
                EasyMicPipelineTextKey.RecordingMetricsFormat,
                (int)recording.Info.SampleRate,
                (int)recording.Info.Channel,
                recording.Info.NativeCallbackCount);
        }

        private static string FormatTelemetry(EasyMicTelemetrySnapshot telemetry)
        {
            return EasyMicEditorLocalization.PipelineText(
                EasyMicPipelineTextKey.TelemetryMetricsFormat,
                telemetry.CallbackAverageMicroseconds,
                telemetry.CallbackMaxMicroseconds,
                telemetry.TransportUnderruns,
                telemetry.TransportOverruns,
                telemetry.FramesReceived);
        }

        private static string FormatQueue(int current, int max, uint sampleRate, uint channels)
        {
            double ms = sampleRate == 0 || channels == 0 ? 0.0 : current * 1000.0 / (sampleRate * channels);
            return EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.QueueMetricsFormat, current, ms, max);
        }

        private static string FormatMixer(EasyMicMixerSnapshot mixer)
        {
            return EasyMicEditorLocalization.PipelineText(
                EasyMicPipelineTextKey.MixerMetricsFormat,
                mixer.Volume,
                mixer.Mute ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.MutedLower) : string.Empty,
                mixer.Solo ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.SoloLower) : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ActiveLower),
                mixer.Processors.Length);
        }

        private static string FormatSource(EasyMicPlaybackSourceSnapshot source)
        {
            return EasyMicEditorLocalization.PipelineText(
                EasyMicPipelineTextKey.SourceMetricsFormat,
                source.SampleRate,
                source.Channels,
                source.BufferedSeconds * 1000.0,
                source.Volume,
                source.IsPlaying
                    ? EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PlayingLower)
                    : EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.PausedLower));
        }

        private static float QueueActivity(int current, int max)
        {
            if (max <= 0)
            {
                return current > 0 ? 0.35f : 0.1f;
            }

            return Mathf.Clamp01(current / (float)max);
        }
    }

    internal readonly struct EasyMicPipelineGraphGroup
    {
        public EasyMicPipelineGraphGroup(string id, string title, string subtitle, Rect bounds)
        {
            Id = id;
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            Bounds = bounds;
        }

        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public Rect Bounds { get; }

        public bool Matches(string filter)
        {
            return Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || Subtitle.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal readonly struct EasyMicPipelineGraphEdge
    {
        public EasyMicPipelineGraphEdge(string fromId, string toId, bool isBoundary)
        {
            FromId = fromId;
            ToId = toId;
            IsBoundary = isBoundary;
        }

        public string FromId { get; }
        public string ToId { get; }
        public bool IsBoundary { get; }
    }

    internal sealed class EasyMicPipelineGraphNode
    {
        public EasyMicPipelineGraphNode(
            string id,
            string title,
            string subtitle,
            EasyMicPipelineNodeKind kind,
            EasyMicPipelineThreadKind thread,
            string metrics,
            float activity,
            Vector2 position,
            float width)
        {
            Id = id;
            Title = string.IsNullOrEmpty(title) ? kind.ToString() : title;
            Subtitle = subtitle ?? string.Empty;
            Kind = kind;
            Thread = thread;
            Metrics = metrics ?? string.Empty;
            Activity = Mathf.Clamp01(activity);
            Position = position;
            Width = width;
        }

        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public EasyMicPipelineNodeKind Kind { get; }
        public EasyMicPipelineThreadKind Thread { get; }
        public string Metrics { get; }
        public float Activity { get; }
        public Vector2 Position { get; }
        public float Width { get; }

        public bool Matches(string filter)
        {
            return Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || Subtitle.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || Metrics.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || Kind.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || Thread.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
