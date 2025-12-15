#if EASYMIC_SHERPA_ONNX_INTEGRATION
namespace Eitan.EasyMic.Samples.SherpaONNXUnity.ASR
{
    using UnityEngine;
    using UnityEngine.UI;
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Eitan.EasyMic.Runtime;
    using Eitan.SherpaONNXUnity.Runtime;
    using Eitan.EasyMic.Runtime.Mono.ASR;
    using Eitan.EasyMic.Runtime.Mono;



    /// <summary>
    /// Simplified example demonstrating VoiceMicrophone integration with Sherpa-ONNX Unity.
    ///
    /// VoiceMicrophone encapsulates all ASR/VAD service management, audio pipeline
    /// configuration, and recognition logic internally. This example focuses on:
    /// - Selecting and configuring microphone devices
    /// - Choosing ASR models (streaming or offline with VAD)
    /// - Displaying real-time and final transcription results
    /// - Playing back and saving recorded audio
    ///
    /// The heavy lifting (model loading, audio processing, turn detection) is handled
    /// by VoiceMicrophone, resulting in much cleaner application code.
    /// </summary>
    public class EasyMicSherpaONNXUnityASRExample : MonoBehaviour
    {
        #region UI Text Constants

        private const string STATE_NOT_LOADED = "Not Loaded";
        private const string STATE_LOADING = "Loading Models...";
        private const string STATE_LOADING_FAILED = "Loading Failed";
        private const string STATE_READY = "Ready to Record";
        private const string STATE_RECORDING = "Recording...";
        private const string STATE_LISTENING = "Listening...";
        private const string STATE_NO_SPEECH = "No speech detected.";

        private const string BTN_LOAD_MODEL = "Load Model";
        private const string BTN_UNLOAD_MODEL = "Unload Model";
        private const string BTN_LOADING = "Loading...";
        private const string BTN_START_RECORDING = "Start Recording";
        private const string BTN_STOP_RECORDING = "Stop Recording";
        private const string BTN_PLAY = "Play";
        private const string BTN_STOP = "Stop";

        private const string TXT_TRANSCRIPTION_DEFAULT = "Transcription will appear here...";
        private const string TXT_FETCHING_MODELS = "Fetching model manifest...";
        private const string TXT_NO_DEVICES = "No microphone devices found";

        #endregion

        #region Serialized Fields

        [Header("Core Components")]
        [Tooltip("VoiceMicrophone component that handles ASR/VAD services and audio processing")]
        [SerializeField] private VoiceMicrophone _voiceMicrophone;

        [Tooltip("Playback component for playing recorded audio clips")]
        [SerializeField] private PlaybackAudioSourceBehaviour _playbackSource;

        [Header("Device & Model Selection")]
        [Tooltip("Dropdown for selecting the microphone capture device")]
        [SerializeField] private Dropdown _deviceDropdown;

        [Tooltip("Dropdown for selecting the ASR (Automatic Speech Recognition) model")]
        [SerializeField] private Dropdown _asrModelDropdown;

        [Tooltip("Dropdown for selecting the VAD (Voice Activity Detection) model - only visible for offline mode")]
        [SerializeField] private Dropdown _vadModelDropdown;

        [Tooltip("Container for the VAD dropdown, used for visibility control")]
        [SerializeField] private RectTransform _vadDropdownContainer;

        [Tooltip("Toggle to enable audio loopback (hear yourself while recording)")]
        [SerializeField] private Toggle _loopbackToggle;

        [Tooltip("Button to refresh device and model lists")]
        [SerializeField] private Button _refreshButton;

        [Tooltip("Button to load or unload the selected models")]
        [SerializeField] private Button _loadUnloadButton;

        [Header("Recording Controls")]
        [Tooltip("Container for recording controls, shown only when models are loaded")]
        [SerializeField] private RectTransform _recordingContainer;

        [Tooltip("Button to start or stop recording")]
        [SerializeField] private Button _recordButton;

        [Header("Status Display")]
        [Tooltip("Visual indicator showing current state (gray=idle, yellow=loading, green=ready, red=recording)")]
        [SerializeField] private RawImage _stateIndicator;

        [Tooltip("Text displaying the current application state")]
        [SerializeField] private Text _stateText;

        [Tooltip("Text displaying transcription results")]
        [SerializeField] private Text _transcriptionText;

        [Header("Playback & Save Controls")]
        [Tooltip("Panel containing playback and save options, shown after recording completes")]
        [SerializeField] private RectTransform _resultPanel;

