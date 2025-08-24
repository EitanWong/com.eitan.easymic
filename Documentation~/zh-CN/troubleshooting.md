← [最佳实践](best-practices.md) | [文档首页](../README.md) | [English Version](../en/troubleshooting.md) →

# 🔧 故障排除

Easy Mic 的常见问题、解决方案和调试技术。

## 🚨 安装问题

### 找不到包
**问题：** Unity Package Manager 无法找到 Easy Mic 包。

**解决方案：**
1. **验证 Git URL：**
   ```
   https://github.com/EitanWong/com.eitan.easymic.git#upm
   ```

2. **检查 Unity 版本：**
   - 需要 Unity 2021.3 LTS 或更高版本
   - 如果使用较旧版本请更新 Unity

3. **网络问题：**
   ```bash
   # 测试 Git 访问
   git ls-remote https://github.com/EitanWong/com.eitan.easymic.git
   ```

4. **使用 OpenUPM（替代方案）：**
   ```bash
   openupm add com.eitan.easymic
   ```

### 编译错误
**问题：** 安装后 C# 编译错误。

**常见错误和解决方案：**

```csharp
// 错误："Span<T> 不可用"
// 解决方案：确保 .NET Standard 2.1 或更高版本
// Player Settings > Configuration > Api Compatibility Level
```

```csharp
// 错误："不允许不安全代码"  
// 解决方案：在 Player Settings 中启用不安全代码
// Player Settings > Allow 'unsafe' Code ✓
```

**程序集定义问题：**
1. 验证脚本中的程序集引用
2. 将 `Eitan.EasyMic` 添加到程序集依赖项

---

## 🎤 设备和权限问题

### 找不到麦克风设备
**问题：** `EasyMicAPI.Devices` 返回空数组。

**诊断步骤：**
```csharp
public void DiagnoseDeviceIssues()
{
    // 1. 首先检查权限
    if (!PermissionUtils.HasPermission())
    {
        Debug.LogError("❌ 未授予麦克风权限");
        return;
    }
    
    // 2. 刷新设备列表
    EasyMicAPI.Refresh();
    var devices = EasyMicAPI.Devices;
    
    if (devices.Length == 0)
    {
        Debug.LogError("❌ 刷新后未找到设备");
        Debug.Log("检查系统音频设置并确保麦克风已连接");
    }
    else
    {
        Debug.Log($"✅ 找到 {devices.Length} 个设备：");
        foreach (var device in devices)
        {
            Debug.Log($"  - {device.Name} (默认：{device.IsDefault})");
        }
    }
}
```

**平台特定解决方案：**

#### Windows
- 检查隐私设置：`设置 > 隐私 > 麦克风`
- 验证麦克风在设备管理器中未被禁用
- 在 Windows 录音机中测试麦克风

#### macOS
- 检查系统偏好设置：`安全性与隐私 > 隐私 > 麦克风`
- 将 Unity/你的应用添加到允许的应用程序
- 重置麦克风权限：`tccutil reset Microphone`

#### Linux
- 检查 ALSA/PulseAudio 配置
- 验证用户权限：`sudo usermod -a -G audio $USER`
- 测试：`arecord -l` 列出捕获设备

#### Android
- 在 `AndroidManifest.xml` 中添加权限：
  ```xml
  <uses-permission android:name="android.permission.RECORD_AUDIO" />
  ```
- 在物理设备上测试（模拟器麦克风可能不工作）

#### iOS
- 仅在物理设备上测试
- 检查 iOS 设置：`设置 > 隐私 > 麦克风`

### 权限被拒绝错误
**问题：** 录音因权限错误失败。

**解决方案模式：**
```csharp
public class SafeRecordingStarter : MonoBehaviour
{
    public void StartRecordingWithPermissionCheck()
    {
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogError("❌ 未授予麦克风权限");
            return;
        }
        StartRecording();
    }
    
    // 提示：如未授权，请引导用户在系统设置中开启权限
    
    private void StartRecording()
    {
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        
        if (devices.Length > 0)
        {
            var handle = EasyMicAPI.StartRecording(devices[0].Name);
            if (handle.IsValid)
            {
                Debug.Log("🎙️ 录音成功开始");
            }
            else
            {
                Debug.LogError("❌ 录音开始失败");
            }
        }
    }
}
```

