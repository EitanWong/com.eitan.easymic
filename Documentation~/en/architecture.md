← [Documentation Home](../README.md) | [中文版本](../zh-CN/architecture.md)

# 🏗️ Architecture Overview

This document explains how EasyMic is built under the hood so you can reason about performance, threading, and extensibility when integrating it into your project.

## Layers & Responsibilities

- EasyMicAPI: Public, thread-safe facade to list devices, start/stop recordings, and manage processors. Validates microphone permission before touching devices.
- MicSystem: Manages the native audio context, enumerates devices, and hosts multiple concurrent RecordingSession instances.
- RecordingSession: One active capture stream bound to a single device and format. Owns an AudioPipeline built from AudioWorker blueprints.
- AudioPipeline: Lock-free, ordered chain of IAudioWorker stages. Uses immutable snapshots to avoid locks on the real-time audio callback.
- IAudioWorker: Pluggable processors. Two base forms exist:
  - AudioWriter: RT-safe, in-place modification on the audio callback thread.
  - AudioReader: RT-safe, read-only tap that forwards frames to a dedicated worker thread via a high‑performance SPSC ring buffer.
- Native (miniaudio): Cross‑platform capture/playback via a tiny C facade. Unity C# calls native functions through P/Invoke.
- Utilities: Permission handling and device/channel layout helpers (SoundIO on desktop platforms).
- AudioPlayback: Optional mixing/playback system (AudioSystem, AudioMixer, PlaybackAudioSource) for monitoring and loopback.

## Data Flow

1. Native device callback pulls interleaved float PCM frames from the OS driver.
2. RecordingSession receives the buffer and updates an AudioState struct (channels, sample rate, current frame length).
3. AudioPipeline forwards the frame through its current snapshot of processors in strict order.
4. AudioWriter processors mutate the buffer in place; AudioReader processors enqueue the frame to their own worker thread for async work.

Key properties:
- No blocking on the audio thread: AudioReader never blocks; heavy work runs on its background thread. AudioWriter work must be bounded and allocation‑free.
- Dynamic pipelines: Add/Remove processors at runtime without pausing capture; operations are lock‑free and safe.

## Threading Model

- Audio callback thread: Executes RecordingSession.HandleAudioCallback → AudioPipeline.OnAudioPass. Must never block or allocate.
- AudioReader worker thread: Each AudioReader owns a dedicated thread and an SPSC queue (AudioBuffer). Frames are written by the audio thread and drained by the reader thread. A single empty frame signal is used for endpoint semantics.
- Main thread: You may receive results via SynchronizationContext from some processors (e.g., speech recognizers) for safe UI callbacks.

## Lock‑Free Pipeline (Immutable Snapshot)

AudioPipeline maintains an immutable array snapshot of IAudioWorker stages. Adding/removing processors creates a new array and swaps it via Interlocked.CompareExchange. The audio thread reads the current snapshot with Volatile.Read and iterates without locking.

Benefits:
- RT safety: Zero locks in the callback path.
- Predictable ordering: Stages run in insertion order.
- Hot‑swap: Safe dynamic reconfiguration during recording.

## Device Selection & Permissions

- Permission gate: EasyMicAPI checks microphone permission first. On desktop/editor it returns granted; on Android it triggers the platform request (PermissionUtils).
- Device enumeration: MicSystem queries native devices and caches MicDevice[]; Refresh() rebuilds the list.
- Fallback selection: StartRecording chooses the preferred device; if invalid, tries system default; if none, falls back to the first available.
- Channel layout: On desktop, MicDeviceUtils probes channel count via SoundIO; non‑desktop defaults to Mono.

## Worker Blueprints

AudioWorkerBlueprint is a lightweight factory with a stable key. A blueprint is passed to the API instead of a concrete worker instance. Each RecordingSession creates its own worker instance from the blueprint, ensuring isolation and thread safety.

Example:
```csharp
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(10), key: "capture");
var bpGate    = new AudioWorkerBlueprint(() => new VolumeGateFilter { ThresholdDb = -30 }, key: "gate");

var handle = EasyMicAPI.StartRecording(SampleRate.Hz48000, new[]{ bpGate, bpCapture });

// Later, query the concrete worker instance bound to this session:
var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(handle, bpCapture);
var clip = capturer?.GetCapturedAudioClip();
```

You can also set EasyMicAPI.DefaultWorkers at app init to standardize commonly used pipelines.

## Native Integration (miniaudio)

- Unity C# binds to a small C wrapper over miniaudio via DllImport calls (Native.cs).
- Two callback styles are supported: with userData (preferred) and without; both route to the owning RecordingSession.
- Native memory (device/config/context) is allocated/freed explicitly; MicSystem manages lifecycle and cleanup on quit.

## Playback & Loopback (Optional)

- AudioSystem: One playback device; mixes sources and exposes a MixedFrame event (useful for AEC far‑end reference).
- AudioMixer: Hierarchical mixing tree with per‑mixer pipelines and volume/solo/mute.
- PlaybackAudioSource: Enqueue interleaved PCM and render additively with optional per‑source pipeline and meters.

## Extending EasyMic

- Implement IAudioWorker; choose AudioWriter for RT in‑place modifications or AudioReader for async work.
- Keep AudioWriter RT‑safe: no blocking, no allocations, bounded CPU.
- Use blueprints and keys for dynamic add/remove and to retrieve instances via GetProcessor<T>().

## Integrations

- SherpaOnnxUnity: Optional speech recognition and VAD processors (guarded by scripting defines). Requires the Sherpa package.
- APM (AEC/ANS/AGC): An add‑on package that integrates professional 3A processing. This is a paid extension; contact the author to purchase a license.

## Gotchas & Tips

- Always check permission before recording on mobile.
- Prefer mono input for speech workloads unless you specifically need multi‑channel.
- Add the downmixer early in the chain if you want to convert to mono before analysis/recognition.
- Use DefaultWorkers to avoid duplicating setup code across scenes.

