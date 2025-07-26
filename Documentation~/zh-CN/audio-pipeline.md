← [核心概念](core-concepts.md) | [文档首页](../README.md) | [English Version](../en/audio-pipeline.md) →

# ⛓️ 音频管道深度解析

音频管道是 Easy Mic 处理系统的核心。它让你能够通过串联简单、可重用的处理器来创建复杂的音频工作流程。

## 🎯 管道基础

### 什么是音频管道？

音频管道采用**责任链模式**，通过一系列处理器来处理音频数据。每个处理器接收前一个处理器的输出，让你能够从简单的构建块组成复杂的音频工作流程。

```csharp
// 视觉表示：
原始麦克风数据 → [处理器 A] → [处理器 B] → [处理器 C] → 最终输出
```

### 关键特性

- **🔄 动态配置**：录音过程中添加/移除处理器
- **🧵 线程安全**：所有操作都是线程安全的
- **🎯 有序执行**：处理器按添加顺序执行
- **🧠 零GC设计**：音频处理过程中无内存分配
- **🛡️ 错误恢复**：单个处理器错误不会导致管道崩溃

## 🏗️ 管道架构

### 内部结构

```csharp
public sealed class AudioPipeline : IAudioWorker
{
    private readonly List<IAudioWorker> _workers = new List<IAudioWorker>();
    private readonly object _lock = new object();
    private AudioState _initializeState;
    private bool _isInitialized;
    
    public int WorkerCount { get; } // 线程安全的工作器计数访问
}
```

### 线程安全设计

管道使用精心设计的锁机制来确保线程安全：

```csharp
// 线程安全操作
lock (_lock)
{
    if (!_workers.Contains(worker))
    {
        _workers.Add(worker);
        if (_isInitialized)
            worker.Initialize(_initializeState);
    }
}
```

## 🔄 处理器生命周期

### 1. 添加阶段

当你向管道添加处理器时：

```csharp
EasyMicAPI.AddProcessor(recordingHandle, new VolumeGateFilter());
```

**内部发生的操作：**
1. 管道检查处理器是否已存在
2. 如果管道已经初始化，处理器会立即初始化
3. 处理器添加到链的末尾
4. 整个过程保持线程安全

### 2. 初始化阶段

录音开始时，所有处理器都会被初始化：

```csharp
public void Initialize(AudioState state)
{
    _initializeState = state;
    lock (_lock)
    {
        foreach (var worker in _workers)
            worker.Initialize(state);
    }
    _isInitialized = true;
}
```

### 3. 处理阶段

在录音过程中，音频流经每个处理器：

```csharp
public void OnAudioPass(Span<float> buffer, AudioState state)
{
    foreach (var worker in _workers)
    {
        // 根据 state.Length 处理缓冲区切片
        Span<float> bufferForWorker = buffer;
        if (state.Length > 0 && state.Length < buffer.Length)
            bufferForWorker = buffer.Slice(0, state.Length);
            
        worker.OnAudioPass(bufferForWorker, state);
    }
}
```

### 4. 移除阶段

移除处理器时：

```csharp
EasyMicAPI.RemoveProcessor(recordingHandle, processor);
```

**发生的操作：**
1. 处理器从链中移除
2. 处理器自动释放资源
3. 管道继续运行剩余的处理器

## 🎨 处理器类型详解

### 📖 AudioReader 模式

专为**无修改分析**设计：

```csharp
public class VolumeAnalyzer : AudioReader
{
    private float _currentVolume;
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // 计算RMS音量
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
            
        _currentVolume = MathF.Sqrt(sum / buffer.Length);
    }
    
    public float GetCurrentVolume() => _currentVolume;
}
```

**优势：**
- ✅ **编译时安全**：无法意外修改音频
- ✅ **性能优化**：无需不必要的复制
- ✅ **意图明确**：明确表示只读操作

### ✏️ AudioWriter 模式

专为**修改音频的处理**设计：

