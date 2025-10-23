← [故障排除](troubleshooting.md) | [文档首页](../README.md) | [English Version](../en/examples.md) →

# 🚀 示例和用例

Easy Mic 集成的真实世界实现和模式。从解决常见音频录制挑战的完整示例中学习。

## 🎙️ 基础录音示例

### 简单语音录音

最基本的语音录音实现：

```csharp
using UnityEngine;
using Eitan.EasyMic;

public class SimpleVoiceRecorder : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private AudioWorkerBlueprint _bpCapture;

    [Header("录音设置")]
    public SampleRate sampleRate = SampleRate.Hz16000;
    public float maxDuration = 10f;

    void Start()
    {
        // 确保权限
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogError("❌ 未授予麦克风权限");
            return;
        }

        StartRecording();
    }

    private void StartRecording()
    {
        // 刷新设备并开始录音
        EasyMicAPI.Refresh();
        _recordingHandle = EasyMicAPI.StartRecording(sampleRate);

        if (_recordingHandle.IsValid)
        {
            // 通过蓝图添加捕获器保存音频
            _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer((int)maxDuration), key: "capture");
            EasyMicAPI.AddProcessor(_recordingHandle, _bpCapture);

            var info = EasyMicAPI.GetRecordingInfo(_recordingHandle);
            Debug.Log($"🎙️ 使用 {info.Device.Name} 开始录音");
        }
        else
        {
            Debug.LogError("❌ 录音启动失败");
        }
    }

    public void StopRecording()
    {
        if (_recordingHandle.IsValid)
        {
            EasyMicAPI.StopRecording(_recordingHandle);

            // 获取录制的音频
            var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_recordingHandle, _bpCapture);
            AudioClip clip = capturer?.GetCapturedAudioClip();
            if (clip != null)
            {
                Debug.Log($"✅ 录制了 {clip.length:F1}s 音频");

                // 播放录音
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
        // 会话结束会自动释放处理器
    }
}
```

### 高质量立体声录音

用于音乐或高保真应用：

```csharp
public class HiFiRecorder : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioWorkerBlueprint _bpCapture;

    void Start()
    {
        // 使用最高质量设置
        EasyMicAPI.Refresh();

        // 找到最佳可用设备
        var devices = EasyMicAPI.Devices;
        var bestDevice = devices.FirstOrDefault(d => d.MaxChannels >= 2) ?? devices[0];

        // 开始高质量录音
        _handle = EasyMicAPI.StartRecording(
            bestDevice,
            SampleRate.Hz48000,  // 专业品质
            Channel.Stereo       // 完整立体声捕获
        );

        if (_handle.IsValid)
        {
            _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(60), key: "capture");
            EasyMicAPI.AddProcessor(_handle, _bpCapture);

            Debug.Log($"🎼 高质量录音：{bestDevice.Name} @ 48kHz 立体声");
        }
    }

    public void SaveToFile(string filename)
    {
        var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_handle, _bpCapture);
        var samples = capturer?.GetCapturedAudioSamples();
        AudioExtension.SaveWAV(filename, samples, 48000, 2);
        Debug.Log($"💾 保存到 {filename}");
    }
}
```

## 🔊 实时音频处理

### 实时语音效果

对语音应用实时效果：

```csharp
public class LiveVoiceEffects : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private SimpleReverb _reverb;
    private PitchShifter _pitchShifter;
    private LoopbackPlayer _monitor;

    [Header("效果控制")]
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
            // 构建效果链
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = gateThreshold,
                AttackTime = 0.001f,   // 语音快速启动
                ReleaseTime = 0.2f     // 平滑释放
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

            // 按最佳顺序添加处理器
            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _pitchShifter);
            EasyMicAPI.AddProcessor(_handle, _reverb);
            EasyMicAPI.AddProcessor(_handle, _monitor);

            Debug.Log("🎤 实时语音效果激活");
        }
    }

    void Update()
    {
        // 实时更新效果参数
        if (_noiseGate != null) _noiseGate.ThresholdDb = gateThreshold;
        if (_reverb != null) _reverb.Mix = reverbMix;
        if (_pitchShifter != null) _pitchShifter.PitchRatio = pitchShift;
        if (_monitor != null) _monitor.Volume = monitorVolume;
    }
}
```

### 实时音频可视化

