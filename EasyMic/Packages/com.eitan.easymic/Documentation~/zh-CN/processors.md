← [音频流水线](audio-pipeline.md) | [文档首页](../README.md) | [English Version](../en/processors.md) →

# 🧩 内置处理器

Easy Mic 提供了一整套音频处理器，专为常见的音频处理任务而设计。每个处理器都经过性能优化，能够在音频流水线中无缝工作。

## 📖 AudioReader 处理器

### 📊 VolumeAnalyzer (示例实现)

分析音频音量而不修改数据流。

```csharp
public class VolumeAnalyzer : AudioReader
{
    private float _currentRMS;
    private float _currentPeak;

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

        _currentRMS = MathF.Sqrt(sum / buffer.Length);
        _currentPeak = peak;
    }

    public float GetRMSVolume() => _currentRMS;
    public float GetPeakVolume() => _currentPeak;
    public float GetRMSVolumeDb() => 20f * MathF.Log10(_currentRMS + 1e-10f);
}
```

## ✏️ AudioWriter 处理器

### 📼 AudioCapturer

将传入的音频数据捕获到缓冲区或保存到文件。

#### 功能特性

- **高性能缓冲区**：使用无锁 `AudioBuffer` 实现零 GC 捕获
- **Unity 集成**：直接转换为 `AudioClip`
- **可配置时长**：设置最大捕获时长
- **多声道支持**：处理单声道和立体声音频

#### 使用方法

```csharp
// 创建“蓝图”并加入流水线
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(10), key: "capture");
EasyMicAPI.AddProcessor(recordingHandle, bpCapture);

// 稍后通过蓝图获取实例并取回音频
var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(recordingHandle, bpCapture);
float[] samples = capturer?.GetCapturedAudioSamples();
AudioClip clip = capturer?.GetCapturedAudioClip();
```

#### 构造函数

```csharp
public AudioCapturer(int maxDurationInSeconds = 60)
```

#### 关键方法

- `GetCapturedAudioSamples()` - 返回原始 float 数组
- `GetCapturedAudioClip()` - 返回 Unity AudioClip
- `Clear()` - 清除捕获缓冲区

#### 实现细节

```csharp
public class AudioCapturer : AudioReader
{
    private AudioBuffer _audioBuffer;
    private readonly int _maxCaptureDuration;
    private AudioContext _AudioContext;

    public override void Initialize(AudioContext state)
    {
        // 计算所需的总样本数
        int totalSamples = state.Length * _maxCaptureDuration;
        _audioBuffer = new AudioBuffer(totalSamples);
        _AudioContext = state;
        base.Initialize(state);
    }

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
    {
        _audioBuffer.Write(buffer);
        if (_AudioContext != state)
            _AudioContext = state;
    }
}
```

---

### 🔄 AudioDownmixer

将多声道音频（如立体声）转换为单声道音频。

#### 功能特性

- **智能混音**：在降混过程中保持音频质量
- **可配置算法**：多种混音策略
- **性能优化**：处理过程中零内存分配
- **声道感知**：自动检测输入声道配置

#### 使用方法

```csharp
var bpDownmix = new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmix");
EasyMicAPI.AddProcessor(recordingHandle, bpDownmix);
```

#### 混音算法

```csharp
public enum MixingAlgorithm
{
    Average,        // 所有声道的简单平均
    LeftChannel,    // 只取左声道
    RightChannel,   // 只取右声道
    WeightedMix     // 加权平均（可自定义）
}
```

#### 配置选项

```csharp
var downmixer = new AudioDownmixer
{
    Algorithm = MixingAlgorithm.Average,
    LeftWeight = 0.6f,   // 用于加权混音
    RightWeight = 0.4f
};
```

#### 实现示例

```csharp
protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
{
    if (state.ChannelCount <= 1) return; // 已经是单声道

    int frameCount = buffer.Length / state.ChannelCount;

    for (int frame = 0; frame < frameCount; frame++)
    {
        int baseIndex = frame * state.ChannelCount;
        float mixedSample = 0f;

        // 混合所有声道
        for (int ch = 0; ch < state.ChannelCount; ch++)
            mixedSample += buffer[baseIndex + ch];

        mixedSample /= state.ChannelCount;

        // 将混合的样本写入所有声道（或仅第一个声道用于真正的单声道）
        buffer[baseIndex] = mixedSample;
    }

    // 更新状态以反映新的声道数
    state.ChannelCount = 1;
}
```

---

### 🔇 VolumeGateFilter

一个先进的噪音门，通过平滑过渡在音量阈值以下时静音音频。

#### 功能特性

