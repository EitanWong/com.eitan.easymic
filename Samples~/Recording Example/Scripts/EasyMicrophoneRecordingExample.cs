using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Mono.Components;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Samples.Recording
{
    /// <summary>
    /// Example script demonstrating the usage of EasyMicrophone for audio recording.
    /// /// Provides a complete UI workflow for device selection, recording, playback, and saving.
    /// </summary>
    [AddComponentMenu("Examples/EasyMic/Recording/Recording Example")]
    public class EasyMicrophoneRecordingExample : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Core Components")]
        [Tooltip("Reference to the EasyMicrophone component for recording operations")]
        [SerializeField] private EasyMicrophone _easyMic;

        [Tooltip("Audio source component for playback functionality")]
        [SerializeField] private PlaybackAudioSourceBehaviour _audioSource;

        [Header("Recording Options UI")]
        [Tooltip("Dropdown for selecting the recording device")]
        [SerializeField] private Dropdown _deviceDropdown;

        [Tooltip("Dropdown for selecting the sample rate")]
        [SerializeField] private Dropdown _sampleRateDropdown;

        [Tooltip("Dropdown for selecting the audio channel configuration")]
        [SerializeField] private Dropdown _channelDropdown;

        [Tooltip("Button to manually refresh the device list")]
        [SerializeField] private Button _refreshButton;

        [Header("Recording Controls UI")]
        [Tooltip("Visual indicator for recording state (changes color based on state)")]
        [SerializeField] private RawImage _recordingStateImage;

        [Tooltip("Text label displaying the current recording state")]
        [SerializeField] private Text _recordingStateText;

        [Tooltip("Button to start/stop recording")]
        [SerializeField] private Button _recordButton;

        [Header("Playback & Save UI")]
        [Tooltip("Panel containing playback and save controls (shown after recording)")]
        [SerializeField] private RectTransform _resultPanel;

        [Tooltip("Text displaying audio clip information")]
        [SerializeField] private Text _audioInfoText;

        [Tooltip("Button to play/stop the recorded audio")]
        [SerializeField] private Button _playStopButton;

        [Tooltip("Button to save the recorded audio to file")]
        [SerializeField] private Button _saveButton;

        #endregion

        #region Private Fields

        /// <summary>
        /// Cached array of available microphone devices.
        /// </summary>
        private MicDevice[] _devices = Array.Empty<MicDevice>();

        /// <summary>
        /// Cached list of supported sample rates for the selected device.
        /// </summary>
        private readonly List<SampleRate> _sampleRates = new List<SampleRate>();

        /// <summary>
        /// Cached list of supported audio channels for the selected device.
        /// </summary>
        private readonly List<Channel> _channels = new List<Channel>();

        /// <summary>
        /// Flag to track the previous playback state for detecting playback completion.
        /// </summary>
        private bool _wasPlaying;

        #endregion

        #region Unity Lifecycle Methods

        /// <summary>
        /// Called when the script instance is being loaded.
        /// Initializes component references if not already assigned.
        /// </summary>
        private void Awake()
        {
            // Auto-assign components if not set in inspector
            if (_easyMic == null)
            {
                _easyMic = GetComponent<EasyMicrophone>();
            }

            if (_audioSource == null)
            {
                _audioSource = GetComponent<PlaybackAudioSourceBehaviour>();
            }
        }

        /// <summary>
        /// Called on the frame when the script is enabled.
        /// Sets up event listeners and initializes the microphone system.
        /// </summary>
        private void Start()
        {
            // Bind UI button click events
            _recordButton.onClick.AddListener(OnRecordButtonClicked);
            _refreshButton.onClick.AddListener(OnRefreshButtonClicked);
            _playStopButton.onClick.AddListener(OnPlayStopButtonClicked);
            _saveButton.onClick.AddListener(OnSaveButtonClicked);
            _deviceDropdown.onValueChanged.AddListener(OnDeviceSelectionChanged);

            // Subscribe to EasyMicrophone events
            _easyMic.OnRecordingStateChanged += OnRecordingStateChanged;
            _easyMic.OnMicrophoneInitialized += OnMicrophoneInitialized;

            // Subscribe to device hot-plug events and enable auto-refresh
            EasyMicAPI.DevicesChanged += OnDevicesChanged;
            EasyMicAPI.EnableDeviceAutoRefresh();

            // Set initial UI state
            InitializeUI();

            // Initialize the microphone system
            _easyMic.Init();
        }

        /// <summary>
        /// Called every frame.
        /// Monitors playback state to update UI when playback completes.
        /// </summary>
        private void Update()
        {
            // Detect when playback ends and update button text accordingly
            if (_wasPlaying && !_audioSource.IsPlaying)
            {
                _playStopButton.GetComponentInChildren<Text>().text = "Play";
            }

            _wasPlaying = _audioSource.IsPlaying;
        }

        /// <summary>
        /// Called when the MonoBehaviour will be destroyed.
        /// Cleans up event subscriptions to prevent memory leaks.
        /// </summary>
        private void OnDestroy()
        {
            // Unsubscribe from EasyMicrophone events
            _easyMic.OnRecordingStateChanged -= OnRecordingStateChanged;
            _easyMic.OnMicrophoneInitialized -= OnMicrophoneInitialized;
            EasyMicAPI.DevicesChanged -= OnDevicesChanged;

            // Remove UI event listeners
            _recordButton.onClick.RemoveAllListeners();
            _refreshButton.onClick.RemoveAllListeners();
            _playStopButton.onClick.RemoveAllListeners();
            _saveButton.onClick.RemoveAllListeners();
            _deviceDropdown.onValueChanged.RemoveAllListeners();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Sets up the initial state of all UI elements.
        /// </summary>
        private void InitializeUI()
        {
            _resultPanel.gameObject.SetActive(false);
            _recordingStateImage.color = Color.gray;
            _recordingStateText.text = "Not Recording";
            _recordButton.GetComponentInChildren<Text>().text = "Start Recording";
            _playStopButton.GetComponentInChildren<Text>().text = "Play";
        }

        /// <summary>
        /// Callback invoked when the microphone system initialization completes.
        /// </summary>
        /// <param name="success">True if initialization succeeded, false otherwise.</param>
        private void OnMicrophoneInitialized(bool success)
        {
            if (success)
            {
                RefreshDeviceList();
                Debug.Log("EasyMicrophone initialized successfully");
            }
            else
            {
                Debug.LogWarning("EasyMicrophone initialization failed");
            }
        }

        #endregion

        #region Device Management

        /// <summary>
        /// Refreshes the list of available microphone devices and updates the UI.
        /// Attempts to preserve the previously selected device if still available.
        /// </summary>
        private void RefreshDeviceList()
        {
            // Cache the currently selected device name for restoration
            string previousDevice = _devices.Length > 0 && _deviceDropdown.value < _devices.Length
                ? _devices[_deviceDropdown.value].Name
                : null;

            // Fetch the latest device list
            _devices = _easyMic.AvailableDevices;

            _deviceDropdown.ClearOptions();

            // Handle case when no devices are available
            if (_devices.Length == 0)
            {
                _deviceDropdown.AddOptions(new List<string> { "No Device Available" });
                _deviceDropdown.interactable = false;
                _recordButton.interactable = false;
                return;
            }

            // Populate the device dropdown with device names
            var deviceNames = _devices
                .Select(d => string.IsNullOrEmpty(d.Name) ? "Unnamed Device" : d.Name)
                .ToList();
            _deviceDropdown.AddOptions(deviceNames);
            _deviceDropdown.interactable = true;
            _recordButton.interactable = true;

            // Attempt to restore the previous selection
            int targetIndex = Array.FindIndex(_devices, d => d.IsDefault);
            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            if (!string.IsNullOrEmpty(previousDevice))
            {
                int previousIndex = Array.FindIndex(_devices, d => d.Name == previousDevice);
                if (previousIndex >= 0)
                {
                    targetIndex = previousIndex;
                }
            }

            _deviceDropdown.value = targetIndex;
            _deviceDropdown.RefreshShownValue();

            // Update sample rate and channel options for the selected device
            UpdateDeviceOptions(targetIndex);
        }

        /// <summary>
        /// Callback invoked when the user selects a different device from the dropdown.
        /// </summary>
        /// <param name="index">The index of the newly selected device.</param>
        private void OnDeviceSelectionChanged(int index)
        {
            UpdateDeviceOptions(index);
        }

        /// <summary>
        /// Updates the sample rate and channel dropdowns based on the selected device's capabilities.
        /// </summary>
        /// <param name="deviceIndex">The index of the selected device.</param>
        private void UpdateDeviceOptions(int deviceIndex)
        {
            if (_devices.Length == 0 || deviceIndex >= _devices.Length)
            {
                return;
            }

            var device = _devices[deviceIndex];

            // Update available sample rates for this device
            UpdateSampleRateDropdown(device);

            // Update available channels for this device
            UpdateChannelDropdown(device);
        }

        /// <summary>
        /// Populates the sample rate dropdown with rates supported by the specified device.
        /// </summary>
        /// <param name="device">The microphone device to query for supported sample rates.</param>
        private void UpdateSampleRateDropdown(MicDevice device)
        {
            _sampleRateDropdown.ClearOptions();
            _sampleRates.Clear();

            // Get supported sample rates, fallback to all rates if none specified
            var supported = device.GetSupportedSampleRateEnums();
            if (supported.Length == 0)
            {
                supported = (SampleRate[])Enum.GetValues(typeof(SampleRate));
            }

            _sampleRates.AddRange(supported);

            // Format sample rates as human-readable labels (e.g., "16 kHz")
            var labels = supported.Select(r => $"{(int)r / 1000} kHz").ToList();
            _sampleRateDropdown.AddOptions(labels);

            // Select the preferred sample rate based on device recommendation
            var preferred = device.GetPreferredSampleRate(_easyMic.DeviceOpts.SampleRate);
            int preferredIndex = _sampleRates.IndexOf(preferred);
            if (preferredIndex < 0)
            {
                preferredIndex = 0;
            }

            _sampleRateDropdown.value = preferredIndex;
            _sampleRateDropdown.RefreshShownValue();
        }

        /// <summary>
        /// Populates the channel dropdown with configurations supported by the specified device.
        /// </summary>
        /// <param name="device">The microphone device to query for supported channels.</param>
        private void UpdateChannelDropdown(MicDevice device)
        {
            _channelDropdown.ClearOptions();
            _channels.Clear();

            // Get supported channels, fallback to all channels if none specified
            var supported = device.GetSupportedChannels();
            if (supported.Length == 0)
            {
                supported = (Channel[])Enum.GetValues(typeof(Channel));
            }

            _channels.AddRange(supported);
            _channelDropdown.AddOptions(supported.Select(c => c.ToString()).ToList());

            // Select the preferred channel configuration based on device recommendation
            var preferred = device.GetPreferredChannel(_easyMic.DeviceOpts.Channel);
            int preferredIndex = _channels.IndexOf(preferred);
            if (preferredIndex < 0)
            {
                preferredIndex = 0;
            }

            _channelDropdown.value = preferredIndex;
            _channelDropdown.RefreshShownValue();
        }

        /// <summary>
        /// Callback invoked when microphone devices are added or removed from the system.
        /// </summary>
        /// <param name="args">Event arguments containing information about device changes.</param>
        private void OnDevicesChanged(MicDevicesChangedEventArgs args)
        {
            RefreshDeviceList();

            // Log device changes for debugging purposes
            if (args.HasChanges)
            {
                var messages = new List<string>();

                if (args.Added.Length > 0)
                {
                    messages.Add($"Added: {string.Join(", ", args.Added.Select(d => d.Name))}");
                }

                if (args.Removed.Length > 0)
                {
                    messages.Add($"Removed: {string.Join(", ", args.Removed.Select(d => d.Name))}");
                }

                Debug.Log($"Device changes detected: {string.Join(" | ", messages)}");
            }
        }

        #endregion

        #region Recording Control

        /// <summary>
        /// Handles the record button click event.
        /// Toggles between starting and stopping recording.
        /// </summary>
        private void OnRecordButtonClicked()
        {
            if (_easyMic.IsRecording)
            {
                StopRecording();
            }
            else
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Initiates the recording process with the currently selected device and parameters.
        /// </summary>
        private void StartRecording()
        {
            if (_devices.Length == 0)
            {
                Debug.LogWarning("No recording device available");
                return;
            }

            // Stop any ongoing playback before recording
            if (_audioSource.IsPlaying)
            {
                _audioSource.Stop();
            }

            // Get the selected device and recording parameters
            var device = _devices[Mathf.Clamp(_deviceDropdown.value, 0, _devices.Length - 1)];

            var sampleRate = _sampleRates.Count > 0
                ? _sampleRates[Mathf.Clamp(_sampleRateDropdown.value, 0, _sampleRates.Count - 1)]
                : SampleRate.Hz16000;

            var channel = _channels.Count > 0
                ? _channels[Mathf.Clamp(_channelDropdown.value, 0, _channels.Count - 1)]
                : Channel.Mono;

            // Start recording with the specified configuration
            bool success = _easyMic.StartRecording(device, sampleRate, channel);

            if (!success)
            {
                Debug.LogError("Failed to start recording");
            }
        }

        /// <summary>
        /// Stops the current recording session.
        /// </summary>
        private void StopRecording()
        {
            _easyMic.StopRecording();
        }

        /// <summary>
        /// Callback invoked when the recording state changes.
        /// Updates the UI to reflect the current recording state.
        /// </summary>
        /// <param name="isRecording">True if recording is active, false otherwise.</param>
        private void OnRecordingStateChanged(bool isRecording)
        {
            UpdateRecordingUI(isRecording);
            SetUIInteractable(!isRecording);

            if (!isRecording)
            {
                // Recording ended - display the result panel
                ShowRecordingResult();
            }
            else
            {
                // Recording started - hide the result panel
                _resultPanel.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Updates the recording indicator UI elements based on the current state.
        /// </summary>
        /// <param name="isRecording">True if currently recording, false otherwise.</param>
        private void UpdateRecordingUI(bool isRecording)
        {
            if (isRecording)
            {
                _recordingStateImage.color = Color.red;
                _recordingStateText.text = "Recording...";
                _recordButton.GetComponentInChildren<Text>().text = "Stop Recording";
            }
            else
            {
                _recordingStateImage.color = Color.gray;
                _recordingStateText.text = "Not Recording";
                _recordButton.GetComponentInChildren<Text>().text = "Start Recording";
            }
        }

        /// <summary>
        /// Enables or disables the device configuration dropdowns.
        /// Used to prevent changes during active recording.
        /// </summary>
        /// <param name="interactable">True to enable interaction, false to disable.</param>
        private void SetUIInteractable(bool interactable)
        {
            _deviceDropdown.interactable = interactable && _devices.Length > 0;
            _sampleRateDropdown.interactable = interactable && _sampleRates.Count > 0;
            _channelDropdown.interactable = interactable && _channels.Count > 0;
        }

        #endregion

        #region Playback & Save

        /// <summary>
        /// Displays the result panel with information about the recorded audio clip.
        /// </summary>
        private void ShowRecordingResult()
        {
            var clip = _easyMic.LatestRecordingClip;
            _resultPanel.gameObject.SetActive(true);

            if (clip != null)
            {
                _audioInfoText.text = $"Recording: {clip.name}\n" +
                                      $"Duration: {clip.length:F2} seconds\n" +
                                      $"Sample Rate: {clip.frequency} Hz\n" +
                                      $"Channels: {clip.channels}";
                _playStopButton.interactable = true;
                _saveButton.interactable = true;
            }
            else
            {
                _audioInfoText.text = "No audio captured";
                _playStopButton.interactable = false;
                _saveButton.interactable = false;
            }
        }

        /// <summary>
        /// Handles the play/stop button click event.
        /// Toggles between playing and stopping the recorded audio.
        /// </summary>
        private void OnPlayStopButtonClicked()
        {
            var clip = _easyMic.LatestRecordingClip;

            if (clip == null)
            {
                Debug.LogWarning("No recording available for playback");
                return;
            }

            if (_audioSource.IsPlaying)
            {
                _audioSource.Stop();
                _playStopButton.GetComponentInChildren<Text>().text = "Play";
            }
            else
            {
                _audioSource.Clip = clip;
                _audioSource.Loop = false;
                _audioSource.Play();
                _playStopButton.GetComponentInChildren<Text>().text = "Stop";
            }
        }

        /// <summary>
        /// Handles the save button click event.
        /// Saves the recorded audio to a WAV file in the persistent data path.
        /// </summary>
        private void OnSaveButtonClicked()
        {
            if (!_easyMic.HasRecordedClip)
            {
                Debug.LogWarning("No recording available to save");
                return;
            }

            // Generate a timestamped filename
            string fileName = $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            string savePath = System.IO.Path.Combine(Application.persistentDataPath, fileName);

            // Attempt to save the recording
            bool success = _easyMic.TrySaveLatestRecording(savePath);

            if (success)
            {
                Debug.Log($"Recording saved to: {savePath}");
                _audioInfoText.text += $"\n\nSaved: {fileName}";
            }
            else
            {
                Debug.LogError("Failed to save recording");
            }
        }

        #endregion

        #region Device Refresh

        /// <summary>
        /// Handles the refresh button click event.
        /// Manually triggers a refresh of the available device list.
        /// </summary>
        private void OnRefreshButtonClicked()
        {
            EasyMicAPI.Refresh();
            RefreshDeviceList();
            Debug.Log("Device list refreshed");
        }

        #endregion
    }
}