```csharp
public class SimpleGainProcessor : AudioWriter
{
    public float Gain { get; set; } = 1.0f;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain;
    }
}
```

**优势：**
- ✅ **直接修改**：高效的就地处理
- ✅ **类型清晰**：明显表示音频会被修改
- ✅ **性能优越**：无需中间缓冲区

## 🔧 高级管道模式

### 多声道处理

优雅地处理不同声道配置：

```csharp
public class ChannelAwareProcessor : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        int frameCount = buffer.Length / state.ChannelCount;
        
        for (int frame = 0; frame < frameCount; frame++)
        {
            for (int channel = 0; channel < state.ChannelCount; channel++)
            {
                int sampleIndex = frame * state.ChannelCount + channel;
                
                // 按声道处理
                buffer[sampleIndex] = ProcessChannel(buffer[sampleIndex], channel);
            }
        }
    }
    
    private float ProcessChannel(float sample, int channel)
    {
        // 声道特定处理
        return sample * (channel == 0 ? 1.0f : 0.8f); // 左/右声道平衡
    }
}
```

### 状态保持处理

在音频缓冲区间维护状态：

```csharp
public class DelayProcessor : AudioWriter
{
    private readonly float[] _delayBuffer;
    private int _writePosition;
    private readonly int _delaySamples;
    
    public DelayProcessor(float delaySeconds, int sampleRate)
    {
        _delaySamples = (int)(delaySeconds * sampleRate);
        _delayBuffer = new float[_delaySamples];
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            // 获取延迟样本
            int readPos = (_writePosition - _delaySamples + _delayBuffer.Length) % _delayBuffer.Length;
            float delayedSample = _delayBuffer[readPos];
            
            // 存储当前样本
            _delayBuffer[_writePosition] = buffer[i];
            
            // 输出混合结果
            buffer[i] = (buffer[i] + delayedSample * 0.3f);
            
            _writePosition = (_writePosition + 1) % _delayBuffer.Length;
        }
    }
}
```

### 条件处理

基于条件处理音频：

```csharp
public class ConditionalProcessor : AudioWriter
{
    public bool IsEnabled { get; set; } = true;
    public float Threshold { get; set; } = 0.1f;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (!IsEnabled) return;
        
        // 计算缓冲区能量
        float energy = 0f;
        for (int i = 0; i < buffer.Length; i++)
            energy += buffer[i] * buffer[i];
        energy /= buffer.Length;
        
        // 仅在超过阈值时处理
        if (energy > Threshold)
        {
            // 应用处理
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] *= 1.5f; // 增强响亮信号
        }
    }
}
```

## 🎪 动态管道管理

### 运行时修改

录音过程中修改管道：

```csharp
public class DynamicPipelineController : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _gate;
    private AudioCapturer _capturer;
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording("Microphone");
        _capturer = new AudioCapturer(10);
        EasyMicAPI.AddProcessor(_handle, _capturer);
    }
    
    public void EnableNoiseGate()
    {
        if (_gate == null)
        {
            _gate = new VolumeGateFilter { ThresholdDb = -30f };
            EasyMicAPI.AddProcessor(_handle, _gate);
            Debug.Log("噪音门已启用");
        }
    }
    
    public void DisableNoiseGate()
    {
        if (_gate != null)
        {
            EasyMicAPI.RemoveProcessor(_handle, _gate);
            _gate = null;
            Debug.Log("噪音门已禁用");
        }
    }
}
```

### 管道监控

监控管道性能和状态：

```csharp
public class PipelineMonitor : AudioReader
{
    private int _processedFrames;
    private float _totalProcessingTime;
    private DateTime _lastFrameTime;
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        var frameTime = DateTime.Now;
        if (_lastFrameTime != default)
        {
            var processingTime = (frameTime - _lastFrameTime).TotalMilliseconds;
            _totalProcessingTime += (float)processingTime;
        }
        
        _processedFrames++;
        _lastFrameTime = frameTime;
    }
    
    public float GetAverageProcessingTime()
    {
        return _processedFrames > 0 ? _totalProcessingTime / _processedFrames : 0f;
    }
}
```

