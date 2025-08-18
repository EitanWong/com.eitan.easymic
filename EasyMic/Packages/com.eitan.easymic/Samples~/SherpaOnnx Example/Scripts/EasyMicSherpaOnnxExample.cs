#if EASYMIC_SHERPA_ONNX_INTEGRATION

using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using Eitan.EasyMic.Runtime;
using Eitan.SherpaOnnxUnity.Runtime;
using System;

namespace Eitan.EasyMic.Samples
{
    /// <summary>
    /// This is a comprehensive example demonstrating the capabilities of the EasyMic and SherpaOnnx Unity packages.
    /// It covers:
    /// - Refreshing and listing available microphone devices.
    /// - Asynchronously loading and unloading speech recognition (ASR) and voice activity detection (VAD) models.
    /// - Handling model loading feedback via the ISherpaFeedbackHandler interface.
    /// - Starting and stopping recordings.
    /// - Switching between online (streaming) and offline (buffered) recognition modes.
    /// - Processing audio with loopback, VAD filters, and speech recognizers.
    /// - Displaying real-time and final transcription results.
    /// - Playing back and saving the recorded audio.
    /// - Managing UI states for a clear user experience.
    /// </summary>
    public class EasyMicSherpaOnnxExample : MonoBehaviour, ISherpaFeedbackHandler
    {
        #region UI Constants
        private const string STATE_NOT_LOADED = "Not Loaded";
        private const string STATE_LOADING = "Loading Models...";
        private const string STATE_LOADING_FAILED = "Loading Failed";
        private const string STATE_READY = "Ready to Record";
        private const string STATE_RECORDING = "Recording...";
        private const string STATE_LISTENING = "Listening...";
        private const string STATE_PREPARING = "Preparing to listen...";
        private const string STATE_NO_SPEECH = "No speech detected.";

        private const string BTN_LOAD_MODEL = "Load Model";
        private const string BTN_UNLOAD_MODEL = "Unload Model";
        private const string BTN_LOADING = "Loading...";
        private const string BTN_START_RECORDING = "Start Recording";
        private const string BTN_STOP_RECORDING = "Stop Recording";
        private const string BTN_PLAY = "Play";
        private const string BTN_STOP = "Stop";
        
        private const string TXT_TRANSCRIPTION_DEFAULT = "Transcription will appear here...";
        #endregion

        #region Inspector UI Assignments
        [Header("Required Components")]
        [Tooltip("AudioSource for playing back recorded audio clips. This is required.")]
        [SerializeField] private AudioSource _audioSource;

        [Header("Setup & Configuration")][Tooltip("Dropdown to select the microphone device.")]
        [SerializeField] private Dropdown _selectDeviceDropdown;
        [Tooltip("Dropdown to select the Automatic Speech Recognition (ASR) model.")]
        [SerializeField] private Dropdown _asrModelsDropdown;
        [Tooltip("Dropdown to select the Voice Activity Detection (VAD) model for offline recognition.")]
        [SerializeField] private Dropdown _vadModelsDropdown;
        [Tooltip("Toggle to play audio back through speakers as it is being recorded.")]
        [SerializeField] private Toggle _loopbackToggle;
        [Tooltip("Button to refresh the list of microphones and models.")]
        [SerializeField] private Button _refreshButton;
        [Tooltip("Button to load or unload the selected models.")]
        [SerializeField] private Button _loadOrUnloadButton;
        [Tooltip("The parent object for the VAD dropdown, used for layout control.")]
        [SerializeField] private RectTransform _vadDropdownLayoutContainer;

        [Header("Recording Controls")][Tooltip("Button to start or stop the recording process.")]
        [SerializeField] private Button _recordButton;
        [Tooltip("Parent container for the record button.")]
        [SerializeField] private RectTransform _recordingButtonContainer;

