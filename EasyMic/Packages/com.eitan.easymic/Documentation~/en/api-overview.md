# API Overview

This page lists the developer-facing APIs most integrations use. It is not a private implementation reference.

## Recording

| API | Purpose |
|---|---|
| `EasyMicAPI.IsAvailable` | Whether the microphone system can initialize on the current platform. |
| `EasyMicAPI.UnavailabilityReason` | Initialization failure reason, when unavailable. |
| `EasyMicAPI.Refresh()` | Refresh microphone device list. |
| `EasyMicAPI.Devices` | Current `MicDevice[]` snapshot. |
| `EasyMicAPI.Default` | Default or first available input device. |
| `EasyMicAPI.StartRecording(...)` | Start a recording session. Overloads support device/name/default, workers, and explicit latency profile. |
| `EasyMicAPI.StopRecording(handle)` | Stop and dispose one recording session. |
| `EasyMicAPI.StopAllRecordings()` | Stop all sessions. |
| `EasyMicAPI.AddProcessor(handle, blueprint)` | Add a processor blueprint to an active session. |
| `EasyMicAPI.RemoveProcessor(handle, blueprint)` | Remove a processor from an active session. |
| `EasyMicAPI.GetProcessor<T>(handle, blueprint)` | Get the active session instance created from a blueprint. |
| `EasyMicAPI.GetRecordingInfo(handle)` | Read device, format, callback counters, and telemetry. |
| `EasyMicAPI.GetRecordingPipelineSnapshots()` | Read active capture topology snapshots for diagnostics tools. |

## Playback

| API | Purpose |
|---|---|
| `AudioPlayback.DefaultLatencyProfile` | `Balanced` on Android player, otherwise `LowLatency`. |
| `AudioPlayback.PlayClip(...)` | Create a handle and play an `AudioClip`. |
| `AudioPlayback.CreateStream(...)` | Create a streaming `PlaybackHandle`. |
| `PlaybackHandle.Enqueue(...)` | Enqueue PCM or throw through internal path. |
| `PlaybackHandle.TryEnqueue(...)` | Enqueue PCM and receive `EasyMicEnqueueResult`. |
| `PlaybackHandle.CompleteStream()` | Mark a stream as ending after buffered audio drains. |
| `PlaybackHandle.BufferedSeconds` | Approximate source buffer depth. |
| `AudioSystem.Instance` | Playback system singleton. |
| `AudioSystem.Telemetry` | Playback transport telemetry. |
| `AudioSystem.PipelineSnapshot` | Playback topology and mixer/source snapshot. |
| `AudioSystem.LatencyProfile` | Set before `Start()`. Throws while running. |

## Components

| Component | Namespace | Purpose |
|---|---|---|
| `EasyMicrophone` | `Eitan.EasyMic.Runtime.Mono` | Scene recording wrapper with device options, events, temporary WAV capture, and latest clip access. |
| `PlaybackAudioSourceBehaviour` | `Eitan.EasyMic.Runtime.Mono.Components` | Scene playback wrapper for clip and streamed PCM playback. |
| `VoiceMicrophone` | Sherpa integration namespace | ASR-oriented microphone component when SherpaONNXUnity integration is installed. |
| `SpeechSynthesizer` | Sherpa integration namespace | TTS component that can stream to EasyMic playback when integration dependencies are installed. |

## Data Types

| Type | Purpose |
|---|---|
| `RecordingHandle` | Lightweight recording session id. |
| `PlaybackHandle` | Lightweight playback session id with playback controls. |
| `MicDevice` | Input device identity and format support. |
| `SampleRate` | Supported sample-rate enum. |
| `Channel` | Channel count enum. |
| `EasyMicLatencyProfile` | Latency/buffering policy. |
| `RecordingInfo` | Capture device, format, counters, telemetry, and latency stats. |
| `EasyMicTelemetrySnapshot` | Callback, transport, worker, queue, and exception counters. |
| `EasyMicRealtimeStats` | Smaller realtime-focused telemetry view. |
| `EasyMicLatencyStats` | Queue depth and format data with millisecond conversion. |
| `EasyMicEnqueueResult` | Playback enqueue result and written sample count. |

## Processors

| Type | Purpose |
|---|---|
| `AudioWorkerBlueprint` | Factory plus stable key used to create per-session processor instances. |
| `IAudioWorker` | Base processor contract. |
| `AudioWriter` | In-place processor base class. |
| `AudioReader` | Async reader base class with worker-thread `OnAudioReadAsync`. |
| `IAudioTransportProcessor` | Marker for transport-safe processors. |
| `IMainThreadAudioProcessor` | Marker for main-thread-only processors. |
| `IRealtimeForbiddenProcessor` | Marker for processors not allowed in realtime/transport paths. |

See [Processor Contracts](processors.md) before writing custom processors.