可视化音频等级和频率内容：

```csharp
public class AudioVisualizer : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeAnalyzer _volumeAnalyzer;
    private SpectrumAnalyzer _spectrumAnalyzer;

    [Header("UI引用")]
    public Slider volumeMeter;
    public Image[] spectrumBars = new Image[32];
    public Text volumeText;

    [Header("可视化设置")]
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
            // 添加分析处理器
            _volumeAnalyzer = new VolumeAnalyzer();
            _spectrumAnalyzer = new SpectrumAnalyzer(spectrumBars.Length);

            EasyMicAPI.AddProcessor(_handle, _volumeAnalyzer);
            EasyMicAPI.AddProcessor(_handle, _spectrumAnalyzer);

            Debug.Log("📊 音频可视化激活");
        }
    }

    void Update()
    {
        if (_volumeAnalyzer == null || _spectrumAnalyzer == null) return;

        // 更新音量表
        float currentVolume = _volumeAnalyzer.GetRMSVolume();
        _smoothedVolume = Mathf.Lerp(_smoothedVolume, currentVolume, volumeSmoothing);

        volumeMeter.value = _smoothedVolume;
        volumeText.text = $"{_volumeAnalyzer.GetRMSVolumeDb():F1} dB";

        // 更新频谱显示
        var spectrum = _spectrumAnalyzer.GetSpectrum();
        for (int i = 0; i < spectrumBars.Length && i < spectrum.Length; i++)
        {
            _smoothedSpectrum[i] = Mathf.Lerp(_smoothedSpectrum[i], spectrum[i], spectrumSmoothing);

            // 更新条形高度（假设垂直条形）
            var rectTransform = spectrumBars[i].rectTransform;
            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, _smoothedSpectrum[i] * 100f);
        }
    }
}

// 支持分析器类
public class VolumeAnalyzer : AudioReader
{
    private float _rmsVolume;
    private float _peakVolume;

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
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

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
    {
        // 简化的FFT实现
        // 实际使用中，你会使用适当的FFT库
        for (int i = 0; i < buffer.Length && _bufferIndex < _fftSize; i++)
        {
            _fftBuffer[_bufferIndex] = new Complex(buffer[i], 0);
            _bufferIndex++;
        }

        if (_bufferIndex >= _fftSize)
        {
            // 执行FFT并更新频谱
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

## 🤖 语音助手集成

### 语音识别命令

集成语音识别进行语音命令：

```csharp
public class VoiceCommandSystem : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private SherpaRealtimeSpeechRecognizer _speechRecognizer;

    [Header("语音识别")]
    public string modelPath = "path/to/sherpa/model";
    public float confidenceThreshold = 0.7f;

    [Header("命令")]
    public UnityEvent<string> OnCommandRecognized;

    private readonly Dictionary<string, System.Action> _commands = new Dictionary<string, System.Action>();

    void Start()
    {
        RegisterCommands();
        SetupSpeechRecognition();
    }

    void RegisterCommands()
    {
        _commands["开始录音"] = () => StartRecording();
        _commands["停止录音"] = () => StopRecording();
        _commands["播放音乐"] = () => PlayMusic();
        _commands["开灯"] = () => ControlLights(true);
        _commands["关灯"] = () => ControlLights(false);
        _commands["几点了"] = () => SpeakTime();
    }

    void SetupSpeechRecognition()
    {
        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(SampleRate.Hz16000); // 语音优化

        if (_handle.IsValid)
        {
            // 用于更好识别的噪音门
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = -30f,
                AttackTime = 0.01f,
                ReleaseTime = 0.3f
            };

            // 语音识别器
            _speechRecognizer = new SherpaRealtimeSpeechRecognizer(modelPath);
            _speechRecognizer.OnFinalResult += OnSpeechRecognized;
            _speechRecognizer.OnPartialResult += OnPartialSpeech;

            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _speechRecognizer);

            Debug.Log("🗣️ 语音命令系统就绪");
        }
    }

    private void OnPartialSpeech(string text)
    {
        Debug.Log($"聆听中：{text}");
    }

    private void OnSpeechRecognized(string text)
    {
        Debug.Log($"识别：{text}");

        if (_speechRecognizer.LastConfidence < confidenceThreshold)
        {
            Debug.Log($"置信度低 ({_speechRecognizer.LastConfidence:F2})，忽略");
            return;
        }

        // 查找匹配命令
        string lowerText = text.ToLower();
        foreach (var command in _commands)
        {
            if (lowerText.Contains(command.Key))
            {
                Debug.Log($"执行命令：{command.Key}");
                command.Value.Invoke();
                OnCommandRecognized?.Invoke(command.Key);
                return;
            }
        }

        Debug.Log($"未知命令：{text}");
    }

    // 命令实现
    private void StartRecording() => Debug.Log("▶️ 开始录音...");
    private void StopRecording() => Debug.Log("⏹️ 停止录音...");
    private void PlayMusic() => Debug.Log("🎵 播放音乐...");
    private void ControlLights(bool on) => Debug.Log($"💡 灯 {(on ? "开" : "关")}");
    private void SpeakTime() => Debug.Log($"🕐 现在时间是 {DateTime.Now:HH:mm}");
}
```

## 🎮 游戏集成示例

### 多人游戏语音聊天

实时语音通信：

```csharp
public class VoiceChatManager : MonoBehaviourPunPV, IPunObservable
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private AudioCapturer _capturer;
    private VoiceTransmitter _transmitter;

    [Header("语音聊天设置")]
    public bool pushToTalk = false;
    public KeyCode talkKey = KeyCode.T;
    public float transmissionRate = 10f; // 每秒数据包

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
            // 语音优化
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = -40f,  // 语音聊天敏感门
                AttackTime = 0.005f,
                ReleaseTime = 0.1f
            };

            _capturer = new AudioCapturer(1); // 1秒缓冲
            _transmitter = new VoiceTransmitter(this);

            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _capturer);
            EasyMicAPI.AddProcessor(_handle, _transmitter);

            Debug.Log("🎤 语音聊天捕获就绪");
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

            Debug.Log($"🎙️ 语音传输：{(shouldTransmit ? "开" : "关")}");
        }

        // 定期发送语音数据
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
            // 压缩并发送语音数据
            byte[] compressedData = VoiceCompression.Compress(audioData);
            photonView.RPC("ReceiveVoiceData", RpcTarget.Others, compressedData);
            _capturer.Clear();
        }
    }

    [PunRPC]
    void ReceiveVoiceData(byte[] compressedData)
    {
        // 解压并播放语音数据
        float[] audioData = VoiceCompression.Decompress(compressedData);
        PlayVoiceClip(audioData);
    }

    void PlayVoiceClip(float[] audioData)
    {
        var audioSource = GetComponent<AudioSource>();
        if (audioSource == null) return;

        // 创建并播放音频剪辑
        var clip = AudioExtension.CreateAudioClip(audioData, 22050, 1, "VoiceChat");
        audioSource.clip = clip;
        audioSource.Play();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        // 同步语音传输状态
        if (stream.IsWriting)
        {
            stream.SendNext(_isTransmitting);
        }
        else
        {
            bool remoteTransmitting = (bool)stream.ReceiveNext();
            // 更新UI显示谁在说话
            UpdateTalkingIndicator(remoteTransmitting);
        }
    }

    void UpdateTalkingIndicator(bool talking)
    {
        // 显示/隐藏说话指示器UI
        var indicator = transform.Find("TalkingIndicator");
        if (indicator != null)
            indicator.gameObject.SetActive(talking);
    }
}
```

### 语音控制角色

用语音命令控制游戏角色：

```csharp
public class VoiceControlledCharacter : MonoBehaviour
{
    private RecordingHandle _handle;
    private SimpleCommandRecognizer _commandRecognizer;
    private CharacterController _characterController;

