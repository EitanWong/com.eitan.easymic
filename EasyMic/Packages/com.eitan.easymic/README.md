<div align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="Easy Mic Icon" width="112" height="112">

  # Easy Mic for Unity

  **External audio capture, playback, processing, and SherpaONNXUnity integration for Unity**

  <p>
    <img src="https://img.shields.io/badge/version-0.1.3--exp.3-1f6feb.svg" alt="Version 0.1.3-exp.3">
    <img src="https://img.shields.io/badge/Unity-2021.3%2B-222222.svg" alt="Unity 2021.3+">
    <img src="https://img.shields.io/badge/License-GPLv3-2ea043.svg" alt="GPLv3 License">
    <img src="https://img.shields.io/badge/Platforms-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android%20%7C%20iOS-6e7681.svg" alt="Supported platforms">
  </p>

  <p>
    <strong>English</strong> | <a href="README_zh-CN.md">中文</a>
  </p>

  <p>
    <strong>Release:</strong> <code>0.1.3-exp.3</code> · <span>2026-05-12</span>
  </p>
</div>

---

> **Package scope:** this package contains the open-source Easy Mic core package and its optional EasyMic-side SherpaONNXUnity integration layer. It does **not** include AEC, AGC, or ANS. Those capabilities are provided by **EasyMic APM**, a separate paid extension package.

## What Easy Mic Provides

| Area | Included | Notes |
| --- | --- | --- |
| Microphone capture | Yes | Miniaudio-backed low-latency device capture with raw PCM access. |
| External playback | Yes | Streaming and clip playback through EasyMic's playback transport. |
| Processing pipeline | Yes | Worker-based processing with clear realtime/threading contracts. |
| Diagnostics | Yes | Telemetry for callback health, queue depth, underruns, overflows, and dropped frames. |
| SherpaONNXUnity integration | Optional | Requires [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm). |
| AEC / AGC / ANS | No | Available only in the separate paid EasyMic APM extension. |

## Core Capabilities

- **Low-latency capture:** microphone input with device selection, latency profiles, transport buffering, and raw PCM access.
- **External playback:** clip and streaming playback APIs with queueing, underrun handling, and playback telemetry.
- **Composable processors:** capture, downmix, gates, playback, and integration workers built around explicit worker contracts.
- **Realtime-safe architecture:** device callbacks stay focused on transport work while heavier processing runs outside the hot path.
- **Editor-first workflow:** inspectors, project settings, diagnostics windows, recipes, localized UI, and component icons.
- **Optional Sherpa integration:** EasyMic can feed Sherpa native components without duplicate microphone capture.

## Installation

### Unity Package Manager

1. Open `Window > Package Manager`.
2. Click `+` -> `Add package from git URL...`.
3. Enter:

```text
https://github.com/EitanWong/com.eitan.easymic.git#upm
```

4. Click `Add`.

### Optional SherpaONNXUnity Dependency

Install SherpaONNXUnity only when you need ASR, keyword spotting, VAD, audio tagging, speaker diarization, speech enhancement, source separation, spoken language identification, or TTS workflows:

```text
https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm
```

After the dependency is available, EasyMic exposes the optional `Eitan.EasyMic.Integration.SherpaONNXUnity` integration layer.

## Quick Start

```csharp
using Eitan.EasyMic.Runtime;
using UnityEngine;

public sealed class SimpleRecorder : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioWorkerBlueprint _capture;

    private void Start()
    {
        if (!PermissionUtils.HasPermission())
        {
            return;
        }

        EasyMicAPI.Refresh();
        if (!EasyMicAPI.IsAvailable || EasyMicAPI.Devices.Length == 0)
        {
            return;
        }

        _capture = new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture");
        _handle = EasyMicAPI.StartRecording(
            EasyMicAPI.Devices[0].Name,
            SampleRate.Hz48000,
            EasyMicAPI.Devices[0].GetDeviceChannel(),
            new[] { _capture });
    }

    private void OnDisable()
    {
        if (!_handle.IsValid)
        {
            return;
        }

        EasyMicAPI.StopRecording(_handle);
        _handle = default;
    }
}
```

## SherpaONNXUnity Integration

EasyMic now provides a cleaner optional bridge for SherpaONNXUnity while keeping both packages independently usable.

### Recommended Sherpa Component Path

Use `EasyMicSherpaAudioInputSource` when you want Sherpa native MonoBehaviour components to consume EasyMic microphone capture:

- Add `GameObject > SherpaONNX > Audio > EasyMic Audio Input Source`.
- Add Sherpa components from the input source Inspector with `Add Sherpa Component`.
- The source starts one EasyMic recording session and feeds bound Sherpa components with mono PCM chunks.
- It avoids duplicate microphone capture and keeps `ChunkReady` dispatch on the Unity main thread.

Supported EasyMic-assisted Sherpa recipes include:

