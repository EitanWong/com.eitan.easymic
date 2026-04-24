#if EASYMIC_APM_INTEGRATION
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.IO;
using System.Reflection;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Apm;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Mono.Components;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using EasyMicApmApi = Eitan.EasyMic.Apm.EasyMicAudioProcessing;

namespace Eitan.EasyMic.Apm.Samples
{
    /// <summary>
    /// EasyMicrophone-driven sample showcasing APM processing with a simpler, component-based workflow.
    /// </summary>
    [RequireComponent(typeof(EasyMicrophone))]
    [RequireComponent(typeof(PlaybackAudioSourceBehaviour))]
    [AddComponentMenu("Examples/EasyMic/APM/Audio Processing Example")]
    public class EasyMicAudioProcessingExample : MonoBehaviour
    {
        [Header("Recording Option")]
        [Obfuscation(Exclude = true)]
        [SerializeField] private Dropdown _selectDeviceDropdown;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Dropdown _sampleRateDropdown;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Dropdown _channelDropdown;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Toggle _agcToggle;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Toggle _ansToggle;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Toggle _aecToggle;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Toggle _loopbackToggle;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Toggle _playMusicToggle;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Button _refreshButton;

        [Header("Recording Status")]
        [Obfuscation(Exclude = true)]
        [SerializeField] private RawImage _recordingStateImage;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Text _recordingStateText;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Button _recordButton;

        [Header("Recording Result")]
        [Obfuscation(Exclude = true)]
        [SerializeField] private RectTransform _resultPanel;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Text _audioNameText;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Button _playOrStopButton;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Button _saveButton;

        [Header("Audio Sound")]
        [Obfuscation(Exclude = true)]
        [SerializeField] private AudioClip _speechAudioClip;

        [Header("UI Components")]
        [Obfuscation(Exclude = true)]
        [SerializeField] private InputField _licenseKeyInputField;
        [Obfuscation(Exclude = true)]
        [SerializeField] private Text _machineCodeText;

        private EasyMicrophone _easyMicrophone;
        private readonly List<MicDevice> _devices = new List<MicDevice>();
        private readonly List<SampleRate> _availableSampleRates = new List<SampleRate>();
        private readonly List<Channel> _availableChannels = new List<Channel>();

        private AudioWorkerBlueprint _loopbackBlueprint;

        private PlaybackAudioSourceBehaviour _audioSource;
        private AudioClip _recordedClip;
        private Coroutine _loadRecordedClipRoutine;
        private bool _ownsRecordedClip;
        private bool _wasSourcePlaying;
        private string _currentRecordingSummary = "Recording...";
        private string _lastRecordingDeviceName = "Unknown Device";
        private int _lastRecordingSampleRateHz;
        private string _lastRecordingChannelLabel = "Unknown";
        private bool _lastRecordingAgcEnabled;
        private bool _lastRecordingAnsEnabled;
        private bool _lastRecordingAecEnabled;
        private bool _licenseAuthorized;

        private void Awake()
        {
            _easyMicrophone = GetComponent<EasyMicrophone>();
            _audioSource = GetComponent<PlaybackAudioSourceBehaviour>();
            if (GetComponent<EasyMicApmDiagnosticsLogger>() == null)
            {
                gameObject.AddComponent<EasyMicApmDiagnosticsLogger>();
            }
            EnsureManualRecordingFlow();
        }

