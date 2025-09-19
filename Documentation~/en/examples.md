← [Troubleshooting](troubleshooting.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/examples.md) →

# 🚀 Examples & Use Cases

Real-world implementations and patterns for Easy Mic integration. Learn from complete examples that solve common audio recording challenges.

> Note: API uses AudioWorkerBlueprint for adding/removing processors. Create a blueprint with a stable `key`, pass it to `AddProcessor`, and use `GetProcessor<T>(handle, blueprint)` to access the runtime instance for reading data or updating parameters.

## 🎙️ Basic Recording Examples

### Simple Voice Recording
The most basic implementation for voice recording:

```csharp
using UnityEngine;
using Eitan.EasyMic;

public class SimpleVoiceRecorder : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private AudioWorkerBlueprint _bpCapture;
    
    [Header("Recording Settings")]
    public SampleRate sampleRate = SampleRate.Hz16000;
    public float maxDuration = 10f;
    
    void Start()
    {
        // Ensure permissions
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogError("❌ Microphone permission not granted.");
            return;
        }
        
        StartRecording();
    }
    
    private void StartRecording()
    {
        // Refresh devices and start recording
        EasyMicAPI.Refresh();
        _recordingHandle = EasyMicAPI.StartRecording(sampleRate);
        
        if (_recordingHandle.IsValid)
        {
            // Add capturer via blueprint to save audio
            _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer((int)maxDuration), key: "capture");
            EasyMicAPI.AddProcessor(_recordingHandle, _bpCapture);
            
            var info = EasyMicAPI.GetRecordingInfo(_recordingHandle);
            Debug.Log($"🎙️ Recording started with {info.Device.Name}");
        }
        else
        {
            Debug.LogError("❌ Failed to start recording");
        }
    }
    
    public void StopRecording()
    {
        if (_recordingHandle.IsValid)
        {
            EasyMicAPI.StopRecording(_recordingHandle);
            
            // Get recorded audio
            var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_recordingHandle, _bpCapture);
            AudioClip clip = capturer?.GetCapturedAudioClip();
            if (clip != null)
            {
                Debug.Log($"✅ Recorded {clip.length:F1}s of audio");
                
                // Play back the recording
                var audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                    audioSource = gameObject.AddComponent<AudioSource>();
                    
                audioSource.clip = clip;
                audioSource.Play();
            }
        }
    }
    
    void OnDestroy()
    {
        if (_recordingHandle.IsValid)
            EasyMicAPI.StopRecording(_recordingHandle);
        // workers are disposed with the session
    }
}
```

### High-Quality Stereo Recording
For music or high-fidelity applications:

```csharp
public class HiFiRecorder : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioWorkerBlueprint _bpCapture;
    
    void Start()
    {
        // Use highest quality settings
        EasyMicAPI.Refresh();
        
        // Find best available device
        var devices = EasyMicAPI.Devices;
        var bestDevice = devices.FirstOrDefault(d => d.MaxChannels >= 2) ?? devices[0];
        
        // Start high-quality recording
        _handle = EasyMicAPI.StartRecording(
            bestDevice,
            SampleRate.Hz48000,  // Professional quality
            Channel.Stereo       // Full stereo capture
        );
        
        if (_handle.IsValid)
        {
            _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(60), key: "capture");
            EasyMicAPI.AddProcessor(_handle, _bpCapture);
            
            Debug.Log($"🎼 High-quality recording: {bestDevice.Name} @ 48kHz Stereo");
        }
    }
    
    public void SaveToFile(string filename)
    {
        var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_handle, _bpCapture);
        var samples = capturer?.GetCapturedAudioSamples();
        AudioExtension.SaveWAV(filename, samples, 48000, 2);
        Debug.Log($"💾 Saved to {filename}");
    }
}
```

## 🔊 Real-Time Audio Processing

### Live Voice Effects
Apply real-time effects to voice:

