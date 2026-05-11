# 处理器契约

处理器是 `IAudioWorker` 实现，由 `AudioWorkerBlueprint` factory 创建，并附加到采集、播放 source 或 mixer pipeline。

## 线程规则

| 位置 | 允许 Unity API? | 允许阻塞? | 允许分配? | 说明 |
|---|---:|---:|---:|---|
| miniaudio callback | 否 | 否 | 否 | 仅内部 transport。用户处理器不在这里运行。 |
| capture worker | 否 | 避免 | 避免 | 录音 pipeline 路径。 |
| playback render worker | 否 | 避免 | 避免 | 对 underrun 敏感的 mixer/source 路径。 |
| `AudioReader` worker | 否 | 避免 | 有限 | 用于分析或转发；Unity 工作应 dispatch 到别处。 |
| Unity main thread | 是 | 合理范围内可以 | 是 | UI、gameplay、权限、场景对象。 |

## 接口和标记

| Type | 用途 |
|---|---|
| `IAudioWorker` | 基础处理器接口，包含 `Initialize(AudioContext)` 和 `OnAudioPass(Span<float>, AudioContext)`。 |
| `AudioWriter` | 就地处理基类，可以修改 buffer。 |
| `AudioReader` | 基类，会把 frames 入队，并在自己的 worker thread 上调用 `OnAudioReadAsync`。 |
| `IAudioTransportProcessor` | 标记处理器适合在 transport workers 上运行。 |
| `IMainThreadAudioProcessor` | 标记处理器需要 Unity main-thread execution，不应插入 transport paths。 |
| `IRealtimeForbiddenProcessor` | 标记处理器可能分配、阻塞、执行 I/O 或调用 Unity API，因此禁止进入 realtime/transport execution。 |

`AudioPipeline` 添加 worker 时会验证这些 marker contracts。

## Blueprints

向录音 API 传入 blueprints，而不是共享的处理器实例。

```csharp
var gate = new AudioWorkerBlueprint(
    () => new VolumeGateFilter { ThresholdDb = -35f },
    "gate");

var capture = new AudioWorkerBlueprint(() => new Capturer(), "capture");

var handle = EasyMicAPI.StartRecording(
    EasyMicAPI.Default,
    SampleRate.Hz48000,
    Channel.Mono,
    new[] { gate, capture },
    EasyMicLatencyProfile.LowLatency);
```

每个 session 都会从 blueprint 创建新的 worker 实例。使用同一个 blueprint key 获取活动实例：

```csharp
var capturer = EasyMicAPI.GetProcessor<Capturer>(handle, capture);
```

## 内置处理器

| Processor | Base | 用途 |
|---|---|---|
| `Capturer` | `AudioReader` | 把音频采集到 `AudioBuffer`，支持可选目标采样率、可选 `IAudioSink` 和 `AudioClip` 转换。 |
| `Downmixer` | `AudioWriter` | 就地把多声道输入转换为 mono，并更新 `AudioContext`。 |
| `VolumeGateFilter` | `AudioWriter` | Noise gate，支持 threshold、attack、hold、release 和 lookahead 设置。 |
| `Resampler` | `AudioWriter` | 重采样到目标采样率。 |
| `SoftLimiter` | `AudioWriter` | 默认由 master mixer 使用的播放/采集 limiter。 |
| `NativeGainer` | `AudioWriter` | 带 smoothing 的原生 gain 处理器。 |
| `NativeFader` | `AudioWriter` | 原生 fade in/out 处理器。 |
| `NativePanner` | `AudioWriter` | Stereo pan/balance 处理器。 |
| `HighPassFilter`, `LowShelfFilter`, `PeakingEQ` | `AudioWriter` | 原生 biquad filter 处理器。 |
| `NativeDelay` | `AudioWriter` | 原生 delay effect。 |
| `LoopbackPlayer` | `AudioReader` | 旧 loopback 风格 reader；直接播放由当前播放系统处理。 |

安装 integration assembly 和依赖后，可以使用 SherpaONNXUnity processors 和 components。

## 自定义 AudioWriter

```csharp
using System;
using Eitan.EasyMic.Runtime;

public sealed class GainWriter : AudioWriter, IAudioTransportProcessor
{
    public float Gain = 1f;

    protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= Gain;
        }
    }
}
```

保持 writer 工作有界。对于从主线程修改的值，使用线程安全参数更新。

## 自定义 AudioReader

```csharp
using System;
using Eitan.EasyMic.Runtime;

public sealed class RmsReader : AudioReader
{
    private volatile float _rms;

    protected override void OnAudioReadAsync(ReadOnlySpan<float> buffer)
    {
        double sum = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            sum += buffer[i] * buffer[i];
        }

        _rms = buffer.Length == 0 ? 0f : (float)Math.Sqrt(sum / buffer.Length);
    }

    public float Rms => _rms;
}
```

`AudioReader` 会把 reader 工作从 transport pass 中解耦，但它仍然运行在 worker thread 上。不要从 `OnAudioReadAsync` 调用 Unity API。

## 异常

处理器异常会在 miniaudio callback 外捕获，并计入 telemetry。不要把这当作控制流。热音频路径中的异常仍然会造成 CPU 和分配压力。
