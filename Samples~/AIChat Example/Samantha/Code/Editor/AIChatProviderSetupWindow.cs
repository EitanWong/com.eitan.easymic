using System;
using System.Collections.Generic;
using System.IO;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Configures the sample's provider policy and the local runtime configuration file.
    /// </summary>
    internal sealed class AIChatProviderSetupWindow : EditorWindow
    {
        private const string SetupMenuPath = "Tools/EasyMic/AI Chat/Provider Setup";
        private static readonly JsonAIChatRuntimeConfigStore RuntimeConfigStore = new JsonAIChatRuntimeConfigStore();

        [SerializeField] private AIChatController _controller;
        [SerializeField] private AIChatProviderPresetKind _providerKind = AIChatProviderPresetKind.SiliconFlow;
        [SerializeField] private string _apiBaseUrl = string.Empty;
        [SerializeField] private string _llmModel = string.Empty;
        [SerializeField] private bool _useLocalTts;
        [SerializeField] private string _ttsModel = string.Empty;
        [SerializeField] private string _ttsVoice = string.Empty;
        [SerializeField] private bool _useStreamingTts = true;
        [SerializeField] private bool _debugMode;
        [SerializeField] private bool _normalizeLocalTts = true;
        [SerializeField] private float _localTtsVolume = 1f;
        [SerializeField] private Vector2 _scrollPosition;

        private string _apiKey = string.Empty;
        private string _statusMessage = string.Empty;
        private bool _hasPersistedApiKey;
        private bool _hasUnsavedChanges;
        private AIChatSetupLanguage _language;

        [MenuItem(SetupMenuPath, false, 2010)]
        public static void Open()
        {
            ShowFor(null);
        }

        [MenuItem("CONTEXT/AIChatController/Provider Setup")]
        private static void OpenFromContext(MenuCommand command)
        {
            ShowFor(command.context as AIChatController);
        }

        private static void ShowFor(AIChatController controller)
        {
            var window = GetWindow<AIChatProviderSetupWindow>();
            window.minSize = new Vector2(540f, 640f);
            window.UpdateWindowTitle();
            window.SelectController(controller);
        }

        private void OnEnable()
        {
            _language = AIChatSetupLocalization.CurrentLanguage;
            UpdateWindowTitle();
            SelectController(_controller);
        }

        private void OnGUI()
        {
            _language = AIChatSetupLocalization.CurrentLanguage;
            UpdateWindowTitle();
            DrawHeader();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                DrawControllerPicker();

                if (_controller == null)
                {
                    EditorGUILayout.HelpBox(
                        Text(AIChatSetupTextKey.ControllerMissingHelp),
                        MessageType.Warning);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                DrawProviderFields();
                DrawRuntimeKeyField();
                if (EditorGUI.EndChangeCheck())
                {
                    MarkUnsaved();
                }
                DrawValidation();

                if (!string.IsNullOrWhiteSpace(_statusMessage))
                {
                    EditorGUILayout.HelpBox(
                        _statusMessage,
                        _hasUnsavedChanges ? MessageType.Warning : MessageType.Info);
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            DrawActions();
        }

        private void DrawHeader()
        {
            AIChatSetupGui.DrawHero(
                "d_Settings",
                Text(AIChatSetupTextKey.ProviderHeader),
                Text(AIChatSetupTextKey.ProviderDescription),
                Text(AIChatSetupTextKey.Latest));
        }

        private void DrawControllerPicker()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(1, Text(AIChatSetupTextKey.TargetSection));
                EditorGUI.BeginChangeCheck();
                var selected = (AIChatController)EditorGUILayout.ObjectField(
                    Text(AIChatSetupTextKey.TargetController),
                    _controller,
                    typeof(AIChatController),
                    true);

                if (EditorGUI.EndChangeCheck())
                {
                    SelectController(selected);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.UseCurrentSelection)))
                    {
                        SelectController(GetSelectedController());
                    }

                    if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.FindSceneController)))
                    {
                        SelectController(UnityEngine.Object.FindObjectOfType<AIChatController>());
                    }

                    if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.RevealRuntimeConfig)))
                    {
                        RevealRuntimeConfig();
                    }
                }
            }
        }

        private void DrawProviderFields()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(2, Text(AIChatSetupTextKey.ProviderSection));
                EditorGUILayout.LabelField(Text(AIChatSetupTextKey.Provider), EditorStyles.miniBoldLabel);
                int selectedProvider = GUILayout.Toolbar(
                    (int)_providerKind,
                    new[]
                    {
                        Text(AIChatSetupTextKey.SiliconFlow),
                        Text(AIChatSetupTextKey.OpenAICompatibleCustom)
                    },
                    GUILayout.Height(26f));
                if (selectedProvider != (int)_providerKind)
                {
                    _providerKind = (AIChatProviderPresetKind)selectedProvider;
                    ApplyPresetToFields(AIChatProviderPresets.Get(_providerKind));
                    MarkUnsaved();
                }

                if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.ApplyPresetDefaults)))
                {
                    ApplyPresetToFields(AIChatProviderPresets.Get(_providerKind));
                    MarkUnsaved();
                }

                _apiBaseUrl = EditorGUILayout.TextField(Text(AIChatSetupTextKey.ApiBaseUrl), _apiBaseUrl);
                _llmModel = EditorGUILayout.TextField(Text(AIChatSetupTextKey.LlmModel), _llmModel);
                _debugMode = EditorGUILayout.Toggle(Text(AIChatSetupTextKey.DebugMode), _debugMode);
                if (_debugMode)
                {
                    EditorGUILayout.HelpBox(Text(AIChatSetupTextKey.DebugModeHelp), MessageType.Info);
                }
            }

            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(3, Text(AIChatSetupTextKey.VoiceSection));
                int ttsMode = GUILayout.Toolbar(
                    _useLocalTts ? 1 : 0,
                    new[]
                    {
                        Text(AIChatSetupTextKey.RemoteTts),
                        Text(AIChatSetupTextKey.LocalTts)
                    },
                    GUILayout.Height(26f));
                _useLocalTts = ttsMode == 1;

                if (_useLocalTts)
                {
                    EditorGUILayout.HelpBox(
                        Text(AIChatSetupTextKey.LocalTtsHelp),
                        MessageType.Info);
                    _normalizeLocalTts = EditorGUILayout.Toggle(Text(AIChatSetupTextKey.NormalizeLocalTts), _normalizeLocalTts);
                    _localTtsVolume = EditorGUILayout.Slider(Text(AIChatSetupTextKey.LocalTtsVolume), _localTtsVolume, 0f, 2f);
                    EditorGUILayout.HelpBox(Text(AIChatSetupTextKey.LocalTtsAudioHelp), MessageType.None);
                    if (GUILayout.Button("SpeechSynthesizer", EditorStyles.miniButton))
                    {
                        Selection.activeObject = _controller.CurrentConfig.SpeechSynthesizer ??
                            FindControllerComponent<SpeechSynthesizer>();
                    }
                }
                else
                {
                    _ttsModel = EditorGUILayout.TextField(Text(AIChatSetupTextKey.RemoteTtsModel), _ttsModel);
                    _ttsVoice = EditorGUILayout.TextField(Text(AIChatSetupTextKey.RemoteTtsVoice), _ttsVoice);
                    _useStreamingTts = EditorGUILayout.Toggle(Text(AIChatSetupTextKey.StreamRemoteTts), _useStreamingTts);
                }
            }
        }

        private void DrawRuntimeKeyField()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(4, Text(AIChatSetupTextKey.CredentialsSection));
                _apiKey = EditorGUILayout.PasswordField(Text(AIChatSetupTextKey.ApiKey), _apiKey);
                EditorGUILayout.LabelField(
                    Text(_hasPersistedApiKey
                        ? AIChatSetupTextKey.DeviceKeyStored
                        : AIChatSetupTextKey.DeviceKeyMissing),
                    AIChatSetupGui.BodyStyle);
                EditorGUILayout.LabelField(
                    Text(AIChatSetupTextKey.ApiKeyStorageHelp, _controller.RuntimeConfigPath),
                    AIChatSetupGui.BodyStyle);

                using (new EditorGUI.DisabledScope(!_hasPersistedApiKey))
                {
                    if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.ClearSavedApiKey)))
                    {
                        ClearSavedApiKey();
                    }
                }
            }
        }

        private void DrawValidation()
        {
            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(5, Text(AIChatSetupTextKey.ValidationSection));
                if (TryValidateProvider(out string message))
                {
                    bool hasApiKey = HasApiKeyForSave();
                    EditorGUILayout.HelpBox(
                        Text(hasApiKey
                            ? AIChatSetupTextKey.ProviderReady
                            : AIChatSetupTextKey.ProviderReadyKeyMissing),
                        hasApiKey ? MessageType.Info : MessageType.Warning);
                    return;
                }

                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
        }

        private void DrawActions()
        {
            bool editingDisabled = EditorApplication.isPlayingOrWillChangePlaymode;
            if (_hasUnsavedChanges)
            {
                EditorGUILayout.HelpBox(
                    Text(AIChatSetupTextKey.UnsavedChanges),
                    MessageType.Warning);
            }

            if (editingDisabled)
            {
                EditorGUILayout.HelpBox(Text(AIChatSetupTextKey.PlayModeEditHelp), MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(editingDisabled))
            {
                if (AIChatSetupGui.PrimaryButton(Text(AIChatSetupTextKey.ApplyAndSave)))
                {
                    ApplyAndSave();
                }

                if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.ApplyToScene)))
                {
                    ApplyToScene();
                }
            }
        }

        private void SelectController(AIChatController controller)
        {
            AIChatController validController = controller != null ? controller : null;
            AIChatController nextController = validController ??
                                              GetSelectedController() ??
                                              UnityEngine.Object.FindObjectOfType<AIChatController>();
            if (_hasUnsavedChanges &&
                nextController != _controller &&
                !EditorUtility.DisplayDialog(
                    Text(AIChatSetupTextKey.DiscardChangesTitle),
                    Text(AIChatSetupTextKey.DiscardChangesMessage),
                    Text(AIChatSetupTextKey.DiscardChanges),
                    Text(AIChatSetupTextKey.KeepEditing)))
            {
                return;
            }

            _controller = nextController;
            _statusMessage = string.Empty;
            _hasUnsavedChanges = false;

            if (_controller != null)
            {
                LoadFieldsFromController();
            }

            Repaint();
        }

        private static AIChatController GetSelectedController()
        {
            GameObject selected = Selection.activeGameObject;
            return selected == null ? null : selected.GetComponentInParent<AIChatController>();
        }

        private void LoadFieldsFromController()
        {
            AIChatControllerConfig config = _controller.CurrentConfig;
            var policy = _controller.GetComponent<AIChatConfigurationPolicy>();
            _debugMode = config.DebugMode;
            var synthesizer = config.SpeechSynthesizer ?? FindControllerComponent<SpeechSynthesizer>();
            _normalizeLocalTts = synthesizer == null || synthesizer.NormalizeOutput;
            _localTtsVolume = synthesizer != null ? synthesizer.PlaybackVolume : 1f;

            if (policy != null && policy.EnabledOverride)
            {
                AIChatResolvedConfiguration resolved = policy.PreviewResolvedConfiguration(config);
                _apiBaseUrl = resolved.ApiBaseUrl ?? string.Empty;
                _llmModel = resolved.LlmModel ?? string.Empty;
                _useLocalTts = resolved.UseLocalTts;
                _ttsModel = resolved.TtsModel ?? string.Empty;
                _ttsVoice = resolved.TtsVoice ?? string.Empty;
                _useStreamingTts = resolved.UseStreamingTts;
            }
            else
            {
                _apiBaseUrl = config.ApiBaseUrl ?? string.Empty;
                _llmModel = config.LlmModel ?? string.Empty;
                _useLocalTts = config.UseLocalTts;
                _ttsModel = config.TtsModel ?? string.Empty;
                _ttsVoice = config.TtsVoice ?? string.Empty;
                _useStreamingTts = config.UseStreamingTts;
            }

            if (TryLoadRuntimeConfiguration(out AIChatRuntimeConfig runtimeConfig))
            {
                _apiKey = runtimeConfig.ApiKey ?? string.Empty;
                _hasPersistedApiKey = !string.IsNullOrWhiteSpace(runtimeConfig.ApiKey);
                ApplyRuntimeValues(runtimeConfig);
            }
            else
            {
                _apiKey = string.Empty;
                _hasPersistedApiKey = false;
            }

            _providerKind = AIChatProviderPresets.ResolveKind(_apiBaseUrl);
        }

        private void ApplyRuntimeValues(AIChatRuntimeConfig runtimeConfig)
        {
            if (!string.IsNullOrWhiteSpace(runtimeConfig.ApiBaseUrl))
            {
                _apiBaseUrl = runtimeConfig.ApiBaseUrl;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.LlmModel))
            {
                _llmModel = runtimeConfig.LlmModel;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.TtsModel))
            {
                _ttsModel = runtimeConfig.TtsModel;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.TtsVoice))
            {
                _ttsVoice = runtimeConfig.TtsVoice;
            }

            if (runtimeConfig.UseLocalTts >= 0)
            {
                _useLocalTts = runtimeConfig.UseLocalTts > 0;
            }
            if (runtimeConfig.DebugMode >= 0) _debugMode = runtimeConfig.DebugMode > 0;
            if (runtimeConfig.LocalTtsNormalizeOutput >= 0) _normalizeLocalTts = runtimeConfig.LocalTtsNormalizeOutput > 0;
            if (runtimeConfig.LocalTtsPlaybackVolume >= 0f) _localTtsVolume = Mathf.Clamp(runtimeConfig.LocalTtsPlaybackVolume, 0f, 2f);
        }

        private void ApplyPresetToFields(AIChatProviderPreset preset)
        {
            _apiBaseUrl = preset.ApiBaseUrl;
            _llmModel = preset.LlmModel;
            _ttsModel = preset.TtsModel;
            _ttsVoice = preset.TtsVoice;
            _useLocalTts = false;
            _useStreamingTts = preset.SupportsRemoteTts;
        }

        private bool ApplyToScene()
        {
            if (!TryValidateProvider(out string validationMessage))
            {
                _statusMessage = validationMessage;
                return false;
            }

            string normalizedApiBaseUrl = AIChatProviderPresets.NormalizeApiBaseUrl(_apiBaseUrl);
            Undo.RecordObject(_controller, Text(AIChatSetupTextKey.ConfigureProviderUndo));

            var controllerSerialized = new SerializedObject(_controller);
            SerializedProperty config = controllerSerialized.FindProperty("_config");
            if (config == null)
            {
                _statusMessage = Text(AIChatSetupTextKey.MissingControllerConfig);
                return false;
            }

            SetObjectReferenceWhenEmpty(
                config.FindPropertyRelative(nameof(AIChatControllerConfig.Microphone)),
                FindControllerComponent<VoiceMicrophone>());
            SetObjectReferenceWhenEmpty(
                config.FindPropertyRelative(nameof(AIChatControllerConfig.SpeechSynthesizer)),
                FindControllerComponent<SpeechSynthesizer>());
            SetString(config.FindPropertyRelative(nameof(AIChatControllerConfig.ApiBaseUrl)), normalizedApiBaseUrl);
            SetString(config.FindPropertyRelative(nameof(AIChatControllerConfig.LlmModel)), _llmModel);
            SetBool(config.FindPropertyRelative(nameof(AIChatControllerConfig.UseLocalTts)), _useLocalTts);
            SetBool(config.FindPropertyRelative(nameof(AIChatControllerConfig.DebugMode)), _debugMode);
            SetString(config.FindPropertyRelative(nameof(AIChatControllerConfig.TtsModel)), _ttsModel);
            SetString(config.FindPropertyRelative(nameof(AIChatControllerConfig.TtsVoice)), _ttsVoice);
            SetBool(config.FindPropertyRelative(nameof(AIChatControllerConfig.UseStreamingTts)), !_useLocalTts && _useStreamingTts);
            controllerSerialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(_controller);

            SpeechSynthesizer synthesizer = _controller.CurrentConfig.SpeechSynthesizer;
            if (synthesizer != null)
            {
                Undo.RecordObject(synthesizer, Text(AIChatSetupTextKey.ConfigureProviderUndo));
                synthesizer.NormalizeOutput = _normalizeLocalTts;
                synthesizer.PlaybackVolume = _localTtsVolume;
                synthesizer.EnableLog = _debugMode;
                EditorUtility.SetDirty(synthesizer);
            }
            if (_controller.CurrentConfig.Microphone != null)
            {
                Undo.RecordObject(_controller.CurrentConfig.Microphone, Text(AIChatSetupTextKey.ConfigureProviderUndo));
                _controller.CurrentConfig.Microphone.EnableLog = _debugMode;
                EditorUtility.SetDirty(_controller.CurrentConfig.Microphone);
            }

            AIChatConfigurationPolicy policy = _controller.GetComponent<AIChatConfigurationPolicy>();
            if (policy == null)
            {
                policy = Undo.AddComponent<AIChatConfigurationPolicy>(_controller.gameObject);
            }

            ApplyProviderPolicy(policy, normalizedApiBaseUrl);
            EditorSceneManager.MarkSceneDirty(_controller.gameObject.scene);
            _statusMessage = Text(AIChatSetupTextKey.ProviderApplied);
            return true;
        }

        private void ApplyProviderPolicy(AIChatConfigurationPolicy policy, string normalizedApiBaseUrl)
        {
            Undo.RecordObject(policy, Text(AIChatSetupTextKey.ConfigureProviderPolicyUndo));
            var serialized = new SerializedObject(policy);
            serialized.FindProperty("_enabledOverride").boolValue = true;

            AIChatProviderPreset siliconFlow = AIChatProviderPresets.SiliconFlow;
            bool useSiliconFlowPreset = _providerKind == AIChatProviderPresetKind.SiliconFlow;
            serialized.FindProperty("_preset").enumValueIndex = useSiliconFlowPreset
                ? (int)AIChatConfigurationPolicy.PolicyPreset.SiliconFlow
                : (int)AIChatConfigurationPolicy.PolicyPreset.Custom;

            SetStringOverride(serialized, "_apiBaseUrl", normalizedApiBaseUrl,
                !useSiliconFlowPreset || !SameValue(normalizedApiBaseUrl, siliconFlow.ApiBaseUrl));
            SetStringOverride(serialized, "_llmModel", _llmModel,
                !useSiliconFlowPreset || !SameValue(_llmModel, siliconFlow.LlmModel));
            SetBoolOverride(serialized, "_useLocalTts", _useLocalTts,
                !useSiliconFlowPreset || _useLocalTts);
            SetStringOverride(serialized, "_ttsModel", _ttsModel,
                !_useLocalTts && (!useSiliconFlowPreset || !SameValue(_ttsModel, siliconFlow.TtsModel)));
            SetStringOverride(serialized, "_ttsVoice", _ttsVoice,
                !_useLocalTts && (!useSiliconFlowPreset || !SameValue(_ttsVoice, siliconFlow.TtsVoice)));
            SetBoolOverride(serialized, "_useStreamingTts", !_useLocalTts && _useStreamingTts,
                !useSiliconFlowPreset || _useLocalTts || !_useStreamingTts);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(policy);
        }

        private bool SaveRuntimeConfiguration()
        {
            if (!TryValidateProvider(out string validationMessage))
            {
                _statusMessage = validationMessage;
                return false;
            }

            if (!TryLoadRuntimeConfiguration(out AIChatRuntimeConfig runtimeConfig))
            {
                runtimeConfig = CreateRuntimeConfiguration();
            }

            runtimeConfig.ApiBaseUrl = AIChatProviderPresets.NormalizeApiBaseUrl(_apiBaseUrl);
            runtimeConfig.LlmModel = (_llmModel ?? string.Empty).Trim();
            runtimeConfig.TtsModel = (_ttsModel ?? string.Empty).Trim();
            runtimeConfig.TtsVoice = (_ttsVoice ?? string.Empty).Trim();
            runtimeConfig.UseLocalTts = _useLocalTts ? 1 : 0;
            runtimeConfig.DebugMode = _debugMode ? 1 : 0;
            runtimeConfig.LocalTtsNormalizeOutput = _normalizeLocalTts ? 1 : 0;
            runtimeConfig.LocalTtsPlaybackVolume = _localTtsVolume;

            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                runtimeConfig.ApiKey = _apiKey.Trim();
            }

            if (string.IsNullOrWhiteSpace(runtimeConfig.ApiKey))
            {
                _statusMessage = Text(AIChatSetupTextKey.ValidationMissingApiKey);
                return false;
            }

            if (!TrySaveRuntimeConfiguration(runtimeConfig, out string errorMessage))
            {
                _statusMessage = Text(AIChatSetupTextKey.RuntimeSaveFailed, errorMessage);
                return false;
            }

            _hasPersistedApiKey = true;
            _hasUnsavedChanges = false;
            _statusMessage = Text(AIChatSetupTextKey.RuntimeSaved, _controller.RuntimeConfigPath);
            return true;
        }

        private void ApplyAndSave()
        {
            if (!TryValidateProvider(out string validationMessage))
            {
                _statusMessage = validationMessage;
                return;
            }

            if (!HasApiKeyForSave())
            {
                _statusMessage = Text(AIChatSetupTextKey.ValidationMissingApiKey);
                return;
            }

            if (ApplyToScene() && SaveRuntimeConfiguration())
            {
                _statusMessage = string.Join(
                    "\n",
                    Text(AIChatSetupTextKey.RuntimeSaved, _controller.RuntimeConfigPath),
                    Text(AIChatSetupTextKey.ProviderApplied));
            }
        }

        private bool HasApiKeyForSave()
        {
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                return true;
            }

            return _hasPersistedApiKey;
        }

        private void MarkUnsaved()
        {
            _hasUnsavedChanges = true;
            _statusMessage = Text(AIChatSetupTextKey.UnsavedChanges);
        }

        private bool TryLoadRuntimeConfiguration(out AIChatRuntimeConfig runtimeConfig)
        {
            runtimeConfig = null;
            if (_controller == null)
            {
                return false;
            }

            return RuntimeConfigStore.TryLoad(_controller.RuntimeConfigPath, out runtimeConfig);
        }

        private void ClearSavedApiKey()
        {
            if (_controller == null ||
                !EditorUtility.DisplayDialog(
                    Text(AIChatSetupTextKey.ClearApiKeyTitle),
                    Text(AIChatSetupTextKey.ClearApiKeyMessage),
                    Text(AIChatSetupTextKey.Clear),
                    Text(AIChatSetupTextKey.Cancel)))
            {
                return;
            }

            if (!TryLoadRuntimeConfiguration(out AIChatRuntimeConfig runtimeConfig))
            {
                runtimeConfig = CreateRuntimeConfiguration();
            }

            runtimeConfig.ApiKey = string.Empty;
            if (TrySaveRuntimeConfiguration(runtimeConfig, out string errorMessage))
            {
                _apiKey = string.Empty;
                _hasPersistedApiKey = false;
                _statusMessage = Text(AIChatSetupTextKey.ApiKeyCleared);
                return;
            }

            _statusMessage = Text(AIChatSetupTextKey.ApiKeyClearFailed, errorMessage);
        }

        private void RevealRuntimeConfig()
        {
            if (_controller == null)
            {
                return;
            }

            string path = _controller.RuntimeConfigPath;
            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            _statusMessage = Text(AIChatSetupTextKey.RuntimeConfigNotCreated);
        }

        private bool TryValidateProvider(out string message)
        {
            var issues = new List<string>();
            if (_controller == null)
            {
                issues.Add(Text(AIChatSetupTextKey.ValidationNoController));
            }

            if (!OpenAICompatibleClient.TryNormalizeBaseUrl(_apiBaseUrl, out _, out _))
            {
                issues.Add(Text(AIChatSetupTextKey.ValidationInvalidBaseUrl));
            }

            if (string.IsNullOrWhiteSpace(_llmModel))
            {
                issues.Add(Text(AIChatSetupTextKey.ValidationMissingLlmModel));
            }

            if (_useLocalTts)
            {
                if (FindControllerComponent<SpeechSynthesizer>() == null)
                {
                    issues.Add(Text(AIChatSetupTextKey.ValidationMissingLocalSynthesizer));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_ttsModel))
                {
                    issues.Add(Text(AIChatSetupTextKey.ValidationMissingRemoteTtsModel));
                }

                if (string.IsNullOrWhiteSpace(_ttsVoice))
                {
                    issues.Add(Text(AIChatSetupTextKey.ValidationMissingRemoteTtsVoice));
                }
            }

            if (_controller != null && FindControllerComponent<VoiceMicrophone>() == null)
            {
                issues.Add(Text(AIChatSetupTextKey.ValidationMissingMicrophone));
            }

            message = issues.Count == 0 ? string.Empty : string.Join("\n", issues);
            return issues.Count == 0;
        }

        private T FindControllerComponent<T>() where T : Component
        {
            if (_controller == null)
            {
                return null;
            }

            return _controller.GetComponent<T>() ??
                   _controller.GetComponentInChildren<T>(true) ??
                   _controller.GetComponentInParent<T>();
        }

        private static bool SameValue(string left, string right)
        {
            return string.Equals(
                (left ?? string.Empty).Trim(),
                (right ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        private static void SetString(SerializedProperty property, string value)
        {
            if (property != null)
            {
                property.stringValue = value ?? string.Empty;
            }
        }

        private static void SetBool(SerializedProperty property, bool value)
        {
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetObjectReferenceWhenEmpty(SerializedProperty property, UnityEngine.Object value)
        {
            if (property != null && property.objectReferenceValue == null && value != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetStringOverride(SerializedObject serialized, string propertyName, string value, bool enabled)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            property.FindPropertyRelative("Enabled").boolValue = enabled;
            property.FindPropertyRelative("Value").stringValue = value ?? string.Empty;
        }

        private static void SetBoolOverride(SerializedObject serialized, string propertyName, bool value, bool enabled)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            property.FindPropertyRelative("Enabled").boolValue = enabled;
            property.FindPropertyRelative("Value").boolValue = value;
        }

        private AIChatRuntimeConfig CreateRuntimeConfiguration()
        {
            return RuntimeConfigStore.Capture(_controller.CurrentConfig);
        }

        private bool TrySaveRuntimeConfiguration(AIChatRuntimeConfig runtimeConfig, out string errorMessage)
        {
            if (_controller == null)
            {
                errorMessage = "AIChatController is missing.";
                return false;
            }

            return RuntimeConfigStore.TrySave(_controller.RuntimeConfigPath, runtimeConfig, out errorMessage);
        }

        private void UpdateWindowTitle()
        {
            titleContent = new GUIContent(Text(AIChatSetupTextKey.ProviderWindowTitle));
        }

        private string Text(AIChatSetupTextKey key, params object[] args)
        {
            return AIChatSetupLocalization.Text(key, _language, args);
        }
    }
}
