#if UNITY_EDITOR
using System;
using System.IO;
using Eitan.EasyMic.Editor;
using Eitan.EasyMic.Editor.Icons;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomEditor(typeof(EasyMicrophone))]
    public class EasyMicrophoneInspector : UnityEditor.Editor
    {
        private const double EditModeRepaintIntervalSeconds = 0.25d;

        private EasyMicrophone _mic;

        private SerializedProperty _microphoneOptionsProp;
        private SerializedProperty _deviceOptionsProp;
        private SerializedProperty _enableLogProp;
#if EASYMIC_APM_INTEGRATION
        private SerializedProperty _audioProcessingOptionsProp;
#endif

        // === Preview state ===
        private PreviewPlayer _player = new PreviewPlayer();
        private bool _loopPreview;
        private bool _isScrubbing;
        private bool _isPanning;
        private int _panButton = -1;

        // === Waveform cache ===
        private Texture2D _waveformTex;
        private AudioClip _waveformTexClip;
        private AudioClip _waveformDataClip;
        private float[] _waveformPeaks;
        private int _waveformResolution;
        private int _waveformTexWidth;
        private float _waveformViewStart;
        private float _waveformViewEnd;
        private float _waveformZoom = 1f;
        private float _waveformScroll;
        private double _nextEditModeRepaintTime;

        private const int WaveformHeight = 72;
        private const float WaveformZoomMin = 1f;
        private const float WaveformZoomMax = 32f;
        private static readonly int WaveformControlHint = "EasyMic_Waveform_Control".GetHashCode();

        protected virtual void OnEnable()
        {
            _mic = (EasyMicrophone)target;

            _microphoneOptionsProp = serializedObject.FindProperty("_microphoneOptions");
            _deviceOptionsProp = serializedObject.FindProperty("_deviceOptions");
            _enableLogProp = serializedObject.FindProperty("_enableLog");
#if EASYMIC_APM_INTEGRATION
            _audioProcessingOptionsProp = serializedObject.FindProperty("_audioProcessingOptions");
#endif

            SubscribeToRuntimeEvents();
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        protected virtual void OnDisable()
        {
            UnsubscribeFromRuntimeEvents();
            EditorApplication.update -= EditorUpdate;
            EditorGUIUtility.SetWantsMouseJumping(0);
            _player?.Dispose();
            _player = null;
            ClearWaveformCache();
        }

        [MenuItem("GameObject/Audio/Input/Easy Microphone", false, -1)]
        public static void AddEasyMicrophone()
        {
            var go = new GameObject(EasyMicEditorLocalization.Text(EasyMicEditorTextKey.EasyMicMenuGameObjectName));
            var mic = go.AddComponent<EasyMicrophone>();
            EasyMicComponentIconInstaller.ApplyTemporaryIcon(mic);
            Undo.RegisterCreatedObjectUndo(go, EasyMicEditorLocalization.Text(EasyMicEditorTextKey.EasyMicMenuCreate));

            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawConfigurationSection();
            DrawAdditionalConfigurationSections();

            EditorGUILayout.Space(Styles.SectionSpacing);

            if (!Application.isPlaying)
            {
                DrawPlayModeNotice();
            }
            else
            {
                var mic = _mic;

                DrawStatusSection(mic);
                EditorGUILayout.Space(Styles.SectionSpacing);

                DrawRecordingControls(mic);
                EditorGUILayout.Space(Styles.SectionSpacing);

                DrawRecordingPreview(mic);
                DrawAdditionalRuntimeSections(mic);
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawAdditionalConfigurationSections()
        {
        }

        protected virtual void DrawAdditionalRuntimeSections(EasyMicrophone mic)
        {
        }

        private void DrawPlayModeNotice()
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicSectionRuntimeControls), Styles.SectionHeader);
                EditorGUILayout.HelpBox(T(EasyMicEditorTextKey.EasyMicPlayModeNotice), MessageType.Info);
            }
        }

        private void DrawStatusSection(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicSectionStatus), Styles.SectionHeader);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusBadge(mic.Initialized ? T(EasyMicEditorTextKey.EasyMicStatusInitialized) : T(EasyMicEditorTextKey.EasyMicStatusNotInitialized), mic.Initialized);
                    DrawStatusBadge(mic.IsRecording ? T(EasyMicEditorTextKey.EasyMicStatusRecording) : T(EasyMicEditorTextKey.EasyMicStatusIdle), mic.IsRecording);
                }

                EditorGUILayout.Space(Styles.HeaderBodySpacing);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField(T(EasyMicEditorTextKey.EasyMicAvailableDevices), mic.AvailableDevices.Length);
                }
            }
        }

        private void DrawConfigurationSection()
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicSectionConfiguration), Styles.SectionHeader);

                if (_microphoneOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_microphoneOptionsProp);
                }

                if (_deviceOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_deviceOptionsProp);
                }

                if (_enableLogProp != null)
                {
                    EditorGUILayout.PropertyField(_enableLogProp, EasyMicEditorLocalization.Content(EasyMicEditorTextKey.CommonEnableLog));
                }

#if EASYMIC_APM_INTEGRATION
                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                if (_audioProcessingOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_audioProcessingOptionsProp);
                }