        [Header("Status Display")][Tooltip("Image to visually indicate the current status (e.g., gray for idle, green for ready, red for recording).")]
        [SerializeField] private RawImage _stateImage;
        [Tooltip("Text to display the current status message.")]
        [SerializeField] private Text _stateText;
        [Tooltip("Text to display the real-time or final transcription result.")]
        [SerializeField] private Text _transcriptionText;

        [Header("Playback & Save Result")][Tooltip("The panel containing playback and save controls, visible after a recording is complete.")]
        [SerializeField] private RectTransform _resultPanel;
        [Tooltip("Text to display the name of the recorded audio clip.")]
        [SerializeField] private Text _audioNameText;
        [Tooltip("Button to play or stop the recorded audio clip.")]
        [SerializeField] private Button _playOrStopButton;
        [Tooltip("Button to save the recorded audio clip to a file.")]
        [SerializeField] private Button _saveButton;
        #endregion

        #region Private Fields
        private RecordingHandle _handle;
        private AudioWorkerBlueprint _bpRealtime;
        private AudioWorkerBlueprint _bpOffline;
        private AudioWorkerBlueprint _bpVoiceFilter;
        private AudioWorkerBlueprint _bpLoopback;
        private AudioWorkerBlueprint _bpCapture;
        private AudioClip _audioClip;

        private AppState _currentState = AppState.NotLoaded;
        private bool _wasPlayingAudio = false;
        private int _modelsToLoad = 0;
        private int _modelsLoaded = 0;
        private string _currentTranscription = "";

        private SpeechRecognition _asrService;
        private VoiceActivityDetection _vadService;
        
        private readonly string _defaultVadModelName = "ten-vad";
        private readonly string _defaultAsrModelName = "sherpa-onnx-streaming-zipformer-zh-int8-2025-06-30";
        private const int SAMPLERATE = 16000;
        private const int CHANNEL = 1;
        private const int MAX_CAPTURE_DURATION_SECONDS = 30;
        #endregion

        private enum AppState { NotLoaded, Loading, Ready, Recording }

        #region MonoBehaviour Lifecycle
        private void Start()
        {
            if (_audioSource == null)
            {
                Debug.LogError("AudioSource is not assigned in the Inspector. Please drag an AudioSource component to the appropriate field. Disabling script.", this);
                enabled = false;
                return;
            }

            BindUIEvents();
            SetInitialUIState();
            
            SherpaOnnxUnityAPI.SetGithubProxy("https://gh-proxy.com/");
            OnRefreshButtonPressed();
        }

        private void Update()
        {
            if (_audioSource != null)
            {
                if (_wasPlayingAudio && !_audioSource.isPlaying)
                {
                    _playOrStopButton.GetComponentInChildren<Text>().text = BTN_PLAY;
                }
                _wasPlayingAudio = _audioSource.isPlaying;
            }
        }

        private void OnDestroy()
        {
            EasyMicAPI.StopAllRecordings();
            UnloadModels(); 
            UnbindUIEvents();
        }
        #endregion

        #region UI Initialization and Event Binding
        private void SetInitialUIState()
        {
            _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
            _loadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_LOAD_MODEL;
            _stateText.text = STATE_NOT_LOADED;
            _stateImage.color = Color.gray;
            _resultPanel.gameObject.SetActive(false);
            _recordingButtonContainer.gameObject.SetActive(false);
            _loopbackToggle.isOn = false;
            _transcriptionText.text = TXT_TRANSCRIPTION_DEFAULT;
            SetUIInteractability();
        }

        private void BindUIEvents()
        {
            _recordButton.onClick.AddListener(OnRecordButtonPressed);
            _loadOrUnloadButton.onClick.AddListener(OnLoadOrUnloadButtonPressed);
            _refreshButton.onClick.AddListener(OnRefreshButtonPressed);
            _playOrStopButton.onClick.AddListener(OnPlayOrStopButtonPressed);
            _saveButton.onClick.AddListener(OnSaveButtonPressed);
            _asrModelsDropdown.onValueChanged.AddListener(OnAsrModelChanged);
        }