- **专业门状态**：关闭、启动、开启、保持、释放
- **前瞻处理**：保持瞬态并防止伪影
- **多声道感知**：同时处理所有声道
- **样本精确过渡**：平滑的启动/释放曲线
- **实时参数更新**：录音过程中调整设置

#### 配置属性

```csharp
public class VolumeGateFilter : AudioWriter
{
    public float ThresholdDb { get; set; } = -35.0f;     // 门阈值（dB）
    public float AttackTime { get; set; } = 0.005f;      // 开启时间（5ms）
    public float HoldTime { get; set; } = 0.25f;         // 保持时间（250ms）
    public float ReleaseTime { get; set; } = 0.2f;       // 释放时间（200ms）
    public float LookaheadTime { get; set; } = 0.005f;   // 前瞻时间（5ms）

    // 只读状态
    public VolumeGateState CurrentState { get; private set; }
    public float CurrentDb { get; }
}
```

#### 门状态

```csharp
public enum VolumeGateState
{
    Closed,     // 门关闭，无音频通过
    Attacking,  // 门正在开启
    Open,       // 门完全开启
    Holding,    // 门等待关闭
    Releasing   // 门正在关闭
}
```

#### 使用示例

```csharp
// 以蓝图形式添加基本噪音门
var bpGate = new AudioWorkerBlueprint(() => new VolumeGateFilter
{
    ThresholdDb = -30f,
    AttackTime = 0.001f,   // 语音快速启动
    ReleaseTime = 0.5f     // 慢速释放避免切断单词
}, key: "gate");

EasyMicAPI.AddProcessor(recordingHandle, bpGate);

// 如需在运行时调整参数，先取回实例
var gate = EasyMicAPI.GetProcessor<VolumeGateFilter>(recordingHandle, bpGate);
gate.ThresholdDb = -30f;
```

#### 高级配置

```csharp
public class AdaptiveGateController : MonoBehaviour
{
    private AudioWorkerBlueprint _bpGate;
    private VolumeGateFilter _gate;

    void Start()
    {
        _bpGate = new AudioWorkerBlueprint(() => new VolumeGateFilter(), key: "gate");
        EasyMicAPI.AddProcessor(recordingHandle, _bpGate);
        _gate = EasyMicAPI.GetProcessor<VolumeGateFilter>(recordingHandle, _bpGate);
    }

    void Update()
    {
        // 根据环境噪音调整阈值
        float ambientLevel = GetAmbientNoiseLevel();
        _gate.ThresholdDb = ambientLevel + 6f; // 高于环境噪音6dB

        // 显示当前状态
        Debug.Log($"门状态：{_gate.CurrentState}，电平：{_gate.CurrentDb:F1}dB");
    }
}
```

#### 技术实现亮点

```csharp
protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
{
    ProcessAudio(buffer);
}

private void ProcessAudio(Span<float> audioBuffer)
{
    int frameCount = audioBuffer.Length / _channelCount;

    for (int i = 0; i < frameCount; i++)
    {
        // 前瞻检测以保持瞬态
        int detectionPos = (_writePosition + _lookaheadFrames * _channelCount) % _bufferSize;
        int processPos = _writePosition;

        // 使用未来音频进行包络检测
        float maxInFrame = 0f;
        for (int ch = 0; ch < _channelCount; ch++)
        {
            float sample = MathF.Abs(_internalBuffer[detectionPos + ch]);
            if (sample > maxInFrame) maxInFrame = sample;
        }

        // 用启动/释放更新包络
        if (maxInFrame > _envelope)
            _envelope = maxInFrame; // 瞬时启动
        else
            _envelope *= _envelopeReleaseCoeff; // 平滑释放

        // 状态机更新
        UpdateGateState(_envelope >= _thresholdLinear, 1.0f / _sampleRate);

        // 根据当前状态应用增益
        ApplyGateGain(audioBuffer, i);
    }
}
```

---

### 🔁 LoopbackPlayer

用于监听和测试应用的实时音频回放。

#### 功能特性

- **零延迟监听**：直接音频通路
- **音量控制**：可调节监听电平
- **静音功能**：切换监听开/关
- **性能优化**：最小处理开销

#### 使用方法

```csharp
var bpLoop = new AudioWorkerBlueprint(() => new LoopbackPlayer { Volume = 0.5f, IsMuted = false }, key: "loop");
EasyMicAPI.AddProcessor(recordingHandle, bpLoop);
```

#### 实现

```csharp
public class LoopbackPlayer : AudioWriter
{
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = false;

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        if (IsMuted || Volume <= 0f) return;

        // 简单地缩放音频用于监听
        if (Volume != 1.0f)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] *= Volume;
        }

        // 在真实实现中，这可能路由到扬声器
        // 现在它只是应用音量缩放
    }
}
```

---

