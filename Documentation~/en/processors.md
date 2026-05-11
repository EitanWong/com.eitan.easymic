# Processor Contracts

Processors are `IAudioWorker` implementations created from `AudioWorkerBlueprint` factories and attached to capture, playback source, or mixer pipelines.

## Threading Rules

| Location | Unity API allowed? | Blocking allowed? | Allocation allowed? | Notes |
|---|---:|---:|---:|---|
| miniaudio callback | No | No | No | Internal transport only. User processors do not run here. |
| capture worker | No | Avoid | Avoid | Recording pipeline path. |
| playback render worker | No | Avoid | Avoid | Underrun-sensitive mixer/source path. |
| `AudioReader` worker | No | Avoid | Limited | Use for analysis or forwarding; dispatch Unity work elsewhere. |
| Unity main thread | Yes | Yes, within reason | Yes | UI, gameplay, permissions, scene objects. |

## Interfaces and Markers

| Type | Purpose |
|---|---|
| `IAudioWorker` | Base processor interface with `Initialize(AudioContext)` and `OnAudioPass(Span<float>, AudioContext)`. |
| `AudioWriter` | Base class for in-place processing that can modify the buffer. |
| `AudioReader` | Base class that queues frames and invokes `OnAudioReadAsync` on its own worker thread. |
| `IAudioTransportProcessor` | Marker for processors intended to run on transport workers. |
| `IMainThreadAudioProcessor` | Marker for processors that require Unity main-thread execution and should not be inserted into transport paths. |
| `IRealtimeForbiddenProcessor` | Marker for processors that may allocate, block, perform I/O, or call Unity APIs and are forbidden from realtime/transport execution. |

`AudioPipeline` validates marker contracts when adding workers.

## Blueprints

Pass blueprints, not shared processor instances, to recording APIs.

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

Each session creates fresh worker instances from the blueprint. Use the same blueprint key to retrieve the active instance:

```csharp
var capturer = EasyMicAPI.GetProcessor<Capturer>(handle, capture);
```

## Built-In Processors

| Processor | Base | Use |
|---|---|---|
| `Capturer` | `AudioReader` | Captures audio into an `AudioBuffer`, optional target sample rate, optional `IAudioSink`, and `AudioClip` conversion. |
| `Downmixer` | `AudioWriter` | Converts multichannel input to mono in place and updates `AudioContext`. |
| `VolumeGateFilter` | `AudioWriter` | Noise gate with threshold, attack, hold, release, and lookahead settings. |
| `Resampler` | `AudioWriter` | Resamples to a target sample rate. |
| `SoftLimiter` | `AudioWriter` | Playback/capture limiter used by the master mixer by default. |
| `NativeGainer` | `AudioWriter` | Native gain processor with smoothing. |
| `NativeFader` | `AudioWriter` | Native fade in/out processor. |
| `NativePanner` | `AudioWriter` | Stereo pan/balance processor. |
| `HighPassFilter`, `LowShelfFilter`, `PeakingEQ` | `AudioWriter` | Native biquad filter processors. |
| `NativeDelay` | `AudioWriter` | Native delay effect. |
| `LoopbackPlayer` | `AudioReader` | Legacy loopback-style reader; direct playback is handled by the current playback system. |

SherpaONNXUnity processors and components are available when the integration assembly and dependencies are present.

## Custom AudioWriter

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

Keep writer work bounded. Use thread-safe parameter updates for values modified from the main thread.

## Custom AudioReader

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

`AudioReader` decouples reader work from the transport pass, but it still runs on a worker thread. Do not call Unity APIs from `OnAudioReadAsync`.

## Exceptions

Processor exceptions are caught outside the miniaudio callback and counted in telemetry. Do not rely on this as control flow. Exceptions in a hot audio path still create CPU and allocation pressure.
