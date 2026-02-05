using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class AIChatRuntimeConfigPanel : MonoBehaviour
    {
        private const string DefaultAsrStreamingModelId = "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20";
        private const string DefaultAsrOfflineModelId = "sherpa-onnx-zipformer-zh-en-2023-11-22";
        private const string DefaultAsrVadModelId = "silero_vad_v5";
        private const string DefaultAsrPunctuationModelId = "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8";

        private const string DefaultLocalTtsModelId = "vits-melo-tts-zh_en";
        private const int DefaultLocalTtsVoiceId = 1;
        private const float DefaultLocalTtsSpeed = 1f;
        private const int DefaultLocalTtsSampleRate = 44100;

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

        [Header("Inputs (ASR)")]
        [SerializeField] private TMP_Dropdown _asrRecognitionModeDropdown;
        [SerializeField] private TMP_InputField _asrStreamingModelInput;
        [SerializeField] private TMP_InputField _asrOfflineModelInput;
        [SerializeField] private TMP_InputField _asrVadModelInput;
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

        [Serializable]
        private class RuntimeConfig
        {
            public string ApiKey;
            public string ApiBaseUrl;
            public string LlmModel;
            public float LlmTemperature = -1f;
            public string TtsModel;
            public string TtsVoice;
            public int UseLocalTts = -1;
            public int AsrRecognitionModeIndex = -1;
            public string AsrStreamingModelId;
            public string AsrOfflineModelId;
            public string AsrVadModelId;
            public int AsrEnablePunctuation = -1;
            public string AsrPunctuationModelId;
            public string LocalTtsModelId;
            public int LocalTtsVoiceId = -1;
            public float LocalTtsSpeed = -1f;
            public int LocalTtsSampleRate = -1;
        }

        private void Awake()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }

            ConfigureApiKeyInput();
            EnsureRecognitionModeOptions();

            if (_openButton != null)
            {
                _openButton.onClick.AddListener(OpenPanel);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(ClosePanel);
            }

            if (_saveButton != null)
            {
                _saveButton.onClick.AddListener(SaveAndReload);
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
                _saveButton.onClick.RemoveListener(SaveAndReload);
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
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(true);
            }

            EnsureRecognitionModeOptions();
            LoadIntoFields();
        }

        private void ClosePanel()
        {
            if (_panelRoot != null)
            {
                _panelRoot.SetActive(false);
            }
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
            var path = ResolveConfigPath();
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var config = JsonUtility.FromJson<RuntimeConfig>(json);
                if (config == null)
                {
                    return;
                }

                SetText(_apiKeyInput, config.ApiKey);
                SetText(_apiBaseUrlInput, config.ApiBaseUrl);
                SetText(_llmModelInput, config.LlmModel);
                SetText(_ttsModelInput, config.TtsModel);
                SetText(_ttsVoiceInput, config.TtsVoice);
                SetToggle(_useLocalTtsToggle, config.UseLocalTts);
                SetDropdown(_asrRecognitionModeDropdown, config.AsrRecognitionModeIndex);
                SetText(_asrStreamingModelInput, config.AsrStreamingModelId);
                SetText(_asrOfflineModelInput, config.AsrOfflineModelId);
                SetText(_asrVadModelInput, config.AsrVadModelId);
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
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to load runtime config UI: {ex.Message}");
            }
        }

        private void SaveAndReload()
        {
            var config = new RuntimeConfig
            {
                ApiKey = GetText(_apiKeyInput),
                ApiBaseUrl = GetText(_apiBaseUrlInput),
                LlmModel = GetText(_llmModelInput),
                TtsModel = GetText(_ttsModelInput),
                TtsVoice = GetText(_ttsVoiceInput),
                UseLocalTts = GetToggle(_useLocalTtsToggle),
                AsrRecognitionModeIndex = GetDropdown(_asrRecognitionModeDropdown),
                AsrStreamingModelId = GetText(_asrStreamingModelInput),
                AsrOfflineModelId = GetText(_asrOfflineModelInput),
                AsrVadModelId = GetText(_asrVadModelInput),
                AsrEnablePunctuation = GetToggle(_asrEnablePunctuationToggle),
                AsrPunctuationModelId = GetText(_asrPunctuationModelInput),
                LocalTtsModelId = GetText(_localTtsModelInput),
                LocalTtsVoiceId = ParseInt(_localTtsVoiceIdInput),
                LocalTtsSpeed = ParseFloat(_localTtsSpeedInput),
                LocalTtsSampleRate = ParseInt(_localTtsSampleRateInput),
                LlmTemperature = GetSlider(_llmTemperatureSlider)
            };

            var path = ResolveConfigPath();
            try
            {
                var json = JsonUtility.ToJson(config, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to save runtime config UI: {ex.Message}");
                return;
            }

            try
            {
                Eitan.EasyMic.Runtime.AudioSystem.Instance.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to stop EasyMic playback before reload: {ex.Message}");
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void ResetToDefaults()
        {
            EnsureRecognitionModeOptions();

            var defaults = new AIChatControllerConfig();

            SetText(_apiKeyInput, string.Empty);
            SetText(_apiBaseUrlInput, defaults.ApiBaseUrl);
            SetText(_llmModelInput, defaults.LlmModel);
            SetText(_ttsModelInput, defaults.TtsModel);
            SetText(_ttsVoiceInput, defaults.TtsVoice);
            SetToggle(_useLocalTtsToggle, defaults.UseLocalTts ? 1 : 0);
            SetSlider(_llmTemperatureSlider, defaults.LlmTemperature);

            SetDropdown(_asrRecognitionModeDropdown, 1);
            SetToggle(_asrEnablePunctuationToggle, 0);

            SetText(_asrStreamingModelInput, DefaultAsrStreamingModelId);
            SetText(_asrOfflineModelInput, DefaultAsrOfflineModelId);
            SetText(_asrVadModelInput, DefaultAsrVadModelId);
            SetText(_asrPunctuationModelInput, DefaultAsrPunctuationModelId);

            SetText(_localTtsModelInput, DefaultLocalTtsModelId);
            if (_localTtsVoiceIdInput != null)
            {
                _localTtsVoiceIdInput.text = DefaultLocalTtsVoiceId.ToString(CultureInfo.InvariantCulture);
            }

            if (_localTtsSpeedInput != null)
            {
                _localTtsSpeedInput.text = DefaultLocalTtsSpeed.ToString(CultureInfo.InvariantCulture);
            }

            if (_localTtsSampleRateInput != null)
            {
                _localTtsSampleRateInput.text = DefaultLocalTtsSampleRate.ToString(CultureInfo.InvariantCulture);
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
    }
}
