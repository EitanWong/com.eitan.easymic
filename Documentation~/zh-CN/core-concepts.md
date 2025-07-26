← [入门指南](getting-started.md) | [文档首页](../README.md) | [English Version](../en/core-concepts.md) →

# 🏗️ 核心概念

理解这些基本概念将帮助您充分利用 Easy Mic 强大而灵活的架构。

## 🎯 设计理念

Easy Mic 围绕三个核心原则构建：

1. **🚀 性能至上**：超低延迟音频处理，零GC操作
2. **🔧 模块化设计**：可组合的音频处理器，可以混合和匹配
3. **🛡️ 类型安全**：编译时保证防止常见的音频处理错误

## 🏛️ 架构概览

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   EasyMicAPI    │───▶│    MicSystem     │───▶│ RecordingSession│
│   (Facade)      │    │   (Manager)      │    │   (Instance)    │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                                         │
                                                         ▼
                                               ┌─────────────────┐
                                               │ AudioPipeline   │
                                               │ ┌─────────────┐ │
                                               │ │ Processor A │ │
                                               │ ├─────────────┤ │
                                               │ │ Processor B │ │
                                               │ ├─────────────┤ │
                                               │ │ Processor C │ │
                                               │ └─────────────┘ │
                                               └─────────────────┘
```

## 🎭 关键组件

### 🎪 EasyMicAPI（门面模式）
所有 EasyMic 操作的主要入口点。提供简单、线程安全的接口：

```csharp
// 获取可用设备
EasyMicAPI.Refresh();
var devices = EasyMicAPI.Devices;

// 开始录音
var handle = EasyMicAPI.StartRecording(devices[0].Name);

// 添加处理器
EasyMicAPI.AddProcessor(handle, new AudioCapturer(10));

// 停止录音
EasyMicAPI.StopRecording(handle);
```

### 🎛️ MicSystem（管理器）
管理多个并发录音会话并处理原生音频后端操作：

- **设备管理**：枚举和管理麦克风设备
- **会话生命周期**：创建和销毁录音会话
- **资源管理**：处理原生内存和线程安全
- **权限处理**：跨平台管理麦克风权限

### 🎫 RecordingHandle（标识符）
录音会话的轻量级、类型安全标识符：

```csharp
public struct RecordingHandle
{
    public int Id { get; }
    public MicDevice Device { get; }
    public bool IsValid => Id > 0;
}
```

**优点：**
- ✅ **类型安全**：无法意外混淆录音会话
- ✅ **资源安全**：自动检测无效句柄
- ✅ **线程安全**：可以安全地在线程间传递

### 🔗 AudioPipeline（责任链）
EasyMic 处理系统的核心。管理音频处理器的有序链：

```csharp
// 管道按顺序处理音频：
// 原始麦克风 → 音量门 → 降混器 → 捕获器 → 输出
var handle = EasyMicAPI.StartRecording("Microphone");
EasyMicAPI.AddProcessor(handle, new VolumeGateFilter());
EasyMicAPI.AddProcessor(handle, new AudioDownmixer());
EasyMicAPI.AddProcessor(handle, new AudioCapturer(5));
```

**关键特性：**
- 🔄 **动态修改**：录音期间添加/移除处理器
- 🧵 **线程安全**：所有操作都是线程安全的
- 🎯 **顺序重要**：处理器按添加顺序执行
- 🧠 **内存高效**：音频处理期间零内存分配

## 🎨 处理器类型

Easy Mic 使用类型安全设计来防止常见的音频处理错误：

### 📖 AudioReader（只读）
用于分析和监控而不修改音频：

```csharp
public abstract class AudioReader : AudioWorkerBase
{
    // 密封 - 无法修改缓冲区
    public sealed override void OnAudioPass(Span<float> buffer, AudioState state)
    {
        OnAudioRead(buffer, state); // ReadOnlySpan<float> - 编译时安全！
    }
    
    protected abstract void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state);
}
```

**示例：** 音量表、静音检测、音频分析

### ✏️ AudioWriter（读写）
用于修改音频的处理：

```csharp
public abstract class AudioWriter : AudioWorkerBase
{
    public sealed override void OnAudioPass(Span<float> buffer, AudioState state)
    {
        OnAudioWrite(buffer, state); // Span<float> - 可以修改！
    }
    
