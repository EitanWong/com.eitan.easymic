# EasyMic Next-Generation Audio Architecture

This document records the target architecture and the first implemented cut of the rewrite. The governing rule is that miniaudio remains the low-level backend: EasyMic adds only thin ABI, transport, telemetry, and Unity control-plane code where those layers remove realtime hazards.

## Current Root Problems

- Capture previously executed the managed `AudioPipeline` directly from the miniaudio callback. That path included virtual dispatch, user processors, exception handling, mutable managed state, and optional diagnostic scans.
- Playback previously mixed in managed code on the miniaudio playback callback. It used preallocated buffers for most operations, but it still ran a managed graph and could invoke an internal raw mixed-frame delegate for APM from the callback.
- Device config was mostly default miniaudio configuration with an Android-only tuning helper. There was no package-level latency policy or platform fallback chain.
- Telemetry existed as scattered counters. It did not provide a consistent callback WCET, queue-depth, xrun, or transport-overrun model.
- Callback/device ownership was tied to a static legacy device-pointer registry because no `pUserData` helper exists yet. This remains a thin-interoperability target for the native helper phase.

## Module Model

- `EasyMic.Native`: miniaudio binaries and the smallest native helper surface needed for ABI-safe callback isolation, platform-specific flags, SIMD dispatch, and `pUserData` ownership.
- `EasyMic.Interop`: C# bindings to miniaudio and thin helper structs. No broad wrapper API.
- `EasyMic.Core`: managed control plane, device lifecycle, session ownership, bounded transports, and lifecycle state machines.
- `EasyMic.Unity`: MonoBehaviours, editor workflow, permission handling, and Unity main-thread adaptation.
- `EasyMic.DSP`: deterministic fixed-capacity processor graph, kernel registry, meters, and future voice/AI processors.
- `EasyMic.Streaming`: packetization, jitter buffers, ASR/VoIP bridges, drift correction, and network clocks.
- `EasyMic.Diagnostics`: realtime-safe atomics plus non-realtime visualization/logging.
- `EasyMic.Editor`: device/latency tooling, soak-test runners, telemetry windows.

## Threading Model

- Audio realtime threads: miniaudio callbacks. Only copy/move PCM, update atomics, read/write bounded rings, and perform tiny fixed-cost accounting.
- Capture transport worker: drains SPSC capture blocks and invokes managed processors off the realtime callback.
- Playback render worker: pre-renders the managed mixer graph into a bounded SPSC ring. The miniaudio playback callback consumes from this ring and zero-fills underruns.
- Unity main thread: starts/stops sessions, handles permissions, editor UI, component state, and event delivery.
- Telemetry/event pump thread: drains audio event rings, allocates managed payloads, and posts to Unity synchronization context.
- Future DSP workers: process non-callback graph regions using fixed-capacity command queues and generation-safe resource swaps.

## Queue Topology

- Capture uses a bounded SPSC float ring with one producer, one consumer, and framed messages: `[sampleCount, interleaved samples...]`.
- Playback uses a bounded SPSC float ring with the render worker as producer and the miniaudio callback as consumer.
- Callback overflow is drop-and-count, never block-and-wait.
- User events use a separate SPSC event ring and are formatted away from realtime threads.
- Future command traffic should use MPSC command queues into the control plane, then generation-swapped immutable graph snapshots for audio readers.

## Memory Model

- All realtime buffers are allocated during session initialization.
- Callback path performs no managed allocations and no locks.
- Transport worker may grow its managed work buffer if a backend delivers a larger callback block than expected; this is outside the realtime callback.
- DSP kernels should use aligned native slabs or preallocated arrays and fixed-capacity node tables. Dynamic graph edits allocate on the control thread and publish atomically.

## Latency Profiles

`EasyMicLatencyProfile` maps package policy to miniaudio `ma_device_config` hints:

- `UltraLowLatency`: about 5 ms periods, 2 periods, higher underrun risk.
- `LowLatency`: about 10 ms periods, 3 periods, default playback target.
- `Balanced`: about 15 ms periods, 3 periods, default capture target for stability during managed transport work.
- `SafeStreaming`: about 25 ms periods, 4 periods, higher stability for Bluetooth/network/AI pipelines.

The policy applies WASAPI pro-audio usage, CoreAudio nominal-rate allowance, ALSA no-auto-conversion hints, and Android AAudio/OpenSL voice communication flags where Unity platform defines permit.

## Clock And Drift Plan

- Every device callback needs a monotonic frame counter and backend/device timestamp when available.
- Capture, playback, Unity, and network clocks must be separate domains.
- Drift estimation should run off callback using frame counters and high-resolution monotonic time.
- Duplex/AEC/VoIP paths should use PLL-style correction and adaptive resampling rather than unbounded queue growth.

## DSP Graph Plan

- Realtime graph nodes should be fixed-capacity and scheduled in deterministic topological order.
- Inserts, sends, buses, sidechains, meters, VAD, denoise, AEC prep, and ASR preprocessors become node kinds, not ad hoc managed callbacks.
- SIMD kernels are selected through a capability registry: native SSE2/SSE/AVX/AVX2/NEON where present, Burst jobs for non-callback batch work, scalar fallback everywhere.

## Telemetry

Realtime threads only update atomics:

- callback count
- callback total/max ticks
- transport overruns/underruns
- frames received/dropped
- queue depth last/max

Non-realtime diagnostics derive:

- callback WCET and average microseconds
- xrun rates
- drift ppm
- worker lag
- queue depth trends
- device latency estimates

## Implemented In This Cut

- Added `EasyMicLatencyProfile`.
- Added `MiniaudioDeviceConfigPolicy` and connected it to `Native.AllocateDeviceConfig`.
- Added `RealtimeAudioTelemetry`.
- Added `CaptureAudioTransport`.
- Added `PlaybackRenderTransport`.
- Changed recording callbacks to enqueue PCM to the transport instead of invoking the managed processor graph directly.
- Changed playback callbacks to read pre-rendered PCM from the transport instead of invoking the managed mixer graph directly.
- Changed playback source enqueue to partial/non-blocking behavior instead of unbounded spin-sleep.
- Added structured enqueue status via `EasyMicEnqueueResult` / `EasyMicQueueStatus`.
- Changed playback reset to publish a reset marker consumed by the render side before clearing the ring.
- Added public telemetry snapshots for callback timing, transport queue depth, overruns, underruns, and dropped frames.
- Added public latency-profile selection for playback and capture startup paths.
- Added playback factory overloads for selecting latency profile before native output initialization.
- Applied `LowLatency` profile to playback devices and `Balanced` profile to capture devices.

## Remaining Rewrite Phases

1. Add a tiny native helper for `ma_device.pUserData` ownership, telemetry counters, callback isolation, and backend timestamp extraction. Do not add a broad EasyMic engine wrapper.
2. Replace managed processor interfaces on transport workers with fixed node descriptors and generation-swapped native/Burst kernels.
3. Add device notification callbacks, interruption handling, suspend/resume, and hotplug state machines.
4. Add drift estimator, adaptive resampler controller, and duplex synchronization.
5. Add soak, xrun, hotplug, Bluetooth switch, forced-GC, suspend/resume, and long-session tests.
