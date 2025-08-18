← [文档首页](../README.md) | [English Version](../en/architecture.md)

# 🏗️ 架构总览

本文用通俗的方式说明 EasyMic 的内部架构、线程模型与扩展点，帮助你更快上手与安全集成。

## 分层与职责

- EasyMicAPI：对外的门面（Facade），线程安全。负责设备列表、开始/停止录音、处理器增删等；在访问设备前会检查麦克风权限。
- MicSystem：管理原生音频上下文，枚举设备；维护多个并行的 RecordingSession。
- RecordingSession：单路采集流，绑定一个设备与格式；持有由“蓝图”构建的 AudioPipeline。
- AudioPipeline：有序的 IAudioWorker 阶段链表，使用不可变快照实现无锁切换，避免在实时音频回调中加锁。
- IAudioWorker：可插拔处理器。两类基类：
  - AudioWriter：在音频回调线程上“就地修改”音频数据，需保证实时安全（无阻塞、无分配、有限 CPU）。
  - AudioReader：只读抽头，不在回调线程做重活；通过高性能 SPSC 环形队列把数据投递到专属工作线程。
- Native（miniaudio）：跨平台采/放音。通过 C# P/Invoke 调用 C 封装的最小接口。
- Utilities：权限处理与设备/通道布局（桌面端使用 SoundIO 获取通道数）。
- AudioPlayback：可选的播放/混音系统（AudioSystem、AudioMixer、PlaybackAudioSource），用于监听与回环。

## 数据流

1. 原生设备回调从系统驱动拉取交错（interleaved）的 float PCM 帧。
2. RecordingSession 接收缓冲并刷新 AudioState（通道数、采样率、当前帧长度）。
3. AudioPipeline 将该帧按顺序传给当前快照中的每个处理器。
4. AudioWriter 在回调线程就地修改；AudioReader 仅快速入队，重活在其工作线程执行。

要点：
- 回调线程不阻塞：AudioReader 不阻塞，耗时操作放在其后台线程；AudioWriter 必须实时安全。
- 动态管线：运行时可热插拔处理器，无需暂停采集；操作无锁且安全。

## 线程模型

- 音频回调线程：执行 RecordingSession.HandleAudioCallback → AudioPipeline.OnAudioPass。要求不阻塞、不分配。
- AudioReader 工作线程：每个 AudioReader 自带一个线程与 SPSC 队列（AudioBuffer）。回调线程写入，Reader 线程读取。使用“空帧信号”表达端点。
- 主线程：部分处理器（如语音识别）通过 SynchronizationContext 把结果派发回主线程，便于更新 UI。

## 无锁管线（不可变快照）

AudioPipeline 维护一个不可变数组作为阶段快照。Add/Remove 创建新数组并用 Interlocked.CompareExchange 原子替换；回调线程用 Volatile.Read 读取快照遍历，无需加锁。

优势：
- 实时安全：回调路径零锁。
- 顺序可控：按添加顺序执行。
- 热插拔：录音中安全动态改管线。

## 设备选择与权限

- 权限门禁：EasyMicAPI 先检查权限；桌面/编辑器直接视为已授权；Android 会触发系统申请（PermissionUtils）。
- 设备枚举：MicSystem 从原生层拉取设备并缓存；调用 Refresh() 重新构建列表。
- 兜底选择：StartRecording 优先使用传入设备；否则默认设备；再否则第一个可用设备。
- 通道布局：桌面端用 SoundIO 读取通道数；其他平台默认单声道（Mono）。

## 工作者蓝图（AudioWorkerBlueprint）

蓝图是轻量工厂 + 稳定 key。向 API 传蓝图而不是具体实例；每个 RecordingSession 会依据蓝图“新建”自己的处理器实例，隔离安全。

示例：
```csharp
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(10), key: "capture");
var bpGate    = new AudioWorkerBlueprint(() => new VolumeGateFilter { ThresholdDb = -30 }, key: "gate");

var handle = EasyMicAPI.StartRecording(SampleRate.Hz48000, new[]{ bpGate, bpCapture });

// 之后按蓝图取回本 Session 内对应的具体实例：
var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(handle, bpCapture);
var clip = capturer?.GetCapturedAudioClip();
```

也可以在应用初始化时设置 EasyMicAPI.DefaultWorkers 作为全局默认管线。

## 原生对接（miniaudio）

- C# 通过 DllImport 调用 C 层封装的 miniaudio API（见 Native.cs）。
- 支持携带 userData 的扩展回调与无 userData 的回退两种方式，均能路由到对应 RecordingSession。
- 原生内存（设备/配置/上下文）显式分配与释放；MicSystem 统一管理生命周期并在退出时清理。

## 播放与回环（可选）

- AudioSystem：单一输出设备，混合多个源，并暴露最终混音回调（可用于 AEC 的远端参考）。
- AudioMixer：分层混音树，支持每层后处理、音量/独奏/静音。
- PlaybackAudioSource：入队交错 PCM，按需重采样/处理并叠加混音，带电平表。

## 扩展建议

- 实现 IAudioWorker：需要在回调线程改数据 → 继承 AudioWriter；重活/IO/AI → 继承 AudioReader。
- AudioWriter 必须实时安全：不可阻塞、不可分配、CPU 有界。
- 使用蓝图与 key 来支持热插拔，并用 GetProcessor<T>() 取回实例。

## 集成

- SherpaOnnxUnity：可选的语音识别与 VAD 处理器（脚本宏控制），需单独导入 Sherpa 包。
- APM（三大件 AEC/ANS/AGC）：专业音频增强扩展包。该扩展为付费内容，需要联系作者购买许可证后使用。

## 小贴士

- 移动端务必先检查权限再开始录音。
- 语音工作负载建议优先单声道，除非确实需要多通道。
- 需要下混到单声道时尽量提前做（如在 VAD/识别前）。
- 使用 DefaultWorkers 统一项目的默认处理链。

