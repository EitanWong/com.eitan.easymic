← [API Reference](api-reference.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/best-practices.md) →

# 💡 Best Practices

Optimize your Easy Mic implementation with these proven patterns and techniques for maximum performance, reliability, and maintainability.

## 🚀 Performance Optimization

### ⚡ Audio Thread Best Practices

The audio processing thread is real-time critical. Follow these guidelines:

#### 🚫 Avoid Allocations
```csharp
// ❌ Bad - allocates on every frame
public class BadProcessor : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        var tempArray = new float[buffer.Length]; // ALLOCATION!
        // Process...
    }
}

// ✅ Good - reuse buffers
public class GoodProcessor : AudioWriter
{
    private float[] _workBuffer = new float[4096];
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Resize only if needed
        if (_workBuffer.Length < buffer.Length)
            _workBuffer = new float[buffer.Length * 2]; // Grow with headroom
        
        // Use _workBuffer for processing
    }
}
```

#### 🎯 Minimize Work in Audio Callbacks
```csharp
// ❌ Bad - complex calculations on audio thread
public class BadFilter : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        foreach (var sample in buffer)
        {
            // Expensive operations on every sample!
            var coefficient = Math.Sin(Math.PI * frequency / state.SampleRate);
            var result = sample * coefficient;
        }
    }
}

// ✅ Good - pre-calculate in Initialize()
public class GoodFilter : AudioWriter
{
    private float _preCalculatedCoeff;
    
    public override void Initialize(AudioState state)
    {
        base.Initialize(state);
        // Pre-calculate expensive values
        _preCalculatedCoeff = MathF.Sin(MathF.PI * frequency / state.SampleRate);
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= _preCalculatedCoeff; // Fast multiplication
    }
}
```

#### 🔄 Efficient Channel Processing
```csharp
// ❌ Inefficient - sample-by-sample processing
for (int i = 0; i < buffer.Length; i++)
{
    int channel = i % channelCount;
    buffer[i] = ProcessChannel(buffer[i], channel);
}

// ✅ Efficient - frame-based processing
int frameCount = buffer.Length / channelCount;
for (int frame = 0; frame < frameCount; frame++)
{
    int baseIndex = frame * channelCount;
    
    // Process all channels for this frame together
    for (int ch = 0; ch < channelCount; ch++)
    {
        buffer[baseIndex + ch] = ProcessChannel(buffer[baseIndex + ch], ch);
    }
}
```

### 🧠 Memory Management

#### Buffer Size Optimization
```csharp
public class OptimalBufferSizes
{
    // ✅ Good buffer sizes (powers of 2 or multiples of 64)
    public static readonly int[] RecommendedSizes = { 64, 128, 256, 512, 1024, 2048 };
    
    public static int GetOptimalBufferSize(int requestedSize)
    {
        // Find the next power of 2
        int size = 1;
        while (size < requestedSize)
            size <<= 1;
        return size;
    }
}

// Usage in AudioCapturer
public AudioCapturer(int maxDurationSeconds, int sampleRate = 48000)
{
    int requestedSize = maxDurationSeconds * sampleRate;
    int optimalSize = OptimalBufferSizes.GetOptimalBufferSize(requestedSize);
    _audioBuffer = new AudioBuffer(optimalSize);
}
```

#### Smart Resource Management
```csharp
public class ResourceAwareProcessor : AudioWriter, IDisposable
{
    private float[] _largeBuffer;
    private bool _disposed = false;
    
    public override void Initialize(AudioState state)
    {
        base.Initialize(state);
        
        // Allocate only what we need
        int bufferSize = Math.Max(state.Length * 4, 1024); // 4x headroom
        _largeBuffer = new float[bufferSize];
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (_disposed) return;
        
        // Use the pre-allocated buffer
        var workSpan = new Span<float>(_largeBuffer, 0, buffer.Length);
        // Process...
    }
    
    public override void Dispose()
    {
        if (_disposed) return;
        
        // Clear large arrays to help GC
        _largeBuffer = null;
        _disposed = true;
        
        base.Dispose();
    }
}
```

## 🏗️ Architecture Patterns

### 🎭 Facade Pattern for Complex Operations
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
        
        public Builder AddProcessor<T>(T processor) where T : IAudioWorker
        {
            _processors.Add(processor);
            return this;
        }
        
        public RecordingSession Build()
        {
            var handle = EasyMicAPI.StartRecording(_deviceName, _sampleRate, _channel);
            
            foreach (var processor in _processors)
                EasyMicAPI.AddProcessor(handle, processor);
                
            return new RecordingSession(handle, _processors);
        }
    }
    
    // Usage:
    public void StartHighQualityRecording()
    {
        var session = new Builder()
            .WithDevice("Built-in Microphone")
            .WithQuality(SampleRate.Hz48000, Channel.Stereo)
            .AddProcessor(new VolumeGateFilter { ThresholdDb = -30f })
            .AddProcessor(new AudioDownmixer())
            .AddProcessor(new AudioCapturer(60))
            .Build();
    }
}
```

### 🔧 Strategy Pattern for Processing Algorithms
```csharp
public interface INoiseReductionStrategy
{
    void ReduceNoise(Span<float> buffer, AudioState state);
}

public class SimpleGateStrategy : INoiseReductionStrategy
{
    public float Threshold { get; set; } = 0.01f;
    
    public void ReduceNoise(Span<float> buffer, AudioState state)
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
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        Strategy?.ReduceNoise(buffer, state);
    }
}