---

## 🎧 音频质量问题

### 未捕获音频
**问题：** 录音似乎工作但未捕获音频。

**调试步骤：**
```csharp
public class AudioDebugger : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioCapturer _capturer;
    private VolumeMonitor _monitor;
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording();
        
        // 添加音量监控器检查音频是否流动
        _monitor = new VolumeMonitor();
        EasyMicAPI.AddProcessor(_handle, _monitor);
        
        // 添加捕获器
        _capturer = new AudioCapturer(5);
        EasyMicAPI.AddProcessor(_handle, _capturer);
        
        // 检查音量等级
        InvokeRepeating(nameof(CheckAudioLevels), 1f, 1f);
    }
    
    void CheckAudioLevels()
    {
        float volume = _monitor.GetCurrentVolume();
        Debug.Log($"当前音量：{volume:F4} ({20 * Mathf.Log10(volume + 1e-10f):F1} dB)");
        
        if (volume < 0.001f)
        {
            Debug.LogWarning("⚠️ 检测到非常低的音频等级 - 检查：");
            Debug.LogWarning("  - 麦克风未静音");
            Debug.LogWarning("  - 系统麦克风等级");
            Debug.LogWarning("  - 麦克风在其他应用中工作");
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

### 音频丢失/故障
**问题：** 音频有间隙、爆音或故障。

**常见原因和解决方案：**

#### 1. 缓冲区不足
```csharp
// ❌ 缓冲区太小
var capturer = new AudioCapturer(1); // 只有1秒

// ✅ 足够的缓冲区大小
var capturer = new AudioCapturer(10); // 10秒余量
```

#### 2. 处理过重
```csharp
// ❌ 音频线程上的重处理
public class HeavyProcessor : AudioWriter
{
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        foreach (var sample in buffer)
        {
            // 复杂的数学运算 - 导致丢失！
            var result = Math.Sin(Math.Cos(Math.Tan(sample * Math.PI)));
        }
    }
}

// ✅ 轻量级处理
public class EfficientProcessor : AudioWriter
{
    private float _precomputedGain;
    
    public override void Initialize(AudioState state)
    {
        base.Initialize(state);
        _precomputedGain = 1.5f; // 预计算
    }
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= _precomputedGain; // 简单乘法
    }
}
```

#### 3. 线程问题
```csharp
// ❌ 不安全的属性访问
public class UnsafeProcessor : AudioWriter
{
    public float Gain { get; set; } = 1.0f; // 从UI线程修改！
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        // 竞态条件 - Gain可能在处理中途改变！
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain;
    }
}

// ✅ 线程安全的属性访问
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

### 低音频质量
**问题：** 音频听起来沉闷、失真或低质量。

**解决方案：**

#### 1. 使用更高采样率
```csharp
// ❌ 低质量
var handle = EasyMicAPI.StartRecording("Microphone", SampleRate.Hz8000);

// ✅ 高质量  
var handle = EasyMicAPI.StartRecording("Microphone", SampleRate.Hz48000);
```

#### 2. 检查处理器顺序
```csharp
// ❌ 处理顺序差
var bpc = new AudioWorkerBlueprint(() => new AudioCapturer(5),  key: "capture");
var bpg = new AudioWorkerBlueprint(() => new VolumeGateFilter(), key: "gate");
var bpd = new AudioWorkerBlueprint(() => new AudioDownmixer(),   key: "downmix");
EasyMicAPI.AddProcessor(handle, bpc);      // 早期捕获
EasyMicAPI.AddProcessor(handle, bpg);      // 捕获后门控
EasyMicAPI.AddProcessor(handle, bpd);      // 太晚

// ✅ 最佳处理顺序
EasyMicAPI.AddProcessor(handle, bpg);      // 先去除噪音
EasyMicAPI.AddProcessor(handle, bpd);      // 转换格式
EasyMicAPI.AddProcessor(handle, bpc);      // 捕获干净音频
```

