← [内置处理器](processors.md) | [Mono 组件](components.md) | [文档首页](../README.md) | [English Version](../en/api-reference.md) →

# ⚡ API 参考

Easy Mic 公共 API 的完整参考文档。

## 🎪 EasyMicAPI

提供对所有 Easy Mic 功能简单访问的主要门面。

### 静态属性

#### `Devices`

```csharp
public static MicDevice[] Devices { get; }
```

获取可用麦克风设备列表。首先调用 `Refresh()` 更新列表。

**返回值：** 表示可用麦克风的 `MicDevice` 结构体数组。

**示例：**

```csharp
EasyMicAPI.Refresh();
var devices = EasyMicAPI.Devices;
foreach (var device in devices)
{
    Debug.Log($"设备：{device.Name}，默认：{device.IsDefault}");
}
```

#### `IsRecording`

```csharp
public static bool IsRecording { get; }
```

获取当前是否有录音会话处于活动状态。

**返回值：** 如果有录音处于活动状态则为 `true`，否则为 `false`。

### 静态方法

#### `Refresh()`

```csharp
public static void Refresh()
```

刷新可用麦克风设备列表。访问 `Devices` 前调用此方法。

**权限：** 需要麦克风权限。

**示例：**

```csharp
EasyMicAPI.Refresh();
Debug.Log($"找到 {EasyMicAPI.Devices.Length} 个麦克风设备");
```

---

#### `StartRecording()` - 默认设备

```csharp
public static RecordingHandle StartRecording(SampleRate sampleRate = SampleRate.Hz16000)
```

使用默认麦克风设备开始录音。

**参数：**

- `sampleRate` - 音频采样率（默认：16kHz）

**返回值：** 用于管理录音会话的 `RecordingHandle`。

**示例：**

```csharp
var handle = EasyMicAPI.StartRecording(SampleRate.Hz48000);
if (handle.IsValid)
{
    Debug.Log("使用默认设备开始录音");
}
```

---

#### `StartRecording()` - 按名称

```csharp
public static RecordingHandle StartRecording(string deviceName,
    SampleRate sampleRate = SampleRate.Hz16000,
    Channel channel = Channel.Mono)
```

使用按名称指定的麦克风设备开始录音。

**参数：**

- `deviceName` - 麦克风设备名称
- `sampleRate` - 音频采样率（默认：16kHz）
- `channel` - 声道配置（默认：单声道）

**返回值：** 用于管理录音会话的 `RecordingHandle`。

**示例：**

```csharp
var handle = EasyMicAPI.StartRecording(
    "内置麦克风",
    SampleRate.Hz44100,
    Channel.Stereo
);
```

---

#### `StartRecording()` - 按设备

```csharp
public static RecordingHandle StartRecording(MicDevice device,
    SampleRate sampleRate = SampleRate.Hz16000,
    Channel channel = Channel.Mono)
```

使用指定的麦克风设备开始录音。

**参数：**

- `device` - 要使用的麦克风设备
- `sampleRate` - 音频采样率（默认：16kHz）
- `channel` - 声道配置（默认：单声道）

**返回值：** 用于管理录音会话的 `RecordingHandle`。

**示例：**

```csharp
var devices = EasyMicAPI.Devices;
var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);
var handle = EasyMicAPI.StartRecording(defaultDevice, SampleRate.Hz48000);
```

---

#### `StartRecording()` - 携带“处理器蓝图”

```csharp
public static RecordingHandle StartRecording(
    SampleRate sampleRate,
    IEnumerable<AudioWorkerBlueprint> workers)

public static RecordingHandle StartRecording(
    string deviceName, SampleRate sampleRate, Channel channel,
    IEnumerable<AudioWorkerBlueprint> workers)

public static RecordingHandle StartRecording(
    MicDevice device, SampleRate sampleRate, Channel channel,
    IEnumerable<AudioWorkerBlueprint> workers)
```

