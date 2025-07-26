← [Best Practices](best-practices.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/troubleshooting.md) →

# 🔧 Troubleshooting

Common issues, solutions, and debugging techniques for Easy Mic.

## 🚨 Installation Issues

### Package Not Found
**Problem:** Unity Package Manager cannot find Easy Mic package.

**Solutions:**
1. **Verify Git URL:**
   ```
   https://github.com/EitanWong/com.eitan.easymic.git#upm
   ```

2. **Check Unity Version:**
   - Requires Unity 2021.3 LTS or higher
   - Update Unity if using an older version

3. **Network Issues:**
   ```bash
   # Test Git access
   git ls-remote https://github.com/EitanWong/com.eitan.easymic.git
   ```

4. **Use OpenUPM (Alternative):**
   ```bash
   openupm add com.eitan.easymic
   ```

### Compilation Errors
**Problem:** C# compilation errors after installation.

**Common Errors & Solutions:**

```csharp
// Error: "Span<T> not available"
// Solution: Ensure .NET Standard 2.1 or higher
// Player Settings > Configuration > Api Compatibility Level
```

```csharp
// Error: "unsafe code not enabled"  
// Solution: Enable unsafe code in Player Settings
// Player Settings > Allow 'unsafe' Code ✓
```

**Assembly Definition Issues:**
1. Verify assembly references in your scripts
2. Add `Eitan.EasyMic` to your assembly dependencies

---

## 🎤 Device and Permission Issues

### No Microphone Devices Found
**Problem:** `EasyMicAPI.Devices` returns empty array.

**Diagnostic Steps:**
```csharp
public void DiagnoseDeviceIssues()
{
    // 1. Check permissions first
    if (!PermissionUtils.HasPermission())
    {
        Debug.LogError("❌ Microphone permission not granted");
        PermissionUtils.RequestPermission(granted =>
        {
            if (granted)
            {
                Debug.Log("✅ Permission granted, refreshing devices");
                EasyMicAPI.Refresh();
            }
            else
            {
                Debug.LogError("❌ Permission denied by user");
            }
        });
        return;
    }
    
    // 2. Refresh device list
    EasyMicAPI.Refresh();
    var devices = EasyMicAPI.Devices;
    
    if (devices.Length == 0)
    {
        Debug.LogError("❌ No devices found after refresh");
        Debug.Log("Check system audio settings and ensure microphone is connected");
    }
    else
    {
        Debug.Log($"✅ Found {devices.Length} device(s):");
        foreach (var device in devices)
        {
            Debug.Log($"  - {device.Name} (Default: {device.IsDefault})");
        }
    }
}
```

**Platform-Specific Solutions:**

#### Windows
- Check Privacy Settings: `Settings > Privacy > Microphone`
- Verify microphone is not disabled in Device Manager
- Test microphone in Windows Sound Recorder

#### macOS
- Check System Preferences: `Security & Privacy > Privacy > Microphone`
- Add Unity/your app to allowed applications
- Reset microphone permissions: `tccutil reset Microphone`

#### Linux
- Check ALSA/PulseAudio configuration
- Verify user permissions: `sudo usermod -a -G audio $USER`
- Test with: `arecord -l` to list capture devices

#### Android
- Add permission to `AndroidManifest.xml`:
  ```xml
  <uses-permission android:name="android.permission.RECORD_AUDIO" />
  ```
- Test on physical device (emulator microphone may not work)

#### iOS
- Test on physical device only
- Check iOS Settings: `Settings > Privacy > Microphone`

### Permission Denied Errors
**Problem:** Recording fails with permission errors.

**Solution Pattern:**
```csharp
public class SafeRecordingStarter : MonoBehaviour
{
    public void StartRecordingWithPermissionCheck()
    {
        if (PermissionUtils.HasPermission())
        {
            StartRecording();
        }
        else
        {
            PermissionUtils.RequestPermission(OnPermissionResult);
        }
    }
    
    private void OnPermissionResult(bool granted)
    {
        if (granted)
        {
            Debug.Log("✅ Permission granted");
            StartRecording();
        }
        else
        {
            Debug.LogError("❌ Permission denied - cannot record audio");
            ShowPermissionRequiredUI();
        }
    }
    
    private void StartRecording()
    {
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        
        if (devices.Length > 0)
        {
            var handle = EasyMicAPI.StartRecording(devices[0].Name);
            if (handle.IsValid)
            {
                Debug.Log("🎙️ Recording started successfully");
            }
            else
            {
                Debug.LogError("❌ Failed to start recording");
            }
        }
    }
}
```

