#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Eitan.EasyMic.Runtime;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eitan.EasyMic.Editor
{
    public sealed class EasyMicPipelineGraphWindow : EditorWindow
    {
        private const double TopologyRefreshInterval = 0.5;
        private const double TelemetryRefreshInterval = 1.0 / 15.0;
        private const string InspectorDockKey = "Eitan.EasyMic.PipelineVisualizer.InspectorDock";
        private const string InspectorWidthKey = "Eitan.EasyMic.PipelineVisualizer.InspectorWidth";
        private const string MiniMapDockKey = "Eitan.EasyMic.PipelineVisualizer.MiniMapDock";
        private const string PipelineSplitHeightKey = "Eitan.EasyMic.PipelineVisualizer.PipelineSplitHeight";
        private const float MiniMapWidth = 190f;
        private const float MiniMapHeight = 128f;
        private const float MiniMapMargin = 14f;
        private static readonly Vector2 DefaultWindowSize = new Vector2(1280f, 820f);
        private static readonly Vector2 MinimumWindowSize = new Vector2(980f, 640f);

        private readonly EasyMicPipelineGraphModel _playbackModel = new EasyMicPipelineGraphModel();
        private readonly EasyMicPipelineGraphModel _recordingModel = new EasyMicPipelineGraphModel();
        private EasyMicPipelineGraphView _playbackGraphView;
        private EasyMicPipelineGraphView _recordingGraphView;
        private EasyMicPipelineGraphView _graphView;
        private VisualElement _graphHost;
        private VisualElement _playbackCanvas;
        private VisualElement _recordingCanvas;
        private VisualElement _emptyCanvasState;
        private TwoPaneSplitView _pipelineSplitView;
        private EasyMicPipelineMiniMapState _playbackMiniMap;
        private EasyMicPipelineMiniMapState _recordingMiniMap;
        private EasyMicPipelineGraphModel ActiveModel => _viewMode == EasyMicPipelineViewMode.Playback ? _playbackModel : _recordingModel;
        private EasyMicPipelineDetailsPanel _detailsPanel;
        private TwoPaneSplitView _splitView;
        private Label _statusLabel;
        private VisualElement _toolbar;
        private Button _miniMapButton;
        private Button _recordingViewButton;
        private Button _playbackViewButton;
        private bool _uiReady;
        private bool _responsiveRebuildInProgress;
        private bool _toolbarCompact;
        private double _nextTopologyRefresh;
        private double _nextTelemetryRefresh;
        private int _playbackTopologyHash;
        private int _recordingTopologyHash;
        private EasyMicInspectorDock _inspectorDock;
        private EasyMicPipelineViewMode _viewMode = EasyMicPipelineViewMode.Playback;
        private float _inspectorWidth;
        private float _pipelineSplitHeight;
        private bool _miniMapVisible = true;
        private bool _playbackVisible = true;
        private bool _recordingVisible = true;

        [MenuItem("Window/EasyMic/Pipeline Visualizer")]
        public static void ShowWindow()
        {
            var window = GetWindow<EasyMicPipelineGraphWindow>();
            window.titleContent = new GUIContent("EasyMic Pipeline");
            window.minSize = MinimumWindowSize;
            if (window.position.width < MinimumWindowSize.x || window.position.height < MinimumWindowSize.y)
            {
                Rect main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(
                    main.x + Mathf.Max(0f, (main.width - DefaultWindowSize.x) * 0.5f),
                    main.y + Mathf.Max(0f, (main.height - DefaultWindowSize.y) * 0.5f),
                    Mathf.Min(DefaultWindowSize.x, Mathf.Max(MinimumWindowSize.x, main.width - 80f)),
                    Mathf.Min(DefaultWindowSize.y, Mathf.Max(MinimumWindowSize.y, main.height - 80f)));
            }

            window.Show();
        }

        private void OnEnable()
        {
            minSize = MinimumWindowSize;
            _uiReady = false;
            try
            {
                titleContent = new GUIContent(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WindowTitle));
                BuildUi();
                _uiReady = true;
            }
            catch (Exception ex)
            {
                ShowFallbackUi(ex);
            }

            EasyMicEditorLocalization.ProjectSettingsLanguageChanged -= OnLocalizationChanged;
            EasyMicEditorLocalization.ProjectSettingsLanguageChanged += OnLocalizationChanged;
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;

            if (_uiReady)
            {
                rootVisualElement.schedule.Execute(ForceRefresh).ExecuteLater(1);
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            EasyMicEditorLocalization.ProjectSettingsLanguageChanged -= OnLocalizationChanged;
            _graphView = null;
            _detailsPanel = null;
        }

        private void OnLocalizationChanged()
        {
            _uiReady = false;
            try
            {
                titleContent = new GUIContent(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WindowTitle));
                BuildUi();
                _uiReady = true;
                rootVisualElement.schedule.Execute(ForceRefresh).ExecuteLater(1);
            }
            catch (Exception ex)
            {
                ShowFallbackUi(ex);
            }
        }

        private void ShowFallbackUi(Exception exception)
        {
            _uiReady = false;
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = EasyMicPipelineStyles.CanvasBackground;

            var panel = new VisualElement();
            panel.style.flexGrow = 1;
            panel.style.alignItems = Align.Center;
            panel.style.justifyContent = Justify.Center;
            panel.style.paddingLeft = 24;
            panel.style.paddingRight = 24;
            rootVisualElement.Add(panel);

            var title = new Label("EasyMic Pipeline Visualizer");
            title.style.color = EasyMicPipelineStyles.PrimaryText;
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(title);

            var message = new Label("The visualizer could not finish initializing. Check the Console for details, then reopen the window.");
            message.style.color = EasyMicPipelineStyles.SecondaryText;
            message.style.fontSize = 12;
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.marginTop = 8;
            panel.Add(message);

            Debug.LogException(exception);
        }

        private void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = EasyMicPipelineStyles.CanvasBackground;
            _inspectorDock = (EasyMicInspectorDock)EditorPrefs.GetInt(InspectorDockKey, (int)EasyMicInspectorDock.Left);
            _inspectorWidth = ClampInspectorWidth(EditorPrefs.GetFloat(InspectorWidthKey, 340f));
            _pipelineSplitHeight = Mathf.Clamp(EditorPrefs.GetFloat(PipelineSplitHeightKey, 360f), 180f, 1200f);
            _toolbar = new VisualElement();
            _toolbar.style.height = 38;
            _toolbar.style.flexDirection = FlexDirection.Row;
            _toolbar.style.alignItems = Align.Center;
            _toolbar.style.paddingLeft = 12;
            _toolbar.style.paddingRight = 12;
            _toolbar.style.backgroundColor = new Color(0.105f, 0.11f, 0.118f, 0.98f);
            _toolbar.style.borderBottomColor = new Color(0.23f, 0.24f, 0.25f, 0.72f);
            _toolbar.style.borderBottomWidth = 1;

            var viewSegment = new VisualElement();
            viewSegment.style.flexDirection = FlexDirection.Row;
            viewSegment.style.height = 26;
            viewSegment.style.paddingLeft = 2;
            viewSegment.style.paddingRight = 2;
            viewSegment.style.paddingTop = 2;
            viewSegment.style.paddingBottom = 2;
            viewSegment.style.borderTopLeftRadius = 7;
            viewSegment.style.borderTopRightRadius = 7;
            viewSegment.style.borderBottomLeftRadius = 7;
            viewSegment.style.borderBottomRightRadius = 7;
            viewSegment.style.backgroundColor = new Color(0.06f, 0.064f, 0.07f, 0.72f);

            _playbackViewButton = CreateViewButton(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ToolbarPlayback), EasyMicPipelineViewMode.Playback);
            _recordingViewButton = CreateViewButton(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ToolbarRecording), EasyMicPipelineViewMode.Recording);
            viewSegment.Add(_playbackViewButton);
            viewSegment.Add(_recordingViewButton);
            UpdateViewButtons();

            var frameButton = CreateToolbarButton(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ToolbarFit), FrameGraph);
            frameButton.tooltip = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ToolbarFitTooltip);
            _miniMapButton = CreateToolbarButton(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ToolbarMap), ToggleMiniMap);
            _miniMapButton.tooltip = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ToolbarMapTooltip);
            UpdateMiniMapButton();
            _statusLabel = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.NoSnapshot));
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _statusLabel.style.color = EasyMicPipelineStyles.SecondaryText;
            _statusLabel.style.fontSize = 11;
            _statusLabel.style.marginLeft = 12;
            _statusLabel.style.flexShrink = 1;
            _statusLabel.style.minWidth = 0;

            _toolbar.Add(viewSegment);
            var viewSpacerRight = new VisualElement();
            viewSpacerRight.style.flexGrow = 1;
            _toolbar.Add(viewSpacerRight);
            _toolbar.Add(frameButton);
            _toolbar.Add(_miniMapButton);
            _toolbar.Add(_statusLabel);
            rootVisualElement.Add(_toolbar);

            _detailsPanel = new EasyMicPipelineDetailsPanel();
            _detailsPanel.DockChanged += SetInspectorDock;
            _detailsPanel.RuntimeOverviewRequested += ShowRuntimeOverview;

            _graphHost = new VisualElement();
            _graphHost.style.flexGrow = 1;
            _graphHost.style.position = Position.Relative;
            _graphHost.style.flexDirection = FlexDirection.Column;
            _playbackGraphView = CreateGraphView(EasyMicPipelineViewMode.Playback);
            _recordingGraphView = CreateGraphView(EasyMicPipelineViewMode.Recording);
            _graphView = _playbackGraphView;
            _playbackCanvas = CreateGraphCanvas(_playbackGraphView);
            _recordingCanvas = CreateGraphCanvas(_recordingGraphView);
            _emptyCanvasState = CreateEmptyCanvasState();
            CreateMiniMaps();
            RebuildPipelineCanvasLayout();
            ApplyCanvasFocus();

            RebuildDockLayout();
            rootVisualElement.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            ApplyResponsiveLayout();
        }

        private EasyMicPipelineGraphView CreateGraphView(EasyMicPipelineViewMode mode)
        {
            var view = new EasyMicPipelineGraphView();
            view.OnNodeSelected += node =>
            {
                SetViewMode(mode, clearSelection: false, forceRefresh: false);
                _detailsPanel.SetSelection(node);
            };
            view.OnEmptyGraphSelected += () =>
            {
                SetViewMode(mode, clearSelection: false, forceRefresh: false);
                ShowRuntimeOverview();
            };
            view.viewTransformChanged += graph =>
            {
                if (graph == _graphView)
                {
                    GetMiniMapState(mode)?.Map.SetViewport((EasyMicPipelineGraphView)graph);
                }
            };
            return view;
        }

        private static VisualElement CreateGraphCanvas(EasyMicPipelineGraphView graphView)
        {
            var canvas = new VisualElement();
            canvas.style.flexGrow = 1;
            canvas.style.flexBasis = 0;
            canvas.style.minHeight = 160;
            canvas.style.position = Position.Relative;
            canvas.style.borderTopColor = EasyMicPipelineStyles.Separator;
            canvas.style.borderTopWidth = 1;
            canvas.Add(graphView);
            return canvas;
        }

        private VisualElement CreateEmptyCanvasState()
        {
            var root = new VisualElement();
            root.style.flexGrow = 1;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = EasyMicPipelineStyles.CanvasBackground;

            var panel = new VisualElement();
            panel.style.width = Length.Percent(100);
            panel.style.maxWidth = 420;
            panel.style.paddingLeft = 24;
            panel.style.paddingRight = 24;
            panel.style.paddingTop = 22;
            panel.style.paddingBottom = 22;
            panel.style.borderTopColor = EasyMicPipelineStyles.Separator;
            panel.style.borderRightColor = EasyMicPipelineStyles.Separator;
            panel.style.borderBottomColor = EasyMicPipelineStyles.Separator;
            panel.style.borderLeftColor = EasyMicPipelineStyles.Separator;
            panel.style.borderTopWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderBottomWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderTopLeftRadius = 8;
            panel.style.borderTopRightRadius = 8;
            panel.style.borderBottomLeftRadius = 8;
            panel.style.borderBottomRightRadius = 8;
            panel.style.backgroundColor = new Color(0.13f, 0.138f, 0.148f, 0.94f);
            root.Add(panel);

            var title = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WelcomeTitle));
            title.style.color = EasyMicPipelineStyles.PrimaryText;
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(title);

            var body = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WelcomeBody));
            body.style.color = EasyMicPipelineStyles.SecondaryText;
            body.style.fontSize = 12;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginTop = 8;
            body.style.marginBottom = 8;
            panel.Add(body);

            var hint = new Label(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.WelcomeHint));
            hint.style.color = EasyMicPipelineStyles.SecondaryText;
            hint.style.fontSize = 11;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 16;
            panel.Add(hint);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.flexWrap = Wrap.Wrap;
            panel.Add(buttons);

            var playback = CreateToolbarButton(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ShowPlaybackView), () => SetCanvasVisibility(EasyMicPipelineViewMode.Playback, true));
            playback.style.marginLeft = 0;
            var recording = CreateToolbarButton(EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.ShowRecordingView), () => SetCanvasVisibility(EasyMicPipelineViewMode.Recording, true));
            buttons.Add(playback);
            buttons.Add(recording);
            return root;
        }

        private void CreateMiniMaps()
        {
            _playbackMiniMap = CreateMiniMap(_playbackCanvas, EasyMicPipelineViewMode.Playback, _playbackModel, _playbackGraphView);
            _recordingMiniMap = CreateMiniMap(_recordingCanvas, EasyMicPipelineViewMode.Recording, _recordingModel, _recordingGraphView);
        }

        private EasyMicPipelineMiniMapState CreateMiniMap(VisualElement canvas, EasyMicPipelineViewMode mode, EasyMicPipelineGraphModel model, EasyMicPipelineGraphView graphView)
        {
            var state = new EasyMicPipelineMiniMapState(MiniMapDockKey + "." + mode);
            state.GraphView = graphView;
            state.Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            state.Overlay.style.position = Position.Absolute;
            state.Overlay.style.left = 0;
            state.Overlay.style.right = 0;
            state.Overlay.style.top = 0;
            state.Overlay.style.bottom = 0;

            state.Map = new EasyMicPipelineMiniMap();
            state.Map.style.position = Position.Absolute;
            state.Map.style.width = MiniMapWidth;
            state.Map.style.height = MiniMapHeight;
            state.Map.style.backgroundColor = new Color(0.11f, 0.115f, 0.12f, 0.92f);
            state.Map.style.borderTopColor = EasyMicPipelineStyles.Separator;
            state.Map.style.borderRightColor = EasyMicPipelineStyles.Separator;
            state.Map.style.borderBottomColor = EasyMicPipelineStyles.Separator;
            state.Map.style.borderLeftColor = EasyMicPipelineStyles.Separator;
            state.Map.style.borderTopWidth = 1;
            state.Map.style.borderRightWidth = 1;
            state.Map.style.borderBottomWidth = 1;
            state.Map.style.borderLeftWidth = 1;
            state.Map.RegisterCallback<PointerDownEvent>(evt => OnMiniMapPointerDown(evt, state));
            state.Map.RegisterCallback<PointerMoveEvent>(evt => OnMiniMapPointerMove(evt, state));
            state.Map.RegisterCallback<PointerUpEvent>(evt => OnMiniMapPointerUp(evt, state));
            state.Map.RegisterCallback<WheelEvent>(evt => OnMiniMapWheel(evt, state));
            state.Map.SetModel(model, graphView);
            state.Overlay.Add(state.Map);

            state.SnapPreview = new VisualElement { pickingMode = PickingMode.Ignore };
            state.SnapPreview.style.position = Position.Absolute;
            state.SnapPreview.style.width = MiniMapWidth;
            state.SnapPreview.style.height = MiniMapHeight;
            state.SnapPreview.style.backgroundColor = new Color(0.38f, 0.62f, 0.76f, 0.12f);
            state.SnapPreview.style.borderTopColor = EasyMicPipelineStyles.Edge;
            state.SnapPreview.style.borderRightColor = EasyMicPipelineStyles.Edge;
            state.SnapPreview.style.borderBottomColor = EasyMicPipelineStyles.Edge;
            state.SnapPreview.style.borderLeftColor = EasyMicPipelineStyles.Edge;
            state.SnapPreview.style.borderTopWidth = 1;
            state.SnapPreview.style.borderRightWidth = 1;
            state.SnapPreview.style.borderBottomWidth = 1;
            state.SnapPreview.style.borderLeftWidth = 1;
            state.SnapPreview.style.display = DisplayStyle.None;
            state.Overlay.Add(state.SnapPreview);

            state.Overlay.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!state.Dragging)
                {
                    ApplyMiniMapDock(state, state.Dock, false);
                }
            });
            canvas.Add(state.Overlay);
            ApplyMiniMapDock(state, state.Dock, false);
            return state;
        }

        private Button CreateViewButton(string text, EasyMicPipelineViewMode mode)
        {
            var button = new Button(() => ToggleCanvasVisibility(mode)) { text = text };
            button.tooltip = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.TogglePipelineViewTooltip, text);
            button.style.height = 22;
            button.style.minWidth = 86;
            button.style.flexShrink = 1;
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.unityFontStyleAndWeight = FontStyle.Normal;
            button.style.fontSize = 11;
            return button;
        }

        private static Button CreateToolbarButton(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.height = 24;
            button.style.minWidth = 48;
            button.style.flexShrink = 1;
            button.style.marginLeft = 6;
            button.style.marginRight = 0;
            button.style.paddingLeft = 10;
            button.style.paddingRight = 10;
            button.style.borderTopLeftRadius = 6;
            button.style.borderTopRightRadius = 6;
            button.style.borderBottomLeftRadius = 6;
            button.style.borderBottomRightRadius = 6;
            button.style.backgroundColor = new Color(0.16f, 0.166f, 0.175f, 0.72f);
            button.style.borderTopColor = new Color(0.28f, 0.29f, 0.30f, 0.62f);
            button.style.borderRightColor = new Color(0.28f, 0.29f, 0.30f, 0.62f);
            button.style.borderBottomColor = new Color(0.05f, 0.055f, 0.06f, 0.85f);
            button.style.borderLeftColor = new Color(0.28f, 0.29f, 0.30f, 0.62f);
            button.style.color = EasyMicPipelineStyles.PrimaryText;
            button.style.fontSize = 11;
            return button;
        }

        private void UpdateMiniMapButton()
        {
            if (_miniMapButton == null)
            {
                return;
            }

            _miniMapButton.style.backgroundColor = _miniMapVisible
                ? new Color(0.22f, 0.265f, 0.30f, 0.92f)
                : new Color(0.16f, 0.166f, 0.175f, 0.72f);
            _miniMapButton.style.color = _miniMapVisible
                ? EasyMicPipelineStyles.PrimaryText
                : EasyMicPipelineStyles.SecondaryText;
        }

        private void UpdateViewButtons()
        {
            StyleViewButton(_playbackViewButton, _playbackVisible, _viewMode == EasyMicPipelineViewMode.Playback);
            StyleViewButton(_recordingViewButton, _recordingVisible, _viewMode == EasyMicPipelineViewMode.Recording);
        }

        private static void StyleViewButton(Button button, bool visible, bool focused)
        {
            if (button == null)
            {
                return;
            }

            button.style.backgroundColor = visible
                ? new Color(0.22f, 0.265f, 0.30f, 0.92f)
                : Color.clear;
            button.style.color = visible ? EasyMicPipelineStyles.PrimaryText : EasyMicPipelineStyles.SecondaryText;
            button.style.unityFontStyleAndWeight = focused ? FontStyle.Bold : FontStyle.Normal;
        }

        private void ToggleCanvasVisibility(EasyMicPipelineViewMode mode)
        {
            bool visible = mode == EasyMicPipelineViewMode.Playback ? !_playbackVisible : !_recordingVisible;
            SetCanvasVisibility(mode, visible);
        }

        private void SetCanvasVisibility(EasyMicPipelineViewMode mode, bool visible)
        {
            if (mode == EasyMicPipelineViewMode.Playback)
            {
                _playbackVisible = visible;
            }
            else
            {
                _recordingVisible = visible;
            }

            if (_playbackVisible && !_recordingVisible)
            {
                _viewMode = EasyMicPipelineViewMode.Playback;
                _graphView = _playbackGraphView;
            }
            else if (_recordingVisible && !_playbackVisible)
            {
                _viewMode = EasyMicPipelineViewMode.Recording;
                _graphView = _recordingGraphView;
            }
            else if (_playbackVisible && _recordingVisible && visible)
            {
                _viewMode = mode;
                _graphView = mode == EasyMicPipelineViewMode.Playback ? _playbackGraphView : _recordingGraphView;
            }
            else if (!_playbackVisible && !_recordingVisible)
            {
                _graphView = null;
                _detailsPanel.ClearSelection();
                _statusLabel.text = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.NoPipelineViewSelected);
            }

            RebuildPipelineCanvasLayout();
            ApplyCanvasFocus();
            if (_graphView != null)
            {
                RefreshModel(_viewMode, rebuildWhenTopologyChanges: true, forceRebuild: false);
                _graphView.RequestFrameContent();
                UpdateFocusedAuxiliaryViews();
            }
        }

        private void SetViewMode(EasyMicPipelineViewMode mode)
        {
            SetViewMode(mode, clearSelection: true, forceRefresh: true);
        }

        private void SetViewMode(EasyMicPipelineViewMode mode, bool clearSelection, bool forceRefresh)
        {
            if ((mode == EasyMicPipelineViewMode.Playback && !_playbackVisible)
                || (mode == EasyMicPipelineViewMode.Recording && !_recordingVisible))
            {
                SetCanvasVisibility(mode, true);
                return;
            }

            if (_viewMode == mode)
            {
                ApplyCanvasFocus();
                UpdateFocusedAuxiliaryViews();
                return;
            }

            _viewMode = mode;
            UpdateViewButtons();
            _graphView = mode == EasyMicPipelineViewMode.Playback ? _playbackGraphView : _recordingGraphView;
            ApplyCanvasFocus();
            if (clearSelection)
            {
                _detailsPanel.ClearSelection();
            }

            if (forceRefresh)
            {
                RefreshModel(mode, rebuildWhenTopologyChanges: true, forceRebuild: true);
            }
            else
            {
                UpdateFocusedAuxiliaryViews();
            }

            _graphView?.RequestFrameContent();
        }

        private void ApplyCanvasFocus()
        {
            if (_playbackCanvas != null)
            {
                _playbackCanvas.style.borderTopColor = _viewMode == EasyMicPipelineViewMode.Playback ? EasyMicPipelineStyles.Edge : EasyMicPipelineStyles.Separator;
                _playbackCanvas.style.borderTopWidth = _viewMode == EasyMicPipelineViewMode.Playback ? 1 : 0;
            }

            if (_recordingCanvas != null)
            {
                _recordingCanvas.style.borderTopColor = _viewMode == EasyMicPipelineViewMode.Recording ? EasyMicPipelineStyles.Edge : EasyMicPipelineStyles.Separator;
                _recordingCanvas.style.borderTopWidth = _viewMode == EasyMicPipelineViewMode.Recording ? 1 : 0;
            }

            UpdateViewButtons();
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            ApplyResponsiveLayout();
        }

        private void ApplyResponsiveLayout()
        {
            float width = rootVisualElement.resolvedStyle.width;
            if (width <= 1f)
            {
                return;
            }

            bool compactToolbar = width < 760f;
            if (_toolbarCompact != compactToolbar)
            {
                _toolbarCompact = compactToolbar;
                if (_toolbar != null)
                {
                    _toolbar.style.paddingLeft = compactToolbar ? 8 : 12;
                    _toolbar.style.paddingRight = compactToolbar ? 8 : 12;
                }

                SetButtonMinimum(_playbackViewButton, compactToolbar ? 68f : 86f);
                SetButtonMinimum(_recordingViewButton, compactToolbar ? 68f : 86f);
                SetButtonMinimum(_miniMapButton, compactToolbar ? 42f : 48f);
            }

            if (_statusLabel != null)
            {
                _statusLabel.style.display = width < 900f ? DisplayStyle.None : DisplayStyle.Flex;
            }

            ApplyAllMiniMapDocks();
            RebuildDockLayoutIfInspectorWidthIsOutOfRange();
        }

        private static void SetButtonMinimum(Button button, float width)
        {
            if (button == null)
            {
                return;
            }

            button.style.minWidth = width;
        }

        private void RebuildPipelineCanvasLayout()
        {
            if (_pipelineSplitView != null && _playbackCanvas != null && _playbackCanvas.resolvedStyle.height > 1f)
            {
                _pipelineSplitHeight = Mathf.Clamp(_playbackCanvas.resolvedStyle.height, 180f, 1200f);
                EditorPrefs.SetFloat(PipelineSplitHeightKey, _pipelineSplitHeight);
            }

            _graphHost.Clear();
            _pipelineSplitView = null;
            ResetCanvasSizing(_playbackCanvas);
            ResetCanvasSizing(_recordingCanvas);
            ResetCanvasSizing(_emptyCanvasState);

            if (_playbackVisible && _recordingVisible)
            {
                _pipelineSplitHeight = ClampPipelineSplitHeight(_pipelineSplitHeight);
                _pipelineSplitView = new TwoPaneSplitView(0, _pipelineSplitHeight, TwoPaneSplitViewOrientation.Vertical);
                _pipelineSplitView.style.flexGrow = 1;
                _pipelineSplitView.style.flexShrink = 1;
                _pipelineSplitView.style.minHeight = 0;
                _pipelineSplitView.Add(_playbackCanvas);
                _pipelineSplitView.Add(_recordingCanvas);
                _graphHost.Add(_pipelineSplitView);
                ScheduleVisibleCanvasRefresh();
                return;
            }

            if (_playbackVisible)
            {
                FillGraphHost(_playbackCanvas);
                _graphHost.Add(_playbackCanvas);
                ScheduleVisibleCanvasRefresh();
                return;
            }

            if (_recordingVisible)
            {
                FillGraphHost(_recordingCanvas);
                _graphHost.Add(_recordingCanvas);
                ScheduleVisibleCanvasRefresh();
                return;
            }

            FillGraphHost(_emptyCanvasState);
            _graphHost.Add(_emptyCanvasState);
        }

        private static void ResetCanvasSizing(VisualElement canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.style.flexGrow = 1;
            canvas.style.flexShrink = 1;
            canvas.style.flexBasis = 0;
            canvas.style.width = StyleKeyword.Auto;
            canvas.style.height = StyleKeyword.Auto;
            canvas.style.minHeight = 0;
            canvas.style.maxHeight = StyleKeyword.None;
        }

        private static void FillGraphHost(VisualElement canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.style.flexGrow = 1;
            canvas.style.flexShrink = 1;
            canvas.style.flexBasis = 0;
            canvas.style.width = Length.Percent(100);
            canvas.style.height = Length.Percent(100);
            canvas.style.minHeight = 0;
        }

        private void ScheduleVisibleCanvasRefresh()
        {
            _graphHost.schedule.Execute(() =>
            {
                if (_playbackVisible)
                {
                    _playbackGraphView?.RequestFrameContent();
                    _playbackMiniMap?.Map.SetViewport(_playbackGraphView);
                }

                if (_recordingVisible)
                {
                    _recordingGraphView?.RequestFrameContent();
                    _recordingMiniMap?.Map.SetViewport(_recordingGraphView);
                }

                ApplyAllMiniMapDocks();
            }).ExecuteLater(1);
        }

        private void ToggleMiniMap()
        {
            _miniMapVisible = !_miniMapVisible;
            if (_playbackMiniMap != null)
            {
                _playbackMiniMap.Map.style.display = _miniMapVisible ? DisplayStyle.Flex : DisplayStyle.None;
                _playbackMiniMap.SnapPreview.style.display = DisplayStyle.None;
            }

            if (_recordingMiniMap != null)
            {
                _recordingMiniMap.Map.style.display = _miniMapVisible ? DisplayStyle.Flex : DisplayStyle.None;
                _recordingMiniMap.SnapPreview.style.display = DisplayStyle.None;
            }

            UpdateMiniMapButton();
        }

        private void RebuildDockLayout()
        {
            if (_responsiveRebuildInProgress)
            {
                return;
            }

            if (_splitView != null)
            {
                _inspectorWidth = ClampInspectorWidth(_detailsPanel?.resolvedStyle.width ?? _inspectorWidth);
                EditorPrefs.SetFloat(InspectorWidthKey, _inspectorWidth);
                rootVisualElement.Remove(_splitView);
            }

            int fixedPane = _inspectorDock == EasyMicInspectorDock.Left ? 0 : 1;
            _inspectorWidth = ClampInspectorWidth(_inspectorWidth);
            _splitView = new TwoPaneSplitView(fixedPane, _inspectorWidth, TwoPaneSplitViewOrientation.Horizontal);
            _splitView.style.flexGrow = 1;

            if (_inspectorDock == EasyMicInspectorDock.Left)
            {
                _splitView.Add(_detailsPanel);
                _splitView.Add(_graphHost);
            }
            else
            {
                _splitView.Add(_graphHost);
                _splitView.Add(_detailsPanel);
            }

            _detailsPanel.SetDockSide(_inspectorDock);
            rootVisualElement.Add(_splitView);
            ApplyAllMiniMapDocks();
        }

        private void RebuildDockLayoutIfInspectorWidthIsOutOfRange()
        {
            if (_splitView == null || _detailsPanel == null || rootVisualElement.resolvedStyle.width <= 1f)
            {
                return;
            }

            float current = _detailsPanel.resolvedStyle.width > 1f ? _detailsPanel.resolvedStyle.width : _inspectorWidth;
            float clamped = ClampInspectorWidth(current);
            if (Mathf.Abs(clamped - current) < 8f)
            {
                return;
            }

            _inspectorWidth = clamped;
            _responsiveRebuildInProgress = true;
            try
            {
                _responsiveRebuildInProgress = false;
                RebuildDockLayout();
            }
            finally
            {
                _responsiveRebuildInProgress = false;
            }
        }

        private float ClampInspectorWidth(float width)
        {
            float windowWidth = rootVisualElement?.resolvedStyle.width ?? 0f;
            if (windowWidth <= 1f)
            {
                return Mathf.Clamp(width, 300f, 560f);
            }

            float min = windowWidth < 720f ? 240f : 300f;
            float max = Mathf.Clamp(windowWidth * 0.42f, min, 560f);
            return Mathf.Clamp(width, min, max);
        }

        private float ClampPipelineSplitHeight(float height)
        {
            float hostHeight = _graphHost?.resolvedStyle.height ?? 0f;
            if (hostHeight <= 1f)
            {
                return Mathf.Clamp(height, 180f, 1200f);
            }

            float minPaneHeight = hostHeight < 420f ? 120f : 180f;
            float max = Mathf.Max(minPaneHeight, hostHeight - minPaneHeight);
            return Mathf.Clamp(height, minPaneHeight, max);
        }

        private void SetInspectorDock(EasyMicInspectorDock dock)
        {
            if (_inspectorDock == dock && _splitView != null)
            {
                return;
            }

            _inspectorDock = dock;
            EditorPrefs.SetInt(InspectorDockKey, (int)_inspectorDock);
            RebuildDockLayout();
            _graphView?.RequestFrameContent();
            ApplyAllMiniMapDocks();
        }

        private void ShowRuntimeOverview()
        {
            _detailsPanel.ClearSelection();
            _playbackGraphView?.ClearSelection();
            _recordingGraphView?.ClearSelection();
            if (_graphView != null)
            {
                UpdateFocusedAuxiliaryViews();
            }
        }

        private void OnMiniMapPointerDown(PointerDownEvent evt, EasyMicPipelineMiniMapState state)
        {
            Vector2 mapLocal = state.Map.WorldToLocal(evt.position);
            if (state.Map.ContainsViewport(mapLocal))
            {
                state.DraggingViewport = true;
                state.LastViewportDragLocal = mapLocal;
                state.Map.CapturePointer(evt.pointerId);
                state.SnapPreview.style.display = DisplayStyle.None;
                evt.StopPropagation();
                return;
            }

            state.Dragging = true;
            state.Map.CapturePointer(evt.pointerId);
            state.SnapPreview.style.display = DisplayStyle.Flex;
            UpdateMiniMapDockPreview(state, evt.position);
            evt.StopPropagation();
        }

        private void OnMiniMapPointerMove(PointerMoveEvent evt, EasyMicPipelineMiniMapState state)
        {
            if (state.DraggingViewport)
            {
                Vector2 mapLocal = state.Map.WorldToLocal(evt.position);
                Vector2 localDelta = mapLocal - state.LastViewportDragLocal;
                state.LastViewportDragLocal = mapLocal;
                PanGraphFromMiniMapDelta(state, localDelta);
                evt.StopPropagation();
                return;
            }

            if (!state.Dragging)
            {
                return;
            }

            Vector2 local = state.Overlay.WorldToLocal(evt.position);
            Rect rect = GetMiniMapDockRect(state, state.Dock);
            SetOverlayPosition(state.Map, new Rect(
                Mathf.Clamp(local.x - rect.width * 0.5f, MiniMapMargin, Mathf.Max(MiniMapMargin, state.Overlay.layout.width - rect.width - MiniMapMargin)),
                Mathf.Clamp(local.y - rect.height * 0.5f, MiniMapMargin, Mathf.Max(MiniMapMargin, state.Overlay.layout.height - rect.height - MiniMapMargin)),
                rect.width,
                rect.height));
            UpdateMiniMapDockPreview(state, evt.position);
            evt.StopPropagation();
        }

        private void OnMiniMapPointerUp(PointerUpEvent evt, EasyMicPipelineMiniMapState state)
        {
            if (state.DraggingViewport)
            {
                state.DraggingViewport = false;
                state.Map.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }

            if (!state.Dragging)
            {
                return;
            }

            state.Dragging = false;
            state.Map.ReleasePointer(evt.pointerId);
            ApplyMiniMapDock(state, DockFromWorldPosition(state, evt.position), true);
            state.SnapPreview.style.display = DisplayStyle.None;
            evt.StopPropagation();
        }

        private static void OnMiniMapWheel(WheelEvent evt, EasyMicPipelineMiniMapState state)
        {
            if (state?.Map == null || state.GraphView == null)
            {
                return;
            }

            Vector2 mapLocal = evt.localMousePosition;
            if (!state.Map.ContainsViewport(mapLocal))
            {
                return;
            }

            ZoomGraphFromMiniMapWheel(state, mapLocal, evt.delta.y);
            evt.StopPropagation();
            evt.PreventDefault();
        }

        private static void PanGraphFromMiniMapDelta(EasyMicPipelineMiniMapState state, Vector2 localDelta)
        {
            if (state?.GraphView == null || localDelta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Vector2 graphDelta = state.Map.GraphDeltaFromMiniMapDelta(localDelta);
            Vector3 scale = state.GraphView.viewTransform.scale;
            float zoom = Mathf.Max(0.0001f, scale.x);
            Vector3 position = state.GraphView.viewTransform.position;
            position.x -= graphDelta.x * zoom;
            position.y -= graphDelta.y * zoom;
            state.GraphView.UpdateViewTransform(position, scale);
            state.Map.SetViewport(state.GraphView);
        }

        private static void ZoomGraphFromMiniMapWheel(EasyMicPipelineMiniMapState state, Vector2 mapLocal, float wheelDeltaY)
        {
            if (Mathf.Abs(wheelDeltaY) < 0.001f || !state.Map.TryGraphPositionFromMiniMapLocal(mapLocal, out Vector2 graphPosition))
            {
                return;
            }

            Vector3 oldScale = state.GraphView.viewTransform.scale;
            Vector3 oldPosition = state.GraphView.viewTransform.position;
            float oldZoom = Mathf.Max(0.0001f, oldScale.x);
            float zoomFactor = Mathf.Pow(1.08f, -wheelDeltaY);
            float newZoom = Mathf.Clamp(oldZoom * zoomFactor, 0.18f, 1.35f);
            if (Mathf.Abs(newZoom - oldZoom) < 0.0001f)
            {
                return;
            }

            Vector2 graphViewPivot = new Vector2(
                oldPosition.x + graphPosition.x * oldZoom,
                oldPosition.y + graphPosition.y * oldZoom);
            Vector3 newPosition = new Vector3(
                graphViewPivot.x - graphPosition.x * newZoom,
                graphViewPivot.y - graphPosition.y * newZoom,
                oldPosition.z);

            state.GraphView.UpdateViewTransform(newPosition, new Vector3(newZoom, newZoom, 1f));
            state.Map.SetViewport(state.GraphView);
        }

        private void UpdateMiniMapDockPreview(EasyMicPipelineMiniMapState state, Vector2 worldPosition)
        {
            ApplyDockStyle(state, state.SnapPreview, DockFromWorldPosition(state, worldPosition));
        }

        private EasyMicMiniMapDock DockFromWorldPosition(EasyMicPipelineMiniMapState state, Vector2 worldPosition)
        {
            Vector2 local = state.Overlay.WorldToLocal(worldPosition);
            bool left = local.x < state.Overlay.layout.width * 0.5f;
            bool top = local.y < state.Overlay.layout.height * 0.5f;
            if (left && top) return EasyMicMiniMapDock.TopLeft;
            if (!left && top) return EasyMicMiniMapDock.TopRight;
            if (left) return EasyMicMiniMapDock.BottomLeft;
            return EasyMicMiniMapDock.BottomRight;
        }

        private void ApplyMiniMapDock(EasyMicPipelineMiniMapState state, EasyMicMiniMapDock dock, bool persist)
        {
            if (state == null || state.Map == null || state.Overlay == null || state.Overlay.layout.width <= 1f || state.Overlay.layout.height <= 1f)
            {
                return;
            }

            state.Dock = dock;
            SetOverlayPosition(state.Map, GetMiniMapDockRect(state, dock));
            if (persist)
            {
                EditorPrefs.SetInt(state.PrefsKey, (int)dock);
            }
        }

        private void ApplyAllMiniMapDocks()
        {
            ApplyMiniMapDock(_playbackMiniMap, _playbackMiniMap?.Dock ?? EasyMicMiniMapDock.BottomRight, false);
            ApplyMiniMapDock(_recordingMiniMap, _recordingMiniMap?.Dock ?? EasyMicMiniMapDock.BottomRight, false);
        }

        private Rect GetMiniMapDockRect(EasyMicPipelineMiniMapState state, EasyMicMiniMapDock dock)
        {
            float width = Mathf.Clamp(state.Overlay.layout.width - MiniMapMargin * 2f, 120f, MiniMapWidth);
            float height = Mathf.Clamp(state.Overlay.layout.height - MiniMapMargin * 2f, 96f, MiniMapHeight);
            float left = MiniMapMargin;
            float right = Mathf.Max(MiniMapMargin, state.Overlay.layout.width - width - MiniMapMargin);
            float top = MiniMapMargin;
            float bottom = Mathf.Max(MiniMapMargin, state.Overlay.layout.height - height - MiniMapMargin);

            switch (dock)
            {
                case EasyMicMiniMapDock.TopLeft:
                    return new Rect(left, top, width, height);
                case EasyMicMiniMapDock.TopRight:
                    return new Rect(right, top, width, height);
                case EasyMicMiniMapDock.BottomLeft:
                    return new Rect(left, bottom, width, height);
                default:
                    return new Rect(right, bottom, width, height);
            }
        }

        private void ApplyDockStyle(EasyMicPipelineMiniMapState state, VisualElement element, EasyMicMiniMapDock dock)
        {
            SetOverlayPosition(element, GetMiniMapDockRect(state, dock));
        }

        private static void SetOverlayPosition(VisualElement element, Rect rect)
        {
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        private void EditorUpdate()
        {
            if (!_uiReady || _playbackGraphView == null || _recordingGraphView == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextTelemetryRefresh)
            {
                RefreshAllModels(rebuildWhenTopologyChanges: false);
                _nextTelemetryRefresh = now + TelemetryRefreshInterval;
            }

            if (now >= _nextTopologyRefresh)
            {
                RefreshAllModels(rebuildWhenTopologyChanges: true);
                _nextTopologyRefresh = now + TopologyRefreshInterval;
            }

        }

        private void ForceRefresh()
        {
            RefreshAllModels(rebuildWhenTopologyChanges: true, forceRebuild: true);
            if (_playbackVisible)
            {
                _playbackGraphView?.RequestFrameContent();
            }

            if (_recordingVisible)
            {
                _recordingGraphView?.RequestFrameContent();
            }
        }

        private void RefreshAllModels(bool rebuildWhenTopologyChanges, bool forceRebuild = false)
        {
            RefreshModel(EasyMicPipelineViewMode.Playback, rebuildWhenTopologyChanges, forceRebuild);
            RefreshModel(EasyMicPipelineViewMode.Recording, rebuildWhenTopologyChanges, forceRebuild);
        }

        private void RefreshModel(EasyMicPipelineViewMode mode, bool rebuildWhenTopologyChanges, bool forceRebuild = false)
        {
            var model = mode == EasyMicPipelineViewMode.Playback ? _playbackModel : _recordingModel;
            var view = mode == EasyMicPipelineViewMode.Playback ? _playbackGraphView : _recordingGraphView;
            if (model == null || view == null)
            {
                return;
            }

            try
            {
                model.Capture(mode);
            }
            catch
            {
                if (mode == _viewMode && _statusLabel != null)
                {
                    _statusLabel.text = EasyMicEditorLocalization.PipelineText(EasyMicPipelineTextKey.NoSnapshot);
                }

                return;
            }

            int newHash = model.TopologyHash;
            int oldHash = mode == EasyMicPipelineViewMode.Playback ? _playbackTopologyHash : _recordingTopologyHash;
            if (forceRebuild || (rebuildWhenTopologyChanges && newHash != oldHash))
            {
                if (mode == EasyMicPipelineViewMode.Playback)
                {
                    _playbackTopologyHash = newHash;
                }
                else
                {
                    _recordingTopologyHash = newHash;
                }

                RebuildGraph(model, view);
                UpdateMiniMapTopology(mode, model);
            }
            else
            {
                view.UpdateTelemetry(model);
                GetMiniMapState(mode)?.Map.SetViewport(view);
            }

            if (mode == _viewMode)
            {
                _detailsPanel.UpdateTelemetry(model);
                _statusLabel.text = model.StatusText;
            }
        }

        private void RebuildGraph(EasyMicPipelineGraphModel model, EasyMicPipelineGraphView view)
        {
            if (view == null)
            {
                return;
            }

            view.Build(model, string.Empty);
            if (view == _graphView)
            {
                _detailsPanel.UpdateTelemetry(model);
            }
        }

        private void FrameGraph()
        {
            if (_playbackVisible)
            {
                _playbackGraphView?.RequestFrameContent();
            }

            if (_recordingVisible)
            {
                _recordingGraphView?.RequestFrameContent();
            }
        }

        private void UpdateMiniMapTopology(EasyMicPipelineViewMode mode, EasyMicPipelineGraphModel model)
        {
            var state = GetMiniMapState(mode);
            var view = mode == EasyMicPipelineViewMode.Playback ? _playbackGraphView : _recordingGraphView;
            if (state == null || model == null)
            {
                return;
            }

            if (state.TopologyHash != model.TopologyHash)
            {
                state.TopologyHash = model.TopologyHash;
                state.Map.SetModel(model, view);
            }
            else
            {
                state.Map.SetViewport(view);
            }
        }

        private void UpdateFocusedAuxiliaryViews()
        {
            var model = ActiveModel;
            _detailsPanel.UpdateTelemetry(model);
            _statusLabel.text = model.StatusText;
            UpdateMiniMapTopology(_viewMode, model);
        }

        private EasyMicPipelineMiniMapState GetMiniMapState(EasyMicPipelineViewMode mode)
        {
            return mode == EasyMicPipelineViewMode.Playback ? _playbackMiniMap : _recordingMiniMap;
        }

    }

    internal enum EasyMicInspectorDock
    {
        Left = 0,
        Right = 1
    }

    internal enum EasyMicMiniMapDock
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3
    }

    internal sealed class EasyMicPipelineMiniMapState
    {
        public EasyMicPipelineMiniMapState(string prefsKey)
        {
            PrefsKey = prefsKey;
            int savedDock = EditorPrefs.GetInt(prefsKey, (int)EasyMicMiniMapDock.BottomRight);
            Dock = savedDock >= (int)EasyMicMiniMapDock.TopLeft && savedDock <= (int)EasyMicMiniMapDock.BottomRight
                ? (EasyMicMiniMapDock)savedDock
                : EasyMicMiniMapDock.BottomRight;
        }

        public string PrefsKey { get; }
        public EasyMicPipelineGraphView GraphView;
        public VisualElement Overlay;
        public EasyMicPipelineMiniMap Map;
        public VisualElement SnapPreview;
        public EasyMicMiniMapDock Dock;
        public bool Dragging;
        public bool DraggingViewport;
        public Vector2 LastViewportDragLocal;
        public int TopologyHash;
    }

    internal sealed class EasyMicPipelineMiniMap : VisualElement
    {
        private const float Padding = 10f;
        private readonly List<Rect> _groups = new List<Rect>(16);
        private readonly List<MiniMapNodeRect> _nodes = new List<MiniMapNodeRect>(128);
        private Rect _contentBounds = new Rect(0f, 0f, 1f, 1f);
        private Rect _viewportBounds;
        private bool _hasViewport;
        private bool _lastHadViewport;

        public EasyMicPipelineMiniMap()
        {
            pickingMode = PickingMode.Position;
            generateVisualContent += OnGenerateVisualContent;
        }

        public void SetModel(EasyMicPipelineGraphModel model, EasyMicPipelineGraphView graphView)
        {
            _groups.Clear();
            _nodes.Clear();

            if (model != null)
            {
                _contentBounds = model.GetContentBounds();
                for (int i = 0; i < model.Groups.Count; i++)
                {
                    _groups.Add(model.Groups[i].Bounds);
                }

                for (int i = 0; i < model.Nodes.Count; i++)
                {
                    var node = model.Nodes[i];
                    Color color = EasyMicPipelineStyles.Accent(node.Kind);
                    color.a = 0.78f;
                    _nodes.Add(new MiniMapNodeRect(new Rect(node.Position.x, node.Position.y, node.Width, 118f), color));
                }
            }

            UpdateViewport(graphView);
            MarkDirtyRepaint();
        }

        public void SetViewport(EasyMicPipelineGraphView graphView)
        {
            if (UpdateViewport(graphView))
            {
                MarkDirtyRepaint();
            }
        }

        public bool ContainsViewport(Vector2 localPosition)
        {
            if (!_hasViewport || layout.width <= 1f || layout.height <= 1f)
            {
                return false;
            }

            Rect contentRect = GetMiniMapContentRect();
            if (!TryClipRect(ToMiniMapRect(_viewportBounds, contentRect), contentRect, out Rect viewportRect))
            {
                return false;
            }

            viewportRect = ClampRectInside(viewportRect, contentRect);
            viewportRect.xMin -= 5f;
            viewportRect.xMax += 5f;
            viewportRect.yMin -= 5f;
            viewportRect.yMax += 5f;
            return viewportRect.Contains(localPosition);
        }

        public Vector2 GraphDeltaFromMiniMapDelta(Vector2 localDelta)
        {
            Rect contentRect = GetMiniMapContentRect();
            float scale = contentRect.width / Mathf.Max(1f, _contentBounds.width);
            if (scale <= 0.0001f)
            {
                return Vector2.zero;
            }

            return localDelta / scale;
        }

        public bool TryGraphPositionFromMiniMapLocal(Vector2 localPosition, out Vector2 graphPosition)
        {
            graphPosition = default;
            Rect contentRect = GetMiniMapContentRect();
            if (contentRect.width <= 1f || contentRect.height <= 1f)
            {
                return false;
            }

            float scale = contentRect.width / Mathf.Max(1f, _contentBounds.width);
            if (scale <= 0.0001f)
            {
                return false;
            }

            graphPosition = new Vector2(
                _contentBounds.xMin + (localPosition.x - contentRect.xMin) / scale,
                _contentBounds.yMin + (localPosition.y - contentRect.yMin) / scale);
            return true;
        }

        private bool UpdateViewport(EasyMicPipelineGraphView graphView)
        {
            _lastHadViewport = _hasViewport;
            Rect previousViewport = _viewportBounds;
            _hasViewport = false;
            if (graphView == null || graphView.layout.width <= 1f || graphView.layout.height <= 1f)
            {
                return _lastHadViewport;
            }

            Vector3 position = graphView.viewTransform.position;
            Vector3 scale = graphView.viewTransform.scale;
            float zoom = Mathf.Max(0.0001f, scale.x);
            _viewportBounds = new Rect(
                -position.x / zoom,
                -position.y / zoom,
                graphView.layout.width / zoom,
                graphView.layout.height / zoom);
            _hasViewport = true;
            return !_lastHadViewport || !Approximately(_viewportBounds, previousViewport);
        }

        private static bool Approximately(Rect current, Rect previous)
        {
            return Mathf.Abs(current.x - previous.x) < 0.1f
                && Mathf.Abs(current.y - previous.y) < 0.1f
                && Mathf.Abs(current.width - previous.width) < 0.1f
                && Mathf.Abs(current.height - previous.height) < 0.1f;
        }

        private void OnGenerateVisualContent(MeshGenerationContext context)
        {
            Rect contentRect = GetMiniMapContentRect();
            Rect viewportRect = default;
            bool drawViewport = _hasViewport && TryClipRect(ToMiniMapRect(_viewportBounds, contentRect), contentRect, out viewportRect);
            int rectCount = _groups.Count + _nodes.Count + (drawViewport ? 4 : 0);
            if (rectCount == 0 || layout.width <= 1f || layout.height <= 1f)
            {
                return;
            }

            var mesh = context.Allocate(rectCount * 4, rectCount * 6);
            int vertexIndex = 0;

            for (int i = 0; i < _groups.Count; i++)
            {
                AddRect(mesh, ref vertexIndex, ToMiniMapRect(_groups[i], contentRect), new Color(0.36f, 0.39f, 0.42f, 0.18f));
            }

            for (int i = 0; i < _nodes.Count; i++)
            {
                AddRect(mesh, ref vertexIndex, ToMiniMapRect(_nodes[i].Bounds, contentRect), _nodes[i].Color);
            }

            if (drawViewport)
            {
                AddViewport(mesh, ref vertexIndex, viewportRect, contentRect);
            }
        }

        private Rect GetMiniMapContentRect()
        {
            float contentWidth = Mathf.Max(1f, _contentBounds.width);
            float contentHeight = Mathf.Max(1f, _contentBounds.height);
            float availableWidth = Mathf.Max(1f, layout.width - Padding * 2f);
            float availableHeight = Mathf.Max(1f, layout.height - Padding * 2f);
            float scale = Mathf.Min(availableWidth / contentWidth, availableHeight / contentHeight);
            float width = contentWidth * scale;
            float height = contentHeight * scale;
            return new Rect(
                Padding + (availableWidth - width) * 0.5f,
                Padding + (availableHeight - height) * 0.5f,
                width,
                height);
        }

        private Rect ToMiniMapRect(Rect graphRect, Rect contentRect)
        {
            float scale = contentRect.width / Mathf.Max(1f, _contentBounds.width);
            return new Rect(
                contentRect.xMin + (graphRect.xMin - _contentBounds.xMin) * scale,
                contentRect.yMin + (graphRect.yMin - _contentBounds.yMin) * scale,
                Mathf.Max(1.5f, graphRect.width * scale),
                Mathf.Max(1.5f, graphRect.height * scale));
        }

        private static bool TryClipRect(Rect rect, Rect clipRect, out Rect clipped)
        {
            float xMin = Mathf.Max(rect.xMin, clipRect.xMin);
            float yMin = Mathf.Max(rect.yMin, clipRect.yMin);
            float xMax = Mathf.Min(rect.xMax, clipRect.xMax);
            float yMax = Mathf.Min(rect.yMax, clipRect.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                clipped = default;
                return false;
            }

            clipped = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return true;
        }

        private static void AddViewport(MeshWriteData mesh, ref int vertexIndex, Rect rect, Rect clipRect)
        {
            rect = ClampRectInside(rect, clipRect);
            Color color = new Color(0.86f, 0.90f, 0.94f, 0.72f);
            const float Thickness = 1.5f;
            AddRect(mesh, ref vertexIndex, new Rect(rect.xMin, rect.yMin, rect.width, Thickness), color);
            AddRect(mesh, ref vertexIndex, new Rect(rect.xMin, rect.yMax - Thickness, rect.width, Thickness), color);
            AddRect(mesh, ref vertexIndex, new Rect(rect.xMin, rect.yMin, Thickness, rect.height), color);
            AddRect(mesh, ref vertexIndex, new Rect(rect.xMax - Thickness, rect.yMin, Thickness, rect.height), color);
        }

        private static Rect ClampRectInside(Rect rect, Rect clipRect)
        {
            const float MinimumSize = 2f;
            float width = Mathf.Clamp(rect.width, MinimumSize, Mathf.Max(MinimumSize, clipRect.width));
            float height = Mathf.Clamp(rect.height, MinimumSize, Mathf.Max(MinimumSize, clipRect.height));
            float x = Mathf.Clamp(rect.xMin, clipRect.xMin, clipRect.xMax - width);
            float y = Mathf.Clamp(rect.yMin, clipRect.yMin, clipRect.yMax - height);
            return new Rect(x, y, width, height);
        }

        private static Rect ClampRect(Rect rect)
        {
            rect.width = Mathf.Max(1.5f, rect.width);
            rect.height = Mathf.Max(1.5f, rect.height);
            return rect;
        }

        private static void AddRect(MeshWriteData mesh, ref int vertexIndex, Rect rect, Color color)
        {
            rect = ClampRect(rect);
            int baseIndex = vertexIndex;
            mesh.SetNextVertex(CreateVertex(new Vector2(rect.xMin, rect.yMin), color));
            mesh.SetNextVertex(CreateVertex(new Vector2(rect.xMax, rect.yMin), color));
            mesh.SetNextVertex(CreateVertex(new Vector2(rect.xMax, rect.yMax), color));
            mesh.SetNextVertex(CreateVertex(new Vector2(rect.xMin, rect.yMax), color));
            mesh.SetNextIndex((ushort)baseIndex);
            mesh.SetNextIndex((ushort)(baseIndex + 1));
            mesh.SetNextIndex((ushort)(baseIndex + 2));
            mesh.SetNextIndex((ushort)baseIndex);
            mesh.SetNextIndex((ushort)(baseIndex + 2));
            mesh.SetNextIndex((ushort)(baseIndex + 3));
            vertexIndex += 4;
        }

        private static Vertex CreateVertex(Vector2 position, Color color)
        {
            return new Vertex
            {
                position = new Vector3(position.x, position.y, Vertex.nearZ),
                tint = color
            };
        }

        private readonly struct MiniMapNodeRect
        {
            public MiniMapNodeRect(Rect bounds, Color color)
            {
                Bounds = bounds;
                Color = color;
            }

            public Rect Bounds { get; }
            public Color Color { get; }
        }
    }

    internal sealed class EasyMicPipelineGraphView : GraphView
    {
        private readonly Dictionary<string, EasyMicPipelineNodeView> _nodeViews = new Dictionary<string, EasyMicPipelineNodeView>(128);
        private readonly Dictionary<string, EasyMicPipelineGroupView> _groupViews = new Dictionary<string, EasyMicPipelineGroupView>(16);
        private readonly List<GraphElement> _deleteBuffer = new List<GraphElement>(256);
        private readonly List<Port> _emptyPorts = new List<Port>(0);
        private Rect _lastContentBounds;
        private bool _pendingFrame;
        private int _frameAttempts;
        public Action<EasyMicPipelineGraphNode> OnNodeSelected;
        public Action OnEmptyGraphSelected;

        public EasyMicPipelineGraphView()
        {
            style.flexGrow = 1;
            style.backgroundColor = EasyMicPipelineStyles.CanvasBackground;
            focusable = true;

            SetupZoom(0.18f, 1.35f);
            this.AddManipulator(new ContentDragger());
            serializeGraphElements = _ => string.Empty;
            unserializeAndPaste = (_, __) => { };
            canPasteSerializedData = _ => false;
            deleteSelection = (_, __) => { };

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return _emptyPorts;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            evt.StopPropagation();
        }

        public void Build(EasyMicPipelineGraphModel model, string filter)
        {
            graphViewChanged = null;
            CollectGraphElementsForDelete();
            if (_deleteBuffer.Count > 0)
            {
                DeleteElements(_deleteBuffer);
                _deleteBuffer.Clear();
            }
            _nodeViews.Clear();
            _groupViews.Clear();

            string normalizedFilter = (filter ?? string.Empty).Trim();
            bool hasFilter = normalizedFilter.Length > 0;

            for (int i = 0; i < model.Groups.Count; i++)
            {
                var group = model.Groups[i];
                if (hasFilter && !group.Matches(normalizedFilter))
                {
                    continue;
                }

                var groupView = new EasyMicPipelineGroupView(group);
                _groupViews[group.Id] = groupView;
                AddElement(groupView);
            }

            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var node = model.Nodes[i];
                if (hasFilter && !node.Matches(normalizedFilter))
                {
                    continue;
                }

                var view = new EasyMicPipelineNodeView(node);
                _nodeViews[node.Id] = view;
                AddElement(view);
            }

            AttachNodesToGroups(model);
            AddEdges(model, normalizedFilter, hasFilter);

            graphViewChanged = OnGraphViewChanged;
            UpdateTelemetry(model);
            _lastContentBounds = model.GetContentBounds();
            RequestFrameContent();
        }

        private void AddEdges(EasyMicPipelineGraphModel model, string normalizedFilter, bool hasFilter)
        {
            for (int i = 0; i < model.Edges.Count; i++)
            {
                var edgeModel = model.Edges[i];
                if (!model.TryGetNode(edgeModel.FromId, out var fromNode) || !model.TryGetNode(edgeModel.ToId, out var toNode))
                {
                    continue;
                }

                if (hasFilter && (!fromNode.Matches(normalizedFilter) || !toNode.Matches(normalizedFilter)))
                {
                    continue;
                }

                if (!_nodeViews.TryGetValue(edgeModel.FromId, out var fromView) || !_nodeViews.TryGetValue(edgeModel.ToId, out var toView))
                {
                    continue;
                }

                var baseEdge = fromView.Output.ConnectTo(toView.Input);
                baseEdge.userData = edgeModel;
                baseEdge.capabilities = (Capabilities)0;
                baseEdge.pickingMode = PickingMode.Ignore;
                var color = edgeModel.IsBoundary ? EasyMicPipelineStyles.Boundary : EasyMicPipelineStyles.Edge;
                baseEdge.edgeControl.inputColor = new Color(color.r, color.g, color.b, 0.62f);
                baseEdge.edgeControl.outputColor = baseEdge.edgeControl.inputColor;
                AddElement(baseEdge);

            }
        }

        private void AttachNodesToGroups(EasyMicPipelineGraphModel model)
        {
            for (int n = 0; n < model.Nodes.Count; n++)
            {
                var node = model.Nodes[n];
                if (!_nodeViews.TryGetValue(node.Id, out var view))
                {
                    continue;
                }

                for (int g = 0; g < model.Groups.Count; g++)
                {
                    var group = model.Groups[g];
                    if (group.Bounds.Contains(node.Position) && _groupViews.TryGetValue(group.Id, out var groupView))
                    {
                        groupView.AddElement(view);
                        break;
                    }
                }
            }
        }

        public void UpdateTelemetry(EasyMicPipelineGraphModel model)
        {
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var node = model.Nodes[i];
                if (_nodeViews.TryGetValue(node.Id, out var view))
                {
                    view.UpdateTelemetry(node);
                }
            }
        }

        public void RequestFrameContent()
        {
            _pendingFrame = true;
            _frameAttempts = 0;
            schedule.Execute(TryFrameContent).ExecuteLater(1);
        }

        private void CollectGraphElementsForDelete()
        {
            _deleteBuffer.Clear();
            foreach (var element in graphElements)
            {
                if (element is Edge || element is UnityEditor.Experimental.GraphView.Node || element is Group)
                {
                    _deleteBuffer.Add(element);
                }
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (_pendingFrame)
            {
                schedule.Execute(TryFrameContent).ExecuteLater(1);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || IsGraphElementTarget(evt.target as VisualElement))
            {
                return;
            }

            ClearSelection();
            OnEmptyGraphSelected?.Invoke();
        }

        private static bool IsGraphElementTarget(VisualElement target)
        {
            for (var current = target; current != null; current = current.parent)
            {
                if (current is GraphElement || current is Port)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryFrameContent()
        {
            if (!_pendingFrame)
            {
                return;
            }

            _frameAttempts++;
            if (layout.width <= 1f || layout.height <= 1f || _lastContentBounds.width <= 1f || _lastContentBounds.height <= 1f)
            {
                if (_frameAttempts < 8)
                {
                    schedule.Execute(TryFrameContent).ExecuteLater(16);
                }
                return;
            }

            FrameContent(_lastContentBounds);
            _pendingFrame = false;
        }

        private void FrameContent(Rect contentBounds)
        {
            const float padding = 120f;
            float viewportWidth = Mathf.Max(1f, layout.width);
            float viewportHeight = Mathf.Max(1f, layout.height);
            float contentWidth = Mathf.Max(1f, contentBounds.width + padding * 2f);
            float contentHeight = Mathf.Max(1f, contentBounds.height + padding * 2f);
            float scale = Mathf.Clamp(Mathf.Min(viewportWidth / contentWidth, viewportHeight / contentHeight), 0.22f, 1.0f);

            Vector2 contentCenter = contentBounds.center;
            Vector2 viewportCenter = new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f);
            Vector3 position = viewportCenter - contentCenter * scale;
            UpdateViewTransform(position, new Vector3(scale, scale, 1f));
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            change.edgesToCreate?.Clear();
            change.elementsToRemove?.Clear();
            if (change.movedElements != null)
            {
                change.movedElements.Clear();
            }
            return change;
        }
    }

    internal sealed class EasyMicPipelineNodeView : UnityEditor.Experimental.GraphView.Node
    {
        private readonly Label _subtitle;
        private readonly Label _metrics;
        private readonly Label _thread;
        private readonly VisualElement _activity;
        private string _lastTitle;
        private string _lastSubtitle;
        private string _lastMetrics;
        private EasyMicPipelineThreadKind _lastThread;
        private float _lastActivity = -1f;

        public EasyMicPipelineGraphNode Model { get; private set; }
        public Port Input { get; }
        public Port Output { get; }

        public EasyMicPipelineNodeView(EasyMicPipelineGraphNode model)
        {
            Model = model;
            title = model.Title;
            viewDataKey = model.Id;
            userData = model;
            capabilities = Capabilities.Selectable;

            style.width = model.Width;
            style.minHeight = model.Kind == EasyMicPipelineNodeKind.Group ? 44 : 104;
            style.borderTopLeftRadius = 8;
            style.borderTopRightRadius = 8;
            style.borderBottomLeftRadius = 8;
            style.borderBottomRightRadius = 8;
            style.backgroundColor = EasyMicPipelineStyles.NodeBackground(model.Kind);
            style.borderTopColor = EasyMicPipelineStyles.NodeBorder(model.Thread);
            style.borderRightColor = EasyMicPipelineStyles.NodeBorder(model.Thread);
            style.borderBottomColor = EasyMicPipelineStyles.NodeBorder(model.Thread);
            style.borderLeftColor = EasyMicPipelineStyles.NodeBorder(model.Thread);
            style.borderTopWidth = 1;
            style.borderRightWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;

            titleContainer.style.backgroundColor = Color.clear;
            titleContainer.style.paddingTop = 8;
            titleContainer.style.paddingLeft = 10;
            titleContainer.style.paddingRight = 10;
            titleContainer.style.paddingBottom = 2;

            if (model.Kind == EasyMicPipelineNodeKind.Group)
            {
                Input = null;
                Output = null;
                inputContainer.style.display = DisplayStyle.None;
                outputContainer.style.display = DisplayStyle.None;
                titleContainer.style.paddingBottom = 6;
                titleContainer.style.borderBottomColor = EasyMicPipelineStyles.Separator;
                titleContainer.style.borderBottomWidth = 1;
            }
            else
            {
                Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(float));
                Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
                Input.portName = string.Empty;
                Output.portName = string.Empty;
                Input.capabilities = (Capabilities)0;
                Output.capabilities = (Capabilities)0;
                Input.pickingMode = PickingMode.Ignore;
                Output.pickingMode = PickingMode.Ignore;
                inputContainer.Add(Input);
                outputContainer.Add(Output);
            }

            _subtitle = new Label(model.Subtitle);
            _subtitle.style.color = EasyMicPipelineStyles.SecondaryText;
            _subtitle.style.fontSize = 10;
            _subtitle.style.marginLeft = 10;
            _subtitle.style.marginRight = 10;
            extensionContainer.Add(_subtitle);

            _metrics = new Label(model.Metrics);
            _metrics.style.color = EasyMicPipelineStyles.PrimaryText;
            _metrics.style.fontSize = 11;
            _metrics.style.whiteSpace = WhiteSpace.Normal;
            _metrics.style.marginLeft = 10;
            _metrics.style.marginRight = 10;
            _metrics.style.marginTop = 6;
            extensionContainer.Add(_metrics);

            _activity = new VisualElement();
            _activity.style.height = 3;
            _activity.style.marginLeft = 10;
            _activity.style.marginRight = 10;
            _activity.style.marginTop = 8;
            _activity.style.backgroundColor = EasyMicPipelineStyles.Activity(model.Activity);
            extensionContainer.Add(_activity);

            _thread = new Label(EasyMicPipelineFormatting.ThreadLabel(model.Thread));
            _thread.style.color = EasyMicPipelineStyles.ThreadColor(model.Thread);
            _thread.style.fontSize = 10;
            _thread.style.marginLeft = 10;
            _thread.style.marginRight = 10;
            _thread.style.marginTop = 6;
            _thread.style.marginBottom = 8;
            extensionContainer.Add(_thread);

            if (model.Kind == EasyMicPipelineNodeKind.Group)
            {
                _metrics.style.display = DisplayStyle.None;
                _activity.style.display = DisplayStyle.None;
                _thread.style.display = DisplayStyle.None;
            }

            SetPosition(new Rect(model.Position, new Vector2(model.Width, model.Kind == EasyMicPipelineNodeKind.Group ? 44 : 104)));
            RefreshExpandedState();
            RefreshPorts();
            CacheState(model);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            GetFirstAncestorOfType<EasyMicPipelineGraphView>()?.OnNodeSelected?.Invoke(Model);
        }

        public void UpdateTelemetry(EasyMicPipelineGraphNode model)
        {
            Model = model;
            userData = model;
            if (_lastTitle != model.Title)
            {
                title = model.Title;
                _lastTitle = model.Title;
            }

            if (_lastSubtitle != model.Subtitle)
            {
                _subtitle.text = model.Subtitle;
                _lastSubtitle = model.Subtitle;
            }

            if (_lastMetrics != model.Metrics)
            {
                _metrics.text = model.Metrics;
                _lastMetrics = model.Metrics;
            }

            if (_lastThread != model.Thread)
            {
                _thread.text = EasyMicPipelineFormatting.ThreadLabel(model.Thread);
                _thread.style.color = EasyMicPipelineStyles.ThreadColor(model.Thread);
                var border = EasyMicPipelineStyles.NodeBorder(model.Thread);
                style.borderTopColor = border;
                style.borderRightColor = border;
                style.borderBottomColor = border;
                style.borderLeftColor = border;
                _lastThread = model.Thread;
            }

            if (Mathf.Abs(_lastActivity - model.Activity) > 0.025f)
            {
                _activity.style.backgroundColor = EasyMicPipelineStyles.Activity(model.Activity);
                _lastActivity = model.Activity;
            }
        }

        private void CacheState(EasyMicPipelineGraphNode model)
        {
            _lastTitle = model.Title;
            _lastSubtitle = model.Subtitle;
            _lastMetrics = model.Metrics;
            _lastThread = model.Thread;
            _lastActivity = model.Activity;
        }
    }

    internal sealed class EasyMicPipelineGroupView : Group
    {
        private readonly Label _subtitle;

        public EasyMicPipelineGroupView(EasyMicPipelineGraphGroup model)
        {
            title = model.Title;
            viewDataKey = model.Id;
            capabilities = (Capabilities)0;
            pickingMode = PickingMode.Ignore;
            SetPosition(model.Bounds);

            style.backgroundColor = new Color(0.12f, 0.125f, 0.13f, 0.34f);
            style.borderTopColor = EasyMicPipelineStyles.Separator;
            style.borderRightColor = EasyMicPipelineStyles.Separator;
            style.borderBottomColor = EasyMicPipelineStyles.Separator;
            style.borderLeftColor = EasyMicPipelineStyles.Separator;
            style.borderTopWidth = 1;
            style.borderRightWidth = 1;
            style.borderBottomWidth = 1;
            style.borderLeftWidth = 1;

            _subtitle = new Label(model.Subtitle);
            _subtitle.style.color = EasyMicPipelineStyles.SecondaryText;
            _subtitle.style.fontSize = 10;
            _subtitle.style.marginLeft = 10;
            _subtitle.style.marginBottom = 4;
            Add(_subtitle);
        }
    }

}
#endif