    [Header("移动设置")]
    public float moveSpeed = 5f;
    public float jumpForce = 8f;

    [Header("语音命令")]
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
            _commandRecognizer.AddCommand("前进", () => SetMovement(Vector3.forward));
            _commandRecognizer.AddCommand("后退", () => SetMovement(Vector3.back));
            _commandRecognizer.AddCommand("左转", () => SetMovement(Vector3.left));
            _commandRecognizer.AddCommand("右转", () => SetMovement(Vector3.right));
            _commandRecognizer.AddCommand("跳跃", () => Jump());
            _commandRecognizer.AddCommand("停止", () => Stop());

            _commandRecognizer.OnCommandRecognized += OnVoiceCommand;

            EasyMicAPI.AddProcessor(_handle, _commandRecognizer);
            Debug.Log("🎮 语音控制角色就绪");
        }
    }

    void OnVoiceCommand(string command)
    {
        Debug.Log($"语音命令：{command}");
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
        // 如果没有最近命令则停止移动
        if (Time.time - _lastCommandTime > commandTimeout)
        {
            _moveDirection.x = 0;
            _moveDirection.z = 0;
        }

        // 应用重力
        _moveDirection.y += Physics.gravity.y * Time.deltaTime;

        // 移动角色
        _characterController.Move(_moveDirection * moveSpeed * Time.deltaTime);

        // 检查是否接地
        _isGrounded = _characterController.isGrounded;
        if (_isGrounded && _moveDirection.y < 0)
            _moveDirection.y = 0;
    }
}