// Usage:
var processor = new AdaptiveNoiseReduction
{
    Strategy = new SimpleGateStrategy { Threshold = 0.02f }
};
```

### 🎪 Observer Pattern for Monitoring
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
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        _currentTime += (float)buffer.Length / state.SampleRate;
        
        // Calculate volume metrics
        float rms = CalculateRMS(buffer);
        float peak = CalculatePeak(buffer);
        
        // Notify listeners
        foreach (var listener in _listeners)
            listener.OnVolumeChanged(rms, peak);
        
        // Detect silence/speech
        bool isSilent = rms < 0.01f;
        if (isSilent)
        {
            if (_silenceStartTime < 0)
                _silenceStartTime = _currentTime;
            else if (_currentTime - _silenceStartTime > 1.0f) // 1 second of silence
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

## 🛡️ Thread Safety

### 🔒 Safe Parameter Updates
```csharp
public class ThreadSafeProcessor : AudioWriter
{
    private volatile float _gain = 1.0f;
    private volatile bool _enabled = true;
    
    // Safe property access from any thread
    public float Gain
    {
        get => _gain;
        set => _gain = value; // Atomic write for float
    }
    
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value; // Atomic write for bool
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (!_enabled) return;
        
        float currentGain = _gain; // Atomic read
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= currentGain;
    }
}
```

### 🎭 Thread-Safe State Management
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
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Check for config updates (rare operation)
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
        
        // Use stable config for processing
        ProcessWithConfig(buffer, _config);
    }
}
```

## 🎯 Error Handling

### 🛡️ Graceful Degradation
```csharp
public class RobustProcessor : AudioWriter
{
    private bool _hasError = false;
    private int _errorCount = 0;
    private const int MaxErrors = 5;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (_hasError) return; // Fail-safe mode
        
        try
        {
            ProcessAudioInternal(buffer, state);
            _errorCount = 0; // Reset on success
        }
        catch (Exception ex)
        {
            _errorCount++;
            Debug.LogError($"Audio processing error {_errorCount}/{MaxErrors}: {ex.Message}");
            
            if (_errorCount >= MaxErrors)
            {
                Debug.LogError("Too many errors, disabling processor");
                _hasError = true;
            }
            
            // Pass audio through unmodified on error
        }
    }
    
    private void ProcessAudioInternal(Span<float> buffer, AudioState state)
    {
        // Your processing code here
        // Throws exceptions on error
    }
}
```

### 📊 Health Monitoring
```csharp
public class HealthMonitorProcessor : AudioWriter
{
    private int _processedFrames;
    private int _skippedFrames;
    private DateTime _lastHealthReport = DateTime.Now;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        _processedFrames++;
        
        // Detect audio dropouts
        if (buffer.Length == 0)
        {
            _skippedFrames++;
            return;
        }
        
        // Periodic health reporting
        if ((DateTime.Now - _lastHealthReport).TotalSeconds >= 10)
        {
            float dropoutRate = (float)_skippedFrames / _processedFrames;
            if (dropoutRate > 0.01f) // More than 1% dropouts
            {
                Debug.LogWarning($"Audio health: {dropoutRate:P} dropout rate");
            }
            
            _lastHealthReport = DateTime.Now;
            _skippedFrames = 0;
            _processedFrames = 0;
        }
        
        // Process audio normally...
    }
}
```

## 🎪 Pipeline Patterns

### 🔄 Dynamic Pipeline Reconfiguration
```csharp
public class AdaptivePipeline : MonoBehaviour
{
    private RecordingHandle _handle;
    private VolumeGateFilter _noiseGate;
    private AudioDownmixer _downmixer;
    private readonly Queue<System.Action> _pipelineUpdates = new Queue<System.Action>();
    
    void Update()
    {
        // Process pipeline updates on main thread
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
                _noiseGate = new VolumeGateFilter { ThresholdDb = thresholdDb };
                EasyMicAPI.AddProcessor(_handle, _noiseGate);
            }
        });
    }
    
    public void DisableNoiseGateAsync()
    {
        _pipelineUpdates.Enqueue(() =>
        {
            if (_noiseGate != null)
            {
                EasyMicAPI.RemoveProcessor(_handle, _noiseGate);
                _noiseGate = null;
            }
        });
    }
}
```

### 🎯 Conditional Processing Chain
```csharp
public class ConditionalPipeline : AudioWriter
{
    public bool EnableNoiseGate { get; set; } = true;
    public bool EnableCompression { get; set; } = false;
    public float NoiseThreshold { get; set; } = -35f;
    
    private VolumeGateFilter _gate;
    private SimpleCompressor _compressor;
    
    public override void Initialize(AudioState state)
    {
        base.Initialize(state);
        _gate = new VolumeGateFilter { ThresholdDb = NoiseThreshold };
        _compressor = new SimpleCompressor();
        
        _gate.Initialize(state);
        _compressor.Initialize(state);
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Conditional processing based on settings
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

## 🔧 Testing and Debugging

### 🐛 Debug-Friendly Processors
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
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (_debugMode)
        {
            _frameCount++;
            _totalEnergy += CalculateEnergy(buffer);
            
            if (_frameCount % 100 == 0) // Log every 100 frames
            {
                float avgEnergy = _totalEnergy / _frameCount;
                Debug.Log($"Processor stats: {_frameCount} frames, avg energy: {avgEnergy:F4}");
            }
        }
        
        // Normal processing...
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

### 📊 Performance Profiling
```csharp
public class ProfiledProcessor : AudioWriter
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();
    private long _totalProcessingTime;
    private int _processedFrames;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
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
    
    private void ProcessAudioInternal(Span<float> buffer, AudioState state)
    {
        // Your processing code here
    }
}
```

## 🔍 What's Next?

Continue your Easy Mic journey:

- **[Troubleshooting](troubleshooting.md)** - Solve common issues
- **[Examples](examples.md)** - See real-world implementations
- **[API Reference](api-reference.md)** - Complete API documentation

---

← [API Reference](api-reference.md) | **Next: [Troubleshooting](troubleshooting.md)** →