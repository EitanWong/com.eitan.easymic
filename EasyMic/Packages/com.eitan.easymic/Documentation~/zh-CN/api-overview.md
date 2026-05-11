# API 概览

本页列出集成时最常用的开发者 API。它不是私有实现参考。

## 录音

| API | 用途 |
|---|---|
| `EasyMicAPI.IsAvailable` | 当前平台上麦克风系统是否可以初始化。 |
| `EasyMicAPI.UnavailabilityReason` | 不可用时的初始化失败原因。 |
| `EasyMicAPI.Refresh()` | 刷新麦克风设备列表。 |
| `EasyMicAPI.Devices` | 当前 `MicDevice[]` 快照。 |
| `EasyMicAPI.Default` | 默认输入设备，或第一个可用输入设备。 |
| `EasyMicAPI.StartRecording(...)` | 启动录音 session。重载支持 device/name/default、workers，以及显式 latency profile。 |
| `EasyMicAPI.StopRecording(handle)` | 停止并释放一个录音 session。 |
| `EasyMicAPI.StopAllRecordings()` | 停止所有录音 session。 |
| `EasyMicAPI.AddProcessor(handle, blueprint)` | 向活动 session 添加处理器 blueprint。 |
| `EasyMicAPI.RemoveProcessor(handle, blueprint)` | 从活动 session 移除处理器。 |
| `EasyMicAPI.GetProcessor<T>(handle, blueprint)` | 获取由 blueprint 创建的活动 session 实例。 |
| `EasyMicAPI.GetRecordingInfo(handle)` | 读取设备、格式、回调计数器和 telemetry。 |
| `EasyMicAPI.GetRecordingPipelineSnapshots()` | 为诊断工具读取活动采集拓扑快照。 |

## 播放

| API | 用途 |
|---|---|
| `AudioPlayback.DefaultLatencyProfile` | Android player 为 `Balanced`，其他平台为 `LowLatency`。 |
| `AudioPlayback.PlayClip(...)` | 创建 handle 并播放 `AudioClip`。 |
| `AudioPlayback.CreateStream(...)` | 创建流式 `PlaybackHandle`。 |
| `PlaybackHandle.Enqueue(...)` | 入队 PCM，走内部路径。 |
| `PlaybackHandle.TryEnqueue(...)` | 入队 PCM 并返回 `EasyMicEnqueueResult`。 |
| `PlaybackHandle.CompleteStream()` | 标记流在已缓冲音频 drain 后结束。 |
| `PlaybackHandle.BufferedSeconds` | 近似的源缓冲深度。 |
| `AudioSystem.Instance` | 播放系统单例。 |
| `AudioSystem.Telemetry` | 播放传输 telemetry。 |
| `AudioSystem.PipelineSnapshot` | 播放拓扑和 mixer/source 快照。 |
| `AudioSystem.LatencyProfile` | 在 `Start()` 前设置；运行中设置会抛出异常。 |

## 组件

| Component | Namespace | 用途 |
|---|---|---|
| `EasyMicrophone` | `Eitan.EasyMic.Runtime.Mono` | 场景录音包装器，包含设备选项、事件、临时 WAV 采集和 latest clip 访问。 |
| `PlaybackAudioSourceBehaviour` | `Eitan.EasyMic.Runtime.Mono.Components` | 场景播放包装器，用于 clip 和流式 PCM 播放。 |
| `VoiceMicrophone` | Sherpa integration namespace | 安装 SherpaONNXUnity 集成后可用的 ASR 麦克风组件。 |
| `SpeechSynthesizer` | Sherpa integration namespace | 安装集成依赖后，可向 EasyMic 播放流式输出的 TTS 组件。 |

## 数据类型

| Type | 用途 |
|---|---|
| `RecordingHandle` | 轻量录音 session id。 |
| `PlaybackHandle` | 带播放控制的轻量播放 session id。 |
| `MicDevice` | 输入设备身份和格式支持。 |
| `SampleRate` | 支持的采样率枚举。 |
| `Channel` | 声道数枚举。 |
| `EasyMicLatencyProfile` | 延迟/缓冲策略。 |
| `RecordingInfo` | 采集设备、格式、计数器、telemetry 和 latency stats。 |
| `EasyMicTelemetrySnapshot` | 回调、传输、worker、队列和异常计数器。 |
| `EasyMicRealtimeStats` | 更小的 realtime 相关 telemetry 视图。 |
| `EasyMicLatencyStats` | 队列深度和格式数据，并提供毫秒换算。 |
| `EasyMicEnqueueResult` | 播放入队结果和写入样本数。 |

## 处理器

| Type | 用途 |
|---|---|
| `AudioWorkerBlueprint` | factory 加稳定 key，用于创建每个 session 的处理器实例。 |
| `IAudioWorker` | 基础处理器契约。 |
| `AudioWriter` | 就地处理器基类。 |
| `AudioReader` | 带 worker-thread `OnAudioReadAsync` 的异步 reader 基类。 |
| `IAudioTransportProcessor` | transport-safe 处理器标记。 |
| `IMainThreadAudioProcessor` | 只能在主线程运行的处理器标记。 |
| `IRealtimeForbiddenProcessor` | 不允许放入 realtime/transport 路径的处理器标记。 |

编写自定义处理器前，请先阅读 [处理器契约](processors.md)。
