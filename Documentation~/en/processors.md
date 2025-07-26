← [Audio Pipeline](audio-pipeline.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/processors.md) →

# 🧩 Built-in Processors

Easy Mic comes with a comprehensive collection of audio processors designed for common audio processing tasks. Each processor is optimized for performance and designed to work seamlessly in the audio pipeline.

## 📖 AudioReader Processors

### 📊 VolumeAnalyzer (Example Implementation)
Analyzes audio volume without modifying the stream.

```csharp
public class VolumeAnalyzer : AudioReader
{
    private float _currentRMS;
    private float _currentPeak;
    
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
        
        _currentRMS = MathF.Sqrt(sum / buffer.Length);
        _currentPeak = peak;
    }
    
    public float GetRMSVolume() => _currentRMS;
    public float GetPeakVolume() => _currentPeak;
    public float GetRMSVolumeDb() => 20f * MathF.Log10(_currentRMS + 1e-10f);
}
```

## ✏️ AudioWriter Processors

### 📼 AudioCapturer
Captures incoming audio data into a buffer or saves it to a file.

#### Features
- **High-Performance Buffer**: Uses lock-free `AudioBuffer` for zero-GC captures
- **Unity Integration**: Direct conversion to `AudioClip`
- **Configurable Duration**: Set maximum capture duration
- **Multi-Channel Support**: Handles mono and stereo audio

#### Usage
```csharp
// Create capturer with 10-second max duration
var capturer = new AudioCapturer(10);
EasyMicAPI.AddProcessor(recordingHandle, capturer);

// Later, get the captured audio
float[] samples = capturer.GetCapturedAudioSamples();
AudioClip clip = capturer.GetCapturedAudioClip();
```

#### Constructor
```csharp
public AudioCapturer(int maxDurationInSeconds = 60)
```

#### Key Methods
- `GetCapturedAudioSamples()` - Returns raw float array
- `GetCapturedAudioClip()` - Returns Unity AudioClip
- `Clear()` - Clears the capture buffer

#### Implementation Details
```csharp
public class AudioCapturer : AudioReader
{
    private AudioBuffer _audioBuffer;
    private readonly int _maxCaptureDuration;
    private AudioState _audioState;

    public override void Initialize(AudioState state)
    {
        // Calculate total samples needed
        int totalSamples = state.Length * _maxCaptureDuration;
        _audioBuffer = new AudioBuffer(totalSamples);
        _audioState = state;
        base.Initialize(state);
    }

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        _audioBuffer.Write(buffer);
        if (_audioState != state)
            _audioState = state;
    }
}
```

---

### 🔄 AudioDownmixer
Converts multi-channel audio (e.g., stereo) into single-channel audio (mono).

#### Features
- **Intelligent Mixing**: Preserves audio quality during downmixing
- **Configurable Algorithms**: Multiple mixing strategies
- **Performance Optimized**: Zero allocations during processing
- **Channel-Aware**: Automatically detects input channel configuration

#### Usage
```csharp
var downmixer = new AudioDownmixer();
EasyMicAPI.AddProcessor(recordingHandle, downmixer);
```

#### Mixing Algorithms
```csharp
public enum MixingAlgorithm
{
    Average,        // Simple average of all channels
    LeftChannel,    // Take only left channel
    RightChannel,   // Take only right channel
    WeightedMix     // Weighted average (customizable)
}
```

#### Configuration
```csharp
var downmixer = new AudioDownmixer
{
    Algorithm = MixingAlgorithm.Average,
    LeftWeight = 0.6f,   // For weighted mixing
    RightWeight = 0.4f
};
```

#### Implementation Example
```csharp
protected override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    if (state.ChannelCount <= 1) return; // Already mono
    
    int frameCount = buffer.Length / state.ChannelCount;
    
    for (int frame = 0; frame < frameCount; frame++)
    {
        int baseIndex = frame * state.ChannelCount;
        float mixedSample = 0f;
        
        // Mix all channels
        for (int ch = 0; ch < state.ChannelCount; ch++)
            mixedSample += buffer[baseIndex + ch];
            
        mixedSample /= state.ChannelCount;
        
        // Write mixed sample to all channels (or just first for true mono)
        buffer[baseIndex] = mixedSample;
    }
    
    // Update state to reflect new channel count
    state.ChannelCount = 1;
}
```

---

