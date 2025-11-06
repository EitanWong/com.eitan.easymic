← [API 参考](api-reference.md) | [文档首页](../README.md) | [English Version](../en/best-practices.md) →

# 💡 最佳实践

通过这些经过验证的模式和技术优化你的 Easy Mic 实现，以获得最佳性能、可靠性和可维护性。

## 🚀 性能优化

### ⚡ 音频线程最佳实践

音频处理线程是实时关键的。遵循以下准则：

#### 🚫 避免内存分配

```csharp
// ❌ 错误 - 每帧都分配内存
public class BadProcessor : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        var tempArray = new float[buffer.Length]; // 分配！
        // 处理...
    }
}

// ✅ 正确 - 重用缓冲区
public class GoodProcessor : AudioWriter
{
    private float[] _workBuffer = new float[4096];

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        // 仅在需要时调整大小
        if (_workBuffer.Length < buffer.Length)
            _workBuffer = new float[buffer.Length * 2]; // 增长并留出余量

        // 使用 _workBuffer 进行处理
    }
}
```

#### 🎯 最小化音频回调中的工作

```csharp
// ❌ 错误 - 音频线程上的复杂计算
public class BadFilter : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        foreach (var sample in buffer)
        {
            // 每个样本上的昂贵操作！
            var coefficient = Math.Sin(Math.PI * frequency / state.SampleRate);
            var result = sample * coefficient;
        }
    }
}

// ✅ 正确 - 在 Initialize() 中预计算
public class GoodFilter : AudioWriter
{
    private float _preCalculatedCoeff;

    public override void Initialize(AudioContext state)
    {
        base.Initialize(state);
        // 预计算昂贵的值
        _preCalculatedCoeff = MathF.Sin(MathF.PI * frequency / state.SampleRate);
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= _preCalculatedCoeff; // 快速乘法
    }
}
```

#### 🔄 高效的声道处理

```csharp
// ❌ 低效 - 逐样本处理
for (int i = 0; i < buffer.Length; i++)
{
    int channel = i % channelCount;
    buffer[i] = ProcessChannel(buffer[i], channel);
}

// ✅ 高效 - 基于帧的处理
int frameCount = buffer.Length / channelCount;
for (int frame = 0; frame < frameCount; frame++)
{
    int baseIndex = frame * channelCount;

    // 一起处理这一帧的所有声道
    for (int ch = 0; ch < channelCount; ch++)
    {
        buffer[baseIndex + ch] = ProcessChannel(buffer[baseIndex + ch], ch);
    }
}
```

### 🧠 内存管理

#### 缓冲区大小优化

```csharp
public class OptimalBufferSizes
{
    // ✅ 良好的缓冲区大小（2的幂或64的倍数）
    public static readonly int[] RecommendedSizes = { 64, 128, 256, 512, 1024, 2048 };

    public static int GetOptimalBufferSize(int requestedSize)
    {
        // 找到下一个2的幂
        int size = 1;
        while (size < requestedSize)
            size <<= 1;
        return size;
    }
}

// 在 AudioCapturer 中使用
public AudioCapturer(int maxDurationSeconds, int sampleRate = 48000)
{
    int requestedSize = maxDurationSeconds * sampleRate;
    int optimalSize = OptimalBufferSizes.GetOptimalBufferSize(requestedSize);
    _audioBuffer = new AudioBuffer(optimalSize);
}
```

#### 智能资源管理

```csharp
public class ResourceAwareProcessor : AudioWriter, IDisposable
{
    private float[] _largeBuffer;
    private bool _disposed = false;

    public override void Initialize(AudioContext state)
    {
        base.Initialize(state);

        // 只分配所需的内存
        int bufferSize = Math.Max(state.Length * 4, 1024); // 4倍余量
        _largeBuffer = new float[bufferSize];
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        if (_disposed) return;

        // 使用预分配的缓冲区
        var workSpan = new Span<float>(_largeBuffer, 0, buffer.Length);
        // 处理...
    }

    public override void Dispose()
    {
        if (_disposed) return;

        // 清除大数组以帮助GC
        _largeBuffer = null;
        _disposed = true;

        base.Dispose();
    }
}
```

## 🏗️ 架构模式

### 🎭 复杂操作的门面模式

```csharp
public class RecordingManager : MonoBehaviour
{
    public class Builder
    {
        private readonly List<IAudioWorker> _processors = new List<IAudioWorker>();
        private string _deviceName;
        private SampleRate _sampleRate = SampleRate.Hz48000;
        private Channel _channel = Channel.Mono;

        public Builder WithDevice(string deviceName)
        {
            _deviceName = deviceName;
            return this;
        }

        public Builder WithQuality(SampleRate sampleRate, Channel channel)
        {
            _sampleRate = sampleRate;
            _channel = channel;
            return this;
        }

        public Builder AddProcessor(AudioWorkerBlueprint blueprint)
        {
            _processors.Add(blueprint);
            return this;
        }

        public RecordingSession Build()
        {
            var handle = EasyMicAPI.StartRecording(_deviceName, _sampleRate, _channel, _processors);
            return new RecordingSession(handle, _processors);
        }
    }

    // 使用方法：
    public void StartHighQualityRecording()
    {
        var session = new Builder()
            .WithDevice("内置麦克风")
            .WithQuality(SampleRate.Hz48000, Channel.Stereo)
            .AddProcessor(new AudioWorkerBlueprint(() => new VolumeGateFilter { ThresholdDb = -30f }, key: "gate"))
            .AddProcessor(new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmix"))
            .AddProcessor(new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture"))
            .Build();
    }
}
```