        [Tooltip("Text displaying the name of the recorded audio clip")]
        [SerializeField] private Text _audioClipNameText;

        [Tooltip("Button to play or stop the recorded audio")]
        [SerializeField] private Button _playStopButton;

        [Tooltip("Button to save the recorded audio to a file")]
        [SerializeField] private Button _saveButton;

        #endregion

        #region Private Fields

        /// <summary>
        /// Current application state for UI management.
        /// </summary>
        private AppState _currentState = AppState.NotLoaded;

        /// <summary>
        /// Tracks playback state to detect when playback completes.
        /// </summary>
        private bool _wasPlaying;

        /// <summary>
        /// Accumulated transcription text from streaming recognition.
        /// </summary>
        private string _currentTranscription = string.Empty;

        /// <summary>
        /// Reference to the last recorded audio clip.
        /// </summary>
        private AudioClip _recordedClip;

        /// <summary>
        /// Cached list of available microphone devices.
        /// </summary>
        private MicDevice[] _devices = Array.Empty<MicDevice>();

        /// <summary>
        /// Default streaming ASR model identifier.
        /// </summary>
        private readonly string _defaultStreamingModel = "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30";

        /// <summary>
        /// Default VAD model identifier.
        /// </summary>
        private readonly string _defaultVadModel = "ten-vad";

        #endregion

        #region Enums

        /// <summary>
        /// Application states for controlling UI behavior.
        /// </summary>
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
            ValidateRequiredComponents();
        }

        private void Start()
        {
            BindUIEvents();
            BindVoiceMicrophoneEvents();
            InitializeUI();
            RefreshDevicesAndModels();
        }

        private void Update()
        {
            UpdatePlaybackButtonState();
        }

