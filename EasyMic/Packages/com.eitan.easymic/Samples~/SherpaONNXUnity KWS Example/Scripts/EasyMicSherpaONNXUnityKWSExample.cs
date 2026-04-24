#if EITAN_SHERPA_ONNX_UNITY_PRESENT
namespace Eitan.EasyMic.Samples.SherpaONNXUnity.KWS
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Eitan.EasyMic.Runtime;
    using Eitan.EasyMic.Runtime.Mono;
    using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
    using Eitan.SherpaONNXUnity.Runtime;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using UnityEngine;
    using UnityEngine.UI;

    [AddComponentMenu("Examples/EasyMic/Sherpa ONNX/KWS Example")]
    public class EasyMicSherpaONNXUnityKWSExample : MonoBehaviour
    {
        #region UI Constants
        private const string STATE_NOT_LOADED = "Not Loaded";
        private const string STATE_LOADING = "Loading Models...";
        private const string STATE_LOADING_FAILED = "Loading Failed";
        private const string STATE_READY = "Ready to Record";
        private const string STATE_RECORDING = "Recording...";
        private const string STATE_LISTENING = "Listening...";
        private const string STATE_STOPPED = "Stopped";

        private const string BTN_LOAD_MODEL = "Load Model";
        private const string BTN_UNLOAD_MODEL = "Unload Model";
        private const string BTN_LOADING = "Loading...";
        private const string BTN_START_RECORDING = "Start Recording";
        private const string BTN_STOP_RECORDING = "Stop Recording";
        #endregion

        #region Inspector UI Assignments
        [Header("Core Components")]
        [Tooltip("Optional; will be auto-created if missing.")]
        [SerializeField] private VoiceMicrophone _voiceMicrophone;

        [Header("Model & Device Setup")]
        [Tooltip("Dropdown to select the Keyword Spotting (KWS) model.")]
        [SerializeField] private Dropdown _modelIDDropdown;
        [Tooltip("Dropdown to select the microphone device.")]
        [SerializeField] private Dropdown _selectDeviceDropdown;
        [Tooltip("Button to load or unload the selected model.")]
        [SerializeField] private Button _modelLoadOrUnloadButton;

        [Header("Recording Controls")]
        [Tooltip("Button to start or stop the recording process.")]
        [SerializeField] private Button _recordButton;

        [Header("Status Display")]
        [Tooltip("Image to visually indicate the current status (e.g., gray for idle, green for ready, red for recording).")]
        [SerializeField] private RawImage _stateImage;
        [Tooltip("Text to display the current status message.")]
        [SerializeField] private Text _stateText;
        [Tooltip("Text for providing tips or status updates to the user.")]
        [SerializeField] private Text _tipsText;
        [Tooltip("Text to display the detected keyword and combo count.")]
        [SerializeField] private Text _keywordText;

        [Header("Keyword Management")]
        [SerializeField] private RectTransform _keywordsPanel;
        [Tooltip("The input field for users to add new custom keywords.")]
        [SerializeField] private InputField _keywordInput;
        [Tooltip("Button to register the keyword from the input field. Should be a child of the Keyword Input Field.")]
        [SerializeField] private Button _registerKeywordButton;
        [Tooltip("The scrollable list area for displaying registered keywords.")]
        [SerializeField] private ScrollRect _keywordsListScrollView;
        [Tooltip("Button to clear all currently registered keywords.")]
        [SerializeField] private Button _clearKeywordsBtn;
        [Tooltip("A template GameObject for instantiating new keyword items in the list.")]
        [SerializeField] private GameObject _keywordTemplate;

        [Header("KWS Configuration")]
        [Tooltip("Initial list of keywords to register when the scene starts.")]
        [SerializeField] private KeywordSpotting.KeywordRegistration[] kwsKeywords;

        [Header("Audio Feedback")]
        [Tooltip("Audio clip to play when a keyword is successfully detected.")]
        [SerializeField] private AudioClip wakeupSoundClip;
        #endregion

        #region Private Fields
        private MicDevice[] _devices = Array.Empty<MicDevice>();

        private AppState _currentState = AppState.NotLoaded;
        private Color _originLoadBtnColor;
        private readonly string defaultModelID = "sherpa-onnx-kws-zipformer-wenetspeech-3.3M-2024-01-01";
        private readonly List<KeywordSpotting.KeywordRegistration> _runtimeKeywords = new();

        private const string KeywordTemplateLabelPath = "Text (Legacy)";
        private const string KeywordTemplateDeleteButtonPath = "Button (Register)";

        // Combo effect fields
        private int _comboCount;
        private string _lastKeyword;
        private float _lastDetectionTime;
        private Coroutine _resetCoroutine;
        private const float DisplayDuration = 3f;
        private int _originalFontSize;
        private string _loadedMessage;
        private Coroutine _readyAfterInitCoroutine;

        private static readonly string[] InterestingFeedback =
        {
            "Double Kill!", "Triple Kill!", "Rampage!", "Godlike!", "Beyond Godlike!"
        };

        private enum AppState
        {
            NotLoaded,
            Loading,
            Ready,
            Recording
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            EnsureVoiceMicrophone();
            if (_voiceMicrophone != null)
            {
                _voiceMicrophone.ApplyMicrophoneOptions(new MicrophoneOptions(recordOnAwake: false, autoFallback: true), restartRecording: false);
            }
        }

        private void Start()
        {
            BindUIEvents();
            BindVoiceMicrophoneEvents();

            _keywordText.text = "Please click the button to load the keyword spotting model";
            _tipsText.text = string.Empty;
            _loadedMessage = "Load a keyword spotting model.";
            _originLoadBtnColor = _modelLoadOrUnloadButton.GetComponent<Image>().color;
            if (_keywordText != null)
            {
                _originalFontSize = _keywordText.fontSize;
            }

            _ = InitDropdownAsync();
            InitKeywordsPanelUI();
            UpdateState(AppState.NotLoaded);
            RefreshDeviceList();
        }

        private void OnDestroy()
        {
            if (_voiceMicrophone != null && _voiceMicrophone.IsRecording)
            {
                _voiceMicrophone.StopRecording();
            }

            UnloadModels();
            UnbindVoiceMicrophoneEvents();
            UnbindUIEvents();

            if (_resetCoroutine != null)
            {
                StopCoroutine(_resetCoroutine);
                _resetCoroutine = null;
            }

            if (_readyAfterInitCoroutine != null)
            {
                StopCoroutine(_readyAfterInitCoroutine);
                _readyAfterInitCoroutine = null;
            }
        }
        #endregion

        #region UI Initialization and Event Binding
        private void BindUIEvents()
        {
            _modelLoadOrUnloadButton.onClick.AddListener(HandleModelLoadOrUnloadButtonClick);
            _recordButton.onClick.AddListener(OnRecordButtonPressed);
            _registerKeywordButton.onClick.AddListener(HandleAddKeywordButtonClick);
            _clearKeywordsBtn.onClick.AddListener(HandleClearKeywordsButtonClick);
        }

        private void UnbindUIEvents()
        {
            _modelLoadOrUnloadButton?.onClick.RemoveAllListeners();
            _recordButton?.onClick.RemoveAllListeners();
            _registerKeywordButton?.onClick.RemoveAllListeners();
            _clearKeywordsBtn?.onClick.RemoveAllListeners();
        }

        private void EnsureVoiceMicrophone()
        {
            if (_voiceMicrophone != null)
            {
                return;
            }

            _voiceMicrophone = FindObjectOfType<VoiceMicrophone>();
            if (_voiceMicrophone != null)
            {
                return;
            }

            var go = new GameObject("VoiceMicrophone");
            _voiceMicrophone = go.AddComponent<VoiceMicrophone>();
        }

        private void BindVoiceMicrophoneEvents()
        {
            if (_voiceMicrophone == null)
            {
                return;
            }

            _voiceMicrophone.OnKeywordActivityChanged += HandleKeywordActivityChanged;
            _voiceMicrophone.OnLoadingProgressFeedback += HandleLoadingProgress;
            _voiceMicrophone.OnLoadingSucceededFeedback += HandleLoadingSucceeded;
            _voiceMicrophone.OnLoadingFailedFeedback += HandleLoadingFailed;
        }

        private void UnbindVoiceMicrophoneEvents()
        {
            if (_voiceMicrophone == null)
            {
                return;
            }

            _voiceMicrophone.OnKeywordActivityChanged -= HandleKeywordActivityChanged;
            _voiceMicrophone.OnLoadingProgressFeedback -= HandleLoadingProgress;
            _voiceMicrophone.OnLoadingSucceededFeedback -= HandleLoadingSucceeded;
            _voiceMicrophone.OnLoadingFailedFeedback -= HandleLoadingFailed;
        }

        private async Task InitDropdownAsync()
        {
            _modelIDDropdown.ClearOptions();
            _modelIDDropdown.captionText.text = "Fetching model manifest...";
            _modelLoadOrUnloadButton.gameObject.SetActive(false);

            try
            {
                var modelIds = await SherpaONNXUnityAPI.GetModelIDByTypeAsync(SherpaONNXModuleType.KeywordSpotting);
                _modelIDDropdown.AddOptions(modelIds.ToList());

                var defaultIndex = _modelIDDropdown.options.FindIndex(o => o.text == defaultModelID);
                _modelIDDropdown.value = Mathf.Max(0, defaultIndex);
                _modelIDDropdown.RefreshShownValue();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to fetch KWS models: {ex.Message}");
                _modelIDDropdown.AddOptions(new List<string> { "Failed to load models" });
                _modelIDDropdown.value = 0;
                _modelIDDropdown.RefreshShownValue();
            }
            finally
            {
                _modelLoadOrUnloadButton.gameObject.SetActive(true);
            }
        }
        #endregion

        #region Device List
        private void RefreshDeviceList()
        {
            EasyMicAPI.Refresh();
            _devices = EasyMicAPI.Devices ?? Array.Empty<MicDevice>();

            var deviceNames = _devices.Select(d => d.Name).ToList();
            _selectDeviceDropdown.ClearOptions();
            if (_devices.Length == 0)
            {
                _selectDeviceDropdown.AddOptions(new List<string> { "No microphone devices found" });
                _selectDeviceDropdown.interactable = false;
                return;
            }

            _selectDeviceDropdown.AddOptions(deviceNames);
            _selectDeviceDropdown.interactable = true;

            int defaultIndex = Array.FindIndex(_devices, d => d.IsDefault);
            _selectDeviceDropdown.value = Mathf.Max(0, defaultIndex);
            _selectDeviceDropdown.RefreshShownValue();
        }

        private MicDevice GetSelectedDevice()
        {
            if (_devices.Length == 0 || _selectDeviceDropdown.value < 0 || _selectDeviceDropdown.value >= _devices.Length)
            {
                return default;
            }

            return _devices[_selectDeviceDropdown.value];
        }
        #endregion

        #region Core Logic - Button Handlers
        private void HandleModelLoadOrUnloadButtonClick()
        {
            if (_currentState == AppState.NotLoaded)
            {
                StartLoadingModels();
            }
            else if (_currentState == AppState.Ready)
            {
                UnloadModels();
            }
        }

        private void OnRecordButtonPressed()
        {
            if (_currentState == AppState.Recording)
            {
                StopRecordingFlow();
            }
            else if (_currentState == AppState.Ready)
            {
                StartRecordingFlow();
            }
        }
        #endregion

        #region Core Logic - Model and Recording Flow
        private void StartLoadingModels()
        {
            EnsureVoiceMicrophone();
            if (_voiceMicrophone == null)
            {
                return;
            }

            if (_readyAfterInitCoroutine != null)
            {
                StopCoroutine(_readyAfterInitCoroutine);
                _readyAfterInitCoroutine = null;
            }

            var modelId = GetSelectedModelId();
            if (string.IsNullOrWhiteSpace(modelId))
            {
                Debug.LogError("No KWS model selected.");
                return;
            }

            UpdateState(AppState.Loading);

            var config = BuildConfiguration(modelId);
            if (!_voiceMicrophone.ApplyConfiguration(config))
            {
                Debug.LogError("Failed to apply VoiceMicrophone configuration.");
                UpdateState(AppState.NotLoaded);
                return;
            }

            var device = GetSelectedDevice();
            if (!string.IsNullOrWhiteSpace(device.Name))
            {
                _voiceMicrophone.SwitchCaptureDevice(device, Channel.Mono, SampleRate.Hz16000, restartRecording: false);
            }

            if (_voiceMicrophone.IsInitializing)
            {
                return;
            }

            if (!_voiceMicrophone.Initialized || _voiceMicrophone.AreModelsDisposed)
            {
                _voiceMicrophone.Init();
            }
        }

        private AutomaticSpeechRecognitionConfiguration BuildConfiguration(string keywordModelId)
        {
            var config = AutomaticSpeechRecognitionConfiguration.CreateDefault();
            var preset = config.GetActivePreset();

            preset.RecognitionMode = RecognitionMode.KeywordSpottingOnly;
            preset.StreamingModelId = string.Empty;
            preset.OfflineModelId = string.Empty;
            preset.VadModelId = string.Empty;
            preset.EnablePunctuation = false;
            preset.PunctuationModelId = string.Empty;

            var keywordOptions = preset.KeywordOptions;
            keywordOptions.Enabled = true;
            keywordOptions.ModelId = keywordModelId;
            keywordOptions.CustomKeywords = _runtimeKeywords.ToArray();
            keywordOptions.UseTriggerSound = wakeupSoundClip != null;
            keywordOptions.TriggerSoundClip = wakeupSoundClip;

            preset.KeywordOptions = keywordOptions;
            config.UpdatePreset(preset);
            return config;
        }

        private void UnloadModels()
        {
            if (_voiceMicrophone == null)
            {
                UpdateState(AppState.NotLoaded);
                return;
            }

            if (_voiceMicrophone.IsRecording)
            {
                StopRecordingFlow();
            }

            _voiceMicrophone.DisposeModels();

            if (_resetCoroutine != null)
            {
                StopCoroutine(_resetCoroutine);
                _resetCoroutine = null;
            }

            UpdateState(AppState.NotLoaded);
        }

        private void StartRecordingFlow()
        {
            if (_voiceMicrophone == null || !_voiceMicrophone.IsOperational)
            {
                Debug.LogError("Models are not ready. Load a model first.");
                return;
            }

            _voiceMicrophone.StartRecording();
            UpdateState(AppState.Recording);
        }

        private void StopRecordingFlow()
        {
            _voiceMicrophone?.StopRecording();
            UpdateState(AppState.Ready);
            _stateText.text = $"{STATE_STOPPED} - {STATE_READY}";
        }

        private string GetSelectedModelId()
        {
            if (_modelIDDropdown.options == null || _modelIDDropdown.options.Count == 0)
            {
                return string.Empty;
            }

            int index = Mathf.Clamp(_modelIDDropdown.value, 0, _modelIDDropdown.options.Count - 1);
            return _modelIDDropdown.options[index].text;
        }
        #endregion

        #region Keyword Management
        private void InitKeywordsPanelUI()
        {
            if (_keywordsPanel == null || _keywordInput == null || _keywordsListScrollView == null || _keywordTemplate == null)
            {
                Debug.LogWarning("[KeywordSpottingExample] Missing keyword UI references. Disabling keyword management.");
                if (_keywordInput)
                {
                    _keywordInput.interactable = false;
                }

                if (_clearKeywordsBtn)
                {
                    _clearKeywordsBtn.interactable = false;
                }

                if (_registerKeywordButton)
                {
                    _registerKeywordButton.interactable = false;
                }

                return;
            }

            if (_keywordTemplate.activeSelf)
            {
                _keywordTemplate.SetActive(false);
            }

            _runtimeKeywords.Clear();
            if (kwsKeywords != null)
            {
                foreach (var registration in kwsKeywords)
                {
                    if (string.IsNullOrWhiteSpace(registration.Keyword))
                    {
                        continue;
                    }

                    _runtimeKeywords.Add(new KeywordSpotting.KeywordRegistration(
                        registration.Keyword.Trim(),
                        registration.BoostingScore,
                        registration.TriggerThreshold));
                }
            }

            SyncSerializedKeywords();
            RefreshKeywordsUI();
        }

        private void HandleClearKeywordsButtonClick()
        {
            if (_currentState != AppState.NotLoaded)
            {
                Debug.LogWarning("[KeywordSpottingExample] Cannot clear keywords while a model is loaded.");
                return;
            }

            _runtimeKeywords.Clear();
            SyncSerializedKeywords();
            RefreshKeywordsUI();
            if (_tipsText != null)
            {
                _tipsText.text = "<color=yellow><b>All keywords cleared.</b></color>";
            }
        }

        private void HandleAddKeywordButtonClick()
        {
            if (_keywordInput == null)
            {
                return;
            }

            TryAddCustomKeyword(_keywordInput.text);
        }

        private void TryAddCustomKeyword(string candidate)
        {
            if (_currentState != AppState.NotLoaded)
            {
                Debug.LogWarning("[KeywordSpottingExample] Cannot edit keywords while a model is loaded.");
                return;
            }

            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var keyword = candidate.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                return;
            }

            if (_runtimeKeywords.Any(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogWarning($"[KeywordSpottingExample] Keyword '{keyword}' already exists.");
                return;
            }

            var registration = new KeywordSpotting.KeywordRegistration(keyword);
            _runtimeKeywords.Add(registration);
            SyncSerializedKeywords();
            CreateKeywordItem(registration);
            UpdateKeywordListLayout(forceScrollToLatest: true);

            if (_tipsText != null)
            {
                _tipsText.text = "<color=yellow><b>Keywords updated.</b></color> Reload model to apply changes.";
            }
        }

        private void RefreshKeywordsUI()
        {
            if (_keywordsListScrollView == null || _keywordsListScrollView.content == null)
            {
                return;
            }

            foreach (Transform child in _keywordsListScrollView.content)
            {
                if (child != null && child.gameObject != _keywordTemplate)
                {
                    Destroy(child.gameObject);
                }
            }

            foreach (var registration in _runtimeKeywords)
            {
                CreateKeywordItem(registration);
            }

            UpdateKeywordListLayout(forceScrollToLatest: false);
        }

        private void CreateKeywordItem(KeywordSpotting.KeywordRegistration registration)
        {
            if (_keywordsListScrollView == null || _keywordsListScrollView.content == null || _keywordTemplate == null)
            {
                return;
            }

            var item = Instantiate(_keywordTemplate, _keywordsListScrollView.content);
            item.name = $"Keyword_{registration.Keyword}";
            item.SetActive(true);

            var keywordLabel = item.transform.Find(KeywordTemplateLabelPath)?.GetComponent<Text>();
            if (keywordLabel != null)
            {
                keywordLabel.text = registration.Keyword;
            }

            var deleteButtonTransform = item.transform.Find(KeywordTemplateDeleteButtonPath);
            if (deleteButtonTransform != null && deleteButtonTransform.TryGetComponent<Button>(out var deleteButton))
            {
                var keyword = registration.Keyword;
                deleteButton.onClick.RemoveAllListeners();
                deleteButton.onClick.AddListener(() => RemoveCustomKeyword(keyword, item));
            }
        }

        private void RemoveCustomKeyword(string keyword, GameObject item)
        {
            if (_currentState != AppState.NotLoaded)
            {
                Debug.LogWarning("[KeywordSpottingExample] Cannot edit keywords while a model is loaded.");
                return;
            }

            var index = _runtimeKeywords.FindIndex(k => string.Equals(k.Keyword, keyword, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            _runtimeKeywords.RemoveAt(index);
            SyncSerializedKeywords();

            if (item != null)
            {
                Destroy(item);
            }

            UpdateKeywordListLayout(forceScrollToLatest: false);
            if (_tipsText != null)
            {
                _tipsText.text = "<color=yellow><b>Keywords updated.</b></color> Reload model to apply changes.";
            }
        }

        private void SyncSerializedKeywords()
        {
            kwsKeywords = _runtimeKeywords.ToArray();
        }

        private void UpdateKeywordListLayout(bool forceScrollToLatest)
        {
            if (_keywordsListScrollView == null || _keywordsListScrollView.content == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_keywordsListScrollView.content);

            if (forceScrollToLatest)
            {
                _keywordsListScrollView.verticalNormalizedPosition = 0f;
            }
        }
        #endregion

        #region State Management & UI Updates
        private void SetUIInteractability()
        {
            bool isIdle = _currentState != AppState.Loading && _currentState != AppState.Recording;
            _modelLoadOrUnloadButton.interactable = isIdle;
            _selectDeviceDropdown.interactable = isIdle;
            _modelIDDropdown.interactable = _currentState == AppState.NotLoaded;
            _recordButton.interactable = _currentState == AppState.Ready || _currentState == AppState.Recording;

            bool canEditKeywords = _currentState == AppState.NotLoaded;
            if (_keywordInput)
            {
                _keywordInput.interactable = canEditKeywords;
            }
            if (_registerKeywordButton)
            {
                _registerKeywordButton.interactable = canEditKeywords;
            }
            if (_clearKeywordsBtn)
            {
                _clearKeywordsBtn.interactable = canEditKeywords;
            }
            UpdateKeywordsListDeleteBtnInteractable(canEditKeywords);
        }

        private void UpdateState(AppState newState)
        {
            _currentState = newState;
            _recordButton.gameObject.SetActive(false);

            switch (_currentState)
            {
                case AppState.NotLoaded:
                    _modelLoadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_LOAD_MODEL;
                    _modelLoadOrUnloadButton.GetComponent<Image>().color = _originLoadBtnColor;
                    _stateText.text = STATE_NOT_LOADED;
                    _stateImage.color = Color.gray;
                    _keywordText.text = "Please load a model.";
                    break;
                case AppState.Loading:
                    _modelLoadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_LOADING;
                    _stateText.text = STATE_LOADING;
                    _stateImage.color = Color.yellow;
                    _keywordText.text = "Loading model...";
                    break;
                case AppState.Ready:
                    _modelLoadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_UNLOAD_MODEL;
                    _modelLoadOrUnloadButton.GetComponent<Image>().color = Color.red;
                    _recordButton.gameObject.SetActive(true);
                    _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
                    _stateText.text = STATE_READY;
                    _stateImage.color = Color.green;
                    break;
                case AppState.Recording:
                    _recordButton.gameObject.SetActive(true);
                    _recordButton.GetComponentInChildren<Text>().text = BTN_STOP_RECORDING;
                    _stateText.text = STATE_RECORDING;
                    _stateImage.color = Color.red;
                    _keywordText.text = STATE_LISTENING;
                    break;
            }

            SetUIInteractability();
        }

        private void UpdateKeywordsListDeleteBtnInteractable(bool interactable)
        {
            if (_keywordsListScrollView && _keywordsListScrollView.content)
            {
                foreach (Transform child in _keywordsListScrollView.content)
                {
                    if (child == null || child.gameObject == _keywordTemplate)
                    {
                        continue;
                    }

                    var childBtn = child.GetComponentInChildren<Button>();
                    if (childBtn)
                    {
                        childBtn.interactable = interactable;
                    }
                }
            }
        }
        #endregion

        #region Keyword Detection Handling
        private void HandleKeywordActivityChanged(string keyword, bool isActive)
        {
            if (!isActive)
            {
                return;
            }

            HandleKeywordDetected(keyword);
        }

        private void HandleKeywordDetected(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return;
            }

            if (_resetCoroutine != null)
            {
                StopCoroutine(_resetCoroutine);
            }

            if (!string.IsNullOrEmpty(_lastKeyword) && _lastKeyword == keyword && (Time.time - _lastDetectionTime) < DisplayDuration)
            {
                _comboCount++;
            }
            else
            {
                _comboCount = 1;
            }

            _lastKeyword = keyword;
            _lastDetectionTime = Time.time;

            var comboDisplay = _comboCount > 1 ? $" x{_comboCount}" : "";
            _keywordText.text = $"<color=cyan><b>{keyword}</b></color>{comboDisplay}";
            _keywordText.fontSize = _originalFontSize + (_comboCount - 1) * 4;

            if (_comboCount > 1)
            {
                var feedbackIndex = Mathf.Clamp(_comboCount - 2, 0, InterestingFeedback.Length - 1);
                _tipsText.text = $"<b><color=yellow>[COMBO]</color></b> {InterestingFeedback[feedbackIndex]}";
            }
            else
            {
                _tipsText.text = "<b><color=green>[Detected]</color></b> Say the keyword again to start a combo!";
            }

            Debug.Log($"Wake-up word detected: {keyword}, combo: {_comboCount}");
            _resetCoroutine = StartCoroutine(ResetKeywordDisplayAfterDelay());
        }

        private IEnumerator ResetKeywordDisplayAfterDelay()
        {
            yield return new WaitForSeconds(DisplayDuration);
            _keywordText.text = "<b><i>Listening for wake-up words...</i></b>";
            _tipsText.text = _loadedMessage;
            if (_keywordText != null)
            {
                _keywordText.fontSize = _originalFontSize;
            }

            _comboCount = 0;
            _lastKeyword = string.Empty;
            _resetCoroutine = null;
        }
        #endregion

        #region VoiceMicrophone Feedback
        private void HandleLoadingProgress(string message, float progress)
        {
            if (_currentState != AppState.Loading)
            {
                return;
            }

            int pct = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);
            _tipsText.text = $"<b>[Loading]</b> {pct}% {message}";
        }

        private void HandleLoadingSucceeded(SuccessFeedback feedback)
        {
            var modelId = feedback?.Metadata.modelId;
            _loadedMessage = string.IsNullOrWhiteSpace(modelId)
                ? "<b><color=green>[Loaded]</color></b> Ready to detect keywords."
                : $"<b><color=green>[Loaded]</color></b> {modelId} is active. Ready to detect keywords.";
            _tipsText.text = _loadedMessage;
            _keywordText.text = "Model loaded. Click record and speak the keyword to test.";

            if (_voiceMicrophone != null && _voiceMicrophone.IsOperational)
            {
                UpdateState(AppState.Ready);
                return;
            }

            if (_readyAfterInitCoroutine != null)
            {
                StopCoroutine(_readyAfterInitCoroutine);
            }

            _readyAfterInitCoroutine = StartCoroutine(WaitForOperationalAndReady());
        }

        private IEnumerator WaitForOperationalAndReady()
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (_voiceMicrophone != null &&
                   !_voiceMicrophone.IsOperational &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _readyAfterInitCoroutine = null;
            if (_voiceMicrophone != null && _voiceMicrophone.IsOperational)
            {
                UpdateState(AppState.Ready);
            }
        }

        private void HandleLoadingFailed(FailedFeedback feedback)
        {
            Debug.LogError($"[Failed] : {feedback?.Message ?? "Unknown error"}");
            _tipsText.text = "<b><color=red>[Failed]</color>:</b> Model loading failed.";
            _keywordText.text = "<color=red><b>Model loading failed</b></color>";
            UpdateState(AppState.NotLoaded);
            _stateText.text = STATE_LOADING_FAILED;
            _stateImage.color = Color.red;

            if (_readyAfterInitCoroutine != null)
            {
                StopCoroutine(_readyAfterInitCoroutine);
                _readyAfterInitCoroutine = null;
            }
        }
        #endregion

        public void OpenGithubRepo()
        {
            Application.OpenURL("https://github.com/EitanWong/com.eitan.sherpa-onnx-unity");
        }
    }
}
#endif