- Realtime speech recognition with EasyMic input.
- Offline speech recognition with EasyMic VAD.
- Keyword spotting with EasyMic input.
- Voice activity detection with EasyMic input.
- Audio tagging with EasyMic input.
- EasyMic playback speech synthesizer.

### EasyMic Pipeline Path

Use EasyMic workers/facades when you want Sherpa capability inside an EasyMic pipeline or service workflow:

- `SherpaRealtimeSpeechRecognizer`
- `SherpaOfflineSpeechRecognizer`
- `SherpaKeywordDetector`
- `SherpaVoiceFilter`
- `SherpaAudioTagger`
- `SherpaSpeechEnhancementFilter`
- `SherpaSourceSeparator`
- `SherpaSpeakerDiarizer`
- `SherpaSpokenLanguageIdentifier`

The integration also includes a model service registry for EasyMic-side worker/facade reuse. Sherpa native components still own their own models because this package does not modify SherpaONNXUnity.

### Editor UX

- `Project Settings > Easy Mic > Integrations > SherpaONNXUnity` shows dependency and integration status.
- If SherpaONNXUnity is missing, the settings page can install it from the package git URL.
- `Window > Easy Mic > SherpaONNXUnity Diagnostics` provides integration diagnostics.
- EasyMic Sherpa components and recipe-created Sherpa components have dedicated editor icons.
- The EasyMic audio input inspector warns about duplicate microphone inputs and exposes runtime counters.

## Samples

Import samples from Unity Package Manager after installing the package.

| Sample | Purpose | Best For |
| --- | --- | --- |
| `Recording Example` | Basic microphone recording and WAV persistence. | First-time setup, device checks, permissions. |
| `Playback Example` | Playback through EasyMic's playback stack. | Low-latency output validation. |
| `AudioPlayback API Example` | Programmatic playback and queue-style audio feeding. | Custom runtime playback systems. |
| `SherpaONNXUnity ASR Example` | Realtime speech recognition with SherpaONNXUnity and EasyMic. | Speech-to-text and voice command prototypes. |
| `SherpaONNXUnity KWS Example` | Keyword/wake-word workflow. | Wake-word activation and always-listening assistants. |
| `SherpaONNXUnity AudioTagging Input Example` | EasyMic input source feeding Sherpa audio tagging. | No-duplicate-capture Sherpa component integration. |
| `AIChat Example` | ASR + LLM + TTS + playback orchestration. | Digital human and AI voice assistant prototypes. |

## Documentation

- [Documentation Index](Documentation~/README.md)
- [Overview](Documentation~/en/index.md)
- [Getting Started](Documentation~/en/getting-started.md)
- [Recording](Documentation~/en/recording.md)
- [Playback](Documentation~/en/playback.md)
- [Architecture](Documentation~/en/architecture.md)
- [Latency Profiles](Documentation~/en/latency-profiles.md)
- [Diagnostics](Documentation~/en/diagnostics.md)
- [Processor Contracts](Documentation~/en/processors.md)
- [Platform Notes](Documentation~/en/platform-notes.md)
- [Troubleshooting](Documentation~/en/troubleshooting.md)
- [API Overview](Documentation~/en/api-overview.md)

Chinese documentation:

- [中文文档索引](Documentation~/zh-CN/index.md)
- [快速入门](Documentation~/zh-CN/getting-started.md)
- [SherpaONNXUnity 使用指南](Documentation~/zh-CN/sherpa-onnx-unity-usage.md)
- [SherpaONNXUnity 集成计划](Documentation~/zh-CN/sherpa-onnx-unity-integration-plan.md)

## Common Use Cases

- AI digital humans and virtual assistants.
- Voice command and wake-word workflows.
- Realtime recording and playback tools.
- In-game voice notes and communication features.
- Audio diagnostics and platform capture validation.
- SherpaONNXUnity ASR/KWS/VAD/audio tagging workflows with one EasyMic capture session.

## Requirements

- Unity 2021.3 LTS or newer.
- .NET Standard 2.1 compatible Unity runtime.
- Microphone permission on target platforms.
- Optional: [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm) for Sherpa workflows.

## EasyMic APM

EasyMic APM is a separate paid extension for production voice cleanup:

- AEC: acoustic echo cancellation.
- AGC: automatic gain control.
- ANS: automatic noise suppression.

[Bilibili demo video: echo, volume, and noise handling for Unity digital human voice interaction](https://www.bilibili.com/video/BV18hE46rEzw/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855)

This repository does not include EasyMic APM implementation code, binaries, samples, or licenses.

Contact: [unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)

## License

Easy Mic is licensed under GPLv3. See [LICENSE.md](LICENSE.md).

EasyMic APM is not part of this repository and is distributed separately under its own commercial licensing terms.

---

<div align="center">
  <strong>Made by <a href="https://github.com/EitanWong">Eitan</a></strong>
</div>