```csharp
public class LiveVoiceEffects : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private SimpleReverb _reverb;
    private PitchShifter _pitchShifter;
    private LoopbackPlayer _monitor;
    
    [Header("Effect Controls")]
    [Range(-60f, 0f)]
    public float gateThreshold = -35f;
    
    [Range(0f, 1f)]
    public float reverbMix = 0.3f;
    
    [Range(0.5f, 2f)]
    public float pitchShift = 1f;
    
    [Range(0f, 1f)]
    public float monitorVolume = 0.5f;
    
    void Start()
    {
        SetupRecording();
    }
    
    void SetupRecording()
    {
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(SampleRate.Hz44100);
        
        if (_handle.IsValid)
        {
            // Build effects chain
            _noiseGate = new VolumeGateFilter 
            { 
                ThresholdDb = gateThreshold,
                AttackTime = 0.001f,   // Fast attack for speech
                ReleaseTime = 0.2f     // Smooth release
            };
            
            _reverb = new SimpleReverb 
            { 
                Mix = reverbMix,
                RoomSize = 0.5f
            };
            
            _pitchShifter = new PitchShifter 
            { 
                PitchRatio = pitchShift 
            };
            
            _monitor = new LoopbackPlayer 
            { 
                Volume = monitorVolume 
            };
            
            // Add processors in optimal order
            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _pitchShifter);
            EasyMicAPI.AddProcessor(_handle, _reverb);
            EasyMicAPI.AddProcessor(_handle, _monitor);
            
            Debug.Log("🎤 Live voice effects active");
        }
    }
    
    void Update()
    {
        // Update effect parameters in real-time
        if (_noiseGate != null) _noiseGate.ThresholdDb = gateThreshold;
        if (_reverb != null) _reverb.Mix = reverbMix;
        if (_pitchShifter != null) _pitchShifter.PitchRatio = pitchShift;
        if (_monitor != null) _monitor.Volume = monitorVolume;
    }
}
```

### Real-Time Audio Visualization
Visualize audio levels and frequency content:

```csharp
public class AudioVisualizer : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeAnalyzer _volumeAnalyzer;
    private SpectrumAnalyzer _spectrumAnalyzer;
    
    [Header("UI References")]
    public Slider volumeMeter;
    public Image[] spectrumBars = new Image[32];
    public Text volumeText;
    
    [Header("Visualization Settings")]
    public float volumeSmoothing = 0.3f;
    public float spectrumSmoothing = 0.5f;
    
    private float _smoothedVolume;
    private float[] _smoothedSpectrum;
    
    void Start()
    {
        _smoothedSpectrum = new float[spectrumBars.Length];
        SetupAudioAnalysis();
    }
    
    void SetupAudioAnalysis()
    {
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(SampleRate.Hz44100);
        
        if (_handle.IsValid)
        {
            // Add analysis processors
            _volumeAnalyzer = new VolumeAnalyzer();
            _spectrumAnalyzer = new SpectrumAnalyzer(spectrumBars.Length);
            
            EasyMicAPI.AddProcessor(_handle, _volumeAnalyzer);
            EasyMicAPI.AddProcessor(_handle, _spectrumAnalyzer);
            
            Debug.Log("📊 Audio visualization active");
        }
    }
    
    void Update()
    {
        if (_volumeAnalyzer == null || _spectrumAnalyzer == null) return;
        
        // Update volume meter
        float currentVolume = _volumeAnalyzer.GetRMSVolume();
        _smoothedVolume = Mathf.Lerp(_smoothedVolume, currentVolume, volumeSmoothing);
        
        volumeMeter.value = _smoothedVolume;
        volumeText.text = $"{_volumeAnalyzer.GetRMSVolumeDb():F1} dB";
        
        // Update spectrum display
        var spectrum = _spectrumAnalyzer.GetSpectrum();
        for (int i = 0; i < spectrumBars.Length && i < spectrum.Length; i++)
        {
            _smoothedSpectrum[i] = Mathf.Lerp(_smoothedSpectrum[i], spectrum[i], spectrumSmoothing);
            
            // Update bar height (assuming vertical bars)
            var rectTransform = spectrumBars[i].rectTransform;
            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, _smoothedSpectrum[i] * 100f);
        }
    }
}

// Supporting analyzer classes
public class VolumeAnalyzer : AudioReader
{
    private float _rmsVolume;
    private float _peakVolume;
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        float sum = 0f;
        float peak = 0f;
        
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = Math.Abs(buffer[i]);
            sum += sample * sample;
            if (sample > peak) peak = sample;
        }
        
        _rmsVolume = MathF.Sqrt(sum / buffer.Length);
        _peakVolume = peak;
    }
    
    public float GetRMSVolume() => _rmsVolume;
    public float GetPeakVolume() => _peakVolume;
    public float GetRMSVolumeDb() => 20f * MathF.Log10(_rmsVolume + 1e-10f);
}

public class SpectrumAnalyzer : AudioReader
{
    private readonly int _fftSize;
    private readonly float[] _spectrum;
    private readonly Complex[] _fftBuffer;
    private int _bufferIndex;
    
    public SpectrumAnalyzer(int spectrumSize)
    {
        _fftSize = NextPowerOfTwo(spectrumSize * 2);
        _spectrum = new float[spectrumSize];
        _fftBuffer = new Complex[_fftSize];
    }
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // Simplified FFT implementation
        // In practice, you'd use a proper FFT library
        for (int i = 0; i < buffer.Length && _bufferIndex < _fftSize; i++)
        {
            _fftBuffer[_bufferIndex] = new Complex(buffer[i], 0);
            _bufferIndex++;
        }
        
        if (_bufferIndex >= _fftSize)
        {
            // Perform FFT and update spectrum
            FFT.ForwardTransform(_fftBuffer);
            
            for (int i = 0; i < _spectrum.Length; i++)
            {
                _spectrum[i] = (float)_fftBuffer[i].Magnitude;
            }
            
            _bufferIndex = 0;
        }
    }
    
    public float[] GetSpectrum() => _spectrum;
    
    private static int NextPowerOfTwo(int n)
    {
        int power = 1;
        while (power < n) power *= 2;
        return power;
    }
}
```

