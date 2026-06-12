namespace Eitan.EasyMic.Runtime.Mono
{

    using System;
    using System.Collections;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using Eitan.EasyMic.Runtime;
    using Eitan.EasyMic.Runtime.Exceptions;
    using Eitan.EasyMic.Runtime.Mono.Recording;
    using Eitan.EasyMic.Runtime.Mono.Utilities;
    using UnityEngine;

    [AddComponentMenu("Audio/EasyMic/Input/Easy Microphone")]
    public class EasyMicrophone : MonoBehaviour
    {

        #region Configuration
        [SerializeField]
        private MicrophoneOptions _microphoneOptions = MicrophoneOptions.Default;

        [SerializeField] private DeviceOptions _deviceOptions = DeviceOptions.Default;

        [Header("Logging")]
        [SerializeField] private bool _enableLog = false;
        [SerializeField] private AudioProcessingOptions _audioProcessingOptions = AudioProcessingOptions.Default;
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
        private float _nextRecordingDiagnosticsLogTime;
        private bool _reportedMacEditorSilentCapture;

        private static string s_lastApmUnavailableReason = string.Empty;

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
            set
            {
                _enableLog = value;
                if (_recordingHandle.IsValid)
                {
                    EasyMicAPI.SetRecordingCallbackDiagnostics(_recordingHandle, value);
                }
            }
        }

        /// <summary>
        /// APM options snapshot.
        /// - Not recording: reads/writes the serialized staging field.
        /// - Recording: reads/writes the active APM worker inside the current AudioPipeline.
        /// </summary>
        public AudioProcessingOptions AudioProcessingOpts
        {
            get => GetCurrentAudioProcessingOptions();
            set => SetAudioProcessingOptions(value);
        }

        public bool AecEnabled
        {
            get => AudioProcessingOpts.EnableAEC;
            set
            {
                var options = AudioProcessingOpts;
                if (options.EnableAEC == value)
                {
                    return;
                }

                options.EnableAEC = value;
                SetAudioProcessingOptions(options);
            }
        }

        public bool AnsEnabled
        {
            get => AudioProcessingOpts.EnableANS;
            set
            {
                var options = AudioProcessingOpts;
                if (options.EnableANS == value)
                {
                    return;
                }

                options.EnableANS = value;
                SetAudioProcessingOptions(options);
            }
        }

        public bool AgcEnabled
        {
            get => AudioProcessingOpts.EnableAGC;
            set
            {
                var options = AudioProcessingOpts;
                if (options.EnableAGC == value)
                {
                    return;
                }

                options.EnableAGC = value;
                SetAudioProcessingOptions(options);
            }
        }

        public bool TryGetApmDiagnostics(out object diagnostics)
        {
            var apmWorker = GetCurrentApmWorker();
            if (apmWorker == null)
            {
                diagnostics = default;
                return false;
            }

            return apmWorker.TryGetDiagnostics(out diagnostics);
        }

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
            LogRecordingDiagnostics();
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
            StopOwnedRecording(finalizeClip: false);
            FinalizeStreamingWriter(logAsError: false);
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

        public RecordingInfo CurrentRecordingInfo => _recordingHandle.IsValid
            ? EasyMicAPI.GetRecordingInfo(_recordingHandle)
            : new RecordingInfo();
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
            while (!Initialized || !PermissionUtils.HasPermission())
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

        private void LogRecordingDiagnostics()
        {
            if (!LogEnabled || Time.unscaledTime < _nextRecordingDiagnosticsLogTime)
            {
                return;
            }

            _nextRecordingDiagnosticsLogTime = Time.unscaledTime + 1f;
            var info = CurrentRecordingInfo;
            bool unityMicAuthorized = Application.HasUserAuthorization(UserAuthorization.Microphone);
            LogInfo(
                $"Recording diagnostics: device='{info.Device.Name}', rate={(int)info.SampleRate}, channel={(int)info.Channel}, callbacks={info.NativeCallbackCount}, " +
                $"inputNull={info.NativeInputNullCount}, outputNull={info.NativeOutputNullCount}, nonZeroCallbacks={info.NativeNonZeroCallbackCount}, " +
                $"nonZeroByteCallbacks={info.NativeNonZeroByteCallbackCount}, nonZeroOutputByteCallbacks={info.NativeNonZeroOutputByteCallbackCount}, " +
                $"lastRawPeak={info.LastRawInputPeak:0.000000}, maxRawPeak={info.MaxRawInputPeak:0.000000}, " +
                $"lastNonZeroBytes={info.LastRawInputNonZeroBytes}, maxNonZeroBytes={info.MaxRawInputNonZeroBytes}, " +
                $"lastOutputNonZeroBytes={info.LastRawOutputNonZeroBytes}, maxOutputNonZeroBytes={info.MaxRawOutputNonZeroBytes}, unityMicAuthorized={unityMicAuthorized}");

#if UNITY_EDITOR_OSX
            if (!_reportedMacEditorSilentCapture &&
                unityMicAuthorized &&
                info.NativeCallbackCount >= 300 &&
                info.NativeInputNullCount == 0 &&
                info.NativeNonZeroByteCallbackCount == 0)
            {
                _reportedMacEditorSilentCapture = true;
                LogWarning(
                    "EasyMicrophone: CoreAudio capture is running but delivering silent buffers in Unity Editor. " +
                    "On macOS this usually means microphone access was granted to Unity Hub, but not to the Unity Editor process " +
                    "(bundle id com.unity3d.UnityEditor5.x). This Unity Editor build may also lack NSMicrophoneUsageDescription in its Info.plist. " +
                    "A Player build with microphone permission should not use the Editor's TCC state.");
            }
#endif
        }
        private void InternalStartRecordingHandler()
        {
            if (!PermissionUtils.HasPermission())
            {
                QueueStartRecording();
                return;
            }

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

            _activeTempFilePath = RecordingPathUtility.PrepareActiveTempRecordingPath(RuntimeHelpers.GetHashCode(this));
            _latestRecordingTempPath = null;

            var deviceName = string.IsNullOrEmpty(_deviceOptions.DeviceName) ? null : _deviceOptions.DeviceName;

            _streamingWriter = new StreamingWavWriter(_activeTempFilePath);
            try
            {
                _recordingHandle = EasyMicAPI.StartRecording(deviceName, _deviceOptions.SampleRate, _deviceOptions.Channel, new[] { _pipelineBlueprint });
            }
            catch
            {
                _recordingHandle = default;
                FinalizeStreamingWriter(logAsError: false);
                _activeTempFilePath = null;
                throw;
            }

            if (!_recordingHandle.IsValid)
            {
                FinalizeStreamingWriter(logAsError: false);
                _activeTempFilePath = null;
                LogWarning("EasyMicrophone: Recording did not start. Microphone permission may still be pending or denied.");
                return;
            }

            EasyMicAPI.SetRecordingCallbackDiagnostics(_recordingHandle, LogEnabled);
            OnRecordingStateChanged?.Invoke(true);
            OnStartRecording(_recordingHandle);
        }
        private void InternalStopRecordingHandler()
        {
            if (!IsRecording)
            {
                return;
            }

            var stoppedHandle = _recordingHandle;
            EasyMicAPI.SetRecordingCallbackDiagnostics(stoppedHandle, false);
            StopOwnedRecording(finalizeClip: false);
            FinalizeStreamingWriter(logAsError: true);

            _activeTempFilePath = null;
            CacheLatestRecording();
            OnStopRecording(stoppedHandle);
            _recordingHandle = default;
            OnRecordingStateChanged?.Invoke(false);
        }

        private void StopOwnedRecording(bool finalizeClip)
        {
            if (_recordingHandle.IsValid)
            {
                try
                {
                    EasyMicAPI.StopRecording(_recordingHandle);
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    LogWarning($"EasyMicrophone: Failed to stop owned recording. {ex.Message}");
                }
            }

            if (!finalizeClip)
            {
                _recordingHandle = default;
            }
        }

        private void FinalizeStreamingWriter(bool logAsError)
        {
            if (_streamingWriter == null)
            {
                return;
            }

            try
            {
                _latestRecordingTempPath = _streamingWriter.FinalizeRecording();
            }
            catch (Exception ex)
            {
                string message = $"EasyMicrophone: Failed to finalize temporary recording. {ex.Message}";
                if (logAsError)
                {
                    LogError(message);
                }
                else
                {
                    LogWarning(message);
                }
            }
            finally
            {
                _streamingWriter.Dispose();
                _streamingWriter = null;
            }
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

                if (_audioProcessingOptions.AnyEnabled)
                {
                    if (CanUseApmProcessing())
                    {
                        var apm = EasyMicApmBridgeRegistry.CreateWorker();
                        if (apm != null)
                        {
                            apm.SetProcessingOptions(
                            _audioProcessingOptions.EnableAEC,
                            _audioProcessingOptions.EnableANS,
                            _audioProcessingOptions.EnableAGC);
                            pipeline.AddWorker(apm);
                        }
                    }
                }

                var downmixer = new Downmixer();
                pipeline.AddWorker(downmixer);
                OnAudioPiplineBuild(pipeline);

                if (_capturer == null)
                {
                    _capturer = new Capturer();
                }
                _capturer.SetSink(_streamingWriter);
                pipeline.AddWorker(_capturer);

                return pipeline;
            });
        }

        private static bool CanUseApmProcessing()
        {
            if (!EasyMicApmBridgeRegistry.IsAvailable)
            {
                LogApmIssue("EasyMic APM is enabled in EasyMicrophone, but the EasyMic APM package is not installed or its runtime bridge is unavailable.");
                return false;
            }

            if (EasyMicApmBridgeRegistry.IsAuthorized())
            {
                s_lastApmUnavailableReason = string.Empty;
                return true;
            }

            string error;
            if (!EasyMicApmBridgeRegistry.HasConfiguredLicenseToken())
            {
                error =
                    "EasyMic APM is enabled, but no license token was discovered at runtime. " +
                    "Activate the token in Project Settings and rebuild so EasyMic can embed it into the player, " +
                    "or configure an EasyMic APM runtime license token before starting APM.";
            }
            else
            {
                var result = EasyMicApmBridgeRegistry.Authorize();
                error = result.Error;
                if (result.Authorized)
                {
                    s_lastApmUnavailableReason = string.Empty;
                    return true;
                }
            }

            if (string.IsNullOrWhiteSpace(error))
            {
                error = EasyMicApmBridgeRegistry.LastError();
            }

            if (EasyMicApmBridgeRegistry.HasConfiguredLicenseToken())
            {
                error = (string.IsNullOrWhiteSpace(error) ? "EasyMic APM license authorization failed. No error details were returned." : error) +
                        " Token source: " +
                        (string.IsNullOrWhiteSpace(EasyMicApmBridgeRegistry.LastTokenSource()) ? "<unknown>" : EasyMicApmBridgeRegistry.LastTokenSource());
            }

            LogApmIssue(error);
            return false;
        }

        private static void LogApmIssue(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || string.Equals(s_lastApmUnavailableReason, message, StringComparison.Ordinal))
            {
                return;
            }

            s_lastApmUnavailableReason = message;
            Debug.LogWarning("[EasyMic] " + message);
        }

        private AudioProcessingOptions GetCurrentAudioProcessingOptions()
        {
            var apmWorker = GetCurrentApmWorker();
            if (apmWorker == null)
            {
                return _audioProcessingOptions;
            }

            apmWorker.GetProcessingOptions(
                out bool enableAEC,
                out bool enableANS,
                out bool enableAGC);

            var runtimeOptions = new AudioProcessingOptions(enableAEC, enableANS, enableAGC);

            // Keep serialized staging options in sync with the active runtime worker state.
            _audioProcessingOptions = runtimeOptions;
            return runtimeOptions;
        }

        private void SetAudioProcessingOptions(AudioProcessingOptions options)
        {
            _audioProcessingOptions = options;

            var apmWorker = GetCurrentApmWorker();
            if (apmWorker == null)
            {
                return;
            }

            apmWorker.SetProcessingOptions(options.EnableAEC, options.EnableANS, options.EnableAGC);
        }

        private IEasyMicApmWorkerBridge GetCurrentApmWorker()
        {
            if (!IsRecording || !_recordingHandle.IsValid || _pipelineBlueprint == null)
            {
                return null;
            }

            AudioPipeline pipeline;
            try
            {
                pipeline = EasyMicAPI.GetProcessor<AudioPipeline>(_recordingHandle, _pipelineBlueprint);
            }
            catch
            {
                return null;
            }

            if (pipeline == null)
            {
                return null;
            }

            return pipeline.GetWorker<IEasyMicApmWorkerBridge>();
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
