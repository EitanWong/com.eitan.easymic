← [Core Concepts](core-concepts.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/audio-pipeline.md) →

# ⛓️ Audio Pipeline Deep Dive

The AudioPipeline is the heart of Easy Mic's processing system. It enables you to create complex audio workflows by chaining simple, reusable processors together.

## 🎯 Pipeline Fundamentals

### What is the Audio Pipeline?
The AudioPipeline acts as a **Chain of Responsibility** that processes audio data through a sequence of processors. Each processor receives the output of the previous processor, allowing you to build complex audio workflows from simple building blocks.

```csharp
// Visual representation:
Raw Mic Data → [Processor A] → [Processor B] → [Processor C] → Final Output
```

### Key Characteristics
- **🔄 Dynamic**: Add/remove processors during recording
- **🧵 Thread-Safe**: All operations are thread-safe
- **🎯 Ordered**: Processors execute in the order they were added
- **🧠 Zero-GC**: No allocations during audio processing
- **🛡️ Error-Resilient**: Errors in one processor don't crash the pipeline

## 🏗️ Pipeline Architecture

### Internal Structure
```csharp
public sealed class AudioPipeline : IAudioWorker
{
    private readonly List<IAudioWorker> _workers = new List<IAudioWorker>();
    private readonly object _lock = new object();
    private AudioState _initializeState;
    private bool _isInitialized;
    
    public int WorkerCount { get; } // Thread-safe access to worker count
}
```

### Thread Safety Design
The pipeline uses careful locking to ensure thread safety:

```csharp
// Thread-safe operations
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

## 🔄 Processor Lifecycle

### 1. Addition Phase
When you add a processor to the pipeline:

```csharp
EasyMicAPI.AddProcessor(recordingHandle, new VolumeGateFilter());
```

**What happens internally:**
1. Pipeline checks if processor already exists
2. If pipeline is already initialized, processor is initialized immediately
3. Processor is added to the end of the chain
4. Pipeline remains thread-safe throughout

### 2. Initialization Phase
When recording starts, all processors are initialized:

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

### 3. Processing Phase
During active recording, audio flows through each processor:

```csharp
public void OnAudioPass(Span<float> buffer, AudioState state)
{
    foreach (var worker in _workers)
    {
        // Handle buffer slicing based on state.Length
        Span<float> bufferForWorker = buffer;
        if (state.Length > 0 && state.Length < buffer.Length)
            bufferForWorker = buffer.Slice(0, state.Length);
            
        worker.OnAudioPass(bufferForWorker, state);
    }
}
```

### 4. Removal Phase
When you remove a processor:

```csharp
EasyMicAPI.RemoveProcessor(recordingHandle, processor);
```

**What happens:**
1. Processor is removed from the chain
2. Processor is automatically disposed
3. Pipeline continues operating with remaining processors

## 🎨 Processor Types in Detail

### 📖 AudioReader Pattern
Designed for **analysis without modification**:

```csharp
public class VolumeAnalyzer : AudioReader
{
    private float _currentVolume;
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // Calculate RMS volume
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
            
        _currentVolume = MathF.Sqrt(sum / buffer.Length);
    }
    
    public float GetCurrentVolume() => _currentVolume;
}
```

**Benefits:**
- ✅ **Compile-time safety**: Cannot accidentally modify audio
- ✅ **Performance**: No unnecessary copying
- ✅ **Clear intent**: Signals read-only operation

### ✏️ AudioWriter Pattern
Designed for **processing that modifies audio**:

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

**Benefits:**
- ✅ **Direct modification**: Efficient in-place processing
- ✅ **Type clarity**: Obvious that audio will be modified
- ✅ **Performance**: No intermediate buffers needed

## 🔧 Advanced Pipeline Patterns

### Multi-Channel Processing
Handle different channel configurations elegantly:

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
                
                // Process per-channel
                buffer[sampleIndex] = ProcessChannel(buffer[sampleIndex], channel);
            }
        }
    }
    
    private float ProcessChannel(float sample, int channel)
    {
        // Channel-specific processing
        return sample * (channel == 0 ? 1.0f : 0.8f); // Left/Right balance
    }
}
```

### Stateful Processing
Maintain state across audio buffers:

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
            // Get delayed sample
            int readPos = (_writePosition - _delaySamples + _delayBuffer.Length) % _delayBuffer.Length;
            float delayedSample = _delayBuffer[readPos];
            
            // Store current sample
            _delayBuffer[_writePosition] = buffer[i];
            
            // Output mix
            buffer[i] = (buffer[i] + delayedSample * 0.3f);
            
            _writePosition = (_writePosition + 1) % _delayBuffer.Length;
        }
    }
}
```

### Conditional Processing
Process audio based on conditions:

```csharp
public class ConditionalProcessor : AudioWriter
{
    public bool IsEnabled { get; set; } = true;
    public float Threshold { get; set; } = 0.1f;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (!IsEnabled) return;
        