        private void Start()
        {
            _recordButton.onClick.AddListener(OnRecordButtonPressed);
            _refreshButton.onClick.AddListener(OnRefreshButtonPressed);
            _playOrStopButton.onClick.AddListener(OnPlayOrStop);
            _saveButton.onClick.AddListener(OnSaveRecording);
            _selectDeviceDropdown.onValueChanged.AddListener(_ => OnDeviceSelectionChanged());
            _loopbackToggle.onValueChanged.AddListener(OnLoopbackToggleChanged);
            _playMusicToggle.onValueChanged.AddListener(OnPlayMusicToggleChanged);
            _agcToggle.onValueChanged.AddListener(_ => OnProcessingToggleChanged());
            _ansToggle.onValueChanged.AddListener(_ => OnProcessingToggleChanged());
            _aecToggle.onValueChanged.AddListener(_ => OnProcessingToggleChanged());
            if (_licenseKeyInputField != null)
            {
                _licenseKeyInputField.onEndEdit.AddListener(OnLicenseKeySubmitted);
            }

            _easyMicrophone.OnMicrophoneInitialized += OnMicrophoneInitialized;
            _easyMicrophone.OnRecordingStateChanged += OnRecordingStateChanged;

            _resultPanel.gameObject.SetActive(false);
            _recordingStateImage.color = Color.gray;
            _recordingStateText.text = "Not Recording";
            SetRecordButtonLabel("Start Recording");
            _playMusicToggle.isOn = true;
            _machineCodeText.text = GetMachineCodeOrFallback();
            RegisterMachineCodeCopyHandler();
            SyncLicenseInputFromSession();
            ApplyProcessingOptionsFromToggles();

            EasyMicAPI.EnableDeviceAutoRefresh();
            EasyMicAPI.DevicesChanged += OnDevicesChanged;
            _easyMicrophone.Init();
        }

        private void Update()
        {
            if (_audioSource == null)
            {
                return;
            }

            if (_wasSourcePlaying && !_audioSource.IsPlaying)
            {
                _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
            }

            _wasSourcePlaying = _audioSource.IsPlaying;
        }

        private void OnDestroy()
        {
            EasyMicAPI.DevicesChanged -= OnDevicesChanged;
            if (_easyMicrophone != null)
            {
                _easyMicrophone.OnMicrophoneInitialized -= OnMicrophoneInitialized;
                _easyMicrophone.OnRecordingStateChanged -= OnRecordingStateChanged;
            }

            _recordButton.onClick.RemoveListener(OnRecordButtonPressed);
            _refreshButton.onClick.RemoveListener(OnRefreshButtonPressed);
            _playOrStopButton.onClick.RemoveListener(OnPlayOrStop);
            _saveButton.onClick.RemoveListener(OnSaveRecording);
            _selectDeviceDropdown.onValueChanged.RemoveAllListeners();
            _loopbackToggle.onValueChanged.RemoveAllListeners();
            _playMusicToggle.onValueChanged.RemoveAllListeners();
            _agcToggle.onValueChanged.RemoveAllListeners();
            _ansToggle.onValueChanged.RemoveAllListeners();
            _aecToggle.onValueChanged.RemoveAllListeners();
            if (_licenseKeyInputField != null)
            {
                _licenseKeyInputField.onEndEdit.RemoveListener(OnLicenseKeySubmitted);
            }

            if (_easyMicrophone != null && _easyMicrophone.IsRecording)
            {
                _easyMicrophone.StopRecording();
            }

            if (_loadRecordedClipRoutine != null)
            {
                StopCoroutine(_loadRecordedClipRoutine);
                _loadRecordedClipRoutine = null;
            }

            ReplaceRecordedClipForPlayback(null, ownsClip: false);
        }

        private void EnsureManualRecordingFlow()
        {
            if (_easyMicrophone == null)
            {
                return;
            }

            var micOptions = _easyMicrophone.MicrophoneOpts;
            if (micOptions.recordOnAwake)
            {
                micOptions.recordOnAwake = false;
                _easyMicrophone.ApplyMicrophoneOptions(micOptions, restartRecording: false);
            }
        }

        private void SyncLicenseInputFromSession()
        {
            if (_licenseKeyInputField == null)
            {
                return;
            }

            string token = _licenseKeyInputField.text == null ? string.Empty : _licenseKeyInputField.text.Trim();
            if (!string.IsNullOrEmpty(token))
            {
                _licenseKeyInputField.SetTextWithoutNotify(token);
                _licenseAuthorized = EasyMicApmApi.Authorize(token, out _);
            }
        }

