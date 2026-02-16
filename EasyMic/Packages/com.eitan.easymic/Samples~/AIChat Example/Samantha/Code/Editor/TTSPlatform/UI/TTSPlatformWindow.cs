// ============================================================================
// TTSPlatformWindow.cs - TTS平台管理主窗口
// 提供音色管理和语音合成测试的完整UI
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TTSPlatform.Core;
using UnityEditor;
using UnityEngine;

namespace TTSPlatform.UI
{
    /// <summary>
    /// TTS平台管理主窗口
    /// </summary>
    public class TTSPlatformWindow : EditorWindow
    {
        #region 菜单入口

        [MenuItem("Tools/TTS Platform Manager %#t")]
        public static void ShowWindow()
        {
            var window = GetWindow<TTSPlatformWindow>();
            window.titleContent = new GUIContent("TTS Platform", EditorGUIUtility.IconContent("d_SceneViewAudio").image);
            window.minSize = new Vector2(600, 500);
            window.Show();
        }

        #endregion

        #region 字段

        // 当前状态
        private ITTSService _currentService;
        private int _selectedServiceIndex;
        private int _selectedTab;
        private string[] _serviceNames;
        private string[] _tabNames = { "Voice Management", "Synthesis", "Settings" };

        // 滚动位置
        private Vector2 _mainScrollPos;
        private Vector2 _voiceListScrollPos;

        // 音色管理
        private List<VoiceInfo> _voiceList = new List<VoiceInfo>();
        private bool _isLoadingVoices;
        private string _voiceSearchFilter = "";

        // 上传音色
        private string _uploadCustomName = "";
        private string _uploadText = "";
        private string _uploadAudioPath = "";
        private int _uploadModelIndex;
        private bool _isUploading;
        private float _uploadProgress;

        // 语音合成
        private string _synthesisInput = "Hello, this is a test.";
        private int _synthesisModelIndex;
        private int _synthesisVoiceIndex;
        private bool _useCustomVoice;
        private int _customVoiceIndex;
        private float _synthesisSpeed = 1f;
        private float _synthesisGain = 0f;
        private int _synthesisFormatIndex;
        private int _synthesisSampleRateIndex = 3; // 默认32000
        private bool _isSynthesizing;
        private float _synthesisProgress;
        private byte[] _lastSynthesizedAudio;
        private AudioClip _previewClip;

        // 设置
        private string _tempApiKey = "";
        private bool _showApiKey;

        // 状态消息
        private string _statusMessage = "";
        private MessageType _statusType = MessageType.None;
        private double _statusClearTime;

        #endregion

        #region 生命周期

        private void OnEnable()
        {
            RefreshServiceList();
            if (_currentService != null && _currentService.IsConfigured)
            {
                _ = RefreshVoiceListAsync();
            }
        }

        private void OnDisable()
        {
            CleanupPreviewClip();
        }

        private void Update()
        {
            // 清除过期的状态消息
            if (!string.IsNullOrEmpty(_statusMessage) && EditorApplication.timeSinceStartup > _statusClearTime)
            {
                _statusMessage = "";
                Repaint();
            }
        }

        #endregion

        #region 主绘制

        private void OnGUI()
        {
            // 重置样式（编辑器重载后）
            if (Event.current.type == EventType.Layout)
            {
                TTSPlatformStyles.Reset();
            }

            DrawToolbar();

            _mainScrollPos = EditorGUILayout.BeginScrollView(_mainScrollPos);

            EditorGUILayout.Space(10);

            // 服务选择
            DrawServiceSelector();

            EditorGUILayout.Space(5);

            // 检查服务配置
            if (_currentService == null)
            {
                EditorGUILayout.HelpBox("Select a TTS service platform.", MessageType.Info);
            }
            else if (!_currentService.IsConfigured)
            {
                EditorGUILayout.HelpBox(
                    $"Configure the API key for {_currentService.DisplayName} in Settings.",
                    MessageType.Warning);
                _selectedTab = 2; // 自动跳转到设置
            }

            // 标签页
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabNames, GUILayout.Height(28));

            EditorGUILayout.Space(10);

            // 绘制当前标签页
            switch (_selectedTab)
            {
                case 0: DrawVoiceManagementTab(); break;
                case 1: DrawSynthesisTab(); break;
                case 2: DrawSettingsTab(); break;
            }

            EditorGUILayout.EndScrollView();

            // 状态栏
            DrawStatusBar();
        }

