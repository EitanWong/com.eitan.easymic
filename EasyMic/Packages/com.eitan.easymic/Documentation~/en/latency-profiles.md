# Latency Profiles

`EasyMicLatencyProfile` is a policy hint. The backend may relax it when a platform or device cannot provide the requested period size or buffer geometry.

## Profiles

| Profile | Best for | Tradeoff |
|---|---|---|
| `UltraLowLatency` | Desktop experiments, tight monitoring, controlled CPU load | Least tolerant of GC, scene loading, and processor spikes. |
| `LowLatency` | Interactive desktop apps, voice, digital humans | Good default for desktop; still requires bounded processors. |
| `Balanced` | Mobile, general app use, safer default recording | More buffering and more tolerance for scheduling jitter. |
| `Stable` / `SafeStreaming` | Kiosks, exhibitions, long-running streaming, unstable devices | Prioritizes continuity over minimum latency. |

`Stable` is an alias of `SafeStreaming`.

## Current Buffer Policy

Approximate policy values from the current implementation:

| Profile | Native period hint | Native periods | Capture transport capacity | Playback ring capacity | Playback target |
|---|---:|---:|---:|---:|---:|
| `UltraLowLatency` | ~5 ms | 2 | ~0.08 s | ~0.04 s | ~20 ms |
| `LowLatency` | ~10 ms | 3 | ~0.12 s | ~0.08 s | ~45 ms |
| `Balanced` | ~15 ms | 3 | ~0.25 s | ~0.12 s | ~80 ms |
| `SafeStreaming` / `Stable` | ~25 ms | 4 | ~0.50 s | ~0.30 s | ~160 ms |

These are buffer policies, not measured end-to-end latency. Actual latency depends on OS backend, device driver, sample-rate conversion, Unity scheduling, CPU load, and the producer/processor workload.

## Defaults

Recording overloads without an explicit profile currently use `Balanced`.

Playback defaults:

- Android player: `Balanced`.
- Other platforms: `LowLatency`.

## Choosing a Profile

Use `LowLatency` first on desktop. Move to `UltraLowLatency` only after measuring underruns and CPU headroom on the target machine.

Use `Balanced` for Android, iOS, and app flows that can spike due to loading, UI, networking, or model inference.

Use `Stable` / `SafeStreaming` when the application must keep audio continuous for long periods and extra latency is acceptable.

## Tuning Signals

Move to a safer profile when you see:

- capture `TransportOverruns`;
- capture `FramesDropped`;
- playback `TransportUnderruns`;
- playback `ZeroFilledFrames`;
- increasing `WorkerLateCount`;
- processor exceptions or event queue drops.

Move to a lower-latency profile only after these counters remain stable on real target hardware.