#### 3. 正确的声道处理
```csharp
// 检查设备能力
var device = EasyMicAPI.Devices[0];
Debug.Log($"设备支持 {device.MaxChannels} 声道");

// 使用适当的声道数
var channelMode = device.MaxChannels > 1 ? Channel.Stereo : Channel.Mono;
var handle = EasyMicAPI.StartRecording(device.Name, SampleRate.Hz48000, channelMode);
```

---

## 🏗️ 流水线问题

### 处理器不工作
**问题：** 添加到流水线的处理器但对音频无效果。

**调试步骤：**
```csharp
public class PipelineDebugger : MonoBehaviour
{
    private RecordingHandle _handle;
    
    void Start()
    {
        _handle = EasyMicAPI.StartRecording();
        
        // 添加测试处理器验证流水线工作
        var testProcessor = new PipelineTestProcessor();
        EasyMicAPI.AddProcessor(_handle, testProcessor);
        
        // 添加实际处理器
        var gate = new VolumeGateFilter();
        EasyMicAPI.AddProcessor(_handle, gate);
        
        // 监控流水线
        InvokeRepeating(nameof(CheckPipelineStatus), 1f, 1f);
    }
    
    void CheckPipelineStatus()
    {
        var info = EasyMicAPI.GetRecordingInfo(_handle);
        Debug.Log($"录音活动：{info.IsActive}，处理器：{info.ProcessorCount}");
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
            Debug.Log($"✅ 流水线活动 - 已处理 {_frameCount} 帧");
        }
    }
}
```

### 处理器异常
**问题：** 处理器中抛出的异常导致录音崩溃。

**安全处理器模板：**
```csharp
public class SafeProcessor : AudioWriter
{
    private bool _hasError = false;
    private int _errorCount = 0;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        if (_hasError) return; // 如果处于错误状态则跳过处理
        
        try
        {
            ProcessAudioSafely(buffer, state);
        }
        catch (Exception ex)
        {
            _errorCount++;
            Debug.LogError($"处理器错误 ({_errorCount})：{ex.Message}");
            
            if (_errorCount > 5)
            {
                Debug.LogError("错误过多，禁用处理器");
                _hasError = true;
            }
        }
    }
    
    private void ProcessAudioSafely(Span<float> buffer, AudioState state)
    {
        // 你的处理代码
    }
}
```

---

## 🛠️ 性能问题

### 高CPU使用率
**问题：** Easy Mic 使用过多CPU。

**性能分析和优化：**
```csharp
public class PerformanceProfiler : AudioWriter
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = new System.Diagnostics.Stopwatch();
    private long _totalTime = 0;
    private int _frameCount = 0;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        _stopwatch.Restart();
        
        // 你的处理代码
        ProcessAudio(buffer, state);
        
        _stopwatch.Stop();
        _totalTime += _stopwatch.ElapsedTicks;
        _frameCount++;
        
        // 每1000帧报告一次
        if (_frameCount % 1000 == 0)
        {
            double avgMs = (_totalTime / (double)_frameCount) / TimeSpan.TicksPerMillisecond;
            Debug.Log($"平均处理时间：{avgMs:F3}ms 每帧");
            
            if (avgMs > 1.0) // 超过1ms令人担忧
            {
                Debug.LogWarning("⚠️ 检测到高处理时间！");
            }
        }
    }
}
```

**常见优化：**
1. **在 Initialize() 中预计算值**
2. **尽可能使用定点数学而非浮点数**
3. **最小化内存分配**
4. **使用高效算法（O(n) vs O(n²)）**

### 内存泄漏
**问题：** 内存使用随时间增长。