        private void OnLicenseKeySubmitted(string rawValue)
        {
            string token = rawValue == null ? string.Empty : rawValue.Trim();

            if (string.IsNullOrEmpty(token))
            {
                EasyMicApmApi.ClearLicenseToken();
                _licenseAuthorized = false;
                return;
            }

            _licenseAuthorized = EasyMicApmApi.Authorize(token, out string error);
            if (!_licenseAuthorized)
            {
                Debug.LogWarning("EasyMic: License authorization failed. " + error);
            }
        }

        private string GetMachineCodeOrFallback()
        {
            try
            {
                return EasyMicApmApi.GetMachineCode();
            }
            catch
            {
                return "Machine code unavailable";
            }
        }

        private void OnMicrophoneInitialized(bool success)
        {
            if (!success)
            {
                _recordingStateText.text = "Microphone init failed";
                _recordButton.interactable = false;
                return;
            }

            RefreshDevicesFromSystem(keepSelection: false);
            SetUiInteractable(true);
        }

        private void OnRecordingStateChanged(bool isRecording)
        {
            UpdateRecordingStateUI(isRecording);
            SetUiInteractable(!isRecording);

            if (isRecording)
            {
                CancelRecordedClipLoad();
                ReplaceRecordedClipForPlayback(null, ownsClip: false);
                if (_loopbackToggle.isOn)
                {
                    EnsureLoopbackProcessor();
                }
                _resultPanel.gameObject.SetActive(false);
                return;
            }

            _resultPanel.gameObject.SetActive(true);
            _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
            _audioNameText.text = "Loading recorded clip...";
            BeginLoadRecordedClip();

            if (_playMusicToggle.isOn)
            {
                StopAudioSource();
            }
        }

        private void OnDevicesChanged(MicDevicesChangedEventArgs args)
        {
            RefreshDevicesFromSystem(keepSelection: true);
            DisplayDeviceChange(args);
        }

        private void RefreshDevicesFromSystem(bool keepSelection)
        {
            string previousName = keepSelection && _devices.Count > 0 && _selectDeviceDropdown.options.Count > 0
                ? _devices[Mathf.Clamp(_selectDeviceDropdown.value, 0, _devices.Count - 1)].Name
                : null;

            _devices.Clear();
            _devices.AddRange(_easyMicrophone.AvailableDevices);

            _selectDeviceDropdown.ClearOptions();

            if (_devices.Count == 0)
            {
                _selectDeviceDropdown.AddOptions(new List<string> { "No device" });
                _selectDeviceDropdown.interactable = false;
                _recordButton.interactable = false;
                _sampleRateDropdown.ClearOptions();
                _channelDropdown.ClearOptions();
                _recordingStateText.text = "No microphone device";
                return;
            }

            var labels = _devices.Select(d => string.IsNullOrEmpty(d.Name) ? "Unnamed Device" : d.Name).ToList();
            _selectDeviceDropdown.AddOptions(labels);
            _selectDeviceDropdown.interactable = true;
            _recordButton.interactable = true;

            int targetIndex = 0;
            if (!string.IsNullOrEmpty(previousName))
            {
                targetIndex = _devices.FindIndex(d => string.Equals(d.Name, previousName, StringComparison.Ordinal));
            }

            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            _selectDeviceDropdown.value = targetIndex;
            _selectDeviceDropdown.RefreshShownValue();

            OnDeviceSelectionChanged();
            SetUiInteractable(!_easyMicrophone.IsRecording);
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

            var supported = device.GetSupportedSampleRateEnums();
            if (supported.Length == 0)
            {
                supported = (SampleRate[])Enum.GetValues(typeof(SampleRate));
            }

            _availableSampleRates.AddRange(supported);
            _sampleRateDropdown.AddOptions(supported.Select(r => $"{(int)r / 1000} kHz").ToList());

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

            var supported = device.GetSupportedChannels();
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
            if (_easyMicrophone.IsRecording)
            {
                _easyMicrophone.StopRecording();
                return;
            }

            if (_devices.Count == 0)
            {
                Debug.LogWarning("EasyMic: No devices available to start recording.");
                return;
            }

            if (!EnsureProcessingAuthorized(stopRecordingOnFailure: false))
            {
                return;
            }

            StopAudioSource();

            var device = GetSelectedDevice();
            var sampleRate = device.ResolveSampleRate(GetSelectedSampleRateOrDefault());
            var channel = ResolveChannelForStart(device, GetSelectedChannelOrDefault());
            CaptureRecordingMetadata(device, sampleRate, channel);
            _currentRecordingSummary = $"Recording ({device.Name}, {(int)sampleRate / 1000} kHz, {channel})";

            bool started = _easyMicrophone.StartRecording(device, sampleRate, channel);
            if (!started)
            {
                Debug.LogError("EasyMic: Failed to start recording on selected device.");
                return;
            }

            if (_playMusicToggle.isOn)
            {
                PlayMusic(_speechAudioClip);
            }
        }