        #endregion

        #region 工具栏

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _ = RefreshVoiceListAsync();
            }

            GUILayout.FlexibleSpace();

            // 服务状态指示
            if (_currentService != null)
            {
                var statusIcon = _currentService.IsConfigured
                    ? "d_greenLight"
                    : "d_orangeLight";
                var statusText = _currentService.IsConfigured
                    ? "Configured"
                    : "Not configured";
                GUILayout.Label(new GUIContent(statusText, EditorGUIUtility.IconContent(statusIcon).image),
                    EditorStyles.toolbarButton);
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 服务选择器

        private void DrawServiceSelector()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Service", GUILayout.Width(60));

            var newIndex = EditorGUILayout.Popup(_selectedServiceIndex, _serviceNames);
            if (newIndex != _selectedServiceIndex)
            {
                _selectedServiceIndex = newIndex;
                OnServiceChanged();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RefreshServiceList()
        {
            var services = TTSServiceRegistry.RegisteredServices.ToList();
            _serviceNames = services.Select(s => s.DisplayName).ToArray();

            if (services.Count > 0)
            {
                _selectedServiceIndex = Mathf.Clamp(_selectedServiceIndex, 0, services.Count - 1);
                _currentService = services[_selectedServiceIndex];
                _tempApiKey = _currentService.ApiKey ?? "";
            }
        }

        private void OnServiceChanged()
        {
            var services = TTSServiceRegistry.RegisteredServices.ToList();
            if (_selectedServiceIndex >= 0 && _selectedServiceIndex < services.Count)
            {
                _currentService = services[_selectedServiceIndex];
                _tempApiKey = _currentService.ApiKey ?? "";
                _voiceList.Clear();

                if (_currentService.IsConfigured)
                {
                    _ = RefreshVoiceListAsync();
                }
            }
        }

        #endregion

        #region 音色管理标签页

        private void DrawVoiceManagementTab()
        {
            if (_currentService == null || !_currentService.IsConfigured)
            {
                EditorGUILayout.HelpBox("Configure a service first.", MessageType.Warning);
                return;
            }

            // 上传区域
            DrawUploadSection();

            EditorGUILayout.Space(15);

            // 音色列表
            DrawVoiceListSection();
        }

        private void DrawUploadSection()
        {
            EditorGUILayout.LabelField("Upload Reference Audio", TTSPlatformStyles.Header);

            EditorGUILayout.BeginVertical(TTSPlatformStyles.Box);

            var capabilities = _currentService.GetCapabilities();
            EnsureSynthesisOptionIndices(capabilities);

            // 模型选择
            if (capabilities.VoiceUploadModels.Count > 0)
            {
                var modelNames = capabilities.VoiceUploadModels.Select(m => m.DisplayName).ToArray();
                _uploadModelIndex = EditorGUILayout.Popup("Model", _uploadModelIndex, modelNames);
            }

            // 自定义名称
            _uploadCustomName = EditorGUILayout.TextField("Voice Name", _uploadCustomName);

            // 参考文本
            EditorGUILayout.LabelField("Reference text (must match the audio content)");
            _uploadText = EditorGUILayout.TextArea(_uploadText, TTSPlatformStyles.TextArea,
                GUILayout.Height(60));

            // 音频文件
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Audio File", GUILayout.Width(80));

            _uploadAudioPath = EditorGUILayout.TextField(_uploadAudioPath);

            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                var path = EditorUtility.OpenFilePanel("Select audio file", "", "mp3,wav,ogg,flac,m4a");
                if (!string.IsNullOrEmpty(path))
                {
                    _uploadAudioPath = path;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 上传进度
            if (_isUploading)
            {
                var rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, _uploadProgress, $"Uploading... {(_uploadProgress * 100):F0}%");
            }

            EditorGUILayout.Space(5);

            // 上传按钮
            GUI.enabled = !_isUploading && CanUpload();
            if (GUILayout.Button("Upload Voice", TTSPlatformStyles.ButtonLarge))
            {
                _ = UploadVoiceAsync();
            }
            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        private bool CanUpload()
        {
            return !string.IsNullOrWhiteSpace(_uploadCustomName)
                && !string.IsNullOrWhiteSpace(_uploadText)
                && !string.IsNullOrWhiteSpace(_uploadAudioPath)
                && File.Exists(_uploadAudioPath);
        }

        private async Task UploadVoiceAsync()
        {
            _isUploading = true;
            _uploadProgress = 0;

            var capabilities = _currentService.GetCapabilities();
            var model = capabilities.VoiceUploadModels.Count > _uploadModelIndex
                ? capabilities.VoiceUploadModels[_uploadModelIndex].Id
                : "";

            var request = new VoiceUploadRequest
            {
                Model = model,
                CustomName = _uploadCustomName,
                Text = _uploadText,
                AudioFilePath = _uploadAudioPath
            };

            var progress = new Progress<float>(p =>
            {
                _uploadProgress = p;
                Repaint();
            });

            var result = await _currentService.UploadVoiceAsync(request, progress);

            _isUploading = false;

            if (result.Success)
            {
                ShowStatus($"Upload succeeded. Voice URI: {result.VoiceUri}", MessageType.Info);
                _uploadCustomName = "";
                _uploadText = "";
                _uploadAudioPath = "";
                await RefreshVoiceListAsync();
            }
            else
            {
                ShowStatus($"Upload failed: {result.Message}", MessageType.Error);
            }

            Repaint();
        }

        private void DrawVoiceListSection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("My Voices", TTSPlatformStyles.Header);

            GUILayout.FlexibleSpace();

            // 搜索框
            _voiceSearchFilter = EditorGUILayout.TextField(_voiceSearchFilter,
                EditorStyles.toolbarSearchField, GUILayout.Width(200));

            if (GUILayout.Button("Refresh", GUILayout.Width(60)))
            {
                _ = RefreshVoiceListAsync();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (_isLoadingVoices)
            {
                EditorGUILayout.HelpBox("Loading...", MessageType.None);
                return;
            }

            if (_voiceList.Count == 0)
            {
                EditorGUILayout.HelpBox("No voices yet. Upload reference audio to create one.", MessageType.Info);
                return;
            }

            // 筛选
            var filteredVoices = string.IsNullOrEmpty(_voiceSearchFilter)
                ? _voiceList
                : _voiceList.Where(v =>
                    v.CustomName.IndexOf(_voiceSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    v.Uri.IndexOf(_voiceSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // 表头
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Name", EditorStyles.toolbarButton, GUILayout.Width(150));
            GUILayout.Label("Model", EditorStyles.toolbarButton, GUILayout.Width(200));
            GUILayout.Label("Created", EditorStyles.toolbarButton, GUILayout.Width(150));
            GUILayout.Label("Actions", EditorStyles.toolbarButton, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            // 列表
            _voiceListScrollPos = EditorGUILayout.BeginScrollView(_voiceListScrollPos,
                GUILayout.MaxHeight(300));

            foreach (var voice in filteredVoices)
            {
                DrawVoiceItem(voice);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawVoiceItem(VoiceInfo voice)
        {
            EditorGUILayout.BeginHorizontal(TTSPlatformStyles.Box);

            GUILayout.Label(voice.CustomName, GUILayout.Width(150));
            GUILayout.Label(voice.Model, GUILayout.Width(200));
            GUILayout.Label(voice.CreatedAt.ToString("yyyy-MM-dd HH:mm"), GUILayout.Width(150));

            // 复制URI
            if (GUILayout.Button("Copy", GUILayout.Width(45)))
            {
                EditorGUIUtility.systemCopyBuffer = voice.Uri;
                ShowStatus("URI copied to clipboard.", MessageType.Info);
            }

            // 删除
            GUI.color = new Color(1f, 0.6f, 0.6f);
            if (GUILayout.Button("Delete", GUILayout.Width(55)))
            {
                if (EditorUtility.DisplayDialog("Confirm deletion",
                    $"Delete voice '{voice.CustomName}'? This cannot be undone.",
                    "Delete", "Cancel"))
                {
                    _ = DeleteVoiceAsync(voice.Uri);
                }
            }
            GUI.color = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private async Task RefreshVoiceListAsync()
        {
            if (_currentService == null || !_currentService.IsConfigured)
            {
                return;
            }


            _isLoadingVoices = true;
            Repaint();

            var result = await _currentService.GetVoiceListAsync();

            _isLoadingVoices = false;

            if (result.Success)
            {
                _voiceList = result.Voices ?? new List<VoiceInfo>();
            }
            else
            {
                ShowStatus($"Failed to load voice list: {result.Message}", MessageType.Error);
            }

            Repaint();
        }

        private async Task DeleteVoiceAsync(string uri)
        {
            var result = await _currentService.DeleteVoiceAsync(uri);

            if (result.Success)
            {
                ShowStatus("Voice deleted.", MessageType.Info);
                await RefreshVoiceListAsync();
            }
            else
            {
                ShowStatus($"Delete failed: {result.Message}", MessageType.Error);
            }

            Repaint();
        }

        #endregion

        #region 语音合成标签页

        private void DrawSynthesisTab()
        {
            if (_currentService == null || !_currentService.IsConfigured)
            {
                EditorGUILayout.HelpBox("Configure a service first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Synthesis Test", TTSPlatformStyles.Header);

            EditorGUILayout.BeginVertical(TTSPlatformStyles.Box);

            var capabilities = _currentService.GetCapabilities();

            // 模型选择
            if (capabilities.SynthesisModels.Count > 0)
            {
                var modelNames = capabilities.SynthesisModels.Select(m => m.DisplayName).ToArray();
                _synthesisModelIndex = EditorGUILayout.Popup("Synthesis Model", _synthesisModelIndex, modelNames);
            }

            EditorGUILayout.Space(5);

            // 音色选择
            _useCustomVoice = EditorGUILayout.Toggle("Use Custom Voice", _useCustomVoice);

            if (_useCustomVoice)
            {
                if (_voiceList.Count > 0)
                {
                    var voiceNames = _voiceList.Select(v => v.CustomName).ToArray();
                    _customVoiceIndex = EditorGUILayout.Popup("Custom Voice", _customVoiceIndex, voiceNames);
                }
                else
                {
                    EditorGUILayout.HelpBox("No custom voices. Upload one first.", MessageType.Info);
                }
            }
            else
            {
                if (capabilities.PresetVoices.Count > 0)
                {
                    var presetNames = capabilities.PresetVoices.Select(v => v.DisplayName).ToArray();
                    _synthesisVoiceIndex = EditorGUILayout.Popup("Preset Voice", _synthesisVoiceIndex, presetNames);
                }
            }

            EditorGUILayout.Space(5);

            // 输入文本
            EditorGUILayout.LabelField("Input Text");
            _synthesisInput = EditorGUILayout.TextArea(_synthesisInput, TTSPlatformStyles.TextArea,
                GUILayout.Height(80));

            EditorGUILayout.Space(5);

            // 参数设置
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            _synthesisSpeed = EditorGUILayout.Slider("Speed", _synthesisSpeed,
                capabilities.SpeedRange.Min, capabilities.SpeedRange.Max);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(GUILayout.Width(200));
            _synthesisGain = EditorGUILayout.Slider("Gain", _synthesisGain,
                capabilities.GainRange.Min, capabilities.GainRange.Max);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            // 格式
            if (capabilities.AudioFormats.Count > 0)
            {
                _synthesisFormatIndex = EditorGUILayout.Popup("Format", _synthesisFormatIndex,
                    capabilities.AudioFormats.ToArray(), GUILayout.Width(150));
            }
            else
            {
                EditorGUILayout.LabelField("Format", "N/A", GUILayout.Width(150));
            }

            // 采样率
            if (capabilities.SampleRates.Count > 0)
            {
                var rateNames = capabilities.SampleRates.Select(r => $"{r} Hz").ToArray();
                _synthesisSampleRateIndex = EditorGUILayout.Popup("Sample Rate", _synthesisSampleRateIndex,
                    rateNames, GUILayout.Width(150));
            }
            else
            {
                EditorGUILayout.LabelField("Sample Rate", "N/A", GUILayout.Width(150));
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // 进度条
            if (_isSynthesizing)
            {
                var rect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(rect, _synthesisProgress, $"Synthesizing... {(_synthesisProgress * 100):F0}%");
            }

            EditorGUILayout.Space(5);

            // 操作按钮
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = !_isSynthesizing && !string.IsNullOrWhiteSpace(_synthesisInput);
            if (GUILayout.Button("Start", TTSPlatformStyles.ButtonLarge))
            {
                _ = SynthesizeSpeechAsync();
            }

            GUI.enabled = _lastSynthesizedAudio != null && _lastSynthesizedAudio.Length > 0;
            if (GUILayout.Button("Play", TTSPlatformStyles.ButtonLarge, GUILayout.Width(80)))
            {
                PlayPreviewAudio();
            }

            if (GUILayout.Button("Save", TTSPlatformStyles.ButtonLarge, GUILayout.Width(80)))
            {
                SaveSynthesizedAudio();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            // 结果信息
            if (_lastSynthesizedAudio != null && _lastSynthesizedAudio.Length > 0)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(
                    $"Synthesis complete.\n" +
                    $"File size: {(_lastSynthesizedAudio.Length / 1024f):F1} KB",
                    MessageType.Info);
            }
        }

        private async Task SynthesizeSpeechAsync()
        {
            _isSynthesizing = true;
            _synthesisProgress = 0;
            _lastSynthesizedAudio = null;
            CleanupPreviewClip();

            var capabilities = _currentService.GetCapabilities();

            // 构建请求
            var request = new SynthesisRequest
            {
                Model = capabilities.SynthesisModels.Count > _synthesisModelIndex
                    ? capabilities.SynthesisModels[_synthesisModelIndex].Id : "",
                Input = _synthesisInput,
                Speed = _synthesisSpeed,
                Gain = _synthesisGain,
                ResponseFormat = capabilities.AudioFormats.Count > _synthesisFormatIndex
                    ? capabilities.AudioFormats[_synthesisFormatIndex] : "mp3",
                SampleRate = capabilities.SampleRates.Count > _synthesisSampleRateIndex
                    ? capabilities.SampleRates[_synthesisSampleRateIndex] : 32000
            };

            // 设置音色
            if (_useCustomVoice && _voiceList.Count > _customVoiceIndex)
            {
                request.Voice = _voiceList[_customVoiceIndex].Uri;
            }
            else if (capabilities.PresetVoices.Count > _synthesisVoiceIndex)
            {
                request.Voice = capabilities.PresetVoices[_synthesisVoiceIndex].Id;
            }

            var progress = new Progress<float>(p =>
            {
                _synthesisProgress = p;
                Repaint();
            });

            var result = await _currentService.SynthesizeSpeechAsync(request, progress);

            _isSynthesizing = false;

            if (result.Success)
            {
                _lastSynthesizedAudio = result.AudioData;
                ShowStatus($"Synthesis succeeded. Size: {(result.AudioData.Length / 1024f):F1} KB", MessageType.Info);
            }
            else
            {
                ShowStatus($"Synthesis failed: {result.Message}", MessageType.Error);
            }

            Repaint();
        }

        private void EnsureSynthesisOptionIndices(ServiceCapabilities capabilities)
        {
            if (capabilities.AudioFormats.Count > 0)
            {
                var preferredFormatIndex = capabilities.AudioFormats.FindIndex(
                    format => string.Equals(format, "mp3", StringComparison.OrdinalIgnoreCase));
                if (_synthesisFormatIndex < 0 || _synthesisFormatIndex >= capabilities.AudioFormats.Count)
                {
                    _synthesisFormatIndex = preferredFormatIndex >= 0 ? preferredFormatIndex : 0;
                }
            }
            else
            {
                _synthesisFormatIndex = 0;
            }

            if (capabilities.SampleRates.Count > 0)
            {
                var preferredRateIndex = capabilities.SampleRates.IndexOf(32000);
                if (_synthesisSampleRateIndex < 0 || _synthesisSampleRateIndex >= capabilities.SampleRates.Count)
                {
                    _synthesisSampleRateIndex = preferredRateIndex >= 0 ? preferredRateIndex : 0;
                }
            }
            else
            {
                _synthesisSampleRateIndex = 0;
            }
        }

        private void PlayPreviewAudio()
        {
            if (_lastSynthesizedAudio == null || _lastSynthesizedAudio.Length == 0)
            {
                return;
            }


            CleanupPreviewClip();

            try
            {
                // 保存临时文件用于播放
                var tempPath = Path.Combine(Application.temporaryCachePath, "tts_preview.mp3");
                File.WriteAllBytes(tempPath, _lastSynthesizedAudio);

                // 使用系统默认播放器
                System.Diagnostics.Process.Start(tempPath);
            }
            catch (Exception e)
            {
                ShowStatus($"Playback failed: {e.Message}", MessageType.Error);
            }
        }

        private void SaveSynthesizedAudio()
        {
            if (_lastSynthesizedAudio == null || _lastSynthesizedAudio.Length == 0)
            {
                return;
            }


            var capabilities = _currentService.GetCapabilities();
            var format = capabilities.AudioFormats.Count > _synthesisFormatIndex
                ? capabilities.AudioFormats[_synthesisFormatIndex] : "mp3";

            var path = EditorUtility.SaveFilePanel("Save audio", "", $"synthesized.{format}", format);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    File.WriteAllBytes(path, _lastSynthesizedAudio);
                    ShowStatus($"Saved to: {path}", MessageType.Info);
                }
                catch (Exception e)
                {
                    ShowStatus($"Save failed: {e.Message}", MessageType.Error);
                }
            }
        }

        private void CleanupPreviewClip()
        {
            if (_previewClip != null)
            {
                DestroyImmediate(_previewClip);
                _previewClip = null;
            }
        }

        #endregion

        #region 设置标签页

        private void DrawSettingsTab()
        {
            EditorGUILayout.LabelField("Service Settings", TTSPlatformStyles.Header);

            if (_currentService == null)
            {
                EditorGUILayout.HelpBox("Select a service.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(TTSPlatformStyles.Box);

            EditorGUILayout.LabelField($"Current service: {_currentService.DisplayName}", EditorStyles.boldLabel);

            EditorGUILayout.Space(10);

            // API Key
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Key", GUILayout.Width(60));

            if (_showApiKey)
            {
                _tempApiKey = EditorGUILayout.TextField(_tempApiKey);
            }
            else
            {
                _tempApiKey = EditorGUILayout.PasswordField(_tempApiKey);
            }

            if (GUILayout.Button(_showApiKey ? "Hide" : "Show", GUILayout.Width(50)))
            {
                _showApiKey = !_showApiKey;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Save Settings", GUILayout.Height(25)))
            {
                _currentService.ApiKey = _tempApiKey;
                TTSServiceRegistry.SaveServiceConfig(_currentService);
                ShowStatus("Settings saved.", MessageType.Info);
            }

            if (GUILayout.Button("Validate API Key", GUILayout.Height(25)))
            {
                _ = ValidateApiKeyAsync();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(15);

            // 服务能力信息
            DrawServiceCapabilities();
        }

        private void DrawServiceCapabilities()
        {
            EditorGUILayout.LabelField("Service Capabilities", TTSPlatformStyles.SubHeader);

            EditorGUILayout.BeginVertical(TTSPlatformStyles.Box);

            var cap = _currentService.GetCapabilities();

            EditorGUILayout.LabelField($"Upload models: {cap.VoiceUploadModels.Count}");
            EditorGUILayout.LabelField($"Synthesis models: {cap.SynthesisModels.Count}");
            EditorGUILayout.LabelField($"Preset voices: {cap.PresetVoices.Count}");
            EditorGUILayout.LabelField($"Streaming support: {(cap.SupportsStreaming ? "Yes" : "No")}");
            EditorGUILayout.LabelField($"Multi-speaker support: {(cap.SupportsMultiSpeaker ? "Yes" : "No")}");
            EditorGUILayout.LabelField($"Voice cloning support: {(cap.SupportsVoiceCloning ? "Yes" : "No")}");
            EditorGUILayout.LabelField($"Speed range: {cap.SpeedRange.Min} ~ {cap.SpeedRange.Max}");
            EditorGUILayout.LabelField($"Gain range: {cap.GainRange.Min} ~ {cap.GainRange.Max}");
            EditorGUILayout.LabelField($"Audio formats: {string.Join(", ", cap.AudioFormats)}");

            EditorGUILayout.EndVertical();
        }

        private async Task ValidateApiKeyAsync()
        {
            _currentService.ApiKey = _tempApiKey;

            ShowStatus("Validating...", MessageType.None);
            Repaint();

            var result = await _currentService.ValidateApiKeyAsync();

            if (result.Success)
            {
                ShowStatus("API key validated.", MessageType.Info);
                TTSServiceRegistry.SaveServiceConfig(_currentService);
            }
            else
            {
                ShowStatus($"Validation failed: {result.Message}", MessageType.Error);
            }

            Repaint();
        }

        #endregion

        #region 状态栏

        private void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(_statusMessage))
            {
                return;
            }


            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        private void ShowStatus(string message, MessageType type, float duration = 5f)
        {
            _statusMessage = message;
            _statusType = type;
            _statusClearTime = EditorApplication.timeSinceStartup + duration;

            // 同时输出到Console
            switch (type)
            {
                case MessageType.Error:
                    Debug.LogError($"[TTSPlatform] {message}");
                    break;
                case MessageType.Warning:
                    Debug.LogWarning($"[TTSPlatform] {message}");
                    break;
                default:
                    Debug.Log($"[TTSPlatform] {message}");
                    break;
            }

            Repaint();
        }

        #endregion
    }
}
