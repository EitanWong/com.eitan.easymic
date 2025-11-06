#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomEditor(typeof(EasyMicrophone))]
    public class EasyMicrophoneInspector : UnityEditor.Editor
    {
        private EasyMicrophone _mic;

        private SerializedProperty _microphoneOptionsProp;
        private SerializedProperty _deviceOptionsProp;
#if EASYMIC_APM_INTEGRATION
        private SerializedProperty _audioProcessingOptionsProp;
#endif

        // === Preview state ===
        private PreviewPlayer _player = new PreviewPlayer();
        private bool _loopPreview;
        private bool _isScrubbing;

        // === Waveform cache ===
        private Texture2D _waveformTex;
        private AudioClip _waveformFor;
        private int _waveformWidth;
        private const int WaveformHeight = 72;

        private void OnEnable()
        {
            _mic = (EasyMicrophone)target;

            _microphoneOptionsProp = serializedObject.FindProperty("_microphoneOptions");
            _deviceOptionsProp = serializedObject.FindProperty("_deviceOptions");
#if EASYMIC_APM_INTEGRATION
            _audioProcessingOptionsProp = serializedObject.FindProperty("_audioProcessingOptions");
#endif

            SubscribeToRuntimeEvents();
            EditorApplication.update -= EditorUpdate;
            EditorApplication.update += EditorUpdate;
        }

        private void OnDisable()
        {
            UnsubscribeFromRuntimeEvents();
            EditorApplication.update -= EditorUpdate;
            _player?.Dispose();
            _player = null;
        }

        [MenuItem("GameObject/Audio/Input/Easy Microphone", false, -1)]
        public static void AddEasyMicrophone()
        {
            var go = new GameObject("Easy Microphone");
            go.AddComponent<EasyMicrophone>();
            Undo.RegisterCreatedObjectUndo(go, "Create Easy Microphone");

            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawConfigurationSection();

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
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPlayModeNotice()
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Runtime Controls", Styles.SectionHeader);
                EditorGUILayout.HelpBox("Enter Play Mode to monitor microphone status, control recording, and preview captured clips.", MessageType.Info);
            }
        }

        private void DrawStatusSection(EasyMicrophone mic)
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Status", Styles.SectionHeader);

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawStatusBadge(mic.Initialized ? "Initialized" : "Not Initialized", mic.Initialized);
                    DrawStatusBadge(mic.IsRecording ? "Recording" : "Idle", mic.IsRecording);
                }

                EditorGUILayout.Space(Styles.HeaderBodySpacing);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField("Available Devices", mic.AvailableDevices.Length);
                }
            }
        }

        private void DrawConfigurationSection()
        {
            using (new EditorGUILayout.VerticalScope(Styles.Section))
            {
                EditorGUILayout.LabelField("Configuration", Styles.SectionHeader);

                if (_microphoneOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_microphoneOptionsProp);
                }

                if (_deviceOptionsProp != null)
                {
                    EditorGUILayout.PropertyField(_deviceOptionsProp);
                }

#if EASYMIC_APM_INTEGRATION
                EditorGUILayout.Space(Styles.HeaderBodySpacing);
                EditorGUILayout.LabelField("Audio Processing", Styles.SectionHeader);
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
                EditorGUILayout.LabelField("Recording Control", Styles.SectionHeader);

                if (!mic.Initialized && GUILayout.Button("Initialize", Styles.PrimaryButton))
                {
                    mic.Init();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(mic.IsRecording))
                    {
                        if (GUILayout.Button("Start Recording", Styles.PrimaryButton))
                        {
                            mic.StartRecording();
                        }
                    }

                    using (new EditorGUI.DisabledScope(!mic.IsRecording))
                    {
                        if (GUILayout.Button("Stop Recording", Styles.PrimaryButton))
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
                EditorGUILayout.LabelField("Recording Preview", Styles.SectionHeader);

                var clip = mic.LatestRecordingClip;
                if (clip != null)
                {
                    clip.LoadAudioData();
                }


                if (clip == null)
                {
                    EditorGUILayout.HelpBox("Stop recording to generate a preview clip.", MessageType.Info);
                    return;
                }

                // Ensure player is ready
                _player.Ensure(clip);
                _player.SetLoop(_loopPreview);

                // Waveform + playhead
                var waveformRect = GUILayoutUtility.GetRect(10, 10000, WaveformHeight, WaveformHeight, GUILayout.ExpandWidth(true));

                // Mouse seek (drag anywhere, whether playing or not)
                var e = Event.current;
                if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && waveformRect.Contains(e.mousePosition))
                {
                    float tSeek = Mathf.InverseLerp(waveformRect.xMin, waveformRect.xMax, e.mousePosition.x);
                    int frameSeek = Mathf.Clamp(Mathf.RoundToInt(tSeek * clip.samples), 0, Mathf.Max(0, clip.samples - 1));
                    _player.SeekSamples(frameSeek);
                    _isScrubbing = true;
                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isScrubbing = false;
                }

                EnsureWaveformTexture(clip, (int)waveformRect.width);
                if (_waveformTex != null)
                {
                    // background + waveform
                    EditorGUI.DrawRect(waveformRect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.25f) : new Color(0f, 0f, 0f, 0.1f));
                    GUI.DrawTexture(waveformRect, _waveformTex, ScaleMode.StretchToFill);
                    // playhead
                    int currentFrame = _player.CurrentSamples;
                    float t = Mathf.InverseLerp(0f, clip.samples, currentFrame);
                    float x = Mathf.Lerp(waveformRect.xMin, waveformRect.xMax, t);
                    var playhead = new Rect(x - 1f, waveformRect.yMin, 2f, waveformRect.height);
                    EditorGUI.DrawRect(playhead, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.85f) : new Color(0f, 0f, 0f, 0.95f));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    // Icon-only Play/Pause
                    var playIcon = _player.IsPlaying
                        ? EditorGUIUtility.IconContent("PauseButton", "Pause")
                        : EditorGUIUtility.IconContent("PlayButton", "Play");

                    if (GUILayout.Button(playIcon, Styles.IconButton))
                    {
                        _player.PlayPause();
                    }

                    // Icon-only Loop (smaller, styled)
                    var loopIcon = _loopPreview
                        ? EditorGUIUtility.IconContent("preAudioLoopOn", "Loop")
                        : EditorGUIUtility.IconContent("preAudioLoopOff", "Loop");

                    bool newLoop = GUILayout.Toggle(_loopPreview, loopIcon, Styles.IconToggle);
                    if (newLoop != _loopPreview)
                    {
                        _loopPreview = newLoop;
                        _player.SetLoop(_loopPreview);
                    }

                    GUILayout.FlexibleSpace();
                }

                // Lightweight metadata
                EditorGUILayout.LabelField("Length", $"{clip.length:F2} s", Styles.MutedLabel);
                EditorGUILayout.LabelField("Sample Rate", $"{clip.frequency} Hz", Styles.MutedLabel);
                EditorGUILayout.LabelField("Channels", clip.channels.ToString(), Styles.MutedLabel);
            }
        }

        public override bool HasPreviewGUI() => false; // All preview UI is in the inspector.
        public override GUIContent GetPreviewTitle() => new GUIContent("Recording Preview");
        public override void OnPreviewGUI(Rect r, GUIStyle background) { }
        public override void OnPreviewSettings() { }

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
            if ((_player != null && _player.IsPlaying) || _isScrubbing)
            {
                Repaint();
            }
        }

        private void EnsureWaveformTexture(AudioClip clip, int targetWidth)
        {
            targetWidth = Mathf.Clamp(targetWidth, 64, 4096);
            if (_waveformTex != null && _waveformFor == clip && _waveformWidth == targetWidth)
            {
                return;
            }


            _waveformFor = clip;
            _waveformWidth = targetWidth;

            if (_waveformTex != null)
            {
                DestroyImmediate(_waveformTex);
                _waveformTex = null;
            }

            if (clip == null || clip.samples <= 0)
            {
                return;
            }


            _waveformTex = BuildWaveformTexture(clip, targetWidth, WaveformHeight);
        }

        private static Texture2D BuildWaveformTexture(AudioClip clip, int width, int height)
        {
            if (clip == null || width <= 0 || height <= 0)
            {
                return null;
            }

            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                }
            }
            catch
            {
                // Ignore load failures: we'll fallback to an empty waveform.
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            int channels = Mathf.Max(1, clip.channels);
            int totalFrames = Mathf.Max(1, clip.samples);
            int totalColumns = Mathf.Max(1, width);
            var peaks = new float[totalColumns];

            int chunkFrames = Mathf.Clamp(Mathf.Max(clip.frequency / 4, 1024), 2048, 16384);
            float[] reusable = chunkFrames > 0 ? new float[chunkFrames * channels] : Array.Empty<float>();

            for (int frameOffset = 0; frameOffset < totalFrames; frameOffset += chunkFrames)
            {
                int framesToRead = Mathf.Min(chunkFrames, totalFrames - frameOffset);
                if (framesToRead <= 0)
                {
                    break;
                }

                float[] buffer = framesToRead == chunkFrames ? reusable : new float[framesToRead * channels];
                try
                {
                    clip.GetData(buffer, frameOffset);
                }
                catch
                {
                    break; // Access failed (e.g., streaming clip). Abort gracefully.
                }

                for (int frame = 0; frame < framesToRead; frame++)
                {
                    int sampleIndex = frame * channels;
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        sum += Mathf.Abs(buffer[sampleIndex + c]);
                    }
                    float value = sum / channels;
                    int column = (int)((long)(frameOffset + frame) * totalColumns / totalFrames);
                    if (column >= totalColumns)
                    {
                        column = totalColumns - 1;
                    }
                    if (value > peaks[column])
                    {
                        peaks[column] = value;
                    }
                }
            }

            Color bg = EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.9f, 0.9f, 0.9f, 1f);
            Color fg = EditorGUIUtility.isProSkin ? new Color(0.3f, 0.9f, 0.6f, 1f) : new Color(0.1f, 0.5f, 0.3f, 1f);

            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = bg;
            }


            int half = height / 2;
            for (int x = 0; x < width; x++)
            {
                float max = Mathf.Clamp01(peaks[x]);
                int yExtent = Mathf.Clamp(Mathf.RoundToInt(max * (height - 2) * 0.5f), 1, half);
                for (int y = half - yExtent; y <= half + yExtent; y++)
                {
                    int pi = y * width + x;
                    if (pi >= 0 && pi < pixels.Length)
                    {
                        pixels[pi] = fg;
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false);
            return tex;
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
                    if (UnityEngine.Object.FindObjectsOfType<AudioListener>().Length == 0)
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
        }
    }
}
#endif