传入蓝图集合以构建流水线。每个会话都会从蓝图“新建”自己的处理器实例。

---

#### `StopRecording()`

```csharp
public static void StopRecording(RecordingHandle handle)
```

停止指定的录音会话。

**参数：**

- `handle` - 要停止的录音句柄

**示例：**

```csharp
EasyMicAPI.StopRecording(recordingHandle);
```

---

#### `StopAllRecordings()`

```csharp
public static void StopAllRecordings()
```

停止所有活动的录音会话。

**示例：**

```csharp
EasyMicAPI.StopAllRecordings();
```

---

#### `DefaultWorkers`

```csharp
public static List<AudioWorkerBlueprint> DefaultWorkers { get; set; }
```

可配置的全局默认蓝图集合。对于未显式传入 `workers` 的 StartRecording 重载会自动使用它。

---

#### `AddProcessor()`

```csharp
public static void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
```

按蓝图在运行时新增处理器（同 key 已存在则忽略）。

**参数：**

- `handle` - 录音句柄
- `blueprint` - 处理器蓝图（包含工厂与稳定 key）

**示例：**

```csharp
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture");
EasyMicAPI.AddProcessor(recordingHandle, bpCapture);
```

---

#### `RemoveProcessor()`

```csharp
public static void RemoveProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
```

按蓝图 key 从流水线中移除并释放处理器。

**参数：**

- `handle` - 录音句柄
- `blueprint` - 处理器蓝图（根据 key 匹配）

**示例：**

```csharp
EasyMicAPI.RemoveProcessor(recordingHandle, bpCapture);
```

---

#### `GetProcessor<T>()`

```csharp
public static T GetProcessor<T>(RecordingHandle handle, AudioWorkerBlueprint blueprint)
    where T : class, IAudioWorker
```

按蓝图查询该会话内绑定的具体处理器实例。未找到返回 `null`。

---

#### `GetRecordingInfo()`

```csharp
public static RecordingInfo GetRecordingInfo(RecordingHandle handle)
```

获取录音会话的信息。

**参数：**

- `handle` - 录音句柄

**返回值：** 包含会话详情的 `RecordingInfo`。

**示例：**

```csharp
var info = EasyMicAPI.GetRecordingInfo(recordingHandle);
Debug.Log($"录音：{info.SampleRate}Hz，{info.ChannelCount} 声道，{info.ProcessorCount} 处理器");
```

---

#### `Cleanup()`

```csharp
public static void Cleanup()
```

清理所有 Easy Mic 资源。关闭应用程序时调用此方法。

**示例：**

```csharp
void OnApplicationQuit()
{
    EasyMicAPI.Cleanup();
}
```

---

## 🎫 RecordingHandle

录音会话的轻量级标识符。

### 结构体

```csharp
public struct RecordingHandle
{
    public int Id { get; }
    public MicDevice Device { get; }
    public bool IsValid => Id > 0;
}
```

### 属性

#### `Id`

```csharp
public int Id { get; }
```

录音会话的唯一标识符。

#### `Device`

```csharp
public MicDevice Device { get; }
```

与此录音关联的麦克风设备。

#### `IsValid`

```csharp
public bool IsValid { get; }
```

此句柄是否表示有效的录音会话。

**示例：**

```csharp
if (recordingHandle.IsValid)
{
    var info = EasyMicAPI.GetRecordingInfo(recordingHandle);
    Debug.Log($"录音 {recordingHandle.Id} 使用 {info.Device.Name}");
}
```

---

## 🎤 MicDevice

表示麦克风设备。

### 结构体

```csharp
public struct MicDevice
{
    public string Name { get; }
    public string Id { get; }
    public bool IsDefault { get; }
    public int MaxChannels { get; }
    public int MinSampleRate { get; }
    public int MaxSampleRate { get; }
}
```

### 属性

#### `Name`

```csharp
public string Name { get; }
```

麦克风设备的人类可读名称。

#### `Id`

