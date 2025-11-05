namespace Eitan.EasyMic.Runtime.Mono
{

    using System;
    using System.IO;
    using System.Linq;
    using UnityEngine;
#if EASYMIC_APM_INTEGRATION
    using Eitan.EasyMic.Runtime.Apm;
    using Eitan.EasyMic.Runtime.Exceptions;
    using System.Collections;
#endif

    [AddComponentMenu("Audio/Input/Easy Microphone")]
    public class EasyMicrophone : MonoBehaviour
    {

        #region Configuration
        [SerializeField]
        private MicrophoneOptions _microphoneOptions = MicrophoneOptions.Default;

        [SerializeField] private DeviceOptions _deviceOptions = DeviceOptions.Default;
#if  EASYMIC_APM_INTEGRATION       
        [SerializeField] private AudioProcessingOptions _audioProcessingOptions = AudioProcessingOptions.Default;
#endif
        #endregion

        #region  Internal Fields

        [SerializeField, Range(5, 300)]
        private int _maxRecordingDurationSeconds = 30;

        #region  Private  Fields

        private AudioWorkerBlueprint _pipelineBlueprint;

        private RecordingHandle _recordingHandle;
        private AudioCapturer _capturer;
        private AudioClip _latestRecordingClip;
        private string _latestRecordingPath;


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

#if EASYMIC_APM_INTEGRATION
        public AudioProcessingOptions AudioProcessingOpts => _audioProcessingOptions;
#endif

        #endregion
        #region  Event
        public event Action<bool> OnRecordingStateChanged;

        public event Action<bool> OnMicrophoneInitialized;
        #endregion


        #region Structure 

        [Serializable]
        public struct MicrophoneOptions
        {

            public bool recordOnAwake;// Automatically start Recording when "MonoBehaviour" called "OnAwake"
            public bool autoFallback;// If the current device is disconnected, it will automatically fall back to another MicDevice;

            public MicrophoneOptions(bool recordOnAwake, bool autoFallback)
            {
                this.recordOnAwake = recordOnAwake;
                this.autoFallback = autoFallback;
            }
            public static MicrophoneOptions Default
            {
                get
                {
                    return new MicrophoneOptions(true, true);
                }
            }
        }

        [Serializable]
        public struct DeviceOptions
        {
            public string DeviceName;
            public Channel Channel;
            public SampleRate SampleRate;

            public DeviceOptions(string deviceName, Channel channel, SampleRate sampleRate)
            {
                DeviceName = deviceName;
                Channel = channel;
                SampleRate = sampleRate;
            }

            public DeviceOptions(MicDevice device, Channel channel, SampleRate sampleRate)
            {
                DeviceName = device.Name;
                Channel = channel;
                SampleRate = sampleRate;
            }

            public bool HasDeviceName => !string.IsNullOrEmpty(DeviceName);

            public static DeviceOptions Default
            {
                get
                {
                    var defaultDevice = EasyMicAPI.Default;
                    if (!defaultDevice.HasValidId)
                    {
                        throw new EasyMicDeviceNotFoundException("No default microphone devices found !");
                    }

                    return new DeviceOptions(defaultDevice.Name, defaultDevice.GetPreferredChannel(), defaultDevice.GetPreferredSampleRate());
                }
            }

        }

#if EASYMIC_APM_INTEGRATION
        /// <summary>
        /// Optional WebRTC audio preprocessing profile applied when the EasyMic APM package is installed.
        /// </summary>
        [Serializable]
        public struct AudioProcessingOptions
        {
            public bool EnableAEC;
            public bool EnableANS;
            public bool EnableAGC;

            public bool AnyEnabled => EnableAEC || EnableANS || EnableAGC;
            public AudioProcessingOptions(bool enableAEC, bool enableANS, bool enableAGC)
            {
                EnableAEC = enableAEC;
                EnableANS = enableANS;
                EnableAGC = enableAGC;
            }
            public static AudioProcessingOptions Default => new AudioProcessingOptions(true, true, true);
            public static AudioProcessingOptions Disable => new AudioProcessingOptions(false, false, false);
            public static AudioProcessingOptions AECOnly => new AudioProcessingOptions(true, false, false);
            public static AudioProcessingOptions ANSOnly => new AudioProcessingOptions(false, true, false);
            public static AudioProcessingOptions AGCOnly => new AudioProcessingOptions(false, true, true);
        }
#endif
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
            EasyMicAPI.StopAllRecordings();
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
        #region Public Methods

        public void Init()
        {
            InternalMicrophoneInitialization();
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
            InternalStartRecordingHandler();
        }
        /// <summary>
        /// Starts recording using a specific microphone device and optional channel override.
        /// </summary>
        public bool StartRecording(MicDevice device, SampleRate? sampleRateOverride = null, Channel? channelOverride = null)
        {
            if (!device.HasValidId)
            {
                UnityEngine.Debug.LogWarning("Cannot start recording: provided device is not valid.");
                return false;
            }

            if (IsRecording)
            {
                UnityEngine.Debug.LogWarning("A recording session is already active. Stop it before starting a new one.");
                return false;
            }
            if (EasyMicAPI.IsDeviceRecording(device))
            {
                UnityEngine.Debug.LogWarning($"The device: {device}. is recording, please stop it first.");
                return false;
            }
            var channelToUse = channelOverride.HasValue
                ? channelOverride.Value
                : device.GetPreferredChannel(_deviceOptions.Channel);

            var sampleRateToUse = sampleRateOverride.HasValue
                ? sampleRateOverride.Value
                : device.GetPreferredSampleRate(_deviceOptions.SampleRate);

            _deviceOptions = new DeviceOptions(device.Name, channelToUse, sampleRateToUse);

            InternalStartRecordingHandler();

            return true;

        }

        /// <summary>
        /// Stops the active recording session and clears pending recognition buffers.
        /// </summary>
        public void StopRecording() => InternalStopRecordingHandler();

        /// <summary>
        /// Hot-inserts an additional audio worker into the active pipeline.
        /// </summary>
        public void AppendProcessor(AudioWorkerBlueprint blueprint)
        {
            if (!IsRecording)
            {
                UnityEngine.Debug.LogWarning("Cannot add processor: not recording.");
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
                UnityEngine.Debug.LogWarning("Cannot remove processor: not recording.");
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
        #endregion

        #region Private Methods


        private void InternalMicrophoneInitialization()

        {

            InternalBuildAudioPipelineBlueprint();
            OnInitialization();
        }
        private void InternalStartRecordingHandler()
        {
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
                UnityEngine.Debug.LogError("Recognition pipeline blueprint is missing.");
                return;
            }

            var deviceName = string.IsNullOrEmpty(_deviceOptions.DeviceName) ? null : _deviceOptions.DeviceName;

            _recordingHandle = EasyMicAPI.StartRecording(deviceName, _deviceOptions.SampleRate, _deviceOptions.Channel);
            EasyMicAPI.AddProcessor(_recordingHandle, _pipelineBlueprint);
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

                var downmixer = new AudioDownmixer();
                pipeline.AddWorker(downmixer);
                OnAudioPiplineBuild(pipeline);

                if (_capturer == null)
                {
                    _capturer = new AudioCapturer(Mathf.Max(5, _maxRecordingDurationSeconds));
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
            if (!HasRecordedClip)
            {
                UnityEngine.Debug.LogWarning("EasyMicrophone: No recorded audio available to save.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                UnityEngine.Debug.LogWarning("EasyMicrophone: Destination path is empty.");
                return false;
            }

            var finalPath = EnsureWavExtension(destinationPath);

            try
            {
                var directory = Path.GetDirectoryName(finalPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!overwrite && File.Exists(finalPath))
                {
                    UnityEngine.Debug.LogWarning($"EasyMicrophone: Target file already exists and overwrite is disabled. Path: {finalPath}");
                    return false;
                }

                WriteClipToWav(_latestRecordingClip, finalPath);
                _latestRecordingPath = finalPath;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"EasyMicrophone: Failed to save recording. {ex.Message}");
                return false;
            }
        }

        private static string EnsureWavExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var extension = Path.GetExtension(path);
            return string.IsNullOrEmpty(extension) || !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                ? path + ".wav"
                : path;
        }

        private static void WriteClipToWav(AudioClip clip, string path)
        {
            const int headerSize = 44;
            var channels = Mathf.Max(1, clip.channels);
            var sampleRate = Mathf.Max(8000, clip.frequency);
            var samplesPerChannel = Mathf.Max(1, clip.samples);

            var totalSamples = samplesPerChannel * channels;

            var samples = new float[totalSamples];
            clip.GetData(samples, 0);

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            fileStream.Seek(headerSize, SeekOrigin.Begin);

            var bytes = new byte[2];
            for (int i = 0; i < totalSamples; i++)
            {
                var clamped = Mathf.Clamp(samples[i], -1f, 1f);
                short value = (short)Mathf.RoundToInt(clamped * short.MaxValue);
                bytes[0] = (byte)(value & 0xFF);
                bytes[1] = (byte)((value >> 8) & 0xFF);
                fileStream.Write(bytes);
            }

            int byteRate = sampleRate * channels * sizeof(short);
            int dataSize = totalSamples * sizeof(short);
            int fileSize = dataSize + headerSize - 8;

            fileStream.Seek(0, SeekOrigin.Begin);
            using var writer = new BinaryWriter(fileStream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
            writer.Write(fileSize);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * sizeof(short)));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
            writer.Write(dataSize);
        }

        #endregion

    }
}
