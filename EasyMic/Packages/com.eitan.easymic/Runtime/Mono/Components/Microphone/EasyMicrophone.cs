namespace Eitan.EasyMic.Runtime.Mono
{

    using System;
    using System.Collections;
    using System.IO;
    using System.Linq;
    using Eitan.EasyMic.Runtime;
    using Eitan.EasyMic.Runtime.Exceptions;
    using Eitan.EasyMic.Runtime.Mono.Recording;
    using Eitan.EasyMic.Runtime.Mono.Utilities;
    using UnityEngine;
#if EASYMIC_APM_INTEGRATION
    using Eitan.EasyMic.Runtime.Apm;
#endif

    [AddComponentMenu("Audio/Input/Easy Microphone")]
    public class EasyMicrophone : MonoBehaviour
    {

        #region Configuration
        [SerializeField]
        private MicrophoneOptions _microphoneOptions = MicrophoneOptions.Default;

        [SerializeField] private DeviceOptions _deviceOptions = DeviceOptions.Default;

        [Header("Logging")]
        [SerializeField] private bool _enableLog = false;
#if  EASYMIC_APM_INTEGRATION
        [SerializeField] private AudioProcessingOptions _audioProcessingOptions = AudioProcessingOptions.Default;
#endif
        #endregion

        #region  Internal Fields
        #region  Private  Fields

        private AudioWorkerBlueprint _pipelineBlueprint;

        private RecordingHandle _recordingHandle;
        private Capturer _capturer;
        private AudioClip _latestRecordingClip;
        private string _latestRecordingPath;
        private StreamingWavWriter _streamingWriter;
        private string _activeTempFilePath;
        private string _latestRecordingTempPath;
        private Coroutine _pendingStartRecordingRoutine;
        private bool _pendingStartRecording;


        #endregion

        #endregion
        #region  Properties

        public bool Initialized { get; protected set; }
        /// <summary>
        /// Returns the latest snapshot of available microphone devices from EasyMic.
        /// </summary>
        public MicDevice[] AvailableDevices
        {
            get
            {
                EasyMicAPI.Refresh();
                var devices = EasyMicAPI.Devices;
                return devices ?? Array.Empty<MicDevice>();
            }
        }

        public bool IsRecording
        {
            get
            {
                if (!_recordingHandle.IsValid)
                {
                    return false;
                }

                try
                {
                    return EasyMicAPI.IsHandleAlive(_recordingHandle);
                }
                catch
                {
                    return false;
                }
            }
        }

        public MicrophoneOptions MicrophoneOpts => _microphoneOptions;
        public DeviceOptions DeviceOpts => _deviceOptions;
        public bool EnableLog
        {
            get => _enableLog;
            set => _enableLog = value;
        }

#if EASYMIC_APM_INTEGRATION
        public AudioProcessingOptions AudioProcessingOpts => _audioProcessingOptions;
#endif

        #endregion
        #region  Event
        public event Action<bool> OnRecordingStateChanged;

        public event Action<bool> OnMicrophoneInitialized;
        #endregion


        #region  MonoBehaviour lifeCycle

        private IEnumerator Start()
        {
            if (_microphoneOptions.recordOnAwake)
            {
                if (!Initialized)
                {
                    Init();
                }
            }
            yield return new WaitUntil(() => Initialized == true);

            OnMicrophoneInitialized?.Invoke(Initialized);
            if (_microphoneOptions.recordOnAwake)
            {
                StartRecording();
            }

            yield return null;
        }

        private void OnEnable()
        {
            try
            {
                EasyMicAPI.DevicesChanged -= InternalDevicesChangedHandler;
                EasyMicAPI.DevicesChanged += InternalDevicesChangedHandler;
                OnMicrophoneEnable();
            }
            catch (Exception)
            {
                // ignored
                throw;
            }

        }

        private void Update()
        {
            if (!_recordingHandle.IsValid)
            {
                return;
            }
            OnMicrophoneUpdate();

        }
        private void OnDisable()
        {
            try
            {
                EasyMicAPI.DevicesChanged -= InternalDevicesChangedHandler;
                OnMicrophoneDisable();
            }
            catch { }
        }

        private void OnDestroy()
        {
            try
            {
                EasyMicAPI.DevicesChanged -= InternalDevicesChangedHandler;
                OnMicrophoneDispose();
            }
            catch { }
            CancelPendingStartRecording();
            EasyMicAPI.StopAllRecordings();
            if (_streamingWriter != null)
            {
                try
                {
                    _latestRecordingTempPath = _streamingWriter.FinalizeRecording();
                }
                catch (Exception ex)
                {
                    LogWarning($"EasyMicrophone: Error while finalizing recording on destroy. {ex.Message}");
                }
                finally
                {
                    _streamingWriter.Dispose();
                    _streamingWriter = null;
                }
            }
            _activeTempFilePath = null;
        }


        #endregion


        #region Microphone liveCycle

        protected virtual void OnInitialization()
        {
            Initialized = true;
        }
        protected virtual void OnMicrophoneEnable()
        {

        }

        protected virtual void OnStartRecording(RecordingHandle handle)
        {

        }

        protected virtual void OnMicrophoneUpdate()
        {

        }

        protected virtual void OnDevicesChanged(MicDevicesChangedEventArgs args)
        {

        }

        protected virtual void OnAudioPiplineBuild(AudioPipeline pipeline)
        {

        }

        protected virtual void OnStopRecording(RecordingHandle handle)
        {

        }

        protected virtual void OnMicrophoneDisable()
        {

        }
        protected virtual void OnMicrophoneDispose()
        {

        }

        #endregion

        #region Logging
        protected bool LogEnabled => _enableLog;
        protected virtual string LogPrefix => string.Empty;

        private string FormatLogMessage(string message)
        {
            if (string.IsNullOrEmpty(LogPrefix))
            {
                return message;
            }

            return $"{LogPrefix} {message}";
        }

        protected void LogInfo(string message)
        {
            if (!_enableLog)
            {
                return;
            }
            Debug.Log(FormatLogMessage(message), this);
        }

        protected void LogWarning(string message)
        {
            if (!_enableLog)
            {
                return;
            }
            Debug.LogWarning(FormatLogMessage(message), this);
        }

        protected void LogError(string message)
        {
            if (!_enableLog)
            {
                return;
            }
            Debug.LogError(FormatLogMessage(message), this);
        }
        #endregion
        #region Public Methods

        public void Init()
        {
            InternalMicrophoneInitialization();
        }

        /// <summary>
        /// Applies new microphone options and optionally restarts the recording session.
        /// </summary>
        public void ApplyMicrophoneOptions(MicrophoneOptions options, bool restartRecording = true)
        {
            bool shouldRestart = restartRecording && IsRecording;
            if (shouldRestart)
            {
                StopRecording();
            }

            _microphoneOptions = options;

            if (shouldRestart)
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Applies new device options and optionally restarts the recording session.
        /// </summary>
        public void ApplyDeviceOptions(DeviceOptions options, bool restartRecording = true)
        {
            bool shouldRestart = restartRecording && IsRecording;
            if (shouldRestart)
            {
                StopRecording();
            }

            _deviceOptions = options;

            if (shouldRestart)
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Starts capturing audio and streaming it through the configured pipeline.
        /// </summary>
        public void StartRecording()
        {
            if (IsRecording)
            {
                LogWarning("A recording session is already active. Stop it before starting a new one.");
                return;
            }
            RequestStartRecording();
        }
        /// <summary>
        /// Starts recording using a specific microphone device and optional channel override.
        /// </summary>
        public bool StartRecording(MicDevice device, SampleRate? sampleRateOverride = null, Channel? channelOverride = null)
        {
            if (!device.HasValidId)
            {
                LogWarning("Cannot start recording: provided device is not valid.");
                return false;
            }

            if (IsRecording)
            {
                LogWarning("A recording session is already active. Stop it before starting a new one.");
                return false;
            }
            if (EasyMicAPI.IsDeviceRecording(device))
            {
                LogWarning($"The device: {device}. is recording, please stop it first.");
                return false;
            }
            var channelToUse = channelOverride.HasValue
                ? channelOverride.Value
                : device.GetPreferredChannel(_deviceOptions.Channel);

            var sampleRateToUse = sampleRateOverride.HasValue
                ? sampleRateOverride.Value
                : device.GetPreferredSampleRate(_deviceOptions.SampleRate);

            _deviceOptions = new DeviceOptions(device.Name, channelToUse, sampleRateToUse);

            RequestStartRecording();

            return true;

        }

        /// <summary>
        /// Stops the active recording session and clears pending recognition buffers.
        /// </summary>
        public void StopRecording()
        {
            CancelPendingStartRecording();
            InternalStopRecordingHandler();
        }

        /// <summary>
        /// Hot-inserts an additional audio worker into the active pipeline.
        /// </summary>
        public void AppendProcessor(AudioWorkerBlueprint blueprint)
        {
            if (!IsRecording)
            {
                LogWarning("Cannot add processor: not recording.");
                return;
            }
            EasyMicAPI.AddProcessor(_recordingHandle, blueprint);
        }

        /// <summary>
        /// Removes a previously appended audio worker from the active pipeline.
        /// </summary>
        public void RemoveProcessor(AudioWorkerBlueprint blueprint)
        {
            if (!IsRecording)
            {
                LogWarning("Cannot remove processor: not recording.");
                return;
            }
            EasyMicAPI.RemoveProcessor(_recordingHandle, blueprint);
        }


        public void SelectDefaultDevice()
        {
            EasyMicAPI.Refresh();
            var devices = EasyMicAPI.Devices;
            if (devices == null || devices.Length == 0)
            {
                throw new EasyMicDeviceNotFoundException("No microphone devices avaliable");
            }

            var defaultDevice = EasyMicAPI.Default;
            if (!_deviceOptions.HasDeviceName || !devices.Any(d => string.Equals(d.Name, _deviceOptions.DeviceName, StringComparison.Ordinal)))
            {
                _deviceOptions.DeviceName = defaultDevice.Name;
                if (defaultDevice.HasValidId)
                {
                    _deviceOptions.Channel = defaultDevice.GetPreferredChannel();
                    _deviceOptions.SampleRate = defaultDevice.GetPreferredSampleRate();
                }
            }
        }

        public AudioClip GetRecordedAudioClip()
        {
            if (_capturer == null)
            {
                return null;
            }
            return _capturer.GetCapturedAudioClip();
        }

        public AudioClip LatestRecordingClip => _latestRecordingClip;

        public bool HasRecordedClip => _latestRecordingClip != null;

        public string LastSavedPath => _latestRecordingPath;

        public string LatestRecordingTempPath => _latestRecordingTempPath;

        public string CurrentRecordingTempPath => _activeTempFilePath;
        #endregion

        #region Private Methods

        private void InternalMicrophoneInitialization()
        {

            InternalBuildAudioPipelineBlueprint();
            OnInitialization();
        }

        private void RequestStartRecording()
        {
            if (!Initialized)
            {
                QueueStartRecording();
                InternalMicrophoneInitialization();
                return;
            }

            InternalStartRecordingHandler();
        }

        private void QueueStartRecording()
        {
            if (_pendingStartRecording)
            {
                return;
            }

            _pendingStartRecording = true;
            if (_pendingStartRecordingRoutine != null)
            {
                StopCoroutine(_pendingStartRecordingRoutine);
            }
            _pendingStartRecordingRoutine = StartCoroutine(WaitForInitializationAndStart());
        }

        private IEnumerator WaitForInitializationAndStart()
        {
            while (!Initialized)
            {
                if (this == null || !isActiveAndEnabled)
                {
                    _pendingStartRecording = false;
                    _pendingStartRecordingRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _pendingStartRecording = false;
            _pendingStartRecordingRoutine = null;
            InternalStartRecordingHandler();
        }

        private void CancelPendingStartRecording()
        {
            _pendingStartRecording = false;
            if (_pendingStartRecordingRoutine != null)
            {
                StopCoroutine(_pendingStartRecordingRoutine);
                _pendingStartRecordingRoutine = null;
            }
        }
        private void InternalStartRecordingHandler()
        {
            if (_pipelineBlueprint == null)
            {
                InternalBuildAudioPipelineBlueprint();
            }

            EasyMicAPI.Refresh();
            if (!TryResolveCurrentDevice(out var device))
            {
                SelectDefaultDevice();
                EasyMicAPI.Refresh();
                if (!TryResolveCurrentDevice(out device))
                {
                    device = EasyMicAPI.Default;
                    if (!device.HasValidId)
                    {
                        throw new EasyMicDeviceNotFoundException("No microphone device available.");
                    }
                }
            }

            if (device.HasValidId)
            {
                _deviceOptions.DeviceName = device.Name;
                _deviceOptions.Channel = device.GetPreferredChannel(_deviceOptions.Channel);
                _deviceOptions.SampleRate = device.GetPreferredSampleRate(_deviceOptions.SampleRate);
            }

            if (_pipelineBlueprint == null)
            {
                LogError("Recognition pipeline blueprint is missing.");
                return;
            }

            _streamingWriter?.Dispose();
            _streamingWriter = null;

            _activeTempFilePath = RecordingPathUtility.PrepareActiveTempRecordingPath(GetInstanceID());
            _latestRecordingTempPath = null;

            _streamingWriter = new StreamingWavWriter(_activeTempFilePath);

            var deviceName = string.IsNullOrEmpty(_deviceOptions.DeviceName) ? null : _deviceOptions.DeviceName;

            _recordingHandle = EasyMicAPI.StartRecording(deviceName, _deviceOptions.SampleRate, _deviceOptions.Channel);
            EasyMicAPI.AddProcessor(_recordingHandle, _pipelineBlueprint);
            // Route captured audio to streaming sink
            if (_capturer != null)
            {
                _capturer.SetSink(_streamingWriter);
            }
            OnRecordingStateChanged?.Invoke(true);
            OnStartRecording(_recordingHandle);
        }
        private void InternalStopRecordingHandler()
        {
            if (!IsRecording)
            {
                return;
            }

            EasyMicAPI.StopRecording(_recordingHandle);
            if (_streamingWriter != null)
            {
                try
                {
                    _latestRecordingTempPath = _streamingWriter.FinalizeRecording();
                }
                catch (Exception ex)
                {
                    LogError($"EasyMicrophone: Failed to finalize temporary recording. {ex.Message}");
                }
                finally
                {
                    _streamingWriter.Dispose();
                    _streamingWriter = null;
                }
            }

            _activeTempFilePath = null;
            CacheLatestRecording();
            OnStopRecording(_recordingHandle);
            _recordingHandle = default;
            OnRecordingStateChanged?.Invoke(false);
        }

        private void InternalDevicesChangedHandler(MicDevicesChangedEventArgs args)
        {
            var wasRecording = IsRecording;

            var currentStillPresent = TryResolveCurrentDevice(out _);

            if (!currentStillPresent && IsRecording)
            {
                StopRecording();
            }
            if (_microphoneOptions.autoFallback)
            {
                SelectDefaultDevice();
                EasyMicAPI.Refresh();

                if (TryResolveCurrentDevice(out var fallbackDevice) && fallbackDevice.HasValidId)
                {
                    _deviceOptions.DeviceName = fallbackDevice.Name;
                    _deviceOptions.Channel = fallbackDevice.GetPreferredChannel(_deviceOptions.Channel);
                    _deviceOptions.SampleRate = fallbackDevice.GetPreferredSampleRate(_deviceOptions.SampleRate);
                }

                if (wasRecording && !IsRecording)
                {
                    StartRecording();
                }

                OnDevicesChanged(args);
            }
        }

        private bool TryResolveCurrentDevice(out MicDevice device)
        {
            var devices = EasyMicAPI.Devices;
            if (devices == null || devices.Length == 0)
            {
                device = default;
                return false;
            }

            if (!_deviceOptions.HasDeviceName)
            {
                device = devices.FirstOrDefault(d => string.IsNullOrEmpty(d.Name));
                return device.HasValidId;
            }

            device = devices.FirstOrDefault(d => string.Equals(d.Name, _deviceOptions.DeviceName, StringComparison.Ordinal));
            if (device.HasValidId)
            {
                return true;
            }

            device = devices.FirstOrDefault(d => string.Equals(d.Name, _deviceOptions.DeviceName, StringComparison.OrdinalIgnoreCase));
            return device.HasValidId;
        }

        private void InternalBuildAudioPipelineBlueprint()
        {
            _pipelineBlueprint = new AudioWorkerBlueprint(() =>
            {
                var pipeline = new AudioPipeline();

#if EASYMIC_APM_INTEGRATION
                if (_audioProcessingOptions.AnyEnabled)
                {
                    var apm = new WebRtcApmModifier
                    {
                        EchoCancellationEnabled = _audioProcessingOptions.EnableAEC,
                        Agc1Enabled = _audioProcessingOptions.EnableAGC,
                        Agc2Enabled = _audioProcessingOptions.EnableAGC,
                        NoiseSuppressionEnabled = _audioProcessingOptions.EnableANS,
                        HighPassFilterEnabled = true
                    };
                    pipeline.AddWorker(apm);
                }
#endif

                var downmixer = new Downmixer();
                pipeline.AddWorker(downmixer);
                OnAudioPiplineBuild(pipeline);

                if (_capturer == null)
                {
                    _capturer = new Capturer();
                }
                pipeline.AddWorker(_capturer);

                return pipeline;
            });
        }

        private void CacheLatestRecording()
        {
            if (_capturer == null)
            {
                ReplaceLatestRecording(null);
                return;
            }


            var clip = _capturer.GetCapturedAudioClip();
            if (clip == null)
            {
                ReplaceLatestRecording(null);
                return;
            }

            clip.name = $"{name}_Recording_{DateTime.Now:yyyyMMdd_HHmmss}";
            ReplaceLatestRecording(clip);
        }

        private void ReplaceLatestRecording(AudioClip clip)
        {
            if (_latestRecordingClip != null && _latestRecordingClip != clip)
            {
                if (Application.isPlaying)
                {
                    Destroy(_latestRecordingClip);
                }
                else
                {
                    DestroyImmediate(_latestRecordingClip);
                }
            }

            _latestRecordingClip = clip;
        }

        public bool TrySaveLatestRecording(string destinationPath, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(_latestRecordingTempPath) || !File.Exists(_latestRecordingTempPath))
            {
                LogWarning("EasyMicrophone: No temporary recording available to save.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                LogWarning("EasyMicrophone: Destination path is empty.");
                return false;
            }

            var finalPath = RecordingPathUtility.EnsureWavExtension(destinationPath);

            try
            {
                var directory = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!overwrite && File.Exists(finalPath))
                {
                    LogWarning($"EasyMicrophone: Target file already exists and overwrite is disabled. Path: {finalPath}");
                    return false;
                }

                bool samePath = false;
                try
                {
                    samePath = string.Equals(
                        Path.GetFullPath(_latestRecordingTempPath),
                        Path.GetFullPath(finalPath),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    // ignore path normalization failures and treat as different paths
                }

                if (!samePath)
                {
                    File.Copy(_latestRecordingTempPath, finalPath, overwrite: true);
                }
                else if (!File.Exists(finalPath))
                {
                    File.Copy(_latestRecordingTempPath, finalPath, overwrite: true);
                }

                _latestRecordingPath = finalPath;
                return true;
            }
            catch (Exception ex)
            {
                LogError($"EasyMicrophone: Failed to save recording. {ex.Message}");
                return false;
            }
        }

        #endregion

    }
}