## 🤖 Voice Assistant Integration

### Speech-to-Text with Commands
Integrate speech recognition for voice commands:

```csharp
public class VoiceCommandSystem : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private SherpaRealtimeSpeechRecognizer _speechRecognizer;
    
    [Header("Speech Recognition")]
    public string modelPath = "path/to/sherpa/model";
    public float confidenceThreshold = 0.7f;
    
    [Header("Commands")]
    public UnityEvent<string> OnCommandRecognized;
    
    private readonly Dictionary<string, System.Action> _commands = new Dictionary<string, System.Action>();
    
    void Start()
    {
        RegisterCommands();
        SetupSpeechRecognition();
    }
    
    void RegisterCommands()
    {
        _commands["start recording"] = () => StartRecording();
        _commands["stop recording"] = () => StopRecording();
        _commands["play music"] = () => PlayMusic();
        _commands["turn on lights"] = () => ControlLights(true);
        _commands["turn off lights"] = () => ControlLights(false);
        _commands["what time is it"] = () => SpeakTime();
    }
    
    void SetupSpeechRecognition()
    {
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(SampleRate.Hz16000); // Optimal for speech
        
        if (_handle.IsValid)
        {
            // Noise gate for better recognition
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = -30f,
                AttackTime = 0.01f,
                ReleaseTime = 0.3f
            };
            
            // Speech recognizer
            _speechRecognizer = new SherpaRealtimeSpeechRecognizer(modelPath);
            _speechRecognizer.OnFinalResult += OnSpeechRecognized;
            _speechRecognizer.OnPartialResult += OnPartialSpeech;
            
            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _speechRecognizer);
            
            Debug.Log("🗣️ Voice command system ready");
        }
    }
    
    private void OnPartialSpeech(string text)
    {
        Debug.Log($"Listening: {text}");
    }
    
    private void OnSpeechRecognized(string text)
    {
        Debug.Log($"Recognized: {text}");
        
        if (_speechRecognizer.LastConfidence < confidenceThreshold)
        {
            Debug.Log($"Low confidence ({_speechRecognizer.LastConfidence:F2}), ignoring");
            return;
        }
        
        // Find matching command
        string lowerText = text.ToLower();
        foreach (var command in _commands)
        {
            if (lowerText.Contains(command.Key))
            {
                Debug.Log($"Executing command: {command.Key}");
                command.Value.Invoke();
                OnCommandRecognized?.Invoke(command.Key);
                return;
            }
        }
        
        Debug.Log($"Unknown command: {text}");
    }
    
    // Command implementations
    private void StartRecording() => Debug.Log("▶️ Starting recording...");
    private void StopRecording() => Debug.Log("⏹️ Stopping recording...");
    private void PlayMusic() => Debug.Log("🎵 Playing music...");
    private void ControlLights(bool on) => Debug.Log($"💡 Lights {(on ? "ON" : "OFF")}");
    private void SpeakTime() => Debug.Log($"🕐 The time is {DateTime.Now:HH:mm}");
}
```