---

## 🎧 Audio Quality Issues

### No Audio Captured
**Problem:** Recording appears to work but no audio is captured.

**Debugging Steps:**
```csharp
public class AudioDebugger : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioCapturer _capturer;
    private VolumeMonitor _monitor;
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording();
        
        // Add volume monitor to check if audio is flowing
        _monitor = new VolumeMonitor();
        EasyMicAPI.AddProcessor(_handle, _monitor);
        
        // Add capturer
        _capturer = new AudioCapturer(5);
        EasyMicAPI.AddProcessor(_handle, _capturer);
        
        // Check volume levels
        InvokeRepeating(nameof(CheckAudioLevels), 1f, 1f);
    }
    
    void CheckAudioLevels()
    {
        float volume = _monitor.GetCurrentVolume();
        Debug.Log($"Current volume: {volume:F4} ({20 * Mathf.Log10(volume + 1e-10f):F1} dB)");
        
        if (volume < 0.001f)
        {
            Debug.LogWarning("⚠️ Very low audio levels - check:");
            Debug.LogWarning("  - Microphone is not muted");
            Debug.LogWarning("  - System microphone levels");
            Debug.LogWarning("  - Microphone is working in other apps");
        }
    }
}

public class VolumeMonitor : AudioReader
{
    private float _currentVolume;
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
        _currentVolume = MathF.Sqrt(sum / buffer.Length);
    }
    
    public float GetCurrentVolume() => _currentVolume;
}
```

### Audio Dropouts/Glitches
**Problem:** Audio has gaps, pops, or glitches.

**Common Causes & Solutions:**

#### 1. Buffer Underrun
```csharp
// ❌ Too small buffer
var capturer = new AudioCapturer(1); // Only 1 second

// ✅ Adequate buffer size
var capturer = new AudioCapturer(10); // 10 seconds headroom
```

#### 2. Processing Too Heavy
```csharp
// ❌ Heavy processing on audio thread
public class HeavyProcessor : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        foreach (var sample in buffer)
        {
            // Complex math operations - causes dropouts!
            var result = Math.Sin(Math.Cos(Math.Tan(sample * Math.PI)));
        }
    }
}

// ✅ Lightweight processing
public class EfficientProcessor : AudioWriter
{
    private float _precomputedGain;
    
    public override void Initialize(AudioState state)
    {
        base.Initialize(state);
        _precomputedGain = 1.5f; // Pre-calculate
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= _precomputedGain; // Simple multiplication
    }
}
```

#### 3. Threading Issues
```csharp
// ❌ Unsafe property access
public class UnsafeProcessor : AudioWriter
{
    public float Gain { get; set; } = 1.0f; // Modified from UI thread!
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // Race condition - Gain might change mid-processing!
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain;
    }
}

// ✅ Thread-safe property access
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

### Low Audio Quality
**Problem:** Audio sounds muffled, distorted, or low quality.

**Solutions:**

#### 1. Use Higher Sample Rates
```csharp
// ❌ Low quality
var handle = EasyMicAPI.StartRecording("Microphone", SampleRate.Hz8000);

// ✅ High quality  
var handle = EasyMicAPI.StartRecording("Microphone", SampleRate.Hz48000);
```

#### 2. Check Processor Order
```csharp
// ❌ Poor processing order
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));      // Captures early
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());    // Gate after capture
EasyMicAPI.AddProcessor(handle, new AudioDownmixer());      // Too late

// ✅ Optimal processing order
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());    // Remove noise first
EasyMicAPI.AddProcessor(handle, new AudioDownmixer());      // Convert format
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));      // Capture clean audio
```

#### 3. Proper Channel Handling
```csharp
// Check device capabilities
var device = EasyMicAPI.Devices[0];
Debug.Log($"Device supports {device.MaxChannels} channels");