        // Calculate buffer energy
        float energy = 0f;
        for (int i = 0; i < buffer.Length; i++)
            energy += buffer[i] * buffer[i];
        energy /= buffer.Length;
        
        // Only process if above threshold
        if (energy > Threshold)
        {
            // Apply processing
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] *= 1.5f; // Boost loud signals
        }
    }
}
```

## 🎪 Dynamic Pipeline Management

### Runtime Modification
Modify the pipeline while recording:

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
            Debug.Log("Noise gate enabled");
        }
    }
    
    public void DisableNoiseGate()
    {
        if (_gate != null)
        {
            EasyMicAPI.RemoveProcessor(_handle, _gate);
            _gate = null;
            Debug.Log("Noise gate disabled");
        }
    }
}
```

### Pipeline Monitoring
Monitor pipeline performance and state:

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

## 🛠️ Pipeline Best Practices

### ⚡ Performance Optimization

1. **Minimize Allocations**
```csharp
// ❌ Bad - allocates on every frame
public override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    var tempBuffer = new float[buffer.Length]; // ALLOCATION!
    // ... process
}

// ✅ Good - reuse buffers
private float[] _reusableBuffer = new float[4096];

public override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    if (_reusableBuffer.Length < buffer.Length)
        _reusableBuffer = new float[buffer.Length];
    // ... process
}
```

2. **Efficient Channel Processing**
```csharp
// ✅ Efficient frame-based processing
for (int frame = 0; frame < frameCount; frame++)
{
    int baseIndex = frame * channelCount;
    // Process all channels for this frame together
    for (int ch = 0; ch < channelCount; ch++)
        buffer[baseIndex + ch] = Process(buffer[baseIndex + ch]);
}
```

### 🎯 Order Optimization

Order processors by their impact and dependencies:

```csharp
// ✅ Optimal order:
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());    // 1. Remove noise first
EasyMicAPI.AddProcessor(handle, new AudioDownmixer());      // 2. Convert to mono
EasyMicAPI.AddProcessor(handle, new GainProcessor());       // 3. Adjust levels
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));      // 4. Capture result

// ❌ Poor order:
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));      // Captures noisy audio
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());    // Too late!
```

### 🧹 Resource Management

```csharp
public class ProperProcessorManagement : MonoBehaviour
{
    private RecordingHandle _handle;
    private readonly List<IAudioWorker> _processors = new List<IAudioWorker>();
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording("Microphone");
        
        // Keep references for proper disposal
        var gate = new VolumeGateFilter();
        var capturer = new AudioCapturer(5);
        
        _processors.Add(gate);
        _processors.Add(capturer);
        
        EasyMicAPI.AddProcessor(_handle, gate);
        EasyMicAPI.AddProcessor(_handle, capturer);
    }
    
    void OnDestroy()
    {
        // Stop recording first
        if (_handle.IsValid)
            EasyMicAPI.StopRecording(_handle);
        
        // Dispose all processors
        foreach (var processor in _processors)
            processor?.Dispose();
            
        _processors.Clear();
    }
}
```

## 🚨 Common Pitfalls

### 1. Modifying Processor State During Processing
```csharp
// ❌ Dangerous - can cause race conditions
public class DangerousProcessor : AudioWriter
{
    public float Gain { get; set; } // Modified from main thread!
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Audio thread reading Gain while main thread modifies it!
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain; // RACE CONDITION!
    }
}

// ✅ Safe - use atomic operations or locks
public class SafeProcessor : AudioWriter
{
    private volatile float _gain = 1.0f;
    
    public float Gain
    {
        get => _gain;
        set => _gain = value; // Atomic write
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        float currentGain = _gain; // Atomic read
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= currentGain;
    }
}
```

### 2. Forgetting to Handle AudioState Changes
```csharp
// ✅ Always check for format changes
private int _lastSampleRate = -1;
private int _lastChannelCount = -1;

protected override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    // Reinitialize if format changed
    if (state.SampleRate != _lastSampleRate || state.ChannelCount != _lastChannelCount)
    {
        ReinitializeForNewFormat(state);
        _lastSampleRate = state.SampleRate;
        _lastChannelCount = state.ChannelCount;
    }
    
    // Process audio...
}
```

## 🔍 What's Next?

Now that you understand the audio pipeline, explore:

- **[Built-in Processors](processors.md)** - Learn about all available processors
- **[API Reference](api-reference.md)** - Complete API documentation
- **[Best Practices](best-practices.md)** - Performance and architecture tips
- **[Examples](examples.md)** - Real-world pipeline configurations

---

← [Core Concepts](core-concepts.md) | **Next: [Built-in Processors](processors.md)** →