## 🎮 Game Integration Examples

### Voice Chat for Multiplayer
Real-time voice communication:

```csharp
public class VoiceChatManager : MonoBehaviourPunPV, IPunObservable
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private AudioCapturer _capturer;
    private VoiceTransmitter _transmitter;
    
    [Header("Voice Chat Settings")]
    public bool pushToTalk = false;
    public KeyCode talkKey = KeyCode.T;
    public float transmissionRate = 10f; // Packets per second
    
    private bool _isTransmitting;
    private float _lastTransmissionTime;
    
    void Start()
    {
        if (photonView.isMine)
        {
            SetupVoiceCapture();
        }
        else
        {
            SetupVoicePlayback();
        }
    }
    
    void SetupVoiceCapture()
    {
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(SampleRate.Hz22050);
        
        if (_handle.IsValid)
        {
            // Voice optimization
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = -40f,  // Sensitive gate for voice chat
                AttackTime = 0.005f,
                ReleaseTime = 0.1f
            };
            
            _capturer = new AudioCapturer(1); // 1 second buffer
            _transmitter = new VoiceTransmitter(this);
            
            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _capturer);
            EasyMicAPI.AddProcessor(_handle, _transmitter);
            
            Debug.Log("🎤 Voice chat capture ready");
        }
    }
    
    void Update()
    {
        if (!photonView.isMine) return;
        
        bool shouldTransmit = !pushToTalk || Input.GetKey(talkKey);
        
        if (shouldTransmit != _isTransmitting)
        {
            _isTransmitting = shouldTransmit;
            _transmitter.SetTransmitting(shouldTransmit);
            
            Debug.Log($"🎙️ Voice transmission: {(shouldTransmit ? "ON" : "OFF")}");
        }
        
        // Send voice data at regular intervals
        if (_isTransmitting && Time.time - _lastTransmissionTime > 1f / transmissionRate)
        {
            SendVoiceData();
            _lastTransmissionTime = Time.time;
        }
    }
    
    void SendVoiceData()
    {
        var audioData = _capturer.GetCapturedAudioSamples();
        if (audioData.Length > 0)
        {
            // Compress and send voice data
            byte[] compressedData = VoiceCompression.Compress(audioData);
            photonView.RPC("ReceiveVoiceData", RpcTarget.Others, compressedData);
            _capturer.Clear();
        }
    }
    
    [PunRPC]
    void ReceiveVoiceData(byte[] compressedData)
    {
        // Decompress and play voice data
        float[] audioData = VoiceCompression.Decompress(compressedData);
        PlayVoiceClip(audioData);
    }
    
    void PlayVoiceClip(float[] audioData)
    {
        var audioSource = GetComponent<AudioSource>();
        if (audioSource == null) return;
        
        // Create and play audio clip
        var clip = AudioExtension.CreateAudioClip(audioData, 22050, 1, "VoiceChat");
        audioSource.clip = clip;
        audioSource.Play();
    }
    
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        // Sync voice transmission state
        if (stream.IsWriting)
        {
            stream.SendNext(_isTransmitting);
        }
        else
        {
            bool remoteTransmitting = (bool)stream.ReceiveNext();
            // Update UI to show who's talking
            UpdateTalkingIndicator(remoteTransmitting);
        }
    }
    
    void UpdateTalkingIndicator(bool talking)
    {
        // Show/hide talking indicator UI
        var indicator = transform.Find("TalkingIndicator");
        if (indicator != null)
            indicator.gameObject.SetActive(talking);
    }
}
```