        private void UnbindUIEvents()
        {
            if (_saveButton) _saveButton.onClick.RemoveAllListeners();
            if (_playOrStopButton) _playOrStopButton.onClick.RemoveAllListeners();
            if (_recordButton) _recordButton.onClick.RemoveAllListeners();
            if (_loadOrUnloadButton) _loadOrUnloadButton.onClick.RemoveAllListeners();
            if (_refreshButton) _refreshButton.onClick.RemoveAllListeners();
            if (_asrModelsDropdown) _asrModelsDropdown.onValueChanged.RemoveAllListeners();
        }

        private void SetUIInteractability()
        {
            bool isIdle = _currentState != AppState.Loading && _currentState != AppState.Recording;
            _loadOrUnloadButton.interactable = isIdle;
            _selectDeviceDropdown.interactable = isIdle;
            _asrModelsDropdown.interactable = isIdle;
            _loopbackToggle.interactable = isIdle;
            _refreshButton.interactable = isIdle;

            _recordButton.interactable = _currentState == AppState.Ready || _currentState == AppState.Recording;

            UpdateVadDropdownState();
            if (_vadModelsDropdown.gameObject.activeSelf)
            {
                _vadModelsDropdown.interactable = isIdle;
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

            _asrModelsDropdown.ClearOptions();
            _asrModelsDropdown.AddOptions(SherpaOnnxUnityAPI.GetModelIDByType(SherpaOnnxModuleType.SpeechRecognition).ToList());
            _asrModelsDropdown.value = _asrModelsDropdown.options.FindIndex(m => m.text == _defaultAsrModelName);

            _vadModelsDropdown.ClearOptions();
            _vadModelsDropdown.AddOptions(SherpaOnnxUnityAPI.GetModelIDByType(SherpaOnnxModuleType.VoiceActivityDetection).ToList());
            _vadModelsDropdown.value = _vadModelsDropdown.options.FindIndex(m => m.text == _defaultVadModelName);

            UpdateVadDropdownState();
        }

        private void OnAsrModelChanged(int index) => UpdateVadDropdownState();

        private void UpdateVadDropdownState()
        {
            if (_asrModelsDropdown.options.Count > 0)
            {
                var selectedAsrModel = _asrModelsDropdown.options[_asrModelsDropdown.value].text;
                bool isOnlineModel = SherpaOnnxUnityAPI.IsOnlineModel(selectedAsrModel);
                _vadModelsDropdown.gameObject.SetActive(!isOnlineModel);
                _vadDropdownLayoutContainer.gameObject.SetActive(!isOnlineModel);
            }
        }

        private void OnLoadOrUnloadButtonPressed()
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

        private void OnPlayOrStopButtonPressed()
        {
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
                _playOrStopButton.GetComponentInChildren<Text>().text = BTN_PLAY;
            }
            else if (_audioClip != null)
            {
                _audioSource.clip = _audioClip;
                _audioSource.loop = false;
                _audioSource.Play();
                _playOrStopButton.GetComponentInChildren<Text>().text = BTN_STOP;
            }
        }

        private void OnSaveButtonPressed()
        {
            if (_audioClip != null)
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"Recording_{timestamp}.wav";
                // The Save extension method will handle the full path construction.
                if (_audioClip.Save(fileName))
                {
                    Debug.Log($"Successfully saved audio to {fileName}");
                    _transcriptionText.text = $"Saved to persistent data path.";
                }
                else
                {
                     Debug.LogError($"Failed to save audio clip.");
                    _transcriptionText.text = "Failed to save audio.";
                }
            }
        }
        #endregion

        #region Core Logic - Recording Flow