// Use appropriate channel count
var channelMode = device.MaxChannels > 1 ? Channel.Stereo : Channel.Mono;
var handle = EasyMicAPI.StartRecording(device.Name, SampleRate.Hz48000, channelMode);
```

---

## 🏗️ Pipeline Issues

### Processors Not Working
**Problem:** Processors added to pipeline but no effect on audio.

**Debugging Steps:**
```csharp
public class PipelineDebugger : MonoBehaviour
{
    private RecordingHandle _handle;
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording();
        
        // Add a test processor to verify pipeline is working
        var testProcessor = new PipelineTestProcessor();
        EasyMicAPI.AddProcessor(_handle, testProcessor);
        
        // Add your actual processors
        var gate = new VolumeGateFilter();
        EasyMicAPI.AddProcessor(_handle, gate);
        
        // Monitor pipeline
        InvokeRepeating(nameof(CheckPipelineStatus), 1f, 1f);
    }
    
    void CheckPipelineStatus()
    {
        var info = EasyMicAPI.GetRecordingInfo(_handle);
        Debug.Log($"Recording active: {info.IsActive}, Processors: {info.ProcessorCount}");
    }
}

public class PipelineTestProcessor : AudioWriter
{
    private int _frameCount = 0;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        _frameCount++;
        if (_frameCount % 100 == 0)
        {
            Debug.Log($"✅ Pipeline active - processed {_frameCount} frames");
        }
    }
}
```

### Processor Exceptions
**Problem:** Exceptions thrown in processors crash recording.

**Safe Processor Template:**
```csharp
public class SafeProcessor : AudioWriter
{
    private bool _hasError = false;
    private int _errorCount = 0;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (_hasError) return; // Skip processing if in error state
        
        try
        {
            ProcessAudioSafely(buffer, state);
        }
        catch (Exception ex)
        {
            _errorCount++;
            Debug.LogError($"Processor error ({_errorCount}): {ex.Message}");
            
            if (_errorCount > 5)
            {
                Debug.LogError("Too many errors, disabling processor");
                _hasError = true;
            }
        }
    }
    
    private void ProcessAudioSafely(Span<float> buffer, AudioState state)
    {
        // Your processing code here
    }
}
```

---

## 🛠️ Performance Issues

### High CPU Usage
**Problem:** Easy Mic using too much CPU.

**Profiling and Optimization:**
```csharp
public class PerformanceProfiler : AudioWriter
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();
    private long _totalTime = 0;
    private int _frameCount = 0;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        _stopwatch.Restart();
        
        // Your processing code here
        ProcessAudio(buffer, state);
        
        _stopwatch.Stop();
        _totalTime += _stopwatch.ElapsedTicks;
        _frameCount++;
        
        // Report every 1000 frames
        if (_frameCount % 1000 == 0)
        {
            double avgMs = (_totalTime / (double)_frameCount) / TimeSpan.TicksPerMillisecond;
            Debug.Log($"Avg processing time: {avgMs:F3}ms per frame");
            
            if (avgMs > 1.0) // More than 1ms is concerning
            {
                Debug.LogWarning("⚠️ High processing time detected!");
            }
        }
    }
}
```

**Common Optimizations:**
1. **Pre-calculate values in Initialize()**
2. **Use fixed-point math instead of floating-point where possible**
3. **Minimize memory allocations**
4. **Use efficient algorithms (O(n) vs O(n²))**

### Memory Leaks
**Problem:** Memory usage grows over time.

**Leak Detection:**
```csharp
public class MemoryMonitor : MonoBehaviour
{
    void Start()
    {
        InvokeRepeating(nameof(CheckMemory), 5f, 5f);
    }
    
    void CheckMemory()
    {
        long memory = System.GC.GetTotalMemory(false);
        Debug.Log($"Memory usage: {memory / 1024 / 1024}MB");
        
        // Force GC to check for leaks
        System.GC.Collect();
        long afterGC = System.GC.GetTotalMemory(true);
        Debug.Log($"After GC: {afterGC / 1024 / 1024}MB");
    }
}
```

**Common Leak Sources:**
1. **Not disposing processors**
2. **Event handlers not removed**
3. **Static references to processor instances**
4. **Large buffers not cleared**

**Proper Cleanup:**
```csharp
public class ProperCleanup : MonoBehaviour
{
    private RecordingHandle _handle;
    private List<IAudioWorker> _processors = new List<IAudioWorker>();
    