#endif
            }
        }

        private void DrawRecordingControls(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicSectionRecordingControl), Styles.SectionHeader);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(mic.Initialized))
                    {
                        if (GUILayout.Button(Styles.InitializeContent, Styles.SecondaryButton, GUILayout.Width(160f)))
                        {
                            mic.Init();
                        }
                    }

                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(Styles.HeaderBodySpacing);

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(mic.IsRecording))
                    {
                        if (GUILayout.Button(Styles.StartRecordingContent, Styles.PrimaryButton))
                        {
                            mic.StartRecording();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!mic.IsRecording))
                    {
                        if (GUILayout.Button(Styles.StopRecordingContent, Styles.PrimaryButton))
                        {
                            mic.StopRecording();
                        }
                    }
                }
            }
        }

        private void DrawRecordingPreview(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicSectionRecordingPreview), Styles.SectionHeader);

                var clip = mic.LatestRecordingClip;
                if (clip != null)
                {
                    clip.LoadAudioData();
                }

                if (clip == null)
                {
                    EditorGUILayout.HelpBox(T(EasyMicEditorTextKey.EasyMicPreviewUnavailableNotice), MessageType.Info);
                    return;
                }

                _player.Ensure(clip);
                _player.SetLoop(_loopPreview);

                EnsureWaveformData(clip);

                float viewSpan = 1f / Mathf.Max(WaveformZoomMin, _waveformZoom);
                float maxScroll = Mathf.Max(0f, 1f - viewSpan);
                _waveformScroll = Mathf.Clamp(_waveformScroll, 0f, maxScroll);
                float viewStart = _waveformScroll;
                float viewEnd = Mathf.Min(1f, viewStart + viewSpan);

                var waveformRect = GUILayoutUtility.GetRect(10, 10000, WaveformHeight, WaveformHeight, GUILayout.ExpandWidth(true));

                HandleWaveformInput(waveformRect, clip, viewStart, viewEnd);

                viewSpan = 1f / Mathf.Max(WaveformZoomMin, _waveformZoom);
                maxScroll = Mathf.Max(0f, 1f - viewSpan);
                _waveformScroll = Mathf.Clamp(_waveformScroll, 0f, maxScroll);
                viewStart = _waveformScroll;
                viewEnd = Mathf.Min(1f, viewStart + viewSpan);

                EnsureWaveformTexture(clip, Mathf.Max(64, (int)waveformRect.width), viewStart, viewEnd);
                if (_waveformTex != null)
                {
                    DrawWaveform(waveformRect, clip, viewStart, viewEnd);
                }

                DrawTransportControls(clip);
                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                DrawClipMetadata(clip);
            }
        }

        private void DrawTransportControls(AudioClip clip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Styles.JumpStartIcon, Styles.IconButton))
                {
                    _player.SeekSamples(0);
                    if (!_player.IsPlaying)
                    {
                        Repaint();
                    }
                }

                var playIcon = _player.IsPlaying ? Styles.PauseIcon : Styles.PlayIcon;
                if (GUILayout.Button(playIcon, Styles.IconButton))
                {
                    _player.PlayPause();
                }

                if (GUILayout.Button(Styles.JumpEndIcon, Styles.IconButton))
                {
                    _player.SeekSamples(Mathf.Max(0, clip.samples - 1));
                    if (!_player.IsPlaying)
                    {
                        Repaint();
                    }
                }

                bool newLoop = GUILayout.Toggle(_loopPreview, _loopPreview ? Styles.LoopOnIcon : Styles.LoopOffIcon, Styles.IconToggle);
                if (newLoop != _loopPreview)
                {
                    _loopPreview = newLoop;
                    _player.SetLoop(_loopPreview);
                }

                GUILayout.Space(6f);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_mic.LatestRecordingTempPath)))
                {
                    if (GUILayout.Button(Styles.SaveRecordingContent, Styles.PrimaryButton, GUILayout.Width(160f)))
                    {
                        var micToSave = _mic;
                        EditorApplication.delayCall += () => SaveRecordingViaDialog(micToSave);
                    }
                }
            }
        }

        private void DrawClipMetadata(AudioClip clip)
        {
            EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicLengthLabel), $"{clip.length:F2} s", Styles.MutedLabel);
            EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicSampleRateLabel), $"{clip.frequency} Hz", Styles.MutedLabel);
            EditorGUILayout.LabelField(T(EasyMicEditorTextKey.EasyMicChannelsLabel), clip.channels.ToString(), Styles.MutedLabel);
        }

        private void HandleWaveformInput(Rect rect, AudioClip clip, float normalizedStart, float normalizedEnd)
        {
            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(WaveformControlHint, FocusType.Passive, rect);
            EventType type = e.GetTypeForControl(controlId);

            switch (type)
            {
                case EventType.MouseDown:
                    if (!rect.Contains(e.mousePosition))
                    {
                        break;
                    }

                    if (e.button == 0 && !e.alt && !e.control && !e.command)
                    {
                        GUIUtility.hotControl = controlId;
                        SeekToMouse(rect, clip, normalizedStart, normalizedEnd, e.mousePosition.x);
                        _isScrubbing = true;
                        Repaint();
                        e.Use();
                    }
                    else if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        GUIUtility.hotControl = controlId;
                        _isPanning = true;
                        _panButton = e.button;
                        EditorGUIUtility.SetWantsMouseJumping(1);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId)
                    {
                        break;
                    }

                    if (_isScrubbing && e.button == 0)
                    {
                        SeekToMouse(rect, clip, normalizedStart, normalizedEnd, e.mousePosition.x);
                        Repaint();
                        e.Use();
                    }
                    else if (_isPanning && e.button == _panButton)
                    {
                        float span = 1f / Mathf.Max(WaveformZoomMin, _waveformZoom);
                        if (span < 1f)
                        {
                            float deltaNormalized = -e.delta.x / Mathf.Max(1f, rect.width) * span;
                            PanWaveform(deltaNormalized);
                        }
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId)
                    {
                        break;
                    }

                    GUIUtility.hotControl = 0;
                    if (_isScrubbing && e.button == 0)
                    {
                        _isScrubbing = false;
                    }

                    if (_isPanning && e.button == _panButton)
                    {
                        _isPanning = false;
                        _panButton = -1;
                    }

                    EditorGUIUtility.SetWantsMouseJumping(0);
                    e.Use();
                    break;

                case EventType.ScrollWheel:
                    if (!rect.Contains(e.mousePosition))
                    {
                        break;
                    }

                    float delta = e.delta.y;
                    if (!Mathf.Approximately(delta, 0f))
                    {
                        float zoomFactor = Mathf.Pow(1.1f, -delta);
                        float relative = Mathf.InverseLerp(rect.xMin, rect.xMax, e.mousePosition.x);
                        float pivot = Mathf.Lerp(normalizedStart, normalizedEnd, Mathf.Clamp01(relative));
                        SetWaveformZoom(_waveformZoom * zoomFactor, pivot);
                        e.Use();
                    }
                    break;

                case EventType.Repaint:
                    if (rect.Contains(e.mousePosition))
                    {
                        bool showPanCursor = _isPanning || e.alt || e.button == 2;
                        EditorGUIUtility.AddCursorRect(rect, showPanCursor ? MouseCursor.Pan : MouseCursor.Link);
                    }
                    break;
            }
        }

        private void SeekToMouse(Rect rect, AudioClip clip, float normalizedStart, float normalizedEnd, float mouseX)
        {
            if (clip == null || clip.samples <= 0)
            {
                return;
            }

            float relative = Mathf.InverseLerp(rect.xMin, rect.xMax, mouseX);
            float sampleNormalized = Mathf.Lerp(normalizedStart, normalizedEnd, Mathf.Clamp01(relative));
            int frameSeek = Mathf.Clamp(Mathf.RoundToInt(sampleNormalized * clip.samples), 0, Mathf.Max(0, clip.samples - 1));
            _player.SeekSamples(frameSeek);
        }

        private void DrawWaveform(Rect rect, AudioClip clip, float normalizedStart, float normalizedEnd)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.25f) : new Color(0f, 0f, 0f, 0.1f));
            GUI.DrawTexture(rect, _waveformTex, ScaleMode.StretchToFill);

            int currentSamples = _player.CurrentSamples;
            float normalizedSample = clip.samples <= 0 ? 0f : (float)currentSamples / clip.samples;
            if (normalizedSample >= normalizedStart && normalizedSample <= normalizedEnd)
            {
                float t = Mathf.InverseLerp(normalizedStart, normalizedEnd, normalizedSample);
                float x = Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01(t));
                var playhead = new Rect(x - 1f, rect.yMin, 2f, rect.height);
                EditorGUI.DrawRect(playhead, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.85f) : new Color(0f, 0f, 0f, 0.95f));
            }

            DrawWaveformOverlay(rect, clip, normalizedStart, normalizedEnd);
        }

        private void DrawWaveformOverlay(Rect rect, AudioClip clip, float normalizedStart, float normalizedEnd)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float clipLength = clip != null ? clip.length : 0f;
            string infoText = clipLength > 0f
                ? $"x{_waveformZoom:0.0} · {normalizedStart * clipLength:F2}s – {normalizedEnd * clipLength:F2}s"
                : $"x{_waveformZoom:0.0} · {normalizedStart:P0} – {normalizedEnd:P0}";

            var infoContent = new GUIContent(infoText);
            Vector2 infoSize = Styles.WaveformInfoLabel.CalcSize(infoContent);
            Rect infoRect = new Rect(
                rect.xMax - infoSize.x - 12f,
                rect.y + 6f,
                infoSize.x + 8f,
                infoSize.y + 6f);

            DrawOverlayBackground(infoRect);
            GUI.Label(infoRect, infoContent, Styles.WaveformInfoLabel);

            GUIContent hintContent = Styles.WaveformHintContent;
            Vector2 hintSize = Styles.WaveformHintLabel.CalcSize(hintContent);
            float hintWidth = Mathf.Min(rect.width - 20f, hintSize.x + 12f);
            Rect hintRect = new Rect(
                rect.x + 10f,
                rect.yMax - hintSize.y - 10f,
                hintWidth,
                hintSize.y + 6f);

            DrawOverlayBackground(hintRect);
            GUI.Label(hintRect, hintContent, Styles.WaveformHintLabel);
        }

        private void DrawOverlayBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, Styles.WaveformOverlayBackground);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Styles.WaveformOverlayBorder);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Styles.WaveformOverlayBorder);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), Styles.WaveformOverlayBorder);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Styles.WaveformOverlayBorder);
        }

        private void EnsureWaveformData(AudioClip clip)
        {
            if (clip == null)
            {
                ClearWaveformCache();
                return;
            }

            if (_waveformDataClip != clip)
            {
                _waveformDataClip = clip;
                _waveformPeaks = null;
                _waveformResolution = 0;
                _waveformZoom = WaveformZoomMin;
                _waveformScroll = 0f;
            }

            if (_waveformPeaks != null && _waveformResolution > 0)
            {
                return;
            }

            int resolution = Mathf.Clamp(Mathf.Max(clip.samples / 256, 1024), 1024, 32768);
            _waveformResolution = resolution;
            _waveformPeaks = new float[resolution];

            int channels = Mathf.Max(1, clip.channels);
            int totalFrames = Mathf.Max(1, clip.samples);

            int chunkFrames = Mathf.Clamp(Mathf.Max(clip.frequency / 4, 1024), 2048, 16384);
            float[] buffer = new float[chunkFrames * channels];

            for (int frameOffset = 0; frameOffset < totalFrames; frameOffset += chunkFrames)
            {
                int framesToRead = Mathf.Min(chunkFrames, totalFrames - frameOffset);
                if (framesToRead <= 0)
                {
                    break;
                }

                int samplesToRead = framesToRead * channels;
                if (buffer.Length < samplesToRead)
                {
                    buffer = new float[samplesToRead];
                }

                try
                {
                    clip.GetData(buffer, frameOffset);
                }
                catch
                {
                    break;
                }

                for (int frame = 0; frame < framesToRead; frame++)
                {
                    int sampleIndex = frame * channels;
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += Mathf.Abs(buffer[sampleIndex + c]);
                    }

                    float magnitude = sum / channels;
                    long absoluteFrame = (long)frameOffset + frame;
                    int column = (int)(absoluteFrame * _waveformResolution / (double)totalFrames);
                    column = Mathf.Clamp(column, 0, _waveformResolution - 1);
                    if (magnitude > _waveformPeaks[column])
                    {
                        _waveformPeaks[column] = magnitude;
                    }
                }
            }
        }

        private void EnsureWaveformTexture(AudioClip clip, int targetWidth, float normalizedStart, float normalizedEnd)
        {
            if (clip == null || _waveformPeaks == null || _waveformPeaks.Length == 0)
            {
                if (_waveformTex != null)
                {
                    DestroyImmediate(_waveformTex);
                    _waveformTex = null;
                }

                _waveformTexClip = null;
                _waveformTexWidth = 0;
                _waveformViewStart = 0f;
                _waveformViewEnd = 0f;
                return;
            }

            targetWidth = Mathf.Clamp(targetWidth, 64, 4096);
            normalizedEnd = Mathf.Clamp01(normalizedEnd);
            normalizedStart = Mathf.Clamp(normalizedStart, 0f, normalizedEnd);

            bool needsRebuild = _waveformTex == null
                                || _waveformTexClip != clip
                                || _waveformTexWidth != targetWidth
                                || !Mathf.Approximately(_waveformViewStart, normalizedStart)
                                || !Mathf.Approximately(_waveformViewEnd, normalizedEnd);

            if (!needsRebuild)
            {
                return;
            }

            if (_waveformTex != null)
            {
                DestroyImmediate(_waveformTex);
                _waveformTex = null;
            }

            _waveformTex = BuildWaveformTexture(_waveformPeaks, targetWidth, WaveformHeight, normalizedStart, normalizedEnd);
            _waveformTexClip = clip;
            _waveformTexWidth = targetWidth;
            _waveformViewStart = normalizedStart;
            _waveformViewEnd = normalizedEnd;
        }

        private static Texture2D BuildWaveformTexture(float[] peaks, int width, int height, float normalizedStart, float normalizedEnd)
        {
            if (peaks == null || peaks.Length == 0 || width <= 0 || height <= 0)
            {
                return null;
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color bg = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.9f, 0.9f, 0.9f, 1f);
            Color fg = EditorGUIUtility.isProSkin ? new Color(0.3f, 0.9f, 0.6f, 1f) : new Color(0.1f, 0.5f, 0.3f, 1f);

            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = bg;
            }

            int half = height / 2;
            float span = Mathf.Max(1e-5f, normalizedEnd - normalizedStart);

            for (int x = 0; x < width; x++)
            {
                float columnStart = normalizedStart + span * x / width;
                float columnEnd = normalizedStart + span * (x + 1f) / width;

                int startIdx = Mathf.Clamp(Mathf.FloorToInt(columnStart * peaks.Length), 0, peaks.Length - 1);
                int endIdx = Mathf.Clamp(Mathf.CeilToInt(columnEnd * peaks.Length), startIdx + 1, peaks.Length);

                float max = 0f;
                for (int i = startIdx; i < endIdx; i++)
                {
                    if (peaks[i] > max)
                    {
                        max = peaks[i];
                    }
                }

                max = Mathf.Clamp01(max);
                int yExtent = Mathf.Clamp(Mathf.RoundToInt(max * (height - 2) * 0.5f), 1, half);
                for (int y = half - yExtent; y <= half + yExtent; y++)
                {
                    int idx = y * width + x;
                    if (idx >= 0 && idx < pixels.Length)
                    {
                        pixels[idx] = fg;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false);
            return tex;
        }

        private void SetWaveformZoom(float targetZoom, float pivotNormalized = -1f)
        {
            float clamped = Mathf.Clamp(targetZoom, WaveformZoomMin, WaveformZoomMax);
            if (Mathf.Approximately(clamped, _waveformZoom))
            {
                return;
            }

            float previousZoom = _waveformZoom;
            float previousSpan = 1f / Mathf.Max(WaveformZoomMin, previousZoom);

            _waveformZoom = clamped;

            float newSpan = 1f / Mathf.Max(WaveformZoomMin, _waveformZoom);
            float maxScroll = Mathf.Max(0f, 1f - newSpan);
            float newScroll;

            if (pivotNormalized >= 0f)
            {
                float pivotRatio = previousSpan <= 0f
                    ? 0.5f
                    : (pivotNormalized - _waveformScroll) / previousSpan;
                if (float.IsNaN(pivotRatio))
                {
                    pivotRatio = 0.5f;
                }

                pivotRatio = Mathf.Clamp01(pivotRatio);
                newScroll = pivotNormalized - pivotRatio * newSpan;
            }
            else
            {
                float center = _waveformScroll + previousSpan * 0.5f;
                newScroll = center - newSpan * 0.5f;
            }

            _waveformScroll = Mathf.Clamp(newScroll, 0f, maxScroll);
            Repaint();
        }

        private void PanWaveform(float normalizedDelta)
        {
            if (!(_waveformZoom > WaveformZoomMin))
            {
                _waveformScroll = 0f;
                return;
            }

            float span = 1f / Mathf.Max(WaveformZoomMin, _waveformZoom);
            float maxScroll = Mathf.Max(0f, 1f - span);
            _waveformScroll = Mathf.Clamp(_waveformScroll + normalizedDelta, 0f, maxScroll);
            Repaint();
        }

        private void AutoScrollToPlayhead(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0 || !_player.IsPlaying || _isScrubbing)
            {
                return;
            }

            float normalizedSample = (float)_player.CurrentSamples / clip.samples;
            float span = 1f / Mathf.Max(WaveformZoomMin, _waveformZoom);
            if (span >= 1f)
            {
                _waveformScroll = 0f;
                return;
            }

            float start = _waveformScroll;
            float end = start + span;
            float maxScroll = Mathf.Max(0f, 1f - span);
            const float margin = 0.05f;

            if (normalizedSample > end - margin)
            {
                _waveformScroll = Mathf.Clamp(normalizedSample - span + margin, 0f, maxScroll);
                Repaint();
            }
            else if (normalizedSample < start + margin)
            {
                _waveformScroll = Mathf.Clamp(normalizedSample - margin, 0f, maxScroll);
                Repaint();
            }
        }

        private void ClearWaveformCache()
        {
            if (_waveformTex != null)
            {
                DestroyImmediate(_waveformTex);
                _waveformTex = null;
            }

            _waveformTexClip = null;
            _waveformDataClip = null;
            _waveformPeaks = null;
            _waveformResolution = 0;
            _waveformTexWidth = 0;
            _waveformViewStart = 0f;
            _waveformViewEnd = 0f;
            _waveformZoom = WaveformZoomMin;
            _waveformScroll = 0f;
        }

        public override bool HasPreviewGUI() => false; // All preview UI is in the inspector.
        public override GUIContent GetPreviewTitle() => EasyMicEditorLocalization.Content(EasyMicEditorTextKey.EasyMicPreviewTitle);
        public override void OnPreviewGUI(Rect r, GUIStyle background) { }
        public override void OnPreviewSettings() { }

        private static void SaveRecordingViaDialog(EasyMicrophone mic)
        {
            if (mic == null)
            {
                return;
            }

            var tempPath = mic.LatestRecordingTempPath;
            if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
            {
                EditorUtility.DisplayDialog(T(EasyMicEditorTextKey.EasyMicSaveRecordingTitle), T(EasyMicEditorTextKey.EasyMicSaveRecordingUnavailable), T(EasyMicEditorTextKey.CommonOk));
                return;
            }

            string defaultName = Path.GetFileNameWithoutExtension(tempPath);
            if (string.IsNullOrEmpty(defaultName))
            {
                defaultName = $"EasyMic_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            var saveDirectory = Path.GetDirectoryName(mic.LastSavedPath);
            if (string.IsNullOrEmpty(saveDirectory) || !Directory.Exists(saveDirectory))
            {
                saveDirectory = Application.persistentDataPath;
            }

            string targetPath = EditorUtility.SaveFilePanel(T(EasyMicEditorTextKey.EasyMicSaveRecordingTitle), saveDirectory, defaultName, "wav");
            if (string.IsNullOrEmpty(targetPath))
            {
                return; // user canceled
            }

            bool success = mic.TrySaveLatestRecording(targetPath);
            if (!success)
            {
                EditorUtility.DisplayDialog(T(EasyMicEditorTextKey.EasyMicSaveRecordingTitle), T(EasyMicEditorTextKey.EasyMicSaveRecordingFailed), T(EasyMicEditorTextKey.CommonOk));
            }
            else
            {
                AssetDatabase.Refresh();
            }
        }

        private static string T(EasyMicEditorTextKey key)
        {
            return EasyMicEditorLocalization.Text(key);
        }

        private void SubscribeToRuntimeEvents()
        {
            if (_mic == null)
            {
                return;
            }


            _mic.OnRecordingStateChanged -= HandleRecordingStateChanged;
            _mic.OnRecordingStateChanged += HandleRecordingStateChanged;

            _mic.OnMicrophoneInitialized -= HandleMicrophoneInitialized;
            _mic.OnMicrophoneInitialized += HandleMicrophoneInitialized;
        }

        private void UnsubscribeFromRuntimeEvents()
        {
            if (_mic == null)
            {
                return;
            }


            _mic.OnRecordingStateChanged -= HandleRecordingStateChanged;
            _mic.OnMicrophoneInitialized -= HandleMicrophoneInitialized;
        }

        private void HandleRecordingStateChanged(bool _)
        {
            Repaint();
        }

        private void HandleMicrophoneInitialized(bool _)
        {
            Repaint();
        }

        private void EditorUpdate()
        {
            if (!Application.isPlaying)
            {
#if EASYMIC_APM_INTEGRATION
                if (_audioProcessingOptionsProp != null)
                {
                    double now = EditorApplication.timeSinceStartup;
                    if (now >= _nextEditModeRepaintTime)
                    {
                        _nextEditModeRepaintTime = now + EditModeRepaintIntervalSeconds;
                        Repaint();
                    }
                }
#endif
                return;
            }

            if (_player == null)
            {
                return;
            }

            if (_player.IsPlaying)
            {
                if (_mic != null)
                {
                    var clip = _mic.LatestRecordingClip;
                    if (clip != null)
                    {
                        AutoScrollToPlayhead(clip);
                    }
                }

                Repaint();
            }
            else if (_isScrubbing)
            {
                Repaint();
            }
        }

        private static void DrawStatusBadge(string label, bool active)
        {
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = active ? Styles.BadgeActiveColor : Styles.BadgeInactiveColor;
            GUILayout.Label(label, Styles.StatusBadge, GUILayout.ExpandWidth(false));
            GUI.backgroundColor = previousColor;
        }

        private sealed class PreviewPlayer : IDisposable
        {
            private GameObject _go;
            private AudioSource _source;
            private int _lastKnownSamples;

            public bool IsPlaying => _source != null && _source.isPlaying;

            public void Ensure(AudioClip clip)
            {
                if (_source == null)
                {
                    _go = new GameObject("EasyMic_Preview_AudioSource");
                    _go.hideFlags = HideFlags.HideAndDontSave; // hidden, unsaved, and cleaned up manually
                    _source = _go.AddComponent<AudioSource>();
                    _source.spatialBlend = 0f;     // 2D
                    _source.playOnAwake = false;
                    _source.loop = false;

                    // Ensure there is at least one AudioListener so playback is audible
                    if (!HasAudioListener())
                    {
                        _go.AddComponent<AudioListener>();
                    }
                }

                if (_source.clip != clip)
                {
                    _source.clip = clip;
                    _lastKnownSamples = 0;
                }
            }

            public void SetLoop(bool loop)
            {
                if (_source != null)
                {
                    _source.loop = loop;
                }

            }

            public void PlayPause()
            {
                if (_source == null || _source.clip == null)
                {
                    return;
                }


                if (_source.isPlaying)
                {
                    _source.Pause();
                    return;
                }

                var clip = _source.clip;
                // Make sure audio data is available before trying to play
                try
                {
                    if (clip.loadState != AudioDataLoadState.Loaded)
                    {
                        clip.LoadAudioData();
                    }

                }
                catch { /* Fallback silently if API not available in older versions */ }

                // If playhead is at or beyond the last sample, restart from 0
                if (_source.timeSamples >= Mathf.Max(0, clip.samples - 1))
                {
                    _source.timeSamples = 0;
                }


                _source.Play();
            }

            public void SeekSamples(int samples)
            {
                if (_source == null || _source.clip == null)
                {
                    return;
                }


                samples = Mathf.Clamp(samples, 0, Mathf.Max(0, _source.clip.samples - 1));
                _source.timeSamples = samples; // precise sample seek
                _lastKnownSamples = samples;
            }

            public int CurrentSamples
            {
                get
                {
                    if (_source == null || _source.clip == null)
                    {
                        return _lastKnownSamples;
                    }


                    _lastKnownSamples = Mathf.Clamp(_source.timeSamples, 0, Mathf.Max(0, _source.clip.samples - 1));
                    return _lastKnownSamples;
                }
            }

            public void Dispose()
            {
                if (_go != null)
                {
                    UnityEngine.Object.DestroyImmediate(_go);
                    _go = null;
                    _source = null;
                    _lastKnownSamples = 0;
                }
            }

            private static bool HasAudioListener()
            {
#if UNITY_2023_1_OR_NEWER
                return UnityEngine.Object.FindObjectsByType<AudioListener>().Length > 0;
#else
                return UnityEngine.Object.FindObjectsOfType<AudioListener>().Length > 0;
#endif
            }
        }

        private static class Styles
        {
            private static GUIStyle _section;
            private static GUIStyle _sectionHeader;
            private static GUIStyle _statusBadge;
            private static GUIStyle _mutedLabel;
            private static GUIStyle _secondaryButton;
            private static GUIStyle _primaryButton;
            private static GUIStyle _iconButton;
            private static GUIStyle _iconToggle;
            private static GUIContent _playIcon;
            private static GUIContent _pauseIcon;
            private static GUIContent _loopOnIcon;
            private static GUIContent _loopOffIcon;
            private static GUIContent _jumpStartIcon;
            private static GUIContent _jumpEndIcon;
            private static GUIStyle _waveformInfoLabel;
            private static GUIStyle _waveformHintLabel;
            private static GUIContent _waveformHintContent;
            private static GUIContent _initializeContent;
            private static GUIContent _startRecordingContent;
            private static GUIContent _stopRecordingContent;
            private static GUIContent _saveRecordingContent;

            public static GUIStyle Section
            {
                get
                {
                    if (_section == null)
                    {
                        _section = new GUIStyle("HelpBox")
                        {
                            padding = new RectOffset(12, 12, 10, 12),
                            margin = new RectOffset(0, 0, 0, 4)
                        };
                    }

                    return _section;
                }
            }

            public static GUIStyle SectionHeader
            {
                get
                {
                    if (_sectionHeader == null)
                    {
                        _sectionHeader = new GUIStyle(EditorStyles.boldLabel)
                        {
                            fontSize = 11,
                            fontStyle = FontStyle.Bold
                        };
                    }

                    return _sectionHeader;
                }
            }

            public static GUIStyle StatusBadge
            {
                get
                {
                    if (_statusBadge == null)
                    {
                        _statusBadge = new GUIStyle(EditorStyles.miniButtonMid)
                        {
                            padding = new RectOffset(10, 10, 3, 3),
                            fontSize = 10,
                            alignment = TextAnchor.MiddleCenter
                        };
                    }

                    return _statusBadge;
                }
            }

            public static GUIStyle MutedLabel
            {
                get
                {
                    if (_mutedLabel == null)
                    {
                        _mutedLabel = new GUIStyle(EditorStyles.label)
                        {
                            fontSize = 10,
                            normal =
                            {
                                textColor = EditorGUIUtility.isProSkin
                                    ? new Color(0.7f, 0.7f, 0.7f)
                                    : new Color(0.35f, 0.35f, 0.35f)
                            }
                        };
                    }

                    return _mutedLabel;
                }
            }

            public static GUIStyle SecondaryButton
            {
                get
                {
                    if (_secondaryButton == null)
                    {
                        _secondaryButton = new GUIStyle(EditorStyles.miniButton)
                        {
                            fixedHeight = 22,
                            padding = new RectOffset(12, 12, 2, 2)
                        };
                    }

                    return _secondaryButton;
                }
            }

            public static GUIStyle PrimaryButton
            {
                get
                {
                    if (_primaryButton == null)
                    {
                        _primaryButton = new GUIStyle(GUI.skin.button)
                        {
                            fixedHeight = 26,
                            fontStyle = FontStyle.Bold
                        };
                    }

                    return _primaryButton;
                }
            }

            public static GUIStyle IconButton
            {
                get
                {
                    if (_iconButton == null)
                    {
                        _iconButton = new GUIStyle(EditorStyles.miniButton)
                        {
                            fixedWidth = 26,
                            fixedHeight = 26,
                            padding = new RectOffset(2, 2, 2, 2),
                            alignment = TextAnchor.MiddleCenter
                        };
                    }
                    return _iconButton;
                }
            }

            public static GUIStyle IconToggle
            {
                get
                {
                    if (_iconToggle == null)
                    {
                        _iconToggle = new GUIStyle(EditorStyles.miniButton)
                        {
                            fixedWidth = 24,
                            fixedHeight = 24,
                            padding = new RectOffset(2, 2, 2, 2),
                            alignment = TextAnchor.MiddleCenter
                        };
                    }
                    return _iconToggle;
                }
            }

            public static float SectionSpacing => 8f;
            public static float HeaderBodySpacing => 4f;

            public static Color BadgeActiveColor => EditorGUIUtility.isProSkin ? new Color(0.2f, 0.65f, 0.4f) : new Color(0.25f, 0.7f, 0.4f);
            public static Color BadgeInactiveColor => EditorGUIUtility.isProSkin ? new Color(0.3f, 0.3f, 0.3f) : new Color(0.75f, 0.75f, 0.75f);
            public static Color WaveformOverlayBackground => EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.6f) : new Color(1f, 1f, 1f, 0.85f);
            public static Color WaveformOverlayBorder => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.12f);

            public static GUIContent PlayIcon => _playIcon ??= MakeIcon("PlayButton", T(EasyMicEditorTextKey.EasyMicPlay), ">");
            public static GUIContent PauseIcon => _pauseIcon ??= MakeIcon("PauseButton", T(EasyMicEditorTextKey.EasyMicPause), "||");
            public static GUIContent LoopOnIcon => _loopOnIcon ??= MakeIcon("preAudioLoopOn", T(EasyMicEditorTextKey.EasyMicLoopEnabled), "Loop");
            public static GUIContent LoopOffIcon => _loopOffIcon ??= MakeIcon("preAudioLoopOff", T(EasyMicEditorTextKey.EasyMicLoopDisabled), "Loop");
            public static GUIContent JumpStartIcon => _jumpStartIcon ??= MakeIcon("beginButton", T(EasyMicEditorTextKey.EasyMicGoToStart), "|<");
            public static GUIContent JumpEndIcon => _jumpEndIcon ??= MakeIcon("endButton", T(EasyMicEditorTextKey.EasyMicGoToEnd), ">|");
            public static GUIStyle WaveformInfoLabel
            {
                get
                {
                    if (_waveformInfoLabel == null)
                    {
                        _waveformInfoLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                        {
                            alignment = TextAnchor.MiddleRight,
                            padding = new RectOffset(6, 6, 2, 2),
                            normal =
                            {
                                textColor = EditorGUIUtility.isProSkin
                                    ? new Color(0.92f, 0.92f, 0.92f)
                                    : new Color(0.12f, 0.12f, 0.12f)
                            }
                        };
                    }

                    return _waveformInfoLabel;
                }
            }

            public static GUIStyle WaveformHintLabel
            {
                get
                {
                    if (_waveformHintLabel == null)
                    {
                        _waveformHintLabel = new GUIStyle(EditorStyles.miniLabel)
                        {
                            alignment = TextAnchor.MiddleLeft,
                            fontSize = 10,
                            wordWrap = true,
                            padding = new RectOffset(6, 6, 2, 2),
                            normal =
                            {
                                textColor = EditorGUIUtility.isProSkin
                                    ? new Color(0.8f, 0.8f, 0.8f)
                                    : new Color(0.2f, 0.2f, 0.2f)
                            }
                        };
                    }

                    return _waveformHintLabel;
                }
            }

            public static GUIContent WaveformHintContent => _waveformHintContent ??= EasyMicEditorLocalization.Content(EasyMicEditorTextKey.EasyMicWaveformHint);
            public static GUIContent InitializeContent => _initializeContent ??= MakeLabeledIcon("Refresh", T(EasyMicEditorTextKey.EasyMicInitialize), T(EasyMicEditorTextKey.EasyMicInitializeTooltip), T(EasyMicEditorTextKey.EasyMicInitialize));
            public static GUIContent StartRecordingContent => _startRecordingContent ??= MakeLabeledIcon("Animation.Record", T(EasyMicEditorTextKey.EasyMicStart), T(EasyMicEditorTextKey.EasyMicStartTooltip), T(EasyMicEditorTextKey.EasyMicStart));
            public static GUIContent StopRecordingContent => _stopRecordingContent ??= MakeLabeledIcon("PauseButton", T(EasyMicEditorTextKey.EasyMicStop), T(EasyMicEditorTextKey.EasyMicStopTooltip), T(EasyMicEditorTextKey.EasyMicStop));
            public static GUIContent SaveRecordingContent => _saveRecordingContent ??= MakeLabeledIcon("SaveActive", T(EasyMicEditorTextKey.EasyMicSave), T(EasyMicEditorTextKey.EasyMicSaveTooltip), T(EasyMicEditorTextKey.EasyMicSave));

            private static GUIContent MakeLabeledIcon(string iconName, string text, string tooltip, string fallbackText)
            {
                EasyMicIconId fallbackId = ResolveFallbackIcon(iconName);
                return iconName == "Refresh"
                    ? EasyMicIcons.LabeledBuiltInContent(EasyMicBuiltInIconId.Refresh, fallbackId, text, tooltip)
                    : EasyMicIcons.LabeledContent(fallbackId, text, tooltip);
            }

            private static GUIContent MakeIcon(string name, string tooltip, string fallbackText)
            {
                EasyMicIconId fallbackId = ResolveFallbackIcon(name);
                return name == "Refresh"
                    ? EasyMicIcons.BuiltInContent(EasyMicBuiltInIconId.Refresh, fallbackId, fallbackText, tooltip)
                    : EasyMicIcons.Content(fallbackId, tooltip);
            }

            private static EasyMicIconId ResolveFallbackIcon(string iconName)
            {
                switch (iconName)
                {
                    case "PlayButton":
                        return EasyMicIconId.AudioOutput;
                    case "Animation.Record":
                        return EasyMicIconId.AudioInput;
                    case "PauseButton":
                        return EasyMicIconId.Warning;
                    case "SaveActive":
                        return EasyMicIconId.Success;
                    case "preAudioLoopOn":
                    case "preAudioLoopOff":
                        return EasyMicIconId.Refresh;
                    case "beginButton":
                    case "endButton":
                        return EasyMicIconId.Pick;
                    case "Refresh":
                        return EasyMicIconId.Refresh;
                    default:
                        return EasyMicIconId.EasyMic;
                }
            }
        }
    }
}
#endif
