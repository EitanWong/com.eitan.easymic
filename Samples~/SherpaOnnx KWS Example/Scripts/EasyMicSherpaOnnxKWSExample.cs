#if EASYMIC_SHERPA_ONNX_INTEGRATION
namespace Eitan.SherpaOnnxUnity.Samples.KWS
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Eitan.EasyMic;

    using Eitan.EasyMic.Runtime;
    using Eitan.EasyMic.Runtime.SherpaOnnxUnity;
    using Eitan.SherpaOnnxUnity.Runtime;
    using UnityEngine;
    using UnityEngine.UI;

    public class EasyMicSherpaOnnxKWSExample : MonoBehaviour, ISherpaFeedbackHandler
    {
        #region UI Constants
        private const string STATE_NOT_LOADED = "Not Loaded";
        private const string STATE_LOADING = "Loading Models...";
        private const string STATE_LOADING_FAILED = "Loading Failed";
        private const string STATE_READY = "Ready to Record";
        private const string STATE_RECORDING = "Recording...";
        private const string STATE_LISTENING = "Listening...";

        private const string BTN_LOAD_MODEL = "Load Model";
        private const string BTN_UNLOAD_MODEL = "Unload Model";
        private const string BTN_LOADING = "Loading...";
        private const string BTN_START_RECORDING = "Start Recording";
        private const string BTN_STOP_RECORDING = "Stop Recording";
        #endregion

        #region Inspector UI Assignments
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
        private KeywordSpotting _keywordSpottingService;
        private RecordingHandle _handle;
        private AudioWorkerBlueprint _keywordDetectorBlueprint;

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
        private static readonly string[] InterestingFeedback = {
            "Double Kill!", "Triple Kill!", "Rampage!", "Godlike!", "Beyond Godlike!"
        };

        private readonly int SampleRate = 16000;
        private enum AppState { NotLoaded, Loading, Ready, Recording }
        #endregion

        #region MonoBehaviour Lifecycle
        private void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 30;

            BindUIEvents();

            _keywordText.text = "Please click the button to load the keyword spotting model";
            _tipsText.text = string.Empty;
            _originLoadBtnColor = _modelLoadOrUnloadButton.GetComponent<Image>().color;
            if (_keywordText != null)
            {
                _originalFontSize = _keywordText.fontSize;
            }

            InitDropdown();
            InitKeywordsPanelUI();
            UpdateState(AppState.NotLoaded);
            OnRefreshButtonPressed();
        }

        private void OnDestroy()
        {
            if (_handle.IsValid)
            {
                EasyMicAPI.StopRecording(_handle);
            }
            UnloadModels();
            UnbindUIEvents();

            if (_resetCoroutine != null)
            {
                StopCoroutine(_resetCoroutine);
                _resetCoroutine = null;
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
            _modelLoadOrUnloadButton.onClick.RemoveAllListeners();
            _recordButton.onClick.RemoveAllListeners();
            _registerKeywordButton.onClick.RemoveAllListeners();
            _clearKeywordsBtn.onClick.RemoveAllListeners();
        }

        private void InitDropdown()
        {
            var manifest = SherpaOnnxModelRegistry.Instance.GetManifest();
            _modelIDDropdown.options.Clear();
            if (manifest.models != null)
            {
                List<Dropdown.OptionData> modelOptions = manifest.Filter(m => m.moduleType == SherpaOnnxModuleType.KeywordSpotting)
                                                               .Select(m => new Dropdown.OptionData(m.modelId)).ToList();
                _modelIDDropdown.AddOptions(modelOptions);

                var defaultIndex = modelOptions.FindIndex(m => m.text == defaultModelID);
                _modelIDDropdown.value = defaultIndex;
            }
        }
        #endregion

        #region Core Logic - Button Handlers
        private void OnRefreshButtonPressed()
        {
            EasyMicAPI.Refresh();
            var devices = EasyMicAPI.Devices.ToList();
            _selectDeviceDropdown.ClearOptions();
            if (devices.Any())
            {
                _selectDeviceDropdown.AddOptions(devices.Select(x => x.Name).ToList());
                int defaultIndex = devices.FindIndex(m => m.IsDefault);
                _selectDeviceDropdown.value = Mathf.Max(0, defaultIndex);
            }
            else
            {
                Debug.LogWarning("No microphone devices found.");
            }
        }

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
            if (_keywordSpottingService != null)
            {
                Debug.LogError("A model is already loaded. Please unload it first.");
                return;
            }
            UpdateState(AppState.Loading);
            var reporter = new SherpaOnnxFeedbackReporter(null, this);
            _keywordSpottingService = new KeywordSpotting(_modelIDDropdown.captionText.text, SampleRate, 2.0f, 0.25f, kwsKeywords, reporter);
        }

        private void UnloadModels()
        {
            if (_keywordSpottingService == null)
            {
                return;
            }


            if (_handle.IsValid)
            {
                StopRecordingFlow();
            }
            _keywordSpottingService.Dispose();
            _keywordSpottingService = null;

            if (_resetCoroutine != null)
            {
                StopCoroutine(_resetCoroutine);
                _resetCoroutine = null;
            }
            UpdateState(AppState.NotLoaded);
        }

        private void StartRecordingFlow()
        {
            if (EasyMicAPI.Devices.Length == 0)
            {
                Debug.LogError("No microphone devices available to start recording.");
                return;
            }

            var selectedDeviceName = _selectDeviceDropdown.options[_selectDeviceDropdown.value].text;
            _handle = EasyMicAPI.StartRecording(selectedDeviceName, (SampleRate)SampleRate, Channel.Mono);

            if (!_handle.IsValid)
            {
                Debug.LogError("Failed to start recording. Please check device compatibility.");
                return;
            }

            AddProcessorsToHandle();
            UpdateState(AppState.Recording);
        }

        private void StopRecordingFlow()
        {
            EasyMicAPI.StopRecording(_handle);
            _handle = default;
            UpdateState(AppState.Ready);
        }

        private void AddProcessorsToHandle()
        {
            if (_keywordSpottingService == null)
            {
                return;
            }

#if EASYMIC_APM_INTEGRATION
            var bpApm = new AudioWorkerBlueprint(() => new EasyMic.Runtime.Apm.WebRtcApmModifier(), key: "webrtc-apm");
            EasyMicAPI.AddProcessor(_handle, bpApm);
#endif

            var bpDownmixer = new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmixer");
            EasyMicAPI.AddProcessor(_handle, bpDownmixer);


            _keywordDetectorBlueprint ??= new AudioWorkerBlueprint(() =>
            {
                var detector = new SherpaKeywordDetector(_keywordSpottingService);
                detector.OnKeywordDetected += HandleKeywordDetected;
                return detector;
            }, key: "kws-detector");
            EasyMicAPI.AddProcessor(_handle, _keywordDetectorBlueprint);
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

                    _runtimeKeywords.Add(new KeywordSpotting.KeywordRegistration(registration.Keyword.Trim(), registration.BoostingScore, registration.TriggerThreshold));
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

            if (_keywordInput != null)
            {
                _keywordInput.text = string.Empty;
            }

            if (_keywordSpottingService != null && _tipsText != null)
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

            if (_keywordSpottingService != null && _tipsText != null)
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
            _keywordInput.interactable = canEditKeywords;
            _registerKeywordButton.interactable = canEditKeywords;
            _clearKeywordsBtn.interactable = canEditKeywords;
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

            if (wakeupSoundClip)
            {
                AudioSource.PlayClipAtPoint(wakeupSoundClip, Camera.main.transform.position);
            }
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

        #region ISherpaFeedbackHandler Implementation
        public void OnFeedback(PrepareFeedback feedback)
        {
            _tipsText.text = $"<b>[Loading]:</b> {feedback.Metadata.modelId} is preparing.";
            _keywordText.text = "Preparing model...";
        }

        public void OnFeedback(DownloadFeedback feedback)
        {
            _keywordText.text = "Downloading model...";
        }

        public void OnFeedback(UncompressFeedback feedback)
        {
            _keywordText.text = "Uncompressing model...";
        }

        public void OnFeedback(VerifyFeedback feedback)
        {
            _keywordText.text = "Verifying model...";
        }

        public void OnFeedback(LoadFeedback feedback)
        {
            _tipsText.text = $"<b><color=cyan>[Loading]</color>:</b> {feedback.Metadata.modelId} is loading into memory.";
            _keywordText.text = "Loading model into memory...";
        }

        public void OnFeedback(CancelFeedback feedback)
        {
            _tipsText.text = $"<b><color=yellow>Cancelled</color>:</b> {feedback.Message}";
            _keywordText.text = "Model loading cancelled.";
            UnloadModels();
        }

        public void OnFeedback(SuccessFeedback feedback)
        {
            _loadedMessage = $"<b><color=green>[Loaded]</color>:</b> {feedback.Metadata.modelId} is active. Ready to detect keywords.";
            _tipsText.text = _loadedMessage;
            _keywordText.text = "Now you can click the record button and speak the keyword to test.";
            UpdateState(AppState.Ready);
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            Debug.LogError($"[Failed] : {feedback.Message}");
            _tipsText.text = "<b><color=red>[Failed]</color>:</b> Model loading failed.";
            _keywordText.text = "<color=red><b>Model loading failed</b></color>";
            UnloadModels();
            UpdateState(AppState.NotLoaded);
            _stateText.text = STATE_LOADING_FAILED;
            _stateImage.color = Color.red;
        }

        public void OnFeedback(CleanFeedback feedback)
        {
            _tipsText.text = "Cleanup complete.";
            _keywordText.text = "<color=yellow><b>Initialization cancelled or failed.</b></color>";
        }
        #endregion

        public void OpenGithubRepo()
        {
            Application.OpenURL("https://github.com/EitanWong/com.eitan.sherpa-onnx-unity");
        }
    }
}
#endif