    void OnDestroy()
    {
        // Stop recording first
        if (_handle.IsValid)
            EasyMicAPI.StopRecording(_handle);
        
        // Dispose all processors
        foreach (var processor in _processors)
            processor?.Dispose();
        
        _processors.Clear();
        
        // Final cleanup
        EasyMicAPI.Cleanup();
    }
}
```

---

## 🔍 Debugging Tools

### Audio Pipeline Visualizer
```csharp
public class PipelineVisualizer : AudioReader
{
    public event System.Action<float[], AudioState> OnAudioData;
    private float[] _visualData = new float[1024];
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // Downsample for visualization
        int step = Math.Max(1, buffer.Length / _visualData.Length);
        
        for (int i = 0; i < _visualData.Length && i * step < buffer.Length; i++)
        {
            _visualData[i] = buffer[i * step];
        }
        
        OnAudioData?.Invoke(_visualData, state);
    }
}

// Usage in UI
public class AudioVisualizer : MonoBehaviour
{
    private PipelineVisualizer _visualizer;
    
    void Start()
    {
        _visualizer = new PipelineVisualizer();
        _visualizer.OnAudioData += DrawWaveform;
        EasyMicAPI.AddProcessor(recordingHandle, _visualizer);
    }
    
    void DrawWaveform(float[] data, AudioState state)
    {
        // Draw waveform in Unity UI or using Debug.Log
        float peak = data.Max(x => Math.Abs(x));
        Debug.Log($"Waveform peak: {peak:F3}");
    }
}
```

### Recording Session Inspector
```csharp
[System.Serializable]
public class RecordingSessionInspector
{
    public static void InspectSession(RecordingHandle handle)
    {
        var info = EasyMicAPI.GetRecordingInfo(handle);
        
        Debug.Log("=== Recording Session Info ===");
        Debug.Log($"Handle Valid: {handle.IsValid}");
        Debug.Log($"Device: {info.Device.Name}");
        Debug.Log($"Sample Rate: {info.SampleRate}");
        Debug.Log($"Channels: {info.Channel}");
        Debug.Log($"Active: {info.IsActive}");
        Debug.Log($"Processors: {info.ProcessorCount}");
        Debug.Log("=============================");
    }
}
```

## 🆘 Getting Help

### Before Reporting Issues
1. **Check Unity Console** for error messages
2. **Test with minimal setup** (just AudioCapturer)
3. **Verify on multiple devices** if possible
4. **Check Unity version compatibility**

### When Reporting Bugs
Include this information:
- Unity version
- Target platform
- Easy Mic version
- Minimal reproduction code
- Console output/error messages
- Device specifications

### Useful Debug Code
```csharp
public static class EasyMicDebugInfo
{
    public static void PrintSystemInfo()
    {
        Debug.Log("=== Easy Mic Debug Info ===");
        Debug.Log($"Unity Version: {Application.unityVersion}");
        Debug.Log($"Platform: {Application.platform}");
        Debug.Log($"Device Model: {SystemInfo.deviceModel}");
        Debug.Log($"OS: {SystemInfo.operatingSystem}");
        
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        Debug.Log($"Audio Devices: {devices.Length}");
        
        foreach (var device in devices)
        {
            Debug.Log($"  - {device.Name} (Default: {device.IsDefault})");
            Debug.Log($"    Channels: {device.MaxChannels}");
            Debug.Log($"    Sample Rate: {device.MinSampleRate}-{device.MaxSampleRate}Hz");
        }
        
        Debug.Log("=========================");
    }
}
```

---

## 🔍 What's Next?

- **[Examples](examples.md)** - See working implementations
- **[API Reference](api-reference.md)** - Complete API documentation
- **[Best Practices](best-practices.md)** - Optimization techniques

---

← [Best Practices](best-practices.md) | **Next: [Examples](examples.md)** →