### Voice-Controlled Character
Control game character with voice commands:

```csharp
public class VoiceControlledCharacter : MonoBehaviour
{
    private RecordingHandle _handle;
    private SimpleCommandRecognizer _commandRecognizer;
    private CharacterController _characterController;
    
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpForce = 8f;
    
    [Header("Voice Commands")]
    public float commandTimeout = 2f;
    
    private Vector3 _moveDirection;
    private bool _isGrounded;
    private float _lastCommandTime;
    
    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        SetupVoiceControl();
    }
    
    void SetupVoiceControl()
    {
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(SampleRate.Hz16000);
        
        if (_handle.IsValid)
        {
            _commandRecognizer = new SimpleCommandRecognizer();
            _commandRecognizer.AddCommand("forward", () => SetMovement(Vector3.forward));
            _commandRecognizer.AddCommand("back", () => SetMovement(Vector3.back));
            _commandRecognizer.AddCommand("left", () => SetMovement(Vector3.left));
            _commandRecognizer.AddCommand("right", () => SetMovement(Vector3.right));
            _commandRecognizer.AddCommand("jump", () => Jump());
            _commandRecognizer.AddCommand("stop", () => Stop());
            
            _commandRecognizer.OnCommandRecognized += OnVoiceCommand;
            
            EasyMicAPI.AddProcessor(_handle, _commandRecognizer);
            Debug.Log("🎮 Voice-controlled character ready");
        }
    }
    
    void OnVoiceCommand(string command)
    {
        Debug.Log($"Voice command: {command}");
        _lastCommandTime = Time.time;
    }
    
    void SetMovement(Vector3 direction)
    {
        _moveDirection = transform.TransformDirection(direction);
    }
    
    void Jump()
    {
        if (_isGrounded)
        {
            _moveDirection.y = jumpForce;
        }
    }
    
    void Stop()
    {
        _moveDirection = Vector3.zero;
    }
    
    void Update()
    {
        // Stop movement if no recent commands
        if (Time.time - _lastCommandTime > commandTimeout)
        {
            _moveDirection.x = 0;
            _moveDirection.z = 0;
        }
        
        // Apply gravity
        _moveDirection.y += Physics.gravity.y * Time.deltaTime;
        
        // Move character
        _characterController.Move(_moveDirection * moveSpeed * Time.deltaTime);
        
        // Check if grounded
        _isGrounded = _characterController.isGrounded;
        if (_isGrounded && _moveDirection.y < 0)
            _moveDirection.y = 0;
    }
}

// Simple pattern-based command recognizer
public class SimpleCommandRecognizer : AudioReader
{
    public event System.Action<string> OnCommandRecognized;
    
    private readonly Dictionary<string, System.Action> _commands = new Dictionary<string, System.Action>();
    private readonly List<float> _audioBuffer = new List<float>();
    private float _lastAnalysisTime;
    private const float AnalysisInterval = 0.5f;
    
    public void AddCommand(string pattern, System.Action action)
    {
        _commands[pattern.ToLower()] = action;
    }
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // Collect audio for analysis
        for (int i = 0; i < buffer.Length; i++)
            _audioBuffer.Add(buffer[i]);
        
        // Analyze periodically
        if (Time.time - _lastAnalysisTime > AnalysisInterval)
        {
            AnalyzeForCommands();
            _lastAnalysisTime = Time.time;
        }
    }
    
    void AnalyzeForCommands()
    {
        if (_audioBuffer.Count == 0) return;
        
        // Simple energy-based command detection
        float energy = 0f;
        foreach (float sample in _audioBuffer)
            energy += sample * sample;
        energy /= _audioBuffer.Count;
        
        if (energy > 0.01f) // Voice detected
        {
            // In a real implementation, you'd use actual speech recognition
            // For this example, we'll simulate command recognition
            var recognizedCommand = SimulateCommandRecognition(energy);
            if (!string.IsNullOrEmpty(recognizedCommand))
            {
                if (_commands.TryGetValue(recognizedCommand, out var action))
                {
                    action.Invoke();
                    OnCommandRecognized?.Invoke(recognizedCommand);
                }
            }
        }
        
        _audioBuffer.Clear();
    }
    
    string SimulateCommandRecognition(float energy)
    {
        // Simplified command simulation based on energy levels
        // In practice, you'd use a proper speech recognition system
        var commands = _commands.Keys.ToArray();
        if (commands.Length > 0)
        {
            int index = Mathf.FloorToInt(energy * 100) % commands.Length;
            return commands[index];
        }
        return null;
    }
}
```

