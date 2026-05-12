# Diagnostics

EasyMic exposes lightweight telemetry snapshots for capture and playback. Counters are written from callback or worker paths and interpreted from normal control code.

## Where to Read Diagnostics

Capture:

```csharp
RecordingInfo info = EasyMicAPI.GetRecordingInfo(handle);
EasyMicTelemetrySnapshot t = info.Telemetry;
EasyMicRealtimeStats rt = info.RealtimeStats;
EasyMicLatencyStats latency = info.LatencyStats;
```

Playback:

```csharp
AudioSystem system = AudioSystem.Instance;
EasyMicTelemetrySnapshot t = system.Telemetry;
EasyMicPlaybackPipelineSnapshot snapshot = system.PipelineSnapshot;
```

Editor:

- Window > EasyMic > Pipeline Visualizer

## Telemetry Fields

| Field | Meaning | Common response |
|---|---|---|
| `CallbackCount` | Number of native device callbacks seen by EasyMic. | If zero, the device did not start or the callback is not firing. |
| `CallbackExceptions` | Exceptions caught inside the callback wrapper. | Treat as serious; inspect logs and native/plugin setup. |
| `CallbackMaxMicroseconds` | Maximum callback duration when `EASYMIC_RT_DIAGNOSTICS` is enabled. | Lower processor/callback pressure; use only for diagnostics. |
| `CallbackAverageMicroseconds` | Average callback duration when diagnostics timing is enabled. | Watch trends, not single samples. |
| `TransportOverruns` | Producer could not write because a transport ring was full. | Use a safer latency profile or reduce worker/processor cost. |
| `TransportUnderruns` | Consumer could not read a complete block. For playback this means output lacked frames. | For playback, increase buffering or feed earlier. |
| `FramesReceived` | Capture frames accepted into the capture transport. | Useful for verifying input flow. |
| `FramesDropped` | Capture frames dropped due to capture transport pressure. | Reduce capture processing or use `Balanced`/`Stable`. |
| `ZeroFilledFrames` | Playback frames cleared to silence when output was unavailable. | Investigate underruns and producer timing. |
| `WorkerLateCount` | Transport worker did not refill/drain fast enough. | Reduce worker work, avoid blocking, or increase buffering. |
| `WorkerMaxMicroseconds` | Maximum observed transport worker block duration. | Identify expensive processors or mixer work. |
| `ProcessorExceptions` | Exceptions caught while running processor/mixer pipeline work. | Fix processors; exceptions are contained but still harmful. |
| `EventQueueDrops` | Main-thread event queue dropped events. | Drain events faster or reduce event rate. |
| `LastQueueDepthSamples` | Latest transport ring depth in samples. | Convert with `LatencyStats` to estimate buffering. |
| `MinQueueDepthSamples` / `MaxQueueDepthSamples` | Observed queue depth range. | Useful for tuning watermarks and profiles. |
| `ActiveCallbacks` | Number of callbacks currently inside EasyMic. | Should normally be zero outside a callback instant. |
| `LastCallbackThreadId` | Managed callback thread id when diagnostics timing is enabled. | Debug-only thread identity signal. |

## Compile-Time Diagnostics

`EASYMIC_RT_DIAGNOSTICS` enables callback timing fields such as max/average callback microseconds and callback thread id capture. Leave it off unless you are profiling; timing itself adds some diagnostic work.

## Common Readings

Playback underruns mean the output callback requested frames that were not available in the playback ring. EasyMic zero-fills missing frames to keep the device running. Frequent underruns usually mean the playback worker is blocked, the latency profile is too aggressive, or user processors are doing too much work.

Capture overruns mean the capture callback could not write a block to the capture ring. EasyMic drops the block rather than blocking the callback. Frequent capture overruns usually mean the capture worker or processors cannot keep up.

Event queue drops mean Unity-facing events are being produced faster than the main-thread event pump can drain them. Reduce event frequency or move bulk data out of event payloads.
