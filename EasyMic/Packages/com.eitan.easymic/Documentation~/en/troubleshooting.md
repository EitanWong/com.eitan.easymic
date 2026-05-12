# Troubleshooting

Use diagnostics counters first. They tell you whether the problem is device startup, capture input, playback output, queue pressure, or processor work.

## No Microphone Input

### Symptom

`EasyMicAPI.Devices` is empty, recording does not start, or callback count stays at zero.

### Likely causes

- Microphone permission is missing.
- The device is disconnected or disabled.
- Native miniaudio plugin is missing for the platform.
- Another app owns the device exclusively.

### Fixes

- Call `PermissionUtils.HasPermission()` on the main thread before recording.
- Call `EasyMicAPI.Refresh()` and inspect `EasyMicAPI.Devices`.
- Check `EasyMicAPI.IsAvailable` and `EasyMicAPI.UnavailabilityReason`.
- Test with the `Recording Example` sample.
- On macOS, verify permission for the Unity Editor or the built player.

## No Playback Output

### Symptom

`AudioPlayback.PlayClip` returns a valid handle but no sound is heard.

### Likely causes

- Output device initialization failed.
- Volume is zero, muted, or routed elsewhere.
- Clip data is empty or unsupported.
- The stream was never fed or was completed before data arrived.

### Fixes

- Check `AudioSystem.Instance.IsRunning`.
- Check system output route and volume.
- Inspect `AudioSystem.Instance.Telemetry.CallbackCount`.
- For streams, check `PlaybackHandle.BufferedSeconds` and `TryEnqueue` result status.

## Frequent Playback Underruns

### Symptom

Audio glitches or silence; `TransportUnderruns` and `ZeroFilledFrames` increase.

### Likely causes

- Latency profile is too aggressive.
- Playback render worker is blocked.
- Stream producer is enqueueing too late.
- Source or mixer processors are expensive.
- Scene loading or GC is starving worker scheduling.

### Fixes

- Use `Balanced` or `Stable`.
- Keep playback processors small and allocation-light.
- Maintain a producer-side buffer budget for streaming.
- Avoid heavy work during scene loads while low-latency playback is active.

## Capture Overflows

### Symptom

`TransportOverruns` or `FramesDropped` increases during recording.

### Likely causes

- Capture worker cannot drain the ring fast enough.
- Capture processors are blocking or allocating.
- `UltraLowLatency` is too aggressive for the target device.

### Fixes

- Use `Balanced` or `Stable`.
- Move heavy analysis to `AudioReader` or another worker queue.
- Avoid file I/O, network I/O, Unity API calls, and long locks in processors.

## High Latency

### Symptom

Input monitoring or streamed playback feels delayed.

### Likely causes

- `Balanced` or `Stable` buffering is intentionally larger.
- Producer is prebuffering too much.
- Platform backend adds device/OS latency.
- Sample-rate conversion or external audio routing adds delay.

### Fixes

- Try `LowLatency` on desktop.
- Reduce producer-side buffering.
- Measure on target hardware, not only the Editor.
- Avoid Bluetooth routes for latency-sensitive monitoring.

## Glitches During Scene Loading

### Symptom

Audio glitches when loading scenes, assets, or models.

### Likely causes

- CPU and GC spikes delay transport workers.
- Main-thread event queues back up.
- Processors allocate during transitions.

### Fixes

- Use `Balanced` or `Stable` during heavy transitions.
- Preload assets before starting low-latency audio.
- Reduce event rate and avoid large event payloads.

## Problems After Entering or Exiting Play Mode

### Symptom

Devices remain busy, callbacks stop, or old events fire after domain reload.

### Likely causes

- Handles were not disposed.
- Components were destroyed without stopping owned sessions.
- Static state reset occurred while user code held old handles.

### Fixes

- Stop recordings in `OnDisable` / `OnDestroy`.
- Dispose long-lived `PlaybackHandle` values.
- Call `EasyMicAPI.Cleanup()` when intentionally resetting the microphone system.

## Android Latency or Routing Issues

### Symptom

Latency is higher than desktop, routing changes unexpectedly, or device behavior varies.

### Likely causes

- Android backend and device-specific buffer behavior.
- Bluetooth or voice communication routing.
- Missing runtime permission.

### Fixes

- Start with `Balanced`.
- Test real devices.
- Add `RECORD_AUDIO` permission.
- Avoid assuming emulator audio behavior matches phones.

## Processor Causes Stutter

### Symptom

Glitches begin after adding a custom processor.

### Likely causes

- Blocking calls, locks, allocation, exceptions, or Unity APIs in worker paths.
- Too much CPU per audio block.
- Shared mutable state without thread-safe access.

### Fixes

- Follow [Processor Contracts](processors.md).
- Use preallocated buffers.
- Dispatch Unity work to the main thread.
- Check `ProcessorExceptions` and `WorkerMaxMicroseconds`.

## Event Queue Drops

### Symptom

`EventQueueDrops` increases or UI callbacks miss updates.

### Likely causes

- Too many Unity-facing events.
- Main thread is blocked.
- Event payloads are too large.

### Fixes

- Reduce event frequency.
- Send summaries instead of full audio buffers.
- Keep main-thread handlers short.