**泄漏检测：**
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
        Debug.Log($"内存使用：{memory / 1024 / 1024}MB");
        
        // 强制GC检查泄漏
        System.GC.Collect();
        long afterGC = System.GC.GetTotalMemory(true);
        Debug.Log($"GC后：{afterGC / 1024 / 1024}MB");
    }
}
```

**常见泄漏源：**
1. **未释放处理器**
2. **未移除事件处理器**
3. **对处理器实例的静态引用**
4. **未清除的大缓冲区**

**正确清理：**
```csharp
public class ProperCleanup : MonoBehaviour
{
    private RecordingHandle _handle;
    private List<IAudioWorker> _processors = new List<IAudioWorker>();
    
    void OnDestroy()
    {
        // 先停止录音
        if (_handle.IsValid)
            EasyMicAPI.StopRecording(_handle);
        
        // 释放所有处理器
        foreach (var processor in _processors)
            processor?.Dispose();
        
        _processors.Clear();
        
        // 最终清理
        EasyMicAPI.Cleanup();
    }
}
```

---

## 🔍 调试工具

### 音频流水线可视化器
```csharp
public class PipelineVisualizer : AudioReader
{
    public event System.Action<float[], AudioState> OnAudioData;
    private float[] _visualData = new float[1024];
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        // 降采样用于可视化
        int step = Math.Max(1, buffer.Length / _visualData.Length);
        
        for (int i = 0; i < _visualData.Length && i * step < buffer.Length; i++)
        {
            _visualData[i] = buffer[i * step];
        }
        
        OnAudioData?.Invoke(_visualData, state);
    }
}

// 在UI中使用
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
        // 在Unity UI中绘制波形或使用Debug.Log
        float peak = data.Max(x => Math.Abs(x));
        Debug.Log($"波形峰值：{peak:F3}");
    }
}
```

### 录音会话检查器
```csharp
[System.Serializable]
public class RecordingSessionInspector
{
    public static void InspectSession(RecordingHandle handle)
    {
        var info = EasyMicAPI.GetRecordingInfo(handle);
        
        Debug.Log("=== 录音会话信息 ===");
        Debug.Log($"句柄有效：{handle.IsValid}");
        Debug.Log($"设备：{info.Device.Name}");
        Debug.Log($"采样率：{info.SampleRate}");
        Debug.Log($"声道：{info.Channel}");
        Debug.Log($"活动：{info.IsActive}");
        Debug.Log($"处理器：{info.ProcessorCount}");
        Debug.Log("===========================");
    }
}
```

## 🆘 获取帮助

### 报告问题前
1. **检查Unity控制台**的错误消息
2. **用最小设置测试**（仅AudioCapturer）
3. **如可能在多个设备上验证**
4. **检查Unity版本兼容性**

### 报告错误时
包含此信息：
- Unity版本
- 目标平台
- Easy Mic版本
- 最小重现代码
- 控制台输出/错误消息
- 设备规格

### 有用的调试代码
```csharp
public static class EasyMicDebugInfo
{
    public static void PrintSystemInfo()
    {
        Debug.Log("=== Easy Mic 调试信息 ===");
        Debug.Log($"Unity版本：{Application.unityVersion}");
        Debug.Log($"平台：{Application.platform}");
        Debug.Log($"设备型号：{SystemInfo.deviceModel}");
        Debug.Log($"操作系统：{SystemInfo.operatingSystem}");
        
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        Debug.Log($"音频设备：{devices.Length}");
        
        foreach (var device in devices)
        {
            Debug.Log($"  - {device.Name} (默认：{device.IsDefault})");
            Debug.Log($"    声道：{device.MaxChannels}");
            Debug.Log($"    采样率：{device.MinSampleRate}-{device.MaxSampleRate}Hz");
        }
        
        Debug.Log("=========================");
    }
}
```

---

## 🔍 下一步

- **[示例](examples.md)** - 查看工作实现
- **[API参考](api-reference.md)** - 完整的API文档
- **[最佳实践](best-practices.md)** - 优化技术

---

← [最佳实践](best-practices.md) | **下一步：[示例](examples.md)** →