### 🔇 VolumeGateFilter
A sophisticated noise gate that silences audio below a volume threshold with smooth transitions.

#### Features
- **Professional Gate States**: Closed, Attacking, Open, Holding, Releasing
- **Lookahead Processing**: Preserves transients and prevents artifacts
- **Multi-Channel Aware**: Processes all channels simultaneously
- **Sample-Accurate Transitions**: Smooth attack/release curves
- **Real-Time Parameter Updates**: Adjust settings during recording

#### Configuration Properties
```csharp
public class VolumeGateFilter : AudioWriter
{
    public float ThresholdDb { get; set; } = -35.0f;     // Gate threshold in dB
    public float AttackTime { get; set; } = 0.005f;      // Time to open (5ms)
    public float HoldTime { get; set; } = 0.25f;         // Hold time (250ms)
    public float ReleaseTime { get; set; } = 0.2f;       // Time to close (200ms)
    public float LookaheadTime { get; set; } = 0.005f;   // Lookahead (5ms)
    
    // Read-only status
    public VolumeGateState CurrentState { get; private set; }
    public float CurrentDb { get; }
}
```

#### Gate States
```csharp
public enum VolumeGateState
{
    Closed,     // Gate is closed, no audio passes
    Attacking,  // Gate is opening
    Open,       // Gate is fully open
    Holding,    // Gate is waiting before closing
    Releasing   // Gate is closing
}
```

#### Usage Examples
```csharp
// Basic noise gate
var gate = new VolumeGateFilter
{
    ThresholdDb = -30f,
    AttackTime = 0.001f,   // Fast attack for speech
    ReleaseTime = 0.5f     // Slow release to avoid cutting words
};

// Aggressive noise gate for noisy environments
var aggressiveGate = new VolumeGateFilter
{
    ThresholdDb = -20f,    // Higher threshold
    HoldTime = 0.1f,       // Shorter hold
    ReleaseTime = 0.1f     // Faster release
};

EasyMicAPI.AddProcessor(recordingHandle, gate);
```

#### Advanced Configuration
```csharp
public class AdaptiveGateController : MonoBehaviour
{
    private VolumeGateFilter _gate;
    
    void Start()
    {
        _gate = new VolumeGateFilter();
        EasyMicAPI.AddProcessor(recordingHandle, _gate);
    }
    
    void Update()
    {
        // Adapt threshold based on ambient noise
        float ambientLevel = GetAmbientNoiseLevel();
        _gate.ThresholdDb = ambientLevel + 6f; // 6dB above ambient
        
        // Display current state
        Debug.Log($"Gate State: {_gate.CurrentState}, Level: {_gate.CurrentDb:F1}dB");
    }
}
```

#### Technical Implementation Highlights
```csharp
protected override void OnAudioWrite(Span<float> buffer, AudioState state)
{
    ProcessAudio(buffer);
}

private void ProcessAudio(Span<float> audioBuffer)
{
    int frameCount = audioBuffer.Length / _channelCount;
    
    for (int i = 0; i < frameCount; i++)
    {
        // Lookahead detection for transient preservation
        int detectionPos = (_writePosition + _lookaheadFrames * _channelCount) % _bufferSize;
        int processPos = _writePosition;
        
        // Envelope detection using future audio
        float maxInFrame = 0f;
        for (int ch = 0; ch < _channelCount; ch++)
        {
            float sample = MathF.Abs(_internalBuffer[detectionPos + ch]);
            if (sample > maxInFrame) maxInFrame = sample;
        }
        
        // Update envelope with attack/release
        if (maxInFrame > _envelope)
            _envelope = maxInFrame; // Instant attack
        else
            _envelope *= _envelopeReleaseCoeff; // Smooth release
            
        // State machine update
        UpdateGateState(_envelope >= _thresholdLinear, 1.0f / _sampleRate);
        
        // Apply gain based on current state
        ApplyGateGain(audioBuffer, i);
    }
}
```

---

### 🔁 LoopbackPlayer
Real-time audio loopback for monitoring and testing applications.

#### Features
- **Zero-Latency Monitoring**: Direct audio passthrough
- **Volume Control**: Adjustable monitoring level
- **Mute Capability**: Toggle monitoring on/off
- **Performance Optimized**: Minimal processing overhead

#### Usage
```csharp
var loopback = new LoopbackPlayer
{
    Volume = 0.5f,        // 50% monitoring volume
    IsMuted = false       // Enable monitoring
};

EasyMicAPI.AddProcessor(recordingHandle, loopback);
```