## 📱 Platform-Specific Examples

### Android Voice Notes App
Optimized for mobile recording:

```csharp
public class AndroidVoiceNotes : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioCapturer _capturer;
    private VolumeGateFilter _noiseGate;
    
    [Header("Mobile Optimization")]
    public bool adaptiveBitrate = true;
    public bool backgroundRecording = true;
    
    void Start()
    {
        #if UNITY_ANDROID
        SetupMobileRecording();
        #endif
    }
    
    void SetupMobileRecording()
    {
        // Ensure permissions
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogError("❌ Microphone permission not granted.");
            return;
        }
        
        StartMobileOptimizedRecording();
    }
    
    void StartMobileOptimizedRecording()
    {
        // Optimize for mobile battery life and bandwidth
        SampleRate rate = adaptiveBitrate ? SampleRate.Hz16000 : SampleRate.Hz22050;
        
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(rate, Channel.Mono);
        
        if (_handle.IsValid)
        {
            // Mobile-optimized noise gate
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = -25f,  // Higher threshold for mobile environments
                AttackTime = 0.01f,
                ReleaseTime = 0.3f
            };
            
            _capturer = new AudioCapturer(300); // 5 minutes max
            
            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _capturer);
            
            Debug.Log("📱 Mobile voice recording active");
        }
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        if (backgroundRecording) return;
        
        if (pauseStatus)
        {
            // Pause recording when app goes to background
            if (_handle.IsValid)
                EasyMicAPI.StopRecording(_handle);
        }
        else
        {
            // Resume recording when app returns to foreground
            if (!_handle.IsValid)
                StartMobileOptimizedRecording();
        }
    }
    
    public void SaveVoiceNote(string filename)
    {
        #if UNITY_ANDROID
        // Save to Android external storage
        string path = Path.Combine(Application.persistentDataPath, "VoiceNotes");
        Directory.CreateDirectory(path);
        
        var samples = _capturer.GetCapturedAudioSamples();
        string fullPath = Path.Combine(path, filename + ".wav");
        
        AudioExtension.SaveWAV(fullPath, samples, 16000, 1);
        Debug.Log($"💾 Voice note saved: {fullPath}");
        
        // Notify Android media scanner
        NotifyAndroidMediaScanner(fullPath);
        #endif
    }
    
    void NotifyAndroidMediaScanner(string filePath)
    {
        #if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.MEDIA_SCANNER_SCAN_FILE"))
        {
            var uri = AndroidJavaClass.CallStatic<AndroidJavaObject>("android.net.Uri", "parse", "file://" + filePath);
            intent.Call<AndroidJavaObject>("setData", uri);
            activity.Call("sendBroadcast", intent);
        }
        #endif
    }
}
```

## 🔍 What's Next?

Apply these patterns to your own projects:

- **[Best Practices](best-practices.md)** - Optimization techniques
- **[Troubleshooting](troubleshooting.md)** - Common issues and solutions  
- **[API Reference](api-reference.md)** - Complete API documentation

---

← [Troubleshooting](troubleshooting.md) | **Back to [Documentation Home](../README.md)** →
