using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Samples.Recording
{
    /// <summary>
    /// Minimal end-to-end capture sample with dynamic device hot-plug handling.
    /// Demonstrates how to subscribe to EasyMic device events and keep the UI in sync.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class EasyMicRecordingExample : MonoBehaviour
    {
        [Header("Recording Option")]
        [SerializeField] private Dropdown _selectDeviceDropdown;
        [SerializeField] private Dropdown _sampleRateDropdown;
        [SerializeField] private Dropdown _channelDropdown;
        [SerializeField] private Toggle _downmixToggle;
        [SerializeField] private Button _refreshButton;

        [Header("Recording Status")]
        [SerializeField] private RawImage _recordingStateImage;
        [SerializeField] private Text _recordingStateText;
        [SerializeField] private Button _recordButton;

        [Header("Recording Result")]
        [SerializeField] private RectTransform _resultPanel;
        [SerializeField] private Text _audioNameText;
        [SerializeField] private Button _playOrStopButton;
        [SerializeField] private Button _saveButton;

        private readonly List<MicDevice> _devices = new List<MicDevice>();
        private readonly List<SampleRate> _availableSampleRates = new List<SampleRate>();
        private readonly List<Channel> _availableChannels = new List<Channel>();

        private RecordingHandle _handle;
        private AudioWorkerBlueprint _bpCapture;
        private AudioWorkerBlueprint _bpDownmix;
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private bool _wasAudioSourcePlaying;
        private const int MaxCaptureDurationSeconds = 30;

        private bool IsRecording
        {
            get
            {
                if (!_handle.IsValid)
                {
                    return false;
                }

                var info = EasyMicAPI.GetRecordingInfo(_handle);
                return info.IsActive;
            }
        }

        private void Start()
        {
            _audioSource = GetComponent<AudioSource>();

            _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(MaxCaptureDurationSeconds), "capture");
            _bpDownmix = new AudioWorkerBlueprint(() => new AudioDownmixer(), "downmix");

            _recordButton.onClick.AddListener(OnRecordButtonPressed);
            _refreshButton.onClick.AddListener(OnRefreshButtonPressed);
            _playOrStopButton.onClick.AddListener(OnPlayOrStop);
            _saveButton.onClick.AddListener(OnSaveRecording);
            _selectDeviceDropdown.onValueChanged.AddListener(_ => OnDeviceSelectionChanged());
            _downmixToggle.onValueChanged.AddListener(OnDownmixToggleChanged);

            _resultPanel.gameObject.SetActive(false);
            _recordingStateImage.color = Color.gray;
            _recordingStateText.text = "Not Recording";
            _recordButton.GetComponentInChildren<Text>().text = "Start Recording";
            _downmixToggle.isOn = true;

            // Auto-refresh keeps the device list fresh without user interaction.
            EasyMicAPI.EnableDeviceAutoRefresh();
            EasyMicAPI.DevicesChanged += OnDevicesChanged;

            RefreshDevicesFromSystem(keepSelection: false);
        }

        private void Update()
        {
            if (_audioSource == null)
            {
                return;
            }

            if (_wasAudioSourcePlaying && !_audioSource.isPlaying)
            {
                _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
            }

            _wasAudioSourcePlaying = _audioSource.isPlaying;
        }

        private void OnDestroy()
        {
            EasyMicAPI.DevicesChanged -= OnDevicesChanged;

            _recordButton.onClick.RemoveListener(OnRecordButtonPressed);
            _refreshButton.onClick.RemoveListener(OnRefreshButtonPressed);
            _playOrStopButton.onClick.RemoveListener(OnPlayOrStop);
            _saveButton.onClick.RemoveListener(OnSaveRecording);
            _selectDeviceDropdown.onValueChanged.RemoveAllListeners();
            _downmixToggle.onValueChanged.RemoveAllListeners();

            EasyMicAPI.StopAllRecordings();
        }

        private void OnDevicesChanged(MicDevicesChangedEventArgs args)
        {
            RefreshDevicesFromSystem(keepSelection: false);
            DisplayDeviceChange(args);
        }

        private void RefreshDevicesFromSystem(bool keepSelection)
        {
            string previouslySelectedName = keepSelection && _devices.Count > 0 && _selectDeviceDropdown.options.Count > 0
                ? _devices[Mathf.Clamp(_selectDeviceDropdown.value, 0, _devices.Count - 1)].Name
                : null;

            _devices.Clear();
            _devices.AddRange(EasyMicAPI.Devices);

            _selectDeviceDropdown.ClearOptions();

            if (_devices.Count == 0)
            {
                _selectDeviceDropdown.AddOptions(new List<string> { "No device" });
                _selectDeviceDropdown.interactable = false;
                _recordButton.interactable = false;
                _sampleRateDropdown.ClearOptions();
                _channelDropdown.ClearOptions();
                return;
            }

            _selectDeviceDropdown.interactable = true;
            _recordButton.interactable = true;

            var options = _devices.Select(d => string.IsNullOrEmpty(d.Name) ? "Unnamed Device" : d.Name).ToList();
            _selectDeviceDropdown.AddOptions(options);

            int targetIndex = 0;
            if (!string.IsNullOrEmpty(previouslySelectedName))
            {
                targetIndex = _devices.FindIndex(d => string.Equals(d.Name, previouslySelectedName, StringComparison.Ordinal));
            }

            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            _selectDeviceDropdown.value = targetIndex;
            _selectDeviceDropdown.RefreshShownValue();

            OnDeviceSelectionChanged();

            SetUiInteractable(!IsRecording);
        }

        private void OnDeviceSelectionChanged()
        {
            if (_devices.Count == 0)
            {
                return;
            }

            var device = GetSelectedDevice();
            UpdateSampleRateOptions(device);
            UpdateChannelOptions(device);
        }

        private void UpdateSampleRateOptions(MicDevice device)
        {
            _sampleRateDropdown.ClearOptions();
            _availableSampleRates.Clear();

            SampleRate[] supported = device.GetSupportedSampleRateEnums();
            if (supported.Length == 0)
            {
                supported = (SampleRate[])Enum.GetValues(typeof(SampleRate));
            }

            _availableSampleRates.AddRange(supported);
            var labels = supported.Select(rate => $"{(int)rate / 1000} kHz").ToList();
            _sampleRateDropdown.AddOptions(labels);

            var resolved = device.ResolveSampleRate(GetSelectedSampleRateOrDefault());
            int resolvedIndex = _availableSampleRates.IndexOf(resolved);
            if (resolvedIndex < 0)
            {
                resolvedIndex = 0;
            }

            _sampleRateDropdown.value = resolvedIndex;
            _sampleRateDropdown.RefreshShownValue();
        }

        private void UpdateChannelOptions(MicDevice device)
        {
            _channelDropdown.ClearOptions();
            _availableChannels.Clear();

            Channel[] supported = device.GetSupportedChannels();
            if (supported.Length == 0)
            {
                supported = (Channel[])Enum.GetValues(typeof(Channel));
            }

            _availableChannels.AddRange(supported);
            _channelDropdown.AddOptions(supported.Select(c => c.ToString()).ToList());

            var preferred = device.GetPreferredChannel(GetSelectedChannelOrDefault());
            int preferredIndex = _availableChannels.IndexOf(preferred);
            if (preferredIndex < 0)
            {
                preferredIndex = 0;
            }

            _channelDropdown.value = preferredIndex;
            _channelDropdown.RefreshShownValue();
        }

        private MicDevice GetSelectedDevice()
        {
            if (_devices.Count == 0)
            {
                return default;
            }

            int index = Mathf.Clamp(_selectDeviceDropdown.value, 0, _devices.Count - 1);
            return _devices[index];
        }

        private SampleRate GetSelectedSampleRateOrDefault()
        {
            if (_availableSampleRates.Count == 0)
            {
                return SampleRate.Hz16000;
            }

            int index = Mathf.Clamp(_sampleRateDropdown.value, 0, _availableSampleRates.Count - 1);
            return _availableSampleRates[index];
        }

        private Channel GetSelectedChannelOrDefault()
        {
            if (_availableChannels.Count == 0)
            {
                return Channel.Mono;
            }

            int index = Mathf.Clamp(_channelDropdown.value, 0, _availableChannels.Count - 1);
            return _availableChannels[index];
        }

        private void OnRecordButtonPressed()
        {
            if (IsRecording)
            {
                StopRecording();
                return;
            }

            if (_devices.Count == 0)
            {
                Debug.LogWarning("EasyMic: No devices available to start recording.");
                return;
            }

            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            var device = GetSelectedDevice();
            var sampleRate = device.ResolveSampleRate(GetSelectedSampleRateOrDefault());
            var channel = ResolveChannelForStart(device, GetSelectedChannelOrDefault());

            _handle = EasyMicAPI.StartRecording(device, sampleRate, channel);
            if (!_handle.IsValid)
            {
                Debug.LogError("EasyMic: Failed to start recording. Validate device compatibility.");
                return;
            }

            if (_downmixToggle.isOn)
            {
                EasyMicAPI.AddProcessor(_handle, _bpDownmix);
            }

            EasyMicAPI.AddProcessor(_handle, _bpCapture);

            UpdateRecordingStateUI(isRecording: true, device.Name, sampleRate, channel);
            _resultPanel.gameObject.SetActive(false);
            SetUiInteractable(false);
        }

        private Channel ResolveChannelForStart(MicDevice device, Channel requested)
        {
            if (device.SupportsChannel(requested))
            {
                return requested;
            }

            return device.GetPreferredChannel(requested);
        }

        private void StopRecording()
        {
            var info = EasyMicAPI.GetRecordingInfo(_handle);
            var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_handle, _bpCapture);
            _audioClip = capturer?.GetCapturedAudioClip();

            EasyMicAPI.StopRecording(_handle);
            _handle = default;

            UpdateRecordingStateUI(isRecording: false, info.Device.Name, info.SampleRate, info.Channel);
            SetUiInteractable(true);

            _resultPanel.gameObject.SetActive(true);
            _audioNameText.text = _audioClip != null
                ? $"{info.Device.Name} - {(int)info.SampleRate / 1000}kHz ({info.Channel})"
                : "No audio captured";
        }

        private void UpdateRecordingStateUI(bool isRecording, string deviceName, SampleRate sampleRate, Channel channel)
        {
            if (isRecording)
            {
                _recordingStateText.text = string.IsNullOrEmpty(deviceName)
                    ? "Recording..."
                    : $"Recording ({deviceName}, {(int)sampleRate / 1000} kHz, {channel})";
                _recordingStateImage.color = Color.red;
                _recordButton.GetComponentInChildren<Text>().text = "Stop Recording";
            }
            else
            {
                _recordingStateText.text = "Not Recording";
                _recordingStateImage.color = Color.gray;
                _recordButton.GetComponentInChildren<Text>().text = "Start Recording";
            }
        }

        private void SetUiInteractable(bool enabled)
        {
            _selectDeviceDropdown.interactable = enabled && _devices.Count > 0;
            _sampleRateDropdown.interactable = enabled && _availableSampleRates.Count > 0;
            _channelDropdown.interactable = enabled && _availableChannels.Count > 0;
            _downmixToggle.interactable = enabled;
        }

        private void DisplayDeviceChange(MicDevicesChangedEventArgs args)
        {
            if (!args.HasChanges)
            {
                return;
            }

            var parts = new List<string>();
            if (args.Added.Length > 0)
            {
                parts.Add($"Added: {string.Join(", ", args.Added.Select(d => d.Name))}");
            }
            if (args.Removed.Length > 0)
            {
                parts.Add($"Removed: {string.Join(", ", args.Removed.Select(d => d.Name))}");
            }
            if (args.Updated.Length > 0)
            {
                parts.Add($"Updated: {string.Join(", ", args.Updated.Select(d => d.Name))}");
            }

            if (parts.Count == 0)
            {
                parts.Add("Devices refreshed");
            }

            string summary = string.Join(" | ", parts);
            try { Debug.Log($"EasyMic Sample: {summary}"); } catch { }

            if (!IsRecording && _recordingStateText != null)
            {
                _recordingStateText.text = summary;
            }
        }

        private void OnRefreshButtonPressed()
        {
            EasyMicAPI.Refresh();
            RefreshDevicesFromSystem(keepSelection: true);
        }

        private void OnPlayOrStop()
        {
            if (_audioClip == null)
            {
                Debug.LogWarning("EasyMic: No recorded clip to play.");
                return;
            }

            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
                _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
                return;
            }

            _audioSource.clip = _audioClip;
            _audioSource.loop = false;
            _audioSource.Play();
            _playOrStopButton.GetComponentInChildren<Text>().text = "Stop";
        }

        private void OnSaveRecording()
        {
            if (_audioClip == null)
            {
                Debug.LogWarning("EasyMic: Nothing to save.");
                return;
            }

            _audioClip.Save();
        }

        private void OnDownmixToggleChanged(bool isOn)
        {
            if (!IsRecording)
            {
                return;
            }

            if (isOn)
            {
                EasyMicAPI.AddProcessor(_handle, _bpDownmix);
            }
            else
            {
                EasyMicAPI.RemoveProcessor(_handle, _bpDownmix);
            }
        }
    }
}
