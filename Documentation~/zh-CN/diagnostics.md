# 诊断

EasyMic 为采集和播放暴露轻量 telemetry snapshots。计数器由 callback 或 worker 路径写入，并在普通控制代码中解释。

## 在哪里读取诊断

采集：

```csharp
RecordingInfo info = EasyMicAPI.GetRecordingInfo(handle);
EasyMicTelemetrySnapshot t = info.Telemetry;
EasyMicRealtimeStats rt = info.RealtimeStats;
EasyMicLatencyStats latency = info.LatencyStats;
```

播放：

```csharp
AudioSystem system = AudioSystem.Instance;
EasyMicTelemetrySnapshot t = system.Telemetry;
EasyMicPlaybackPipelineSnapshot snapshot = system.PipelineSnapshot;
```

Editor：

- Window > EasyMic > Pipeline Visualizer

## Telemetry 字段

| Field | 含义 | 常见响应 |
|---|---|---|
| `CallbackCount` | EasyMic 看到的原生设备 callback 数量。 | 如果为零，设备未启动或 callback 未触发。 |
| `CallbackExceptions` | callback wrapper 内捕获的异常。 | 视为严重问题；检查日志和 native/plugin 设置。 |
| `CallbackMaxMicroseconds` | 启用 `EASYMIC_RT_DIAGNOSTICS` 时的最大 callback 耗时。 | 降低 processor/callback 压力；只用于诊断。 |
| `CallbackAverageMicroseconds` | 启用诊断 timing 时的平均 callback 耗时。 | 关注趋势，不看单个样本。 |
| `TransportOverruns` | Producer 因 transport ring 已满而无法写入。 | 使用更安全的 latency profile，或降低 worker/processor 成本。 |
| `TransportUnderruns` | Consumer 无法读取完整 block。对播放而言，表示输出缺少 frames。 | 播放场景下增加缓冲或更早 feed。 |
| `FramesReceived` | 被 capture transport 接收的 capture frames。 | 用于确认输入流动。 |
| `FramesDropped` | 因 capture transport 压力而丢弃的 capture frames。 | 降低采集处理开销，或使用 `Balanced`/`Stable`。 |
| `ZeroFilledFrames` | 输出不可用时被清零为静音的 playback frames。 | 排查 underruns 和 producer timing。 |
| `WorkerLateCount` | Transport worker 未能足够快地 refill/drain。 | 降低 worker 工作、避免阻塞，或增加缓冲。 |
| `WorkerMaxMicroseconds` | 观察到的 transport worker block 最大耗时。 | 定位昂贵处理器或 mixer 工作。 |
| `ProcessorExceptions` | 运行 processor/mixer pipeline work 时捕获的异常。 | 修复处理器；异常会被隔离但仍有害。 |
| `EventQueueDrops` | 主线程 event queue 丢弃事件。 | 更快 drain 事件或降低事件频率。 |
| `LastQueueDepthSamples` | 最近的 transport ring 深度，单位 samples。 | 用 `LatencyStats` 换算估计缓冲。 |
| `MinQueueDepthSamples` / `MaxQueueDepthSamples` | 观察到的队列深度范围。 | 用于调 watermarks 和 profiles。 |
| `ActiveCallbacks` | 当前位于 EasyMic 内的 callback 数量。 | callback 瞬间之外通常应为零。 |
| `LastCallbackThreadId` | 启用诊断 timing 时的托管 callback thread id。 | 仅用于 debug 的线程身份信号。 |

## 编译期开关

`EASYMIC_RT_DIAGNOSTICS` 启用 callback timing 字段，例如 max/average callback microseconds 和 callback thread id capture。除非正在 profiling，否则保持关闭；timing 本身会增加一些诊断工作。

## 常见读数

Playback underruns 表示输出 callback 请求了 playback ring 中不存在的 frames。EasyMic 会 zero-fill 缺失 frames 以保持设备运行。频繁 underrun 通常意味着 playback worker 被阻塞、latency profile 太激进，或用户处理器做了太多工作。

Capture overruns 表示 capture callback 无法把 block 写入 capture ring。EasyMic 会丢弃 block，而不是阻塞 callback。频繁 capture overrun 通常意味着 capture worker 或 processors 跟不上。

Event queue drops 表示 Unity-facing events 的产生速度超过 main-thread event pump 的 drain 能力。降低事件频率，或把大块数据移出事件 payload。
