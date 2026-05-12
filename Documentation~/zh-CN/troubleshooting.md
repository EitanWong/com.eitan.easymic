# 故障排除

先看诊断计数器。它们会告诉你问题是在设备启动、采集输入、播放输出、队列压力，还是处理器工作。

## 没有麦克风输入

### 现象

`EasyMicAPI.Devices` 为空，录音无法启动，或 callback count 一直为零。

### 可能原因

- 缺少麦克风权限。
- 设备断开或被禁用。
- 当前平台缺少原生 miniaudio plugin。
- 另一个应用以独占方式占用了设备。

### 修复

- 录音前在主线程调用 `PermissionUtils.HasPermission()`。
- 调用 `EasyMicAPI.Refresh()` 并检查 `EasyMicAPI.Devices`。
- 检查 `EasyMicAPI.IsAvailable` 和 `EasyMicAPI.UnavailabilityReason`。
- 使用 `Recording Example` sample 测试。
- 在 macOS 上，确认 Unity Editor 或已构建 player 的麦克风权限。

## 没有播放输出

### 现象

`AudioPlayback.PlayClip` 返回有效 handle，但听不到声音。

### 可能原因

- 输出设备初始化失败。
- 音量为零、mute，或路由到了其他设备。
- Clip 数据为空或不受支持。
- Stream 从未 feed，或数据到达前就 complete。

### 修复

- 检查 `AudioSystem.Instance.IsRunning`。
- 检查系统输出路由和音量。
- 查看 `AudioSystem.Instance.Telemetry.CallbackCount`。
- 对 stream，检查 `PlaybackHandle.BufferedSeconds` 和 `TryEnqueue` 结果状态。

## 频繁 Playback Underruns

### 现象

音频 glitch 或静音；`TransportUnderruns` 和 `ZeroFilledFrames` 增加。

### 可能原因

- Latency profile 太激进。
- Playback render worker 被阻塞。
- Stream producer 入队太晚。
- Source 或 mixer processors 过于昂贵。
- Scene loading 或 GC 影响 worker 调度。

### 修复

- 使用 `Balanced` 或 `Stable`。
- 保持 playback processors 小且少分配。
- 为 streaming 维护 producer-side buffer budget。
- 低延迟播放活动时，避免 scene load 期间执行重工作。

## Capture Overflows

### 现象

录音时 `TransportOverruns` 或 `FramesDropped` 增加。

### 可能原因

- Capture worker 无法足够快地 drain ring。
- Capture processors 阻塞或分配。
- `UltraLowLatency` 对目标设备太激进。

### 修复

- 使用 `Balanced` 或 `Stable`。
- 把重分析移动到 `AudioReader` 或另一个 worker queue。
- 避免在处理器中执行文件 I/O、网络 I/O、Unity API 调用和长时间持锁。

## 高延迟

### 现象

输入监听或 streamed playback 感觉延迟较高。

### 可能原因

- `Balanced` 或 `Stable` 缓冲本来就更大。
- Producer 预缓冲过多。
- 平台 backend 增加设备/OS 延迟。
- 采样率转换或外部音频路由增加延迟。

### 修复

- 桌面端尝试 `LowLatency`。
- 降低 producer-side buffering。
- 在目标硬件上测量，不只在 Editor 中测量。
- 对 latency-sensitive monitoring，避免使用 Bluetooth 路由。

## 场景加载期间出现 Glitch

### 现象

加载场景、资源或模型时出现音频 glitch。

### 可能原因

- CPU 和 GC 尖峰延迟 transport workers。
- Main-thread event queues 堵塞。
- 处理器在 transition 期间分配。

### 修复

- 重 transition 期间使用 `Balanced` 或 `Stable`。
- 启动低延迟音频前预加载资源。
- 降低事件频率并避免大型事件 payload。

## 进入或退出 Play Mode 后出现问题

### 现象

设备仍被占用，callback 停止，或旧事件在 domain reload 后触发。

### 可能原因

- Handles 未释放。
- Components 销毁时没有停止其持有的 sessions。
- 静态状态重置时，用户代码仍持有旧 handles。

### 修复

- 在 `OnDisable` / `OnDestroy` 中停止录音。
- 释放长生命周期的 `PlaybackHandle`。
- 需要主动重置麦克风系统时调用 `EasyMicAPI.Cleanup()`。

## Android 延迟或路由问题

### 现象

延迟高于桌面端，路由意外变化，或不同设备行为不一致。

### 可能原因

- Android backend 和设备特定 buffer 行为。
- Bluetooth 或 voice communication routing。
- 缺少运行时权限。

### 修复

- 从 `Balanced` 开始。
- 测试真实设备。
- 添加 `RECORD_AUDIO` 权限。
- 不要假设 emulator 音频行为和手机一致。

## 处理器导致卡顿

### 现象

添加自定义处理器后开始出现 glitch。

### 可能原因

- Worker 路径中存在 blocking calls、locks、allocation、exceptions 或 Unity APIs。
- 每个 audio block CPU 开销过高。
- 共享可变状态没有线程安全访问。

### 修复

- 遵守 [处理器契约](processors.md)。
- 使用预分配 buffers。
- 把 Unity 工作 dispatch 到主线程。
- 检查 `ProcessorExceptions` 和 `WorkerMaxMicroseconds`。

## Event Queue Drops

### 现象

`EventQueueDrops` 增加，或 UI callbacks 丢失更新。

### 可能原因

- Unity-facing events 太多。
- 主线程被阻塞。
- 事件 payload 过大。

### 修复

- 降低事件频率。
- 发送摘要而不是完整音频 buffers。
- 保持主线程 handlers 很短。