```csharp
public string Id { get; }
```

设备的系统标识符。

#### `IsDefault`

```csharp
public bool IsDefault { get; }
```

这是否是系统的默认麦克风。

#### `MaxChannels`

```csharp
public int MaxChannels { get; }
```

设备支持的最大声道数。

#### `MinSampleRate` / `MaxSampleRate`

```csharp
public int MinSampleRate { get; }
public int MaxSampleRate { get; }
```

设备支持的采样率范围。

### 扩展方法

#### `GetDeviceChannel()`

```csharp
public static Channel GetDeviceChannel(this MicDevice device)
```

获取设备推荐的声道配置。

**返回值：** 单声道设备返回 `Channel.Mono`，多声道设备返回 `Channel.Stereo`。

**示例：**

```csharp
var device = EasyMicAPI.Devices[0];
var recommendedChannels = device.GetDeviceChannel();
Debug.Log($"{device.Name} 推荐声道：{recommendedChannels}");
```

---

## 📊 AudioContext

携带当前音频格式信息。

### 类

```csharp
public class AudioContext
{
    public int ChannelCount { get; set; }
    public int SampleRate { get; set; }
    public int Length { get; set; }
}
```

### 属性

#### `ChannelCount`

```csharp
public int ChannelCount { get; set; }
```

音频声道数（1 = 单声道，2 = 立体声）。

#### `SampleRate`

```csharp
public int SampleRate { get; set; }
```

采样率（Hz）（例如 44100、48000）。

#### `Length`

```csharp
public int Length { get; set; }
```

当前缓冲区长度（样本数）。

### 构造函数

```csharp
public AudioContext(int channelCount, int sampleRate, int length)
```

**示例：**

```csharp
var state = new AudioContext(2, 48000, 1024);
Debug.Log($"音频格式：{state.SampleRate}Hz，{state.ChannelCount} 声道");
```

---

## 🎛️ IAudioWorker

所有音频处理器的基础接口。

### 接口

```csharp
public interface IAudioWorker : IDisposable
{
    void Initialize(AudioContext state);
    void OnAudioPass(Span<float> buffer, AudioContext state);
}
```

### 方法

#### `Initialize()`

```csharp
void Initialize(AudioContext state)
```

当处理器添加到活动录音或录音开始时调用。

**参数：**

- `state` - 当前音频格式信息

#### `OnAudioPass()`

```csharp
void OnAudioPass(Span<float> buffer, AudioContext state)
```

录音期间每个音频缓冲区调用。

**参数：**

- `buffer` - 音频数据缓冲区
- `state` - 当前音频格式信息

---

## 📖 AudioReader

只读音频处理器的抽象基类。

### 类

```csharp
public abstract class AudioReader : AudioWorkerBase
{
    protected abstract void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state);
}
```

### 方法

#### `OnAudioRead()`

```csharp
protected abstract void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
```

重写此方法以分析音频而不修改它。

**参数：**

- `buffer` - 只读音频数据缓冲区
- `state` - 当前音频格式信息

**示例实现：**

```csharp
public class VolumeMonitor : AudioReader
{
    private float _currentVolume;

    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioContext state)
    {
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
        _currentVolume = MathF.Sqrt(sum / buffer.Length);
    }

    public float GetCurrentVolume() => _currentVolume;
}
```

---

## ✏️ AudioWriter

修改音频的音频处理器的抽象基类。

### 类

```csharp
public abstract class AudioWriter : AudioWorkerBase
{
    protected abstract void OnAudioWrite(Span<float> buffer, AudioContext state);
}
```

### 方法

#### `OnAudioWrite()`

```csharp
protected abstract void OnAudioWrite(Span<float> buffer, AudioContext state)
```

重写此方法以处理和修改音频数据。

**参数：**

- `buffer` - 可变音频数据缓冲区
- `state` - 当前音频格式信息

**示例实现：**