### 🔧 处理算法的策略模式

```csharp
public interface INoiseReductionStrategy
{
    void ReduceNoise(Span<float> buffer, AudioContext state);
}

public class SimpleGateStrategy : INoiseReductionStrategy
{
    public float Threshold { get; set; } = 0.01f;

    public void ReduceNoise(Span<float> buffer, AudioContext state)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (Math.Abs(buffer[i]) < Threshold)
                buffer[i] = 0f;
        }
    }
}

public class AdaptiveNoiseReduction : AudioWriter
{
    public INoiseReductionStrategy Strategy { get; set; }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        Strategy?.ReduceNoise(buffer, state);
    }
}

// 使用方法：
var processor = new AdaptiveNoiseReduction
{
    Strategy = new SimpleGateStrategy { Threshold = 0.02f }
};
```

### 🎪 监控的观察者模式

```csharp
public interface IAudioEventListener
{
    void OnVolumeChanged(float rms, float peak);
    void OnSilenceDetected(TimeSpan duration);
    void OnSpeechDetected();
}

public class AudioMonitor : AudioReader
{
    private readonly List<IAudioEventListener> _listeners = new List<IAudioEventListener>();
    private float _silenceStartTime = -1f;
    private float _currentTime;

    public void AddListener(IAudioEventListener listener) => _listeners.Add(listener);
    public void RemoveListener(IAudioEventListener listener) => _listeners.Remove(listener);

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
    {
        _currentTime += (float)buffer.Length / state.SampleRate;

        // 计算音量指标
        float rms = CalculateRMS(buffer);
        float peak = CalculatePeak(buffer);

        // 通知监听器
        foreach (var listener in _listeners)
            listener.OnVolumeChanged(rms, peak);

        // 检测静音/语音
        bool isSilent = rms < 0.01f;
        if (isSilent)
        {
            if (_silenceStartTime < 0)
                _silenceStartTime = _currentTime;
            else if (_currentTime - _silenceStartTime > 1.0f) // 1秒静音
            {
                var duration = TimeSpan.FromSeconds(_currentTime - _silenceStartTime);
                foreach (var listener in _listeners)
                    listener.OnSilenceDetected(duration);
            }
        }
        else
        {
            if (_silenceStartTime >= 0)
            {
                foreach (var listener in _listeners)
                    listener.OnSpeechDetected();
                _silenceStartTime = -1f;
            }
        }
    }
}
```

## 🛡️ 线程安全

### 🔒 安全的参数更新

```csharp
public class ThreadSafeProcessor : AudioWriter
{
    private volatile float _gain = 1.0f;
    private volatile bool _enabled = true;

    // 从任何线程安全的属性访问
    public float Gain
    {
        get => _gain;
        set => _gain = value; // float的原子写入
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value; // bool的原子写入
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        if (!_enabled) return;

        float currentGain = _gain; // 原子读取
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= currentGain;
    }
}
```

### 🎭 线程安全的状态管理

```csharp
public class SafeStateProcessor : AudioWriter
{
    private readonly object _stateLock = new object();
    private ProcessorConfig _config;
    private ProcessorConfig _pendingConfig;

    public void UpdateConfig(ProcessorConfig newConfig)
    {
        lock (_stateLock)
        {
            _pendingConfig = newConfig;
        }
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        // 检查配置更新（少见操作）
        if (_pendingConfig != null)
        {
            lock (_stateLock)
            {
                if (_pendingConfig != null)
                {
                    _config = _pendingConfig;
                    _pendingConfig = null;
                }
            }
        }

        // 使用稳定配置进行处理
        ProcessWithConfig(buffer, _config);
    }
}
```

## 🎯 错误处理

### 🛡️ 优雅降级

```csharp
public class RobustProcessor : AudioWriter
{
    private bool _hasError = false;
    private int _errorCount = 0;
    private const int MaxErrors = 5;

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        if (_hasError) return; // 故障安全模式

        try
        {
            ProcessAudioInternal(buffer, state);
            _errorCount = 0; // 成功时重置
        }
        catch (Exception ex)
        {
            _errorCount++;
            Debug.LogError($"音频处理错误 {_errorCount}/{MaxErrors}：{ex.Message}");

            if (_errorCount >= MaxErrors)
            {
                Debug.LogError("错误过多，禁用处理器");
                _hasError = true;
            }

            // 错误时让音频未修改通过
        }
    }

    private void ProcessAudioInternal(Span<float> buffer, AudioContext state)
    {
        // 你的处理代码
        // 错误时抛出异常
    }
}
```

### 📊 健康监控