### 🗣️ SherpaRealtimeSpeechRecognizer

使用 Sherpa-ONNX 引擎的实时语音转文本处理器。

#### 功能特性

- **实时识别**：低延迟语音转文本
- **多语言支持**：支持各种语言模型
- **置信度评分**：识别置信度等级
- **流式模式**：连续识别
- **事件驱动**：识别事件回调

#### 依赖要求

```csharp
// 需要 Sherpa-ONNX Unity 包
// 安装地址：https://github.com/EitanWong/com.eitan.sherpa-onnx-unity
```

#### 使用方法

```csharp
var bpASR = new AudioWorkerBlueprint(() => new SherpaRealtimeSpeechRecognizer("path/to/model"), key: "asr");
EasyMicAPI.AddProcessor(recordingHandle, bpASR);

// 取回实例并订阅事件
var recognizer = EasyMicAPI.GetProcessor<SherpaRealtimeSpeechRecognizer>(recordingHandle, bpASR);
recognizer.OnPartialResult += (text) => Debug.Log($"部分结果：{text}");
recognizer.OnFinalResult += (text) => Debug.Log($"最终结果：{text}");
```

#### 事件

```csharp
public class SherpaRealtimeSpeechRecognizer : AudioReader
{
    public event Action<string> OnPartialResult;
    public event Action<string> OnFinalResult;
    public event Action<float> OnConfidenceUpdated;

    public float MinConfidence { get; set; } = 0.5f;
    public bool IsListening { get; private set; }
}
```

---

## 🎛️ 创建自定义处理器

### AudioReader 模板

```csharp
public class CustomAnalyzer : AudioReader
{
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
    {
        // 你的分析代码 - 不能修改缓冲区
        // 适用于：音量表、音调检测、静音检测
    }
}
```

### AudioWriter 模板

```csharp
public class CustomEffect : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        // 你的处理代码 - 可以修改缓冲区
        // 适用于：滤波器、效果、格式转换

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ProcessSample(buffer[i]);
        }
    }

    private float ProcessSample(float input)
    {
        // 你的样本处理逻辑
        return input;
    }
}
```

### 高级自定义处理器

```csharp
public class AdvancedProcessor : AudioWriter
{
    private float[] _delayBuffer;
    private int _bufferSize;
    private int _writePos;

    public override void Initialize(AudioContext state)
    {
        base.Initialize(state);

        // 根据音频格式初始化
        _bufferSize = state.SampleRate; // 1秒延迟
        _delayBuffer = new float[_bufferSize * state.ChannelCount];
        _writePos = 0;
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        // 处理格式变化
        if (_delayBuffer.Length != _bufferSize * state.ChannelCount)
        {
            Initialize(state);
        }

        // 使用状态信息处理音频
        ProcessFrames(buffer, state);
    }

    private void ProcessFrames(Span<float> buffer, AudioContext state)
    {
        int frameCount = buffer.Length / state.ChannelCount;

        for (int frame = 0; frame < frameCount; frame++)
        {
            for (int ch = 0; ch < state.ChannelCount; ch++)
            {
                int bufferIndex = frame * state.ChannelCount + ch;
                int delayIndex = _writePos * state.ChannelCount + ch;

                // 获取延迟样本
                float delayed = _delayBuffer[delayIndex];

                // 存储当前样本
                _delayBuffer[delayIndex] = buffer[bufferIndex];

                // 输出延迟样本
                buffer[bufferIndex] = delayed;
            }

            _writePos = (_writePos + 1) % _bufferSize;
        }
    }

    public override void Dispose()
    {
        _delayBuffer = null;
        base.Dispose();
    }
}
```

## 🎯 处理器最佳实践

### 性能指导原则

- **最小化分配**：在 OnAudioPass 方法中
- **重用缓冲区**：尽可能
- **避免复杂计算**：在音频线程上
- **使用高效算法**：用于实时处理

### 线程安全

- **使用 volatile**：用于简单状态变量
- **避免锁**：在音频处理方法中
- **预计算**：在 Initialize() 中进行昂贵操作
- **小心使用**：处理过程中的属性设置器

### 资源管理

- **重写 Dispose()**：清理资源
- **清除大缓冲区**：在 Dispose() 中
- **移除事件处理器**：防止内存泄漏
- **停止处理器**：释放前

## 🔍 下一步

探索更多高级主题：

- **[API 参考](api-reference.md)** - 完整的 API 文档
- **[最佳实践](best-practices.md)** - 性能优化技术
- **[示例](examples.md)** - 真实世界的处理器配置
- **[故障排除](troubleshooting.md)** - 常见问题和解决方案

---

← [音频流水线](audio-pipeline.md) | **下一步：[API 参考](api-reference.md)** →