```csharp
public class GainProcessor : AudioWriter
{
    public float Gain { get; set; } = 1.0f;

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain;
    }
}
```

---

## 📦 枚举

### `SampleRate`

```csharp
public enum SampleRate
{
    Hz8000 = 8000,
    Hz16000 = 16000,
    Hz22050 = 22050,
    Hz44100 = 44100,
    Hz48000 = 48000,
    Hz96000 = 96000
}
```

音频录制的常用采样率。

### `Channel`

```csharp
public enum Channel
{
    Mono = 1,
    Stereo = 2
}
```

音频声道配置。

---

## 🔄 AudioBuffer

SPSC 场景的高性能无锁循环缓冲区。

### 类

```csharp
public class AudioBuffer
{
    public int Capacity { get; }
    public int ReadableCount { get; }
    public int WritableCount { get; }
}
```

### 构造函数

```csharp
public AudioBuffer(int capacity)
```

创建具有指定容量的新音频缓冲区。

**参数：**

- `capacity` - 缓冲区可容纳的最大样本数

### 方法

#### `Write()`

```csharp
public int Write(ReadOnlySpan<float> data)
```

向缓冲区写入音频数据（生产者线程）。

**参数：**

- `data` - 要写入的音频数据

**返回值：** 实际写入的样本数。

#### `Read()`

```csharp
public int Read(Span<float> destination)
```

从缓冲区读取音频数据（消费者线程）。

**参数：**

- `destination` - 接收音频数据的缓冲区

**返回值：** 实际读取的样本数。

#### `Clear()`

```csharp
public void Clear()
```

清除缓冲区中的所有数据。

**示例：**

```csharp
var buffer = new AudioBuffer(48000); // 48kHz 下1秒

// 生产者线程
float[] inputData = GetAudioFromMicrophone();
int written = buffer.Write(inputData);

// 消费者线程
float[] outputData = new float[1024];
int read = buffer.Read(outputData);
```

---

## 📋 RecordingInfo

录音会话的信息。

### 结构体

```csharp
public struct RecordingInfo
{
    public MicDevice Device { get; }
    public SampleRate SampleRate { get; }
    public Channel Channel { get; }
    public bool IsActive { get; }
    public int ProcessorCount { get; }
}
```

### 属性

#### `Device`

```csharp
public MicDevice Device { get; }
```

正在使用的麦克风设备。

#### `SampleRate`

```csharp
public SampleRate SampleRate { get; }
```

录音的当前采样率。

#### `Channel`

```csharp
public Channel Channel { get; }
```

当前声道配置。

#### `IsActive`

```csharp
public bool IsActive { get; }
```

录音当前是否处于活动状态。

#### `ProcessorCount`

```csharp
public int ProcessorCount { get; }
```

流水线中处理器的数量。

---

## 🛠️ 实用工具类

### `PermissionUtils`

```csharp
public static class PermissionUtils
{
    public static bool HasPermission()
}
```

平台特定的麦克风权限处理。桌面/编辑器平台 `HasPermission()` 直接返回 `true`。在 Android 上，`HasPermission()` 可能会内部触发系统权限申请，并在授予前返回 `false`。

### `MicDeviceUtils`

```csharp
public static class MicDeviceUtils
{
    public static MicDevice GetDefaultDevice()
    public static MicDevice FindDeviceByName(string name)
}
```

处理麦克风设备的实用方法。

### `AudioExtension`

```csharp
public static class AudioExtension
{
    public static float[] ConvertToMono(float[] stereoData)
    public static AudioClip CreateAudioClip(float[] data, int sampleRate, int channels, string name)
}
```

音频处理实用方法。

---

## 🔍 下一步

探索实际用法：

- **[最佳实践](best-practices.md)** - 优化技术和模式
- **[示例](examples.md)** - 真实世界的使用示例
- **[故障排除](troubleshooting.md)** - 常见问题和解决方案

---

← [内置处理器](processors.md) | **下一步：[最佳实践](best-practices.md)** →