#### Implementation
```csharp
public class LoopbackPlayer : AudioWriter
{
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = false;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (IsMuted || Volume <= 0f) return;
        
        // Simply scale the audio for monitoring
        if (Volume != 1.0f)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] *= Volume;
        }
        
        // In a real implementation, this might route to speakers
        // For now, it just applies volume scaling
    }
}
```

---

### 🗣️ SherpaRealtimeSpeechRecognizer
Real-time speech-to-text processor using the Sherpa-ONNX engine.

#### Features
- **Real-Time Recognition**: Low-latency speech-to-text
- **Multiple Languages**: Support for various language models
- **Confidence Scoring**: Recognition confidence levels
- **Streaming Mode**: Continuous recognition
- **Event-Based**: Callbacks for recognition events

#### Requirements
```csharp
// Requires the Sherpa-ONNX Unity package
// Install via: https://github.com/EitanWong/com.eitan.sherpa-onnx-unity
```

#### Usage
```csharp
var recognizer = new SherpaRealtimeSpeechRecognizer("path/to/model");
recognizer.OnPartialResult += (text) => Debug.Log($"Partial: {text}");
recognizer.OnFinalResult += (text) => Debug.Log($"Final: {text}");

EasyMicAPI.AddProcessor(recordingHandle, recognizer);
```

#### Events
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

## 🎛️ Creating Custom Processors

### AudioReader Template
```csharp
public class CustomAnalyzer : AudioReader
{
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // Your analysis code here - cannot modify buffer
        // Perfect for: volume meters, pitch detection, silence detection
    }
}
```

### AudioWriter Template
```csharp
public class CustomEffect : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Your processing code here - can modify buffer
        // Perfect for: filters, effects, format conversion
        
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = ProcessSample(buffer[i]);
        }
    }
    
    private float ProcessSample(float input)
    {
        // Your sample processing logic
        return input;
    }
}
```

### Advanced Custom Processor
```csharp
public class AdvancedProcessor : AudioWriter
{
    private float[] _delayBuffer;
    private int _bufferSize;
    private int _writePos;
    
    public override void Initialize(AudioState state)
    {
        base.Initialize(state);
        
        // Initialize based on audio format
        _bufferSize = state.SampleRate; // 1 second delay
        _delayBuffer = new float[_bufferSize * state.ChannelCount];
        _writePos = 0;
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Handle format changes
        if (_delayBuffer.Length != _bufferSize * state.ChannelCount)
        {
            Initialize(state);
        }
        
        // Process audio with state information
        ProcessFrames(buffer, state);
    }
    
    private void ProcessFrames(Span<float> buffer, AudioState state)
    {
        int frameCount = buffer.Length / state.ChannelCount;
        
        for (int frame = 0; frame < frameCount; frame++)
        {
            for (int ch = 0; ch < state.ChannelCount; ch++)
            {
                int bufferIndex = frame * state.ChannelCount + ch;
                int delayIndex = _writePos * state.ChannelCount + ch;
                
                // Get delayed sample
                float delayed = _delayBuffer[delayIndex];
                
                // Store current sample
                _delayBuffer[delayIndex] = buffer[bufferIndex];
                
                // Output delayed sample
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

## 🎯 Processor Best Practices

### Performance Guidelines
- **Minimize allocations** in OnAudioPass methods
- **Reuse buffers** when possible
- **Avoid complex calculations** on the audio thread
- **Use efficient algorithms** for real-time processing

### Thread Safety
- **Use volatile** for simple state variables
- **Avoid locks** in audio processing methods
- **Pre-calculate** expensive operations in Initialize()
- **Be careful** with property setters during processing

### Resource Management
- **Override Dispose()** to clean up resources
- **Clear large buffers** in Dispose()
- **Remove event handlers** to prevent memory leaks
- **Stop processors** before disposing

## 🔍 What's Next?

Explore more advanced topics:

- **[API Reference](api-reference.md)** - Complete API documentation
- **[Best Practices](best-practices.md)** - Performance optimization techniques  
- **[Examples](examples.md)** - Real-world processor configurations
- **[Troubleshooting](troubleshooting.md)** - Common issues and solutions

---

← [Audio Pipeline](audio-pipeline.md) | **Next: [API Reference](api-reference.md)** →