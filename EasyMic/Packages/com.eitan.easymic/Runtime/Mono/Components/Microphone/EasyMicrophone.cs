namespace Eitan.EasyMic.Runtime.Mono
{

    using System;
    using System.Linq;
    using UnityEngine;
#if EASYMIC_APM_INTEGRATION
    using Eitan.EasyMic.Runtime.Apm;
    using Eitan.EasyMic.Runtime.Exceptions;
    using System.Collections;
#endif


    [AddComponentMenu("Input/Easy Microphone")]
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

        #region  Constant
        private int MAX_RECORDING_DURATION = 30;
        #endregion

        #region  Private  Fields

        private AudioWorkerBlueprint _pipelineBlueprint;

        private RecordingHandle _recordingHandle;
        private AudioCapturer _capturer;


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

        public bool IsRecording => EasyMicAPI.IsDeviceRecording(_deviceOptions.Device);

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
            public MicDevice Device;
            public Channel Channel;
            public SampleRate SampleRate;

            public DeviceOptions(MicDevice device, Channel channel, SampleRate sampleRate)
            {
                this.Device = device;
                this.Channel = channel;
                this.SampleRate = sampleRate;
            }

            public static DeviceOptions Default
            {
                get
                {
                    var defaultDevice = EasyMicAPI.Default;
                    if (defaultDevice.HasValidId)
                    {
                        return new DeviceOptions(defaultDevice, defaultDevice.GetPreferredChannel(), defaultDevice.GetPreferredSampleRate());
                    }
                    else
                    {
                        throw new EasyMicDeviceNotFoundException("No default microphone devices found !");
                    }
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
                UnityEngine.Debug.LogWarning($"The device: {_deviceOptions.Device}. is recording, please stop it first.");
                return false;
            }
            if (EasyMicAPI.IsDeviceRecording(device))
            {
                UnityEngine.Debug.LogWarning($"The device: {device}. is recording, please stop it first.");
                return false;
            }
            _deviceOptions.Device = device;
            if (sampleRateOverride != null && sampleRateOverride.HasValue)
            {
                _deviceOptions.SampleRate = sampleRateOverride.Value;
            }
            if (channelOverride != null && channelOverride.HasValue)
            {
                _deviceOptions.Channel = channelOverride.Value;
            }

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
            if (!_deviceOptions.Device.HasValidId || !devices.Any(d => d.Name == _deviceOptions.Device.Name))
            {
                _deviceOptions.Device = defaultDevice;
                if (_deviceOptions.Device.HasValidId)
                {
                    _deviceOptions.Channel = _deviceOptions.Device.GetPreferredChannel();
                    _deviceOptions.SampleRate = _deviceOptions.Device.GetPreferredSampleRate();
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
        #endregion

        #region Private Methods


        private void InternalMicrophoneInitialization()

        {

            InternalBuildAudioPipelineBlueprint();
            OnInitialization();
        }
        private void InternalStartRecordingHandler()
        {
            var device = _deviceOptions.Device;

            if (!device.HasValidId)
            {
                SelectDefaultDevice();
                if (!device.HasValidId)
                {
                    throw new EasyMicDeviceNotFoundException("No microphone device available.");
                }
            }

            if (_pipelineBlueprint == null)
            {
                UnityEngine.Debug.LogError("Recognition pipeline blueprint is missing.");
                return;
            }

            _recordingHandle = EasyMicAPI.StartRecording(_deviceOptions.Device, _deviceOptions.SampleRate, _deviceOptions.Channel);
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
            OnStopRecording(_recordingHandle);
            _recordingHandle = default;
            OnRecordingStateChanged?.Invoke(false);
        }

        private void InternalDevicesChangedHandler(MicDevicesChangedEventArgs _)
        {
            var wasRecording = IsRecording;

            var currentStillPresent = _deviceOptions.Device.HasValidId && EasyMicAPI.Devices.Any(d => d.Name == _deviceOptions.Device.Name);

            if (!currentStillPresent && IsRecording)
            {
                StopRecording();
            }
            if (_microphoneOptions.autoFallback)
            {
                SelectDefaultDevice();

                if (_deviceOptions.Device.HasValidId && ((wasRecording && !IsRecording)))
                {
                    StartRecording();
                }

                OnDevicesChanged(_);
            }
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

                // if (_capturer == null) {
                //     _capturer = new AudioCapturer();
                // }
                if (_capturer == null)
                {
                    _capturer = new AudioCapturer(MAX_RECORDING_DURATION);
                }
                pipeline.AddWorker(_capturer);

                // if (_config.VolumeDbThreshold < 0f)
                // {
                //     pipeline.AddWorker(new VolumeGateFilter { ThresholdDb = _config.VolumeDbThreshold });
                // }

                // var acousticDetector = new AcousticEndpointDetector();
                // acousticDetector.OnSpeechStateChanged = OnAcousticSpeechStateChanged;
                // acousticDetector.OnEndpointDetected = OnAcousticEndpointDetected;
                // pipeline.AddWorker(acousticDetector);
                // _acousticEndpointDetector = acousticDetector;

                // if (_requiresOffline && _vadService != null)
                // {
                //     var vadFilter = new SherpaVoiceFilter(_vadService);
                //     vadFilter.OnVoiceActivityChanged += HandleVoiceActivity;
                //     pipeline.AddWorker(vadFilter);
                // }

                // if (_keywordService != null)
                // {
                //     var kws = new SherpaKeywordDetector(_keywordService);
                //     kws.OnKeywordDetected += HandleWakeWord;
                //     pipeline.AddWorker(kws);
                // }

                // if (_streamingService != null)
                // {
                //     var online = new SherpaRealtimeSpeechRecognizer(_streamingService);
                //     online.OnRecognitionResult += HandleStreamingRecognition;
                //     pipeline.AddWorker(online);
                // }

                // if (_offlineService != null)
                // {
                //     var offline = new SherpaOfflineSpeechRecognizer(_offlineService);
                //     offline.OnRecognitionResult += HandleOfflineRecognition;
                //     pipeline.AddWorker(offline);
                // }

                return pipeline;
            });
        }

        #endregion

    }
}