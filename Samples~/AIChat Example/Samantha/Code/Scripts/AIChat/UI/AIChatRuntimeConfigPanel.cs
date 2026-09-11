#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [AddComponentMenu("Examples/EasyMic/AI Chat/UI/Runtime Config Panel")]
    public sealed class AIChatRuntimeConfigPanel : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private AIChatController _controller;
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private Button _openButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private string _runtimeConfigFileName = "ai_chat_config.json";

        [Header("Inputs (Chat)")]
        [SerializeField] private TMP_InputField _apiKeyInput;
        [SerializeField] private TMP_InputField _apiBaseUrlInput;
        [SerializeField] private TMP_InputField _llmModelInput;
        [SerializeField] private Slider _llmTemperatureSlider;
        [SerializeField] private TMP_InputField _ttsModelInput;
        [SerializeField] private TMP_InputField _ttsVoiceInput;
        [SerializeField] private Toggle _useLocalTtsToggle;

        [Header("Inputs (Microphone)")]
        [SerializeField] private TMP_Dropdown _microphoneDeviceDropdown;

        [Header("Inputs (ASR)")]
        [SerializeField] private TMP_Dropdown _asrRecognitionModeDropdown;
        [SerializeField] private TMP_InputField _asrStreamingModelInput;
        [SerializeField] private TMP_InputField _asrOfflineModelInput;
        [SerializeField] private TMP_InputField _asrVadModelInput;
        [SerializeField] private TMP_InputField _asrTurnDetectionDelayInput;
        [SerializeField] private Toggle _asrEnablePunctuationToggle;
        [SerializeField] private TMP_InputField _asrPunctuationModelInput;

        [Header("Inputs (Local TTS)")]
        [SerializeField] private TMP_InputField _localTtsModelInput;
        [SerializeField] private TMP_InputField _localTtsVoiceIdInput;
        [SerializeField] private TMP_InputField _localTtsSpeedInput;
        [SerializeField] private TMP_InputField _localTtsSampleRateInput;

        private static readonly List<string> AsrRecognitionModeLabels = new List<string>
        {
            "Streaming",
            "Offline (VAD)",
            "Hybrid"
        };
        private const string SystemDefaultMicrophoneOption = "System Default";
        private const string UnavailableMicrophoneSuffix = " (Unavailable)";
        private const float OpenButtonFadeDuration = 0.2f;
        private const float OpenButtonVisibleAlpha = 1f;
        private const float OpenButtonHiddenAlpha = 0f;
        private readonly IAIChatRuntimeConfigStore _runtimeConfigStore = new JsonAIChatRuntimeConfigStore();
        private readonly List<string> _microphoneDeviceNames = new List<string>();
        private CanvasGroup _openButtonCanvasGroup;
        private CanvasGroup _panelRootCanvasGroup;
        private bool _panelRootUsesCanvasGroup;
        private int _saveGeneration;

        private void Awake()
        {
            ConfigurePanelRootVisibility();

            ConfigureApiKeyInput();
            EnsureRecognitionModeOptions();
            EnsureMicrophoneDeviceDropdown();

            if (_openButton != null)
            {
                _openButton.onClick.AddListener(OpenPanel);
            }

            InitializeOpenButtonVisibility();

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(ClosePanel);
            }

            if (_saveButton != null)
            {
                _saveButton.onClick.AddListener(SaveSettings);
            }

            if (_resetButton != null)
            {
                _resetButton.onClick.AddListener(ResetToDefaults);
            }

            if (_apiKeyInput != null)
            {
                _apiKeyInput.onSelect.AddListener(OnApiKeySelect);
                _apiKeyInput.onDeselect.AddListener(OnApiKeyDeselect);
            }
        }

        private void OnEnable()
        {
            ConfigureApiKeyInput();
            EnsureRecognitionModeOptions();
            EnsureMicrophoneDeviceDropdown();
            InitializeOpenButtonVisibility();
        }

        private void Update()
        {
            UpdateOpenButtonVisibility();
        }

        private void OnDestroy()
        {
            if (_openButton != null)
            {
                _openButton.onClick.RemoveListener(OpenPanel);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(ClosePanel);
            }

            if (_saveButton != null)
            {
                _saveButton.onClick.RemoveListener(SaveSettings);
            }

            if (_resetButton != null)
            {
                _resetButton.onClick.RemoveListener(ResetToDefaults);
            }

            if (_apiKeyInput != null)
            {
                _apiKeyInput.onSelect.RemoveListener(OnApiKeySelect);
                _apiKeyInput.onDeselect.RemoveListener(OnApiKeyDeselect);
            }
        }

        private void OpenPanel()
        {
            SetPanelRootVisible(true);

            EnsureRecognitionModeOptions();
            LoadIntoFields();
        }

        private void ClosePanel()
        {
            SetPanelRootVisible(false);
        }

        private string ResolveConfigPath()
        {
            if (_controller != null)
            {
                return _controller.RuntimeConfigPath;
            }

            var fileName = string.IsNullOrWhiteSpace(_runtimeConfigFileName)
                ? "ai_chat_config.json"
                : _runtimeConfigFileName;
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        private void LoadIntoFields()
        {
            EnsureRecognitionModeOptions();
            RefreshMicrophoneDeviceOptions();
            var path = ResolveConfigPath();
            if (!_runtimeConfigStore.TryLoad(path, out var config) || config == null)
            {
                return;
            }

            SetText(_apiKeyInput, config.ApiKey);
            SetText(_apiBaseUrlInput, config.ApiBaseUrl);
            SetText(_llmModelInput, config.LlmModel);
            SetText(_ttsModelInput, config.TtsModel);
            SetText(_ttsVoiceInput, config.TtsVoice);
            SetToggle(_useLocalTtsToggle, config.UseLocalTts);
            SetMicrophoneDeviceDropdown(config.MicrophoneDeviceName);
            SetDropdown(_asrRecognitionModeDropdown, config.AsrRecognitionModeIndex);
            SetText(_asrStreamingModelInput, config.AsrStreamingModelId);
            SetText(_asrOfflineModelInput, config.AsrOfflineModelId);
            SetText(_asrVadModelInput, config.AsrVadModelId);
            if (_asrTurnDetectionDelayInput != null)
            {
                float turnDelay = NormalizeTurnDetectionDelay(config.AsrTurnDetectionDelaySeconds);
                _asrTurnDetectionDelayInput.text = turnDelay.ToString(CultureInfo.InvariantCulture);
            }
            SetToggle(_asrEnablePunctuationToggle, config.AsrEnablePunctuation);
            SetText(_asrPunctuationModelInput, config.AsrPunctuationModelId);
            SetText(_localTtsModelInput, config.LocalTtsModelId);
            SetSlider(_llmTemperatureSlider, config.LlmTemperature);

            if (_localTtsVoiceIdInput != null && config.LocalTtsVoiceId >= 0)
            {
                _localTtsVoiceIdInput.text = config.LocalTtsVoiceId.ToString(CultureInfo.InvariantCulture);
            }

            if (_localTtsSpeedInput != null && config.LocalTtsSpeed >= 0f)
            {
                _localTtsSpeedInput.text = config.LocalTtsSpeed.ToString(CultureInfo.InvariantCulture);
            }

            if (_localTtsSampleRateInput != null && config.LocalTtsSampleRate > 0)
            {
                _localTtsSampleRateInput.text = config.LocalTtsSampleRate.ToString(CultureInfo.InvariantCulture);
            }
        }

        private async void SaveSettings()
        {
            int saveGeneration = ++_saveGeneration;

            try
            {
                AIChatRuntimeConfig config = ReadFields();
                string path = ResolveConfigPath();
                AIChatControllerConfig controllerConfig = _controller != null ? _controller.CurrentConfig : null;
                Func<Task> refreshRuntime = _controller != null
                    ? _controller.RefreshRuntimeConfigurationAsync
                    : null;

                AIChatRuntimeConfigurationResult result =
                    await AIChatRuntimeConfigurationFlow.SaveAndApplyAsync(
                        _runtimeConfigStore,
                        path,
                        config,
                        controllerConfig,
                        refreshRuntime);

                if (this == null || saveGeneration != _saveGeneration)
                {
                    return;
                }

                if (!result.Succeeded)
                {
                    Debug.LogWarning(
                        $"[AIChat] Runtime settings were not fully applied: {result.ErrorMessage}");
                    return;
                }

                if (_controller != null && _controller.CurrentConfig.DebugMode)
                {
                    Debug.Log($"[AIChat] Runtime settings saved to {path} and applied.");
                }
            }
            catch (Exception ex)
            {
                if (this != null && saveGeneration == _saveGeneration)
                {
                    Debug.LogWarning($"[AIChat] Runtime settings save failed: {ex.Message}");
                }
            }
        }

        private AIChatRuntimeConfig ReadFields()
        {
            // This panel has no controls for the editor's output/debug preferences.
            // Preserve them when saving the fields that it does manage.
            var preferences = _runtimeConfigStore.Capture(_controller != null ? _controller.CurrentConfig : null);
            if (_controller == null && _runtimeConfigStore.TryLoad(ResolveConfigPath(), out var saved))
            {
                preferences = saved;
            }
            return new AIChatRuntimeConfig
            {
                DebugMode = preferences.DebugMode,
                LocalTtsNormalizeOutput = preferences.LocalTtsNormalizeOutput,
                LocalTtsPlaybackVolume = preferences.LocalTtsPlaybackVolume,
                ApiKey = GetText(_apiKeyInput),
                ApiBaseUrl = GetText(_apiBaseUrlInput),
                LlmModel = GetText(_llmModelInput),
                TtsModel = GetText(_ttsModelInput),
                TtsVoice = GetText(_ttsVoiceInput),
                UseLocalTts = GetToggle(_useLocalTtsToggle),
                MicrophoneDeviceName = GetSelectedMicrophoneDeviceName(),
                AsrRecognitionModeIndex = GetDropdown(_asrRecognitionModeDropdown),
                AsrStreamingModelId = GetText(_asrStreamingModelInput),
                AsrOfflineModelId = GetText(_asrOfflineModelInput),
                AsrVadModelId = GetText(_asrVadModelInput),
                AsrTurnDetectionDelaySeconds = NormalizeTurnDetectionDelay(ParseFloat(_asrTurnDetectionDelayInput)),
                AsrEnablePunctuation = GetToggle(_asrEnablePunctuationToggle),
                AsrPunctuationModelId = GetText(_asrPunctuationModelInput),
                LocalTtsModelId = GetText(_localTtsModelInput),
                LocalTtsVoiceId = ParseInt(_localTtsVoiceIdInput),
                LocalTtsSpeed = ParseFloat(_localTtsSpeedInput),
                LocalTtsSampleRate = ParseInt(_localTtsSampleRateInput),
                LlmTemperature = GetSlider(_llmTemperatureSlider)
            };
        }

        private void ResetToDefaults()
        {
            EnsureRecognitionModeOptions();
            RefreshMicrophoneDeviceOptions();

            var defaults = _runtimeConfigStore.CreateDefault(new AIChatControllerConfig());

            SetText(_apiKeyInput, string.Empty);
            SetText(_apiBaseUrlInput, defaults.ApiBaseUrl);
            SetText(_llmModelInput, defaults.LlmModel);
            SetText(_ttsModelInput, defaults.TtsModel);
            SetText(_ttsVoiceInput, defaults.TtsVoice);
            SetToggle(_useLocalTtsToggle, defaults.UseLocalTts);
            SetSlider(_llmTemperatureSlider, defaults.LlmTemperature);
            SetMicrophoneDeviceDropdown(defaults.MicrophoneDeviceName);

            SetDropdown(_asrRecognitionModeDropdown, defaults.AsrRecognitionModeIndex);
            SetToggle(_asrEnablePunctuationToggle, defaults.AsrEnablePunctuation);

            SetText(_asrStreamingModelInput, defaults.AsrStreamingModelId);
            SetText(_asrOfflineModelInput, defaults.AsrOfflineModelId);
            SetText(_asrVadModelInput, defaults.AsrVadModelId);
            if (_asrTurnDetectionDelayInput != null)
            {
                _asrTurnDetectionDelayInput.text =
                    NormalizeTurnDetectionDelay(defaults.AsrTurnDetectionDelaySeconds).ToString(CultureInfo.InvariantCulture);
            }
            SetText(_asrPunctuationModelInput, defaults.AsrPunctuationModelId);

            SetText(_localTtsModelInput, defaults.LocalTtsModelId);
            if (_localTtsVoiceIdInput != null)
            {
                _localTtsVoiceIdInput.text = defaults.LocalTtsVoiceId.ToString(CultureInfo.InvariantCulture);
            }

            if (_localTtsSpeedInput != null)
            {
                _localTtsSpeedInput.text = defaults.LocalTtsSpeed.ToString(CultureInfo.InvariantCulture);
            }

            if (_localTtsSampleRateInput != null)
            {
                _localTtsSampleRateInput.text = defaults.LocalTtsSampleRate.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string GetText(TMP_InputField field)
        {
            if (field == null)
            {
                return null;
            }

            var text = field.text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static void SetText(TMP_InputField field, string value)
        {
            if (field == null)
            {
                return;
            }

            field.text = value ?? string.Empty;
        }

        private static int GetToggle(Toggle toggle)
        {
            if (toggle == null)
            {
                return -1;
            }

            return toggle.isOn ? 1 : 0;
        }

        private static void SetToggle(Toggle toggle, int value)
        {
            if (toggle == null || value < 0)
            {
                return;
            }

            toggle.isOn = value > 0;
        }

        private static int GetDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown == null)
            {
                return -1;
            }

            return dropdown.value;
        }

        private static void SetDropdown(TMP_Dropdown dropdown, int value)
        {
            if (dropdown == null || value < 0 || dropdown.options == null || dropdown.options.Count == 0)
            {
                return;
            }

            dropdown.value = Mathf.Clamp(value, 0, dropdown.options.Count - 1);
            dropdown.RefreshShownValue();
        }

        private void EnsureRecognitionModeOptions()
        {
            if (_asrRecognitionModeDropdown == null)
            {
                return;
            }

            _asrRecognitionModeDropdown.ClearOptions();
            _asrRecognitionModeDropdown.AddOptions(AsrRecognitionModeLabels);
            _asrRecognitionModeDropdown.value = 0;
            _asrRecognitionModeDropdown.RefreshShownValue();
        }

        private void EnsureMicrophoneDeviceDropdown()
        {
            if (_microphoneDeviceDropdown != null || _asrRecognitionModeDropdown == null)
            {
                return;
            }

            var templateRow = _asrRecognitionModeDropdown.transform.parent;
            if (templateRow == null || templateRow.parent == null)
            {
                return;
            }

            var microphoneRow = Instantiate(templateRow.gameObject, templateRow.parent);
            microphoneRow.name = "Microphone Device Dropdown";
            microphoneRow.transform.SetSiblingIndex(templateRow.GetSiblingIndex());

            _microphoneDeviceDropdown = microphoneRow.GetComponentInChildren<TMP_Dropdown>(true);
            if (_microphoneDeviceDropdown == null)
            {
                Destroy(microphoneRow);
                return;
            }

            var texts = microphoneRow.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (!texts[i].transform.IsChildOf(_microphoneDeviceDropdown.transform))
                {
                    texts[i].text = "Microphone Device";
                    break;
                }
            }
        }

        private void RefreshMicrophoneDeviceOptions()
        {
            EnsureMicrophoneDeviceDropdown();
            if (_microphoneDeviceDropdown == null)
            {
                return;
            }

            _microphoneDeviceNames.Clear();
            _microphoneDeviceNames.Add(string.Empty);

            var labels = new List<string> { SystemDefaultMicrophoneOption };
            var microphone = _controller != null ? _controller.MicrophoneSource : null;
            var devices = microphone != null ? microphone.AvailableDevices : null;
            if (devices != null)
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    string deviceName = devices[i].Name?.Trim();
                    if (string.IsNullOrEmpty(deviceName) || _microphoneDeviceNames.Contains(deviceName))
                    {
                        continue;
                    }

                    _microphoneDeviceNames.Add(deviceName);
                    labels.Add(devices[i].IsDefault ? $"{deviceName} (Default)" : deviceName);
                }
            }

            _microphoneDeviceDropdown.ClearOptions();
            _microphoneDeviceDropdown.AddOptions(labels);
            _microphoneDeviceDropdown.interactable = microphone != null;
            SetDropdown(_microphoneDeviceDropdown, 0);
        }

        private string GetSelectedMicrophoneDeviceName()
        {
            if (_microphoneDeviceDropdown == null || _microphoneDeviceNames.Count == 0)
            {
                return null;
            }

            int index = Mathf.Clamp(_microphoneDeviceDropdown.value, 0, _microphoneDeviceNames.Count - 1);
            return _microphoneDeviceNames[index];
        }

        private void SetMicrophoneDeviceDropdown(string deviceName)
        {
            if (_microphoneDeviceDropdown == null || _microphoneDeviceNames.Count == 0)
            {
                return;
            }

            int index = 0;
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                string normalizedDeviceName = deviceName.Trim();
                for (int i = 1; i < _microphoneDeviceNames.Count; i++)
                {
                    if (string.Equals(_microphoneDeviceNames[i], normalizedDeviceName, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                if (index == 0)
                {
                    _microphoneDeviceNames.Add(normalizedDeviceName);
                    _microphoneDeviceDropdown.options.Add(
                        new TMP_Dropdown.OptionData(normalizedDeviceName + UnavailableMicrophoneSuffix));
                    index = _microphoneDeviceNames.Count - 1;
                }
            }

            SetDropdown(_microphoneDeviceDropdown, index);
        }

        private void ConfigureApiKeyInput()
        {
            if (_apiKeyInput == null)
            {
                return;
            }

            _apiKeyInput.contentType = TMP_InputField.ContentType.Password;
            _apiKeyInput.lineType = TMP_InputField.LineType.SingleLine;
            _apiKeyInput.characterValidation = TMP_InputField.CharacterValidation.None;
            _apiKeyInput.onValidateInput = ValidateApiKeyChar;
            _apiKeyInput.ForceLabelUpdate();
        }

        private void OnApiKeySelect(string _)
        {
            if (_apiKeyInput == null)
            {
                return;
            }

            _apiKeyInput.contentType = TMP_InputField.ContentType.Standard;
            _apiKeyInput.ForceLabelUpdate();
        }

        private void OnApiKeyDeselect(string _)
        {
            if (_apiKeyInput == null)
            {
                return;
            }

            _apiKeyInput.contentType = TMP_InputField.ContentType.Password;
            _apiKeyInput.ForceLabelUpdate();
        }

        private static char ValidateApiKeyChar(string text, int charIndex, char addedChar)
        {
            if (addedChar < '!' || addedChar > '~')
            {
                return '\0';
            }

            return addedChar;
        }

        private static float GetSlider(Slider slider)
        {
            if (slider == null)
            {
                return -1f;
            }

            return slider.value;
        }

        private static void SetSlider(Slider slider, float value)
        {
            if (slider == null || value < 0f)
            {
                return;
            }

            slider.value = value;
        }

        private static float ParseFloat(TMP_InputField field)
        {
            var text = GetText(field);
            if (string.IsNullOrWhiteSpace(text))
            {
                return -1f;
            }

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return -1f;
        }

        private static float NormalizeTurnDetectionDelay(float value)
        {
            if (value <= 0f)
            {
                return AIChatRuntimeDefaults.DefaultAsrTurnDetectionDelaySeconds;
            }

            return Mathf.Max(0.1f, value);
        }

        private static int ParseInt(TMP_InputField field)
        {
            var text = GetText(field);
            if (string.IsNullOrWhiteSpace(text))
            {
                return -1;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return -1;
        }

        private void ConfigurePanelRootVisibility()
        {
            if (_panelRoot == null)
            {
                _panelRootUsesCanvasGroup = false;
                _panelRootCanvasGroup = null;
                return;
            }

            _panelRootUsesCanvasGroup = ReferenceEquals(_panelRoot, gameObject);
            if (_panelRootUsesCanvasGroup)
            {
                if (!_panelRoot.TryGetComponent(out _panelRootCanvasGroup))
                {
                    _panelRootCanvasGroup = _panelRoot.AddComponent<CanvasGroup>();
                }

                SetPanelRootVisible(false);
                return;
            }

            _panelRootCanvasGroup = null;
            _panelRoot.SetActive(false);
        }

        private void InitializeOpenButtonVisibility()
        {
            if (_openButton == null)
            {
                _openButtonCanvasGroup = null;
                return;
            }

            if (!_openButton.TryGetComponent(out _openButtonCanvasGroup))
            {
                _openButtonCanvasGroup = _openButton.gameObject.AddComponent<CanvasGroup>();
            }

            var initialAlpha = Input.mousePresent ? OpenButtonHiddenAlpha : OpenButtonVisibleAlpha;
            SetOpenButtonCanvasGroup(initialAlpha);
        }

        private void UpdateOpenButtonVisibility()
        {
            if (_openButton == null)
            {
                return;
            }

            if (_openButtonCanvasGroup == null)
            {
                InitializeOpenButtonVisibility();
                if (_openButtonCanvasGroup == null)
                {
                    return;
                }
            }

            var targetAlpha = OpenButtonVisibleAlpha;
            if (Input.mousePresent)
            {
                var buttonRect = _openButton.transform as RectTransform;
                targetAlpha = IsPointerInside(buttonRect) ? OpenButtonVisibleAlpha : OpenButtonHiddenAlpha;
            }

            var nextAlpha = Mathf.MoveTowards(
                _openButtonCanvasGroup.alpha,
                Mathf.Clamp01(targetAlpha),
                Time.unscaledDeltaTime / OpenButtonFadeDuration);
            SetOpenButtonCanvasGroup(nextAlpha);
        }

        private void SetOpenButtonCanvasGroup(float alpha)
        {
            if (_openButtonCanvasGroup == null)
            {
                return;
            }

            var clampedAlpha = Mathf.Clamp01(alpha);
            _openButtonCanvasGroup.alpha = clampedAlpha;

            var allowInteraction = clampedAlpha > 0.001f;
            _openButtonCanvasGroup.interactable = allowInteraction;
            _openButtonCanvasGroup.blocksRaycasts = allowInteraction;
        }

        private void SetPanelRootVisible(bool visible)
        {
            if (_panelRoot == null)
            {
                return;
            }

            if (_panelRootUsesCanvasGroup)
            {
                if (_panelRootCanvasGroup == null && !_panelRoot.TryGetComponent(out _panelRootCanvasGroup))
                {
                    _panelRootCanvasGroup = _panelRoot.AddComponent<CanvasGroup>();
                }

                if (_panelRootCanvasGroup == null)
                {
                    return;
                }

                _panelRootCanvasGroup.alpha = visible ? 1f : 0f;
                _panelRootCanvasGroup.interactable = visible;
                _panelRootCanvasGroup.blocksRaycasts = visible;
                return;
            }

            if (_panelRoot.activeSelf != visible)
            {
                _panelRoot.SetActive(visible);
            }
        }

        private static bool IsPointerInside(RectTransform target)
        {
            if (target == null)
            {
                return false;
            }

            var canvas = target.GetComponentInParent<Canvas>();
            var eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(target, Input.mousePosition, eventCamera);
        }
    }
}
#endif
