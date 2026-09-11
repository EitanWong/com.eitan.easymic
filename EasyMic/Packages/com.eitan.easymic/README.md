<div align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="EasyMic" width="96" height="96">

# EasyMic for Unity

**Low-latency microphone capture, playback, and extensible audio pipelines**

![Version](https://img.shields.io/badge/version-0.1.3--exp.5-175cd3)
![Unity](https://img.shields.io/badge/Unity-2021.3%2B-101828)
![License](https://img.shields.io/badge/license-GPL--3.0-067647)

**English** | [中文](README_zh-CN.md)
</div>

![EasyMic runtime architecture](Documentation~/images/easymic-pipeline.svg)

EasyMic keeps native audio callbacks small and moves processing to explicit worker pipelines. It supports device capture, clip or stream playback, raw PCM access, telemetry, and optional SherpaONNXUnity integration.

## Package Scope

| Capability | Included |
| --- | --- |
| Microphone capture | miniaudio-backed device selection and low-latency PCM transport |
| Playback | Clip and streaming playback through the EasyMic mixer |
| Processing | Composable workers, resampling, channel conversion, and gates |
| Diagnostics | Queue depth, delay, underrun, overflow, and dropped-frame telemetry |
| SherpaONNXUnity | Optional single-capture bridge for speech components |
| AEC / ANS / AGC | Separate paid [EasyMic APM](https://github.com/EitanWong/com.eitan.easymic.apm) extension |

## Installation

In Unity, open `Window > Package Manager`, select `Add package from git URL...`, and enter:

```text
https://github.com/EitanWong/com.eitan.easymic.git#upm
```

For Sherpa workflows, also install:

```text
https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm
```

## Quick Start

Add `Easy Microphone` from `Component > Audio > EasyMic > Input > Easy Microphone`, then reference it from your script:

```csharp
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono;
using UnityEngine;

public sealed class Recorder : MonoBehaviour
{
    [SerializeField] private EasyMicrophone microphone;

    private void Start()
    {
        if (!PermissionUtils.HasPermission())
            return;

        microphone.Init();
        microphone.StartRecording();
    }

    private void OnDisable()
    {
        microphone.StopRecording();
    }
}
```

For cancellable playback or consistent latency telemetry, use `PlaybackAudioSourceBehaviour` instead of Unity `AudioSource`.

## Processing Model

- Device callbacks only move interleaved float PCM through bounded buffers.
- `AudioPipeline` workers perform processing away from the realtime callback.
- `AudioContext` carries sample rate, channels, frame length, and capture-delay estimates.
- Playback exposes the complete mixed render reference required by EasyMic APM.
- Hot-path telemetry is lock-free or bounded; allocations stay outside callbacks.

## SherpaONNXUnity

Use `EasyMicSherpaAudioInputSource` when Sherpa components should share one EasyMic recording session. It feeds mono PCM to ASR, KWS, VAD, audio tagging, and related components without opening the microphone twice.

Editor entry points:

- `GameObject > SherpaONNX > Audio > EasyMic Audio Input Source`
- `Project Settings > Easy Mic > Integrations > SherpaONNXUnity`
- `Window > Easy Mic > SherpaONNXUnity Diagnostics`

## Samples

Import samples from the Package Manager `Samples` tab.

| Sample | Focus |
| --- | --- |
| `Recording Example` | Device capture and WAV output |
| `Playback Example` | EasyMic clip playback |
| `AudioPlayback API Example` | Programmatic streaming playback |
| `SherpaONNXUnity ASR / KWS / AudioTagging` | Shared microphone input for Sherpa |
| `AIChat Example` | ASR, LLM, TTS, and playback orchestration |

## Compatibility

- Unity 2021.3 LTS or newer
- Windows, macOS, Linux, Android, and iOS
- .NET Standard 2.1 Unity runtime
- Microphone permission configured for the target platform

## EasyMic APM

[EasyMic APM](https://github.com/EitanWong/com.eitan.easymic.apm) is a separately licensed paid extension that adds AEC, ANS, and AGC. The EasyMic base package does not include its source, native binaries, samples, or license.

<a href="https://www.bilibili.com/video/BV18hE46rEzw/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855">
  <img src="https://i0.hdslb.com/bfs/archive/bdfa03e4edeb74c9ef6ea4bce25c04fbbb83ab15.jpg" alt="EasyMic APM demo: echo cancellation, stable volume, and noise suppression" width="640">
</a>

[Watch the Bilibili demo](https://www.bilibili.com/video/BV18hE46rEzw/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855) · Purchase and private delivery: [unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)

## Documentation

- [Documentation index](Documentation~/README.md)
- [Getting started](Documentation~/en/getting-started.md)
- [Recording](Documentation~/en/recording.md)
- [Playback](Documentation~/en/playback.md)
- [Architecture](Documentation~/en/architecture.md)
- [Processor contracts](Documentation~/en/processors.md)
- [Diagnostics](Documentation~/en/diagnostics.md)
- [Platform notes](Documentation~/en/platform-notes.md)

## License

EasyMic is licensed under [GPL-3.0-only](LICENSE.md). Third-party notices are listed in [THIRD PARTY NOTICES](THIRD%20PARTY%20NOTICES.md).