    protected abstract void OnAudioWrite(Span<float> buffer, AudioState state);
}
```

**示例：** 滤波器、效果、格式转换、噪音门

### 🛠️ IAudioWorker（接口）
所有处理器实现的基础接口：

```csharp
public interface IAudioWorker : IDisposable
{
    void Initialize(AudioState state);
    void OnAudioPass(Span<float> buffer, AudioState state);
}
```

## 📊 AudioState（上下文）
携带当前音频格式信息：

```csharp
public class AudioState
{
    public int ChannelCount { get; set; }  // 1 = 单声道, 2 = 立体声
    public int SampleRate { get; set; }    // 例如 44100, 48000
    public int Length { get; set; }        // 当前缓冲区长度
}
```

**用途：**
- 处理器使用它来适应不同的音频格式
- 自动传递给管道中的每个处理器
- 可以在录音期间改变（例如格式切换）

## 🔄 AudioBuffer（无锁循环缓冲区）
为单生产者单消费者（SPSC）场景设计的高性能、无锁缓冲区：

```csharp
var buffer = new AudioBuffer(48000); // 48kHz 下1秒

// 生产者线程（音频回调）
buffer.Write(audioData);

// 消费者线程（您的代码）
buffer.Read(outputArray);
```

**关键特性：**
- 🚀 **零锁性能**：无互斥锁开销
- 🗑️ **零GC操作**：读写期间无内存分配
- 🔄 **循环设计**：高效内存使用
- ⚡ **内存屏障**：使用 `Volatile.Read/Write` 进行正确的线程处理

**⚠️ 重要**：仅在SPSC场景下安全！

## 🎪 处理流程

以下是音频在系统中的流动方式：

```
1. 麦克风硬件
   ↓ (原生音频线程)
2. 原生音频后端 (libsoundio)
   ↓ (音频回调)
3. MicSystem.RecordingSession
   ↓ (AudioPipeline.OnAudioPass)
4. 处理器 1 (例如 VolumeGate)
   ↓ (修改后的缓冲区)
5. 处理器 2 (例如 Downmixer)
   ↓ (修改后的缓冲区)
6. 处理器 3 (例如 AudioCapturer)
   ↓ (捕获到缓冲区)
7. 您的应用程序代码
```

**要点：**
- 音频处理在**专用音频线程**上进行
- 每个处理器获得**前一个处理器的输出**
- 管道是**无锁的**以获得最大性能
- 一个处理器中的错误**不会导致整个管道崩溃**

## 🛡️ 线程安全

Easy Mic 在API层面设计为线程安全：

### ✅ 线程安全操作
- 从活动录音中添加/移除处理器
- 开始/停止录音
- 访问设备信息
- 创建/释放处理器

### ⚠️ 线程不安全区域
- 处理过程中修改处理器属性
- 从多个线程访问处理器状态
- 在管道外部手动缓冲区操作

### 🧵 线程模型
```
主线程 (Unity)         音频线程 (原生)        后台线程 (可选)
     │                        │                           │
     │ StartRecording()       │                           │
     ├────────────────────────▶                           │
     │                        │ OnAudioPass()            │
     │                        ├──────────────────────────▶
     │ AddProcessor()         │                           │ 处理音频
     ├────────────────────────▶                           ├─────────────▶
     │                        │                           │
     │ StopRecording()        │                           │
     ├────────────────────────▶                           │
```

## 💡 最佳实践

### 🎯 性能
- **最小化分配**：在音频处理器中
- **保持处理轻量级**：音频线程是实时关键的
- **使用适当的缓冲区大小**：平衡延迟与性能
- **避免复杂操作**：在音频回调中

### 🔧 架构
- **组合简单处理器**：而不是创建复杂的处理器
- **使用 AudioReader 进行分析**：不修改音频
- **使用 AudioWriter 进行效果**：修改音频
- **优雅地处理错误**：不要让音频线程崩溃

### 🧹 资源管理
- **始终释放处理器**：完成后
- **停止录音**：在销毁 GameObjects 之前
- **使用 using 语句**：自动清理
- **监控内存使用**：在长时间运行的应用程序中

## 🔍 下一步

现在您已经理解了核心概念，可以深入了解：

- **[音频管道](audio-pipeline.md)** - 掌握处理管道
- **[内置处理器](processors.md)** - 了解所有可用的处理器
- **[API 参考](api-reference.md)** - 完整的API文档
- **[最佳实践](best-practices.md)** - 优化性能的技巧

---

← [入门指南](getting-started.md) | **下一步：[音频管道](audio-pipeline.md)** →