        private void StartRecordingFlow()
        {
            if (EasyMicAPI.Devices.Length == 0)
            {
                Debug.LogError("No microphone devices available to start recording.");
                return;
            }

            if (_audioSource.isPlaying) _audioSource.Stop();

            _currentTranscription = "";
            _transcriptionText.text = STATE_PREPARING;

            var selectedDeviceName = _selectDeviceDropdown.options[_selectDeviceDropdown.value].text;
            _handle = EasyMicAPI.StartRecording(selectedDeviceName, (SampleRate)SAMPLERATE, (Channel)CHANNEL);

            if (!_handle.IsValid)
            {
                Debug.LogError("Failed to start recording. Please check device compatibility.");
                return;
            }

            AddProcessorsToHandle();

            _transcriptionText.text = STATE_LISTENING;
            UpdateState(AppState.Recording);
        }

        private void StopRecordingFlow()
        {
            var cap = EasyMicAPI.GetProcessor<AudioCapturer>(_handle, _bpCapture);
            _audioClip = cap != null ? cap.GetCapturedAudioClip() : null;
            EasyMicAPI.StopRecording(_handle);
            _handle = default;

            _transcriptionText.text = string.IsNullOrEmpty(_currentTranscription) ? STATE_NO_SPEECH : _currentTranscription;

            UpdateState(AppState.Ready);
            _resultPanel.gameObject.SetActive(true);
            _audioNameText.text = _audioClip.name;
        }

        private void AddProcessorsToHandle()
        {
            var selectedAsrModel = _asrModelsDropdown.options[_asrModelsDropdown.value].text;
            bool isOnlineModel = SherpaOnnxUnityAPI.IsOnlineModel(selectedAsrModel);

            if (isOnlineModel)
            {
                if (_asrService != null)
                {
                    _bpRealtime ??= new AudioWorkerBlueprint(() => {
                        var r = new Eitan.EasyMic.Runtime.SherpaOnnxUnity.SherpaRealtimeSpeechRecognizer(_asrService);
                        r.OnRecognitionResult += OnTranscriptionUpdate;
                        return r;
                    }, key: "asr-realtime");
                    EasyMicAPI.AddProcessor(_handle, _bpRealtime);
                }
            }
            else
            {
                if (_vadService != null)
                {
                    _bpVoiceFilter ??= new AudioWorkerBlueprint(() => new Eitan.EasyMic.Runtime.SherpaOnnxUnity.SherpaVoiceFilter(_vadService), key: "vad-filter");
                    EasyMicAPI.AddProcessor(_handle, _bpVoiceFilter);
                }
                if (_asrService != null)
                {
                    _bpOffline ??= new AudioWorkerBlueprint(() => {
                        var r = new Eitan.EasyMic.Runtime.SherpaOnnxUnity.SherpaOfflineSpeechRecognizer(_asrService);
                        r.OnRecognitionResult += OnTranscriptionUpdate;
                        return r;
                    }, key: "asr-offline");
                    EasyMicAPI.AddProcessor(_handle, _bpOffline);
                }
            }

            if (_loopbackToggle.isOn)
            {
                _bpLoopback ??= new AudioWorkerBlueprint(() => new LoopbackPlayer(), key: "loopback");
                EasyMicAPI.AddProcessor(_handle, _bpLoopback);
            }

            _bpCapture ??= new AudioWorkerBlueprint(() => new AudioCapturer(MAX_CAPTURE_DURATION_SECONDS), key: "capture");
            EasyMicAPI.AddProcessor(_handle, _bpCapture);
        }

        #endregion

        #region Core Logic - Model Loading

        private void StartLoadingModels()
        {
            UpdateState(AppState.Loading);

            var asrModelName = _asrModelsDropdown.options[_asrModelsDropdown.value].text;
            bool isOnlineModel = SherpaOnnxUnityAPI.IsOnlineModel(asrModelName);

            _modelsToLoad = isOnlineModel ? 1 : 2; // Online: ASR only. Offline: ASR + VAD.
            _modelsLoaded = 0;

            var reporter = new SherpaOnnxFeedbackReporter(null, this);

            _asrService = new SpeechRecognition(asrModelName, SAMPLERATE, reporter);

            if (!isOnlineModel)
            {
                var vadModelName = _vadModelsDropdown.options[_vadModelsDropdown.value].text;
                _vadService = new VoiceActivityDetection(vadModelName, SAMPLERATE, reporter);
            }
        }