        private void OnDestroy()
        {
            UnbindUIEvents();
            UnbindVoiceMicrophoneEvents();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Validates that all required components are assigned in the Inspector.
        /// </summary>
        private void ValidateRequiredComponents()
        {
            if (_voiceMicrophone == null)
            {
                _voiceMicrophone = GetComponent<VoiceMicrophone>();
            }

            if (_playbackSource == null)
            {
                _playbackSource = GetComponent<PlaybackAudioSourceBehaviour>();
            }

            if (_voiceMicrophone == null)
            {
                Debug.LogError("VoiceMicrophone component is required. Please assign it in the Inspector.", this);
                enabled = false;
                return;
            }

            if (_playbackSource == null)
            {
                Debug.LogError("PlaybackAudioSourceBehaviour component is required. Please assign it in the Inspector.", this);
                enabled = false;
            }
        }

        /// <summary>
        /// Sets up the initial state of all UI elements.
        /// </summary>
        private void InitializeUI()
        {
            _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
            _loadUnloadButton.GetComponentInChildren<Text>().text = BTN_LOAD_MODEL;
            _playStopButton.GetComponentInChildren<Text>().text = BTN_PLAY;
            _stateText.text = STATE_NOT_LOADED;
            _stateIndicator.color = Color.gray;
            _transcriptionText.text = TXT_TRANSCRIPTION_DEFAULT;
            _resultPanel.gameObject.SetActive(false);
            _recordingContainer.gameObject.SetActive(false);
            _loopbackToggle.isOn = false;

            UpdateUIInteractability();
        }

        /// <summary>
        /// Binds click and value change handlers to UI elements.
        /// </summary>
        private void BindUIEvents()
        {
            _recordButton.onClick.AddListener(OnRecordButtonClicked);
            _loadUnloadButton.onClick.AddListener(OnLoadUnloadButtonClicked);
            _refreshButton.onClick.AddListener(OnRefreshButtonClicked);
            _playStopButton.onClick.AddListener(OnPlayStopButtonClicked);
            _saveButton.onClick.AddListener(OnSaveButtonClicked);
            _asrModelDropdown.onValueChanged.AddListener(OnAsrModelSelectionChanged);
        }

        /// <summary>
        /// Removes all UI event listeners to prevent memory leaks.
        /// </summary>
        private void UnbindUIEvents()
        {
            _recordButton?.onClick.RemoveAllListeners();
            _loadUnloadButton?.onClick.RemoveAllListeners();
            _refreshButton?.onClick.RemoveAllListeners();
            _playStopButton?.onClick.RemoveAllListeners();
            _saveButton?.onClick.RemoveAllListeners();
            _asrModelDropdown?.onValueChanged.RemoveAllListeners();
        }

        /// <summary>
        /// Subscribes to VoiceMicrophone events for transcription and loading feedback.
        /// </summary>
        private void BindVoiceMicrophoneEvents()
        {
            if (_voiceMicrophone == null)
            {
                return;
            }

            _voiceMicrophone.OnASRTranscriptionStreaming += HandleStreamingTranscription;
            _voiceMicrophone.OnASRTranscriptionSubmit += HandleFinalTranscription;
            _voiceMicrophone.OnVoiceActivityChanged += HandleVoiceActivityChanged;
            _voiceMicrophone.OnLoadingProgressFeedback += HandleLoadingProgress;
            _voiceMicrophone.OnLoadingSucceededFeedback += HandleLoadingSucceeded;
            _voiceMicrophone.OnLoadingFailedFeedback += HandleLoadingFailed;
            _voiceMicrophone.OnRecordingStateChanged += HandleRecordingStateChanged;
        }

        /// <summary>
        /// Unsubscribes from all VoiceMicrophone events.
        /// </summary>
        private void UnbindVoiceMicrophoneEvents()
        {
            if (_voiceMicrophone == null)
            {
                return;
            }

            _voiceMicrophone.OnASRTranscriptionStreaming -= HandleStreamingTranscription;
            _voiceMicrophone.OnASRTranscriptionSubmit -= HandleFinalTranscription;
            _voiceMicrophone.OnVoiceActivityChanged -= HandleVoiceActivityChanged;
            _voiceMicrophone.OnLoadingProgressFeedback -= HandleLoadingProgress;
            _voiceMicrophone.OnLoadingSucceededFeedback -= HandleLoadingSucceeded;
            _voiceMicrophone.OnLoadingFailedFeedback -= HandleLoadingFailed;
            _voiceMicrophone.OnRecordingStateChanged -= HandleRecordingStateChanged;
        }

        #endregion

        #region Device & Model Management

        /// <summary>
        /// Refreshes both device list and model manifests from the server.
        /// </summary>
        private void RefreshDevicesAndModels()
        {
            RefreshDeviceList();
            _ = RefreshModelDropdownsAsync();
        }

        /// <summary>
        /// Refreshes the list of available microphone devices.
        /// </summary>
        private void RefreshDeviceList()
        {
            EasyMicAPI.Refresh();
            _devices = EasyMicAPI.Devices;

            _deviceDropdown.ClearOptions();

            if (_devices.Length == 0)
            {
                _deviceDropdown.AddOptions(new List<string> { TXT_NO_DEVICES });
                _deviceDropdown.interactable = false;
                Debug.LogWarning("No microphone devices detected.");
                return;
            }

            var deviceNames = _devices.Select(d => d.Name).ToList();
            _deviceDropdown.AddOptions(deviceNames);
            _deviceDropdown.interactable = true;

            // Select the default device if available
            int defaultIndex = Array.FindIndex(_devices, d => d.IsDefault);
            _deviceDropdown.value = Mathf.Max(0, defaultIndex);
            _deviceDropdown.RefreshShownValue();
        }

        /// <summary>
        /// Asynchronously fetches and populates the ASR and VAD model dropdowns.
        /// </summary>
        private async Task RefreshModelDropdownsAsync()
        {
            // Fetch ASR models
            _asrModelDropdown.ClearOptions();
            _asrModelDropdown.captionText.text = TXT_FETCHING_MODELS;

            try
            {
                var asrModels = await SherpaONNXUnityAPI.GetModelIDByTypeAsync(SherpaONNXModuleType.SpeechRecognition);
                _asrModelDropdown.AddOptions(asrModels.ToList());

                int defaultAsrIndex = _asrModelDropdown.options.FindIndex(o => o.text == _defaultStreamingModel);
                _asrModelDropdown.value = Mathf.Max(0, defaultAsrIndex);
                _asrModelDropdown.RefreshShownValue();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to fetch ASR models: {ex.Message}");
                _asrModelDropdown.AddOptions(new List<string> { "Failed to load models" });
            }

            // Fetch VAD models
            _vadModelDropdown.ClearOptions();
            _vadModelDropdown.captionText.text = TXT_FETCHING_MODELS;

            try
            {
                var vadModels = await SherpaONNXUnityAPI.GetModelIDByTypeAsync(SherpaONNXModuleType.VoiceActivityDetection);
                _vadModelDropdown.AddOptions(vadModels.ToList());

                int defaultVadIndex = _vadModelDropdown.options.FindIndex(o => o.text == _defaultVadModel);
                _vadModelDropdown.value = Mathf.Max(0, defaultVadIndex);
                _vadModelDropdown.RefreshShownValue();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to fetch VAD models: {ex.Message}");
                _vadModelDropdown.AddOptions(new List<string> { "Failed to load models" });
            }

            UpdateVadDropdownVisibility();
        }

        /// <summary>
        /// Updates the visibility of the VAD dropdown based on the selected ASR model type.
        /// VAD is only needed for offline (non-streaming) models.
        /// </summary>
        private void UpdateVadDropdownVisibility()
        {
            if (_asrModelDropdown.options.Count == 0)
            {
                return;
            }

            string selectedModel = _asrModelDropdown.options[_asrModelDropdown.value].text;
            bool isOnlineModel = SherpaONNXUnityAPI.IsOnlineModel(selectedModel);

            // VAD dropdown is only needed for offline models
            _vadModelDropdown.gameObject.SetActive(!isOnlineModel);
            _vadDropdownContainer.gameObject.SetActive(!isOnlineModel);
        }

        /// <summary>
        /// Returns the currently selected microphone device.
        /// </summary>
        private MicDevice GetSelectedDevice()
        {
            if (_devices.Length == 0 || _deviceDropdown.value >= _devices.Length)
            {
                return default;
            }

            return _devices[_deviceDropdown.value];
        }

        /// <summary>
        /// Returns the currently selected ASR model identifier.
        /// </summary>
        private string GetSelectedAsrModel()
        {
            if (_asrModelDropdown.options.Count == 0)
            {
                return string.Empty;
            }

            return _asrModelDropdown.options[_asrModelDropdown.value].text;
        }

        /// <summary>
        /// Returns the currently selected VAD model identifier.
        /// </summary>
        private string GetSelectedVadModel()
        {
            if (_vadModelDropdown.options.Count == 0)
            {
                return string.Empty;
            }

            return _vadModelDropdown.options[_vadModelDropdown.value].text;
        }

        #endregion

        #region Button Handlers

        /// <summary>
        /// Handles the refresh button click - refreshes devices and models.
        /// </summary>
        private void OnRefreshButtonClicked()
        {
            RefreshDevicesAndModels();
            Debug.Log("Device and model lists refreshed.");
        }

        /// <summary>
        /// Handles the ASR model dropdown selection change.
        /// </summary>
        private void OnAsrModelSelectionChanged(int index)
        {
            UpdateVadDropdownVisibility();
        }

        /// <summary>
        /// Handles the load/unload button click - loads or unloads ASR models.
        /// </summary>
        private void OnLoadUnloadButtonClicked()
        {
            if (_currentState == AppState.NotLoaded)
            {
                LoadModels();
            }
            else if (_currentState == AppState.Ready)
            {
                UnloadModels();
            }
        }

        /// <summary>
        /// Handles the record button click - starts or stops recording.
        /// </summary>
        private void OnRecordButtonClicked()
        {
            if (_currentState == AppState.Recording)
            {
                StopRecording();
            }
            else if (_currentState == AppState.Ready)
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Handles the play/stop button click - plays or stops audio playback.
        /// </summary>
        private void OnPlayStopButtonClicked()
        {
            if (_playbackSource.IsPlaying)
            {
                _playbackSource.Stop();
                _playStopButton.GetComponentInChildren<Text>().text = BTN_PLAY;
            }
            else if (_recordedClip != null)
            {
                _playbackSource.PlayClip(_recordedClip, loop: false);
                _playStopButton.GetComponentInChildren<Text>().text = BTN_STOP;
            }
        }

        /// <summary>
        /// Handles the save button click - saves the recorded audio to a file.
        /// </summary>
        private void OnSaveButtonClicked()
        {
            if (_recordedClip == null)
            {
                Debug.LogWarning("No audio clip available to save.");
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"Recording_{timestamp}.wav";

            if (_recordedClip.Save(fileName))
            {
                Debug.Log($"Audio saved successfully: {fileName}");
                _transcriptionText.text = $"Saved to persistent data path:\n{fileName}";
            }
            else
            {
                Debug.LogError("Failed to save audio clip.");
                _transcriptionText.text = "Failed to save audio file.";
            }
        }

        #endregion

        #region Model Loading

        /// <summary>
        /// Initiates the model loading process by configuring VoiceMicrophone.
        /// </summary>
        private void LoadModels()
        {
            string asrModel = GetSelectedAsrModel();
            if (string.IsNullOrEmpty(asrModel))
            {
                Debug.LogError("No ASR model selected.");
                return;
            }

            UpdateState(AppState.Loading);

            // Build configuration based on selected models
            var config = BuildConfiguration(asrModel);
            if (config == null)
            {
                UpdateState(AppState.NotLoaded);
                return;
            }

            // Apply configuration to VoiceMicrophone - this triggers model loading
            _voiceMicrophone.ApplyConfiguration(config);

            // Initialize with the selected device
            var device = GetSelectedDevice();
            if (device.Name != null)
            {
                _voiceMicrophone.SwitchCaptureDevice(
                    device,
                    Channel.Mono,
                    SampleRate.Hz16000,
                    restartRecording: false);
            }

            _voiceMicrophone.Init();
        }
        /// <summary>
        /// Builds an ASR configuration based on the selected models.
        /// </summary>
        private AutomaticSpeechRecognitionConfiguration BuildConfiguration(string asrModel)
        {
            bool isOnline = SherpaONNXUnityAPI.IsOnlineModel(asrModel);

            if (!isOnline && string.IsNullOrWhiteSpace(GetSelectedVadModel()))
            {
                Debug.LogError("Offline ASR requires a VAD model. Please select a VAD model first.");
                return null;
            }

            // Create the appropriate preset first
            var preset = new AutomaticSpeechRecognitionConfiguration.ASRPreset
            {
                DisplayName = "Default",
                Id = "default",
                RecognitionMode = isOnline ? RecognitionMode.Streaming : RecognitionMode.OfflineWithVad,
                StreamingModelId = isOnline ? asrModel : string.Empty,
                OfflineModelId = !isOnline ? asrModel : string.Empty,
                VadModelId = !isOnline ? GetSelectedVadModel() : string.Empty
            };

            var config = AutomaticSpeechRecognitionConfiguration.CreateDefault();
            config.RemovePreset("default");
            config.AddPreset(preset);

            return config;
        }


        /// <summary>
        /// Unloads all models and resets to the initial state.
        /// </summary>
        private void UnloadModels()
        {
            if (_voiceMicrophone.IsRecording)
            {
                _voiceMicrophone.StopRecording();
            }

            _voiceMicrophone.DisposeModels();

            _currentTranscription = string.Empty;
            UpdateState(AppState.NotLoaded);
            Debug.Log("Models unloaded successfully.");
        }

        #endregion

        #region Recording Control

        /// <summary>
        /// Starts the recording and transcription process.
        /// </summary>
        private void StartRecording()
        {
            if (_devices.Length == 0)
            {
                Debug.LogError("No microphone device available.");
                return;
            }

            // Stop any ongoing playback
            if (_playbackSource.IsPlaying)
            {
                _playbackSource.Stop();
            }

            _currentTranscription = string.Empty;
            _transcriptionText.text = STATE_LISTENING;
            _resultPanel.gameObject.SetActive(false);

            _voiceMicrophone.StartRecording();
        }

        /// <summary>
        /// Stops the recording and displays the results.
        /// </summary>
        private void StopRecording()
        {
            _voiceMicrophone.StopRecording();

            // Get the recorded audio clip
            _recordedClip = _voiceMicrophone.LatestRecordingClip;

            // Update transcription display
            _transcriptionText.text = string.IsNullOrEmpty(_currentTranscription)
                ? STATE_NO_SPEECH
                : _currentTranscription;

            // Show result panel
            _resultPanel.gameObject.SetActive(true);
            _audioClipNameText.text = _recordedClip != null
                ? _recordedClip.name
                : "No audio captured";
            _playStopButton.interactable = _recordedClip != null;
            _saveButton.interactable = _recordedClip != null;
        }

        #endregion

        #region VoiceMicrophone Event Handlers

        /// <summary>
        /// Handles streaming (partial) transcription results.
        /// </summary>
        private void HandleStreamingTranscription(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _transcriptionText.text = text;
            }
        }


        /// <summary>
        /// Handles final transcription results when an utterance is complete.
        /// </summary>
        private void HandleFinalTranscription(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _currentTranscription = text;
                _transcriptionText.text = text;
            }
        }

        /// <summary>
        /// Handles voice activity state changes.
        /// </summary>
        private void HandleVoiceActivityChanged(bool isActive)
        {
            if (_currentState == AppState.Recording)
            {
                _stateText.text = isActive ? STATE_LISTENING : STATE_RECORDING;
            }
        }

        /// <summary>
        /// Handles recording state changes from VoiceMicrophone.
        /// </summary>
        private void HandleRecordingStateChanged(bool isRecording)
        {
            if (isRecording)
            {
                UpdateState(AppState.Recording);
            }
            else if (_currentState == AppState.Recording)
            {
                UpdateState(AppState.Ready);
            }
        }

        /// <summary>
        /// Handles loading progress updates for displaying feedback.
        /// </summary>
        private void HandleLoadingProgress(string message, float progress)
        {
            _transcriptionText.text = message;
            _stateText.text = $"{STATE_LOADING} ({progress:P0})";
        }

        /// <summary>
        /// Handles successful model loading completion.
        /// </summary>
        private void HandleLoadingSucceeded(SuccessFeedback feedback)
        {
            // 移除条件检查，直接更新状态
            _transcriptionText.text = feedback?.Message ?? "Models loaded successfully.";
            UpdateState(AppState.Ready);
            Debug.Log("All models loaded successfully.");
        }

        /// <summary>
        /// Handles model loading failure.
        /// </summary>
        private void HandleLoadingFailed(FailedFeedback feedback)
        {
            UpdateState(AppState.NotLoaded);
            _stateText.text = STATE_LOADING_FAILED;
            _stateIndicator.color = Color.red;
            _transcriptionText.text = feedback?.Message ?? "Model loading failed.";
            Debug.LogError($"Model loading failed: {feedback?.Message}");
        }

        #endregion

        #region State Management

        /// <summary>
        /// Updates the application state and refreshes all UI elements accordingly.
        /// </summary>
        private void UpdateState(AppState newState)
        {
            _currentState = newState;

            switch (_currentState)
            {
                case AppState.NotLoaded:
                    _loadUnloadButton.GetComponentInChildren<Text>().text = BTN_LOAD_MODEL;
                    _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
                    _stateText.text = STATE_NOT_LOADED;
                    _stateIndicator.color = Color.gray;
                    _recordingContainer.gameObject.SetActive(false);
                    _resultPanel.gameObject.SetActive(false);
                    break;

                case AppState.Loading:
                    _loadUnloadButton.GetComponentInChildren<Text>().text = BTN_LOADING;
                    _stateText.text = STATE_LOADING;
                    _stateIndicator.color = Color.yellow;
                    _recordingContainer.gameObject.SetActive(false);
                    break;

                case AppState.Ready:
                    _loadUnloadButton.GetComponentInChildren<Text>().text = BTN_UNLOAD_MODEL;
                    _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
                    _stateText.text = STATE_READY;
                    _stateIndicator.color = Color.green;
                    _recordingContainer.gameObject.SetActive(true);
                    break;

                case AppState.Recording:
                    _recordButton.GetComponentInChildren<Text>().text = BTN_STOP_RECORDING;
                    _stateText.text = STATE_RECORDING;
                    _stateIndicator.color = Color.red;
                    _resultPanel.gameObject.SetActive(false);
                    break;
            }

            UpdateUIInteractability();
        }

        /// <summary>
        /// Updates the interactability of UI elements based on current state.
        /// </summary>
        private void UpdateUIInteractability()
        {
            bool isIdle = _currentState != AppState.Loading && _currentState != AppState.Recording;

            _loadUnloadButton.interactable = isIdle;
            _deviceDropdown.interactable = isIdle && _devices.Length > 0;
            _asrModelDropdown.interactable = isIdle;
            _loopbackToggle.interactable = isIdle;
            _refreshButton.interactable = isIdle;

            if (_vadModelDropdown.gameObject.activeSelf)
            {
                _vadModelDropdown.interactable = isIdle;
            }

            _recordButton.interactable = _currentState == AppState.Ready || _currentState == AppState.Recording;
        }

        /// <summary>
        /// Monitors playback state to update the play/stop button when playback completes.
        /// </summary>
        private void UpdatePlaybackButtonState()
        {
            if (_wasPlaying && !_playbackSource.IsPlaying)
            {
                _playStopButton.GetComponentInChildren<Text>().text = BTN_PLAY;
            }

            _wasPlaying = _playbackSource.IsPlaying;
        }

        #endregion
    }
}
#endif