```csharp
public class HealthMonitorProcessor : AudioWriter
{
    private int _processedFrames;
    private int _skippedFrames;
    private DateTime _lastHealthReport = DateTime.Now;

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        _processedFrames++;

        // 检测音频丢失
        if (buffer.Length == 0)
        {
            _skippedFrames++;
            return;
        }

        // 定期健康报告
        if ((DateTime.Now - _lastHealthReport).TotalSeconds >= 10)
        {
            float dropoutRate = (float)_skippedFrames / _processedFrames;
            if (dropoutRate > 0.01f) // 超过1%的丢失
            {
                Debug.LogWarning($"音频健康：{dropoutRate:P} 丢失率");
            }

            _lastHealthReport = DateTime.Now;
            _skippedFrames = 0;
            _processedFrames = 0;
        }

        // 正常处理音频...
    }
}
```

## 🎪 流水线模式

### 🔄 动态流水线重配置

```csharp
public class AdaptivePipeline : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private AudioDownmixer _downmixer;
    private readonly Queue<System.Action> _pipelineUpdates = new Queue<System.Action>();

    void Update()
    {
        // 在主线程上处理流水线更新
        while (_pipelineUpdates.Count > 0)
        {
            var update = _pipelineUpdates.Dequeue();
            update.Invoke();
        }
    }

    public void EnableNoiseGateAsync(float thresholdDb)
    {
        _pipelineUpdates.Enqueue(() =>
        {
            if (_noiseGate == null)
            {
                var bpGate = new AudioWorkerBlueprint(() => new VolumeGateFilter { ThresholdDb = thresholdDb }, key: "gate");
                EasyMicAPI.AddProcessor(_handle, bpGate);
                _noiseGate = EasyMicAPI.GetProcessor<VolumeGateFilter>(_handle, bpGate);
            }
        });
    }

    public void DisableNoiseGateAsync()
    {
        _pipelineUpdates.Enqueue(() =>
        {
            if (_noiseGate != null)
            {
                var bpGate = new AudioWorkerBlueprint(() => new VolumeGateFilter(), key: "gate");
                EasyMicAPI.RemoveProcessor(_handle, bpGate);
                _noiseGate = null;
            }
        });
    }
}
```

### 🎯 条件处理链

```csharp
public class ConditionalPipeline : AudioWriter
{
    public bool EnableNoiseGate { get; set; } = true;
    public bool EnableCompression { get; set; } = false;
    public float NoiseThreshold { get; set; } = -35f;

    private VolumeGateFilter _gate;
    private SimpleCompressor _compressor;

    public override void Initialize(AudioContext state)
    {
        base.Initialize(state);
        _gate = new VolumeGateFilter { ThresholdDb = NoiseThreshold };
        _compressor = new SimpleCompressor();

        _gate.Initialize(state);
        _compressor.Initialize(state);
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        // 基于设置的条件处理
        if (EnableNoiseGate)
            _gate.OnAudioPass(buffer, state);

        if (EnableCompression)
            _compressor.OnAudioPass(buffer, state);
    }

    public override void Dispose()
    {
        _gate?.Dispose();
        _compressor?.Dispose();
        base.Dispose();
    }
}
```

## 🔧 测试和调试

### 🐛 便于调试的处理器

```csharp
public class DebuggableProcessor : AudioWriter
{
    private readonly bool _debugMode;
    private int _frameCount;
    private float _totalEnergy;

    public DebuggableProcessor(bool debugMode = false)
    {
        _debugMode = debugMode;
    }

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        if (_debugMode)
        {
            _frameCount++;
            _totalEnergy += CalculateEnergy(buffer);

            if (_frameCount % 100 == 0) // 每100帧记录一次
            {
                float avgEnergy = _totalEnergy / _frameCount;
                Debug.Log($"处理器统计：{_frameCount} 帧，平均能量：{avgEnergy:F4}");
            }
        }

        // 正常处理...
    }

    private float CalculateEnergy(ReadOnlySpan<float> buffer)
    {
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
        return sum / buffer.Length;
    }
}
```

### 📊 性能分析

```csharp
public class ProfiledProcessor : AudioWriter
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();
    private long _totalProcessingTime;
    private int _processedFrames;

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        _stopwatch.Restart();

        try
        {
            ProcessAudioInternal(buffer, state);
        }
        finally
        {
            _stopwatch.Stop();
            _totalProcessingTime += _stopwatch.ElapsedTicks;
            _processedFrames++;
        }
    }

    public double GetAverageProcessingTimeMs()
    {
        if (_processedFrames == 0) return 0;

        double avgTicks = (double)_totalProcessingTime / _processedFrames;
        return (avgTicks / TimeSpan.TicksPerMillisecond);
    }

    private void ProcessAudioInternal(Span<float> buffer, AudioContext state)
    {
        // 你的处理代码
    }
}
```

## 🔍 下一步

继续你的 Easy Mic 之旅：

- **[故障排除](troubleshooting.md)** - 解决常见问题
- **[示例](examples.md)** - 查看真实世界的实现
- **[API 参考](api-reference.md)** - 完整的 API 文档

---

← [API 参考](api-reference.md) | **下一步：[故障排除](troubleshooting.md)** →