        private void UnloadModels()
        {
            if (_handle.IsValid) EasyMicAPI.StopRecording(_handle);
            _handle = default;

            _asrService?.Dispose();
            _asrService = null;
            _vadService?.Dispose();
            _vadService = null;

            _currentTranscription = "";
            UpdateState(AppState.NotLoaded);
            Debug.Log("Models unloaded successfully!");
        }

        private void OnAllModelsLoaded()
        {
            UpdateState(AppState.Ready);
            Debug.Log("All models loaded successfully!");
        }

        private void OnModelLoadingFailed()
        {
            UpdateState(AppState.NotLoaded);
            _stateText.text = STATE_LOADING_FAILED;
            _stateImage.color = Color.red;
        }
        
        private void OnTranscriptionUpdate(string transcription)
        {
            if (!string.IsNullOrEmpty(transcription))
            {
                _currentTranscription = transcription;
                _transcriptionText.text = $"{transcription}";
            }
        }

        #endregion

        #region State Management
        private void UpdateState(AppState newState)
        {
            _currentState = newState;

            switch (_currentState)
            {
                case AppState.NotLoaded:
                    _loadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_LOAD_MODEL;
                    _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
                    _stateText.text = STATE_NOT_LOADED;
                    _stateImage.color = Color.gray;
                    _recordingButtonContainer.gameObject.SetActive(false);
                    _resultPanel.gameObject.SetActive(false);
                    break;
                case AppState.Loading:
                    _loadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_LOADING;
                    _stateText.text = STATE_LOADING;
                    _stateImage.color = Color.yellow;
                    break;
                case AppState.Ready:
                    _loadOrUnloadButton.GetComponentInChildren<Text>().text = BTN_UNLOAD_MODEL;
                    _recordButton.GetComponentInChildren<Text>().text = BTN_START_RECORDING;
                    _stateText.text = STATE_READY;
                    _stateImage.color = Color.green;
                    _recordingButtonContainer.gameObject.SetActive(true);
                    break;
                case AppState.Recording:
                    _recordButton.GetComponentInChildren<Text>().text = BTN_STOP_RECORDING;
                    _stateText.text = STATE_RECORDING;
                    _stateImage.color = Color.red;
                    _resultPanel.gameObject.SetActive(false);
                    break;
            }
            SetUIInteractability();
        }
        #endregion

        #region ISherpaFeedbackHandler Implementation
        public void OnFeedback(PrepareFeedback feedback) => _transcriptionText.text = feedback.Message;
        public void OnFeedback(DownloadFeedback feedback) => _transcriptionText.text = feedback.Message;
        public void OnFeedback(UncompressFeedback feedback) => _transcriptionText.text = feedback.Message;
        public void OnFeedback(VerifyFeedback feedback) => _transcriptionText.text = feedback.Message;
        public void OnFeedback(CancelFeedback feedback) => _transcriptionText.text = feedback.Message;
        public void OnFeedback(CleanFeedback feedback) => _transcriptionText.text = feedback.Message;

        public void OnFeedback(LoadFeedback feedback)
        {
            _stateText.text = $"Loading: {feedback.Message}";
        }

        public void OnFeedback(SuccessFeedback feedback)
        {
            _transcriptionText.text = feedback.Message;
            _modelsLoaded++;
            _stateText.text = $"Loading Models... ({_modelsLoaded}/{_modelsToLoad})";

            if (_modelsLoaded >= _modelsToLoad)
            {
                OnAllModelsLoaded();
            }
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            _transcriptionText.text = feedback.Message;
            Debug.LogError($"Model loading failed: {feedback.Message}");
            OnModelLoadingFailed();
        }
        #endregion
    }
}
#endif