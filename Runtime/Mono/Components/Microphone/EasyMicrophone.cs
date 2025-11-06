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

        // Optional: directory to save streaming recordings. If empty, defaults to Application.persistentDataPath + "/EasyMicRecordings"
        [SerializeField] private string _recordingDirectory = string.Empty;

        // Segment rollover size to avoid 4GB RIFF WAV limits (bytes). Default ~2GB safety margin.
        [SerializeField] private long _segmentSizeBytes = 2L * 1024 * 1024 * 1024 - (8L * 1024);

        #region  Private  Fields

        private AudioWorkerBlueprint _pipelineBlueprint;

        private RecordingHandle _recordingHandle;
        private AudioCapturer _capturer;
        private AudioClip _latestRecordingClip;
        private string _latestRecordingPath;
        private StreamingWavWriter _streamingWriter;


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
            _streamingWriter?.Dispose();
            _streamingWriter = null;
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

            // Prepare streaming writer (lazy-open on first samples)
            if (string.IsNullOrEmpty(_recordingDirectory))
            {
                _recordingDirectory = System.IO.Path.Combine(Application.persistentDataPath, "EasyMicRecordings");
            }
            try { if (!System.IO.Directory.Exists(_recordingDirectory)) { System.IO.Directory.CreateDirectory(_recordingDirectory); } } catch { }
            string fileBase = $"{name}_Recording_{DateTime.Now:yyyyMMdd_HHmmss}";
            _streamingWriter = new StreamingWavWriter(_recordingDirectory, fileBase, _segmentSizeBytes);

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
            // finalize streaming file(s)
            _streamingWriter?.Dispose();
            _streamingWriter = null;
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
                    _capturer = new AudioCapturer();
                }
                pipeline.AddWorker(_capturer);

                return pipeline;
            });
        }

        /// <summary>
        /// Streaming 16-bit PCM WAV writer with safe segment rollover to avoid 4GB RIFF limits.
        /// Thread-affinity: methods may be called from the audio thread.
        /// </summary>
        private sealed class StreamingWavWriter : IAudioSink, IDisposable
        {
            private readonly string _directory;
            private readonly string _fileBase;
            private readonly long _segmentLimit;

            private System.IO.FileStream _fs;
            private string _currentPath;
            private int _segmentIndex = -1;

            private int _sampleRate;
            private int _channels;
            private long _dataBytesWritten;

            private byte[] _scratch; // reused conversion buffer

            public string CurrentPath => _currentPath;

            public StreamingWavWriter(string directory, string fileBase, long segmentLimit)
            {
                _directory = directory;
                _fileBase = fileBase;
                _segmentLimit = Math.Max(128 * 1024, segmentLimit);
            }

            public void OnSamples(ReadOnlySpan<float> samples, int sampleRate, int channels)
            {
                if (samples.IsEmpty)
                {
                    return;
                }


                if (_fs == null)
                {
                    _sampleRate = Math.Max(8000, sampleRate);
                    _channels = Math.Max(1, channels);
                    OpenNextSegment();
                }

                // Bytes needed for this block
                int needed = samples.Length * sizeof(short);
                if (_scratch == null || _scratch.Length < needed)
                {
                    _scratch = new byte[needed];
                }

                // Convert float [-1,1] -> int16 little-endian in-place into _scratch
                int outIdx = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    float f = samples[i];
                    if (f > 1f)
                    {
                        f = 1f;
                    }
                    else if (f < -1f)
                    {
                        f = -1f;
                    }


                    short v = (short)Mathf.RoundToInt(f * short.MaxValue);
                    _scratch[outIdx++] = (byte)(v & 0xFF);
                    _scratch[outIdx++] = (byte)((v >> 8) & 0xFF);
                }

                // Rollover if this write would exceed segment limit (account for header size)
                const int HeaderSize = 44;
                if (_dataBytesWritten + needed + HeaderSize > _segmentLimit)
                {
                    CloseCurrentSegment();
                    OpenNextSegment();
                }

                _fs.Write(_scratch, 0, needed);
                _dataBytesWritten += needed;
            }

            private void OpenNextSegment()
            {
                _segmentIndex++;
                _currentPath = System.IO.Path.Combine(_directory, _fileBase + $"_part{_segmentIndex:000}.wav");
                _fs = new System.IO.FileStream(_currentPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read, 1 << 20, System.IO.FileOptions.SequentialScan);
                WriteWavHeader(_fs, _sampleRate, _channels, 0); // placeholder sizes
                _dataBytesWritten = 0;
            }

            private static void WriteWavHeader(System.IO.FileStream fs, int sampleRate, int channels, int dataBytes)
            {
                int byteRate = sampleRate * channels * sizeof(short);
                int blockAlign = channels * sizeof(short);
                int riffSize = 36 + dataBytes;

                using var bw = new System.IO.BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(riffSize);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write((short)blockAlign);
                bw.Write((short)16);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                bw.Write(dataBytes);
            }

            private void CloseCurrentSegment()
            {
                if (_fs == null)
                {
                    return;
                }
                // Patch sizes into the header

                long totalBytes = _dataBytesWritten;
                _fs.Seek(0, System.IO.SeekOrigin.Begin);
                WriteWavHeader(_fs, _sampleRate, _channels, (int)Math.Min(int.MaxValue, totalBytes));
                _fs.Flush(true);
                _fs.Dispose();
                _fs = null;
            }

            public void Dispose()
            {
                CloseCurrentSegment();
            }
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