## 🛠️ 管道最佳实践

### ⚡ 性能优化

1. **最小化内存分配**

```csharp
// ❌ 错误 - 每帧都分配内存
public override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    var tempBuffer = new float[buffer.Length]; // 分配！
    // ... 处理
}

// ✅ 正确 - 重用缓冲区
private float[] _reusableBuffer = new float[4096];

public override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    if (_reusableBuffer.Length < buffer.Length)
        _reusableBuffer = new float[buffer.Length];
    // ... 处理
}
```

2. **高效声道处理**

```csharp
// ✅ 高效的基于帧的处理
for (int frame = 0; frame < frameCount; frame++)
{
    int baseIndex = frame * channelCount;
    // 将这一帧的所有声道一起处理
    for (int ch = 0; ch < channelCount; ch++)
        buffer[baseIndex + ch] = Process(buffer[baseIndex + ch]);
}
```

### 🎯 顺序优化

根据影响和依赖关系排序处理器：

```csharp
// ✅ 最佳顺序：
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());    // 1. 先移除噪音
EasyMicAPI.AddProcessor(handle, new AudioDownmixer());      // 2. 转换为单声道
EasyMicAPI.AddProcessor(handle, new GainProcessor());       // 3. 调整音量
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));      // 4. 捕获结果

// ❌ 糟糕的顺序：
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));      // 捕获有噪音的音频
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());    // 太晚了！
```

### 🧹 资源管理

```csharp
public class ProperProcessorManagement : MonoBehaviour
{
    private RecordingHandle _handle;
    private readonly List<IAudioWorker> _processors = new List<IAudioWorker>();
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording("Microphone");
        
        // 保持引用以便正确释放
        var gate = new VolumeGateFilter();
        var capturer = new AudioCapturer(5);
        
        _processors.Add(gate);
        _processors.Add(capturer);
        
        EasyMicAPI.AddProcessor(_handle, gate);
        EasyMicAPI.AddProcessor(_handle, capturer);
    }
    
    void OnDestroy()
    {
        // 先停止录音
        if (_handle.IsValid)
            EasyMicAPI.StopRecording(_handle);
        
        // 释放所有处理器
        foreach (var processor in _processors)
            processor?.Dispose();
            
        _processors.Clear();
    }
}
```

## 🚨 常见陷阱

### 1. 处理过程中修改处理器状态

```csharp
// ❌ 危险 - 可能导致竞态条件
public class DangerousProcessor : AudioWriter
{
    public float Gain { get; set; } // 主线程修改！
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // 音频线程读取 Gain 而主线程同时修改它！
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain; // 竞态条件！
    }
}

// ✅ 安全 - 使用原子操作或锁
public class SafeProcessor : AudioWriter
{
    private volatile float _gain = 1.0f;
    
    public float Gain
    {
        get => _gain;
        set => _gain = value; // 原子写入
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        float currentGain = _gain; // 原子读取
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= currentGain;
    }
}
```

### 2. 忘记处理 AudioState 变化

```csharp
// ✅ 总是检查格式变化
private int _lastSampleRate = -1;
private int _lastChannelCount = -1;

protected override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    // 格式改变时重新初始化
    if (state.SampleRate != _lastSampleRate || state.ChannelCount != _lastChannelCount)
    {
        ReinitializeForNewFormat(state);
        _lastSampleRate = state.SampleRate;
        _lastChannelCount = state.ChannelCount;
    }
    
    // 处理音频...
}
```

## 🔍 下一步

现在你已经理解了音频管道，继续探索：

- **[内置处理器](processors.md)** - 了解所有可用的处理器
- **[API 参考](api-reference.md)** - 完整的 API 文档
- **[最佳实践](best-practices.md)** - 性能和架构建议
- **[示例代码](examples.md)** - 实际的管道配置示例

---

← [核心概念](core-concepts.md) | **下一步：[内置处理器](processors.md)** →