        private Channel ResolveChannelForStart(MicDevice device, Channel requested)
        {
            if (device.SupportsChannel(requested))
            {
                return requested;
            }

            return device.GetPreferredChannel(requested);
        }

        private bool IsProcessingEnabled()
        {
            return (_aecToggle != null && _aecToggle.isOn) ||
                   (_ansToggle != null && _ansToggle.isOn) ||
                   (_agcToggle != null && _agcToggle.isOn);
        }

        private void CaptureRecordingMetadata(MicDevice device, SampleRate sampleRate, Channel channel)
        {
            _lastRecordingDeviceName = string.IsNullOrWhiteSpace(device.Name) ? "Unnamed Device" : device.Name;
            _lastRecordingSampleRateHz = (int)sampleRate;
            _lastRecordingChannelLabel = channel.ToString();
            _lastRecordingAgcEnabled = _agcToggle != null && _agcToggle.isOn;
            _lastRecordingAnsEnabled = _ansToggle != null && _ansToggle.isOn;
            _lastRecordingAecEnabled = _aecToggle != null && _aecToggle.isOn;
        }

        private void SetRecordButtonLabel(string text)
        {
            var label = _recordButton != null ? _recordButton.GetComponentInChildren<Text>() : null;
            if (label != null)
            {
                label.text = text;
            }
        }

        private bool EnsureProcessingAuthorized(bool stopRecordingOnFailure)
        {
            if (!IsProcessingEnabled())
            {
                return true;
            }

            if (_licenseKeyInputField != null)
            {
                EasyMicApmApi.SetLicenseToken(_licenseKeyInputField.text);
            }

            if (EasyMicApmApi.Authorize(out string error))
            {
                _licenseAuthorized = true;
                return true;
            }

            _licenseAuthorized = false;
            HandleProcessingAuthorizationFailure(
                string.IsNullOrWhiteSpace(error) ? "APM license verification failed." : error,
                stopRecordingOnFailure);
            return false;
        }

        private void HandleProcessingAuthorizationFailure(string error, bool stopRecordingOnFailure)
        {
            Debug.LogError("EasyMic: APM authorization failed. " + error);

            if (stopRecordingOnFailure && _easyMicrophone != null && _easyMicrophone.IsRecording)
            {
                _easyMicrophone.StopRecording();
            }

            ReplaceRecordedClipForPlayback(null, ownsClip: false);
            _resultPanel.gameObject.SetActive(false);
            _recordingStateText.text = stopRecordingOnFailure
                ? "APM license failed. Recording stopped."
                : "APM license failed. Recording not started.";
            _recordingStateImage.color = new Color(0.85f, 0.3f, 0.3f);
            SetRecordButtonLabel("Start Recording");
            SetUiInteractable(true);
        }

        private void UpdateRecordingStateUI(bool isRecording)
        {
            if (isRecording)
            {
                _recordingStateText.text = _currentRecordingSummary;
                _recordingStateImage.color = Color.red;
                SetRecordButtonLabel("Stop Recording");
            }
            else
            {
                _recordingStateText.text = "Not Recording";
                _recordingStateImage.color = Color.gray;
                SetRecordButtonLabel("Start Recording");
            }
        }