// 基于简单模式的命令识别器
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

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
    {
        // 收集音频用于分析
        for (int i = 0; i < buffer.Length; i++)
            _audioBuffer.Add(buffer[i]);

        // 定期分析
        if (Time.time - _lastAnalysisTime > AnalysisInterval)
        {
            AnalyzeForCommands();
            _lastAnalysisTime = Time.time;
        }
    }

    void AnalyzeForCommands()
    {
        if (_audioBuffer.Count == 0) return;

        // 基于简单能量的命令检测
        float energy = 0f;
        foreach (float sample in _audioBuffer)
            energy += sample * sample;
        energy /= _audioBuffer.Count;

        if (energy > 0.01f) // 检测到语音
        {
            // 在真实实现中，你会使用实际的语音识别
            // 对于这个示例，我们将模拟命令识别
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
        // 基于能量等级的简化命令模拟
        // 实际使用中，你会使用适当的语音识别系统
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

## 📱 平台特定示例

### Android 语音笔记应用

为移动录音优化：

```csharp
public class AndroidVoiceNotes : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioCapturer _capturer;
    private VolumeGateFilter _noiseGate;

    [Header("移动优化")]
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
        // 确保权限
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogError("❌ 未授予麦克风权限");
            return;
        }

        StartMobileOptimizedRecording();
    }

    void StartMobileOptimizedRecording()
    {
        // 为移动设备电池寿命和带宽优化
        SampleRate rate = adaptiveBitrate ? SampleRate.Hz16000 : SampleRate.Hz22050;

        EasyMicAPI.Refresh();
        _handle = EasyMicAPI.StartRecording(rate, Channel.Mono);

        if (_handle.IsValid)
        {
            // 移动优化噪音门
            _noiseGate = new VolumeGateFilter
            {
                ThresholdDb = -25f,  // 移动环境更高阈值
                AttackTime = 0.01f,
                ReleaseTime = 0.3f
            };

            _capturer = new AudioCapturer(300); // 最多5分钟

            EasyMicAPI.AddProcessor(_handle, _noiseGate);
            EasyMicAPI.AddProcessor(_handle, _capturer);

            Debug.Log("📱 移动语音录音激活");
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (backgroundRecording) return;

        if (pauseStatus)
        {
            // 应用进入后台时暂停录音
            if (_handle.IsValid)
                EasyMicAPI.StopRecording(_handle);
        }
        else
        {
            // 应用返回前台时恢复录音
            if (!_handle.IsValid)
                StartMobileOptimizedRecording();
        }
    }

    public void SaveVoiceNote(string filename)
    {
        #if UNITY_ANDROID
        // 保存到Android外部存储
        string path = Path.Combine(Application.persistentDataPath, "VoiceNotes");
        Directory.CreateDirectory(path);

        var samples = _capturer.GetCapturedAudioSamples();
        string fullPath = Path.Combine(path, filename + ".wav");

        AudioExtension.SaveWAV(fullPath, samples, 16000, 1);
        Debug.Log($"💾 语音笔记已保存：{fullPath}");

        // 通知Android媒体扫描器
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

## 🔍 下一步

将这些模式应用到你自己的项目：

- **[最佳实践](best-practices.md)** - 优化技术
- **[故障排除](troubleshooting.md)** - 常见问题和解决方案
- **[API 参考](api-reference.md)** - 完整的 API 文档

---

← [故障排除](troubleshooting.md) | **返回 [文档首页](../README.md)** →
