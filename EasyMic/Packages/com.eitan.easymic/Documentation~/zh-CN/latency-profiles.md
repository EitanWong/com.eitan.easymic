# 延迟配置

`EasyMicLatencyProfile` 是策略 hint。当平台或设备无法提供请求的 period size 或 buffer geometry 时，backend 可能会放宽这些设置。

## Profiles

| Profile | 适合场景 | 代价 |
|---|---|---|
| `UltraLowLatency` | 桌面实验、严格监听、受控 CPU 负载 | 最不耐受 GC、场景加载和处理器尖峰。 |
| `LowLatency` | 交互式桌面应用、语音、数字人 | 桌面端的良好默认选择；仍需要 bounded processors。 |
| `Balanced` | 移动端、通用应用、更安全的默认录音 | 更多缓冲，对调度抖动更宽容。 |
| `Stable` / `SafeStreaming` | kiosk、展陈、长时间 streaming、不稳定设备 | 优先连续性，而不是最小延迟。 |

`Stable` 是 `SafeStreaming` 的别名。

## 当前缓冲策略

当前实现中的近似策略值：

| Profile | Native period hint | Native periods | Capture transport capacity | Playback ring capacity | Playback target |
|---|---:|---:|---:|---:|---:|
| `UltraLowLatency` | ~5 ms | 2 | ~0.08 s | ~0.04 s | ~20 ms |
| `LowLatency` | ~10 ms | 3 | ~0.12 s | ~0.08 s | ~45 ms |
| `Balanced` | ~15 ms | 3 | ~0.25 s | ~0.12 s | ~80 ms |
| `SafeStreaming` / `Stable` | ~25 ms | 4 | ~0.50 s | ~0.30 s | ~160 ms |

这些是缓冲策略，不是实测端到端延迟。实际延迟取决于 OS backend、设备驱动、采样率转换、Unity 调度、CPU 负载和 producer/processor 工作量。

## 默认值

未显式传入 profile 的录音重载当前使用 `Balanced`。

播放默认值：

- Android player：`Balanced`。
- 其他平台：`LowLatency`。

## 如何选择

桌面端先使用 `LowLatency`。只有在目标机器上测量过 underrun 和 CPU 余量后，再切换到 `UltraLowLatency`。

Android、iOS，以及可能出现 loading、UI、networking 或 model inference 尖峰的应用流程，使用 `Balanced`。

当应用必须长时间保持音频连续，且可以接受额外延迟时，使用 `Stable` / `SafeStreaming`。

## 调参信号

看到以下信号时，切换到更安全的 profile：

- capture `TransportOverruns`；
- capture `FramesDropped`；
- playback `TransportUnderruns`；
- playback `ZeroFilledFrames`；
- `WorkerLateCount` 增加；
- processor exceptions 或 event queue drops。

只有这些计数器在真实目标硬件上保持稳定后，才切换到更低延迟的 profile。