        private void SetUiInteractable(bool enabled)
        {
            _selectDeviceDropdown.interactable = enabled && _devices.Count > 0;
            _sampleRateDropdown.interactable = enabled && _availableSampleRates.Count > 0;
            _channelDropdown.interactable = enabled && _availableChannels.Count > 0;
            _agcToggle.interactable = enabled;
            _ansToggle.interactable = enabled;
            _aecToggle.interactable = enabled;
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

            if (!_easyMicrophone.IsRecording && _recordingStateText != null)
            {
                _recordingStateText.text = summary;
            }
        }

        private void OnRefreshButtonPressed()
        {
            EasyMicAPI.Refresh();
            RefreshDevicesFromSystem(keepSelection: true);
        }

        private void RegisterMachineCodeCopyHandler()
        {
            if (_machineCodeText == null)
            {
                return;
            }

            _machineCodeText.raycastTarget = true;

            var clickHandler = _machineCodeText.GetComponent<MachineCodeTextClickHandler>();
            if (clickHandler == null)
            {
                clickHandler = _machineCodeText.gameObject.AddComponent<MachineCodeTextClickHandler>();
            }

            clickHandler.OnClicked = CopyMachineCodeToClipboard;
        }

        private void CopyMachineCodeToClipboard()
        {
            if (_machineCodeText == null || string.IsNullOrWhiteSpace(_machineCodeText.text))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = _machineCodeText.text;
            Debug.Log("EasyMic: Machine code copied to clipboard.");
        }

        private void OnPlayOrStop()
        {
            if (_recordedClip == null)
            {
                Debug.LogWarning("EasyMic: Recorded clip is not ready yet.");
                return;
            }

            if (_audioSource.IsPlaying)
            {
                StopAudioSource();
                return;
            }

            _audioSource.Clip = _recordedClip;
            _audioSource.Loop = false;
            _audioSource.Play();
            _playOrStopButton.GetComponentInChildren<Text>().text = "Stop";
        }

        private void StopAudioSource()
        {
            _audioSource.Stop();
            _playOrStopButton.GetComponentInChildren<Text>().text = "Play";
        }

        private void PlayMusic(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogWarning("EasyMic: No music clip assigned.");
                return;
            }

            _audioSource.Clip = clip;
            _audioSource.Loop = true;
            _audioSource.Play();
            _playOrStopButton.GetComponentInChildren<Text>().text = "Stop";
        }

        private void OnSaveRecording()
        {
            if (!HasCompleteRecording())
            {
                Debug.LogWarning("EasyMic: Nothing to save.");
                return;
            }

            string path = System.IO.Path.Combine(
                Application.persistentDataPath,
                $"EasyMic_Recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav");

            if (!_easyMicrophone.TrySaveLatestRecording(path))
            {
                Debug.LogWarning("EasyMic: Failed to save recording.");
                return;
            }

            Debug.Log($"EasyMic: Saved recording to {path}");
        }

        private void OnLoopbackToggleChanged(bool isOn)
        {
            if (!_easyMicrophone.IsRecording)
            {
                return;
            }

            if (isOn)
            {
                EnsureLoopbackProcessor();
            }
            else
            {
                RemoveLoopbackProcessor();
            }
        }

        private void OnPlayMusicToggleChanged(bool isOn)
        {
            if (!isOn)
            {
                StopAudioSource();
                return;
            }

            PlayMusic(_speechAudioClip);
        }

        private void OnProcessingToggleChanged()
        {
            if (_easyMicrophone != null && _easyMicrophone.IsRecording && !EnsureProcessingAuthorized(stopRecordingOnFailure: true))
            {
                return;
            }

            ApplyProcessingOptionsFromToggles();
        }

        private void ApplyProcessingOptionsFromToggles()
        {
            if (_easyMicrophone == null)
            {
                return;
            }

            _easyMicrophone.AecEnabled = _aecToggle != null && _aecToggle.isOn;
            _easyMicrophone.AnsEnabled = _ansToggle != null && _ansToggle.isOn;
            _easyMicrophone.AgcEnabled = _agcToggle != null && _agcToggle.isOn;
        }

        private void EnsureLoopbackProcessor()
        {
            if (_loopbackBlueprint == null)
            {
                _loopbackBlueprint = new AudioWorkerBlueprint(() => new LoopbackPlayer(), "loopback");
            }

            _easyMicrophone.AppendProcessor(_loopbackBlueprint);
        }

        private void RemoveLoopbackProcessor()
        {
            if (_loopbackBlueprint == null)
            {
                return;
            }

            _easyMicrophone.RemoveProcessor(_loopbackBlueprint);
        }

        private void BeginLoadRecordedClip()
        {
            CancelRecordedClipLoad();

            string tempPath = _easyMicrophone != null ? _easyMicrophone.LatestRecordingTempPath : null;
            if (!string.IsNullOrWhiteSpace(tempPath))
            {
                _loadRecordedClipRoutine = StartCoroutine(LoadRecordedClipFromTempFile(tempPath));
                return;
            }

            UsePreviewRecordedClip();
        }

        private IEnumerator LoadRecordedClipFromTempFile(string tempPath)
        {
            string clipUrl = "file://" + tempPath;
            using (var request = UnityWebRequestMultimedia.GetAudioClip(clipUrl, AudioType.WAV))
            {
                yield return request.SendWebRequest();

                _loadRecordedClipRoutine = null;

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"EasyMic: Failed to load complete recording from temp file. {request.error}");
                    UsePreviewRecordedClip();
                    yield break;
                }

                var audioClip = DownloadHandlerAudioClip.GetContent(request);
                if (audioClip == null)
                {
                    Debug.LogWarning("EasyMic: Loaded temp recording returned no clip.");
                    UsePreviewRecordedClip();
                    yield break;
                }

                ReplaceRecordedClipForPlayback(audioClip, ownsClip: true);
                RefreshRecordedClipSummary();
            }
        }

        private void UsePreviewRecordedClip()
        {
            ReplaceRecordedClipForPlayback(_easyMicrophone != null ? _easyMicrophone.LatestRecordingClip : null, ownsClip: false);
            RefreshRecordedClipSummary();
        }

        private void RefreshRecordedClipSummary()
        {
            _audioNameText.text = _recordedClip != null
                ? $"{BuildRecordingMetadataLabel()} ({_recordedClip.length:F1}s)"
                : "No audio captured";
        }

        private string BuildRecordingMetadataLabel()
        {
            string sampleRateLabel = _lastRecordingSampleRateHz > 0
                ? $"{_lastRecordingSampleRateHz / 1000f:0.#} kHz"
                : "Unknown Hz";
            return $"{_lastRecordingDeviceName} | {sampleRateLabel} | {_lastRecordingChannelLabel} | " +
                   $"AGC:{ToOnOff(_lastRecordingAgcEnabled)} ANS:{ToOnOff(_lastRecordingAnsEnabled)} AEC:{ToOnOff(_lastRecordingAecEnabled)}";
        }

        private static string ToOnOff(bool enabled)
        {
            return enabled ? "On" : "Off";
        }

        private void ReplaceRecordedClipForPlayback(AudioClip clip, bool ownsClip)
        {
            if (_ownsRecordedClip && _recordedClip != null && _recordedClip != clip)
            {
                Destroy(_recordedClip);
            }

            _recordedClip = clip;
            _ownsRecordedClip = ownsClip;
        }

        private void CancelRecordedClipLoad()
        {
            if (_loadRecordedClipRoutine == null)
            {
                return;
            }

            StopCoroutine(_loadRecordedClipRoutine);
            _loadRecordedClipRoutine = null;
        }

        private bool HasCompleteRecording()
        {
            string tempPath = _easyMicrophone != null ? _easyMicrophone.LatestRecordingTempPath : null;
            return !string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath);
        }

        private sealed class MachineCodeTextClickHandler : MonoBehaviour, IPointerClickHandler
        {
            public Action OnClicked { get; set; }

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button != PointerEventData.InputButton.Left)
                {
                    return;
                }

                OnClicked?.Invoke();
            }
        }
    }
}
#endif
