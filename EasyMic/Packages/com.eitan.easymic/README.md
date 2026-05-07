## 中文开发者请注意 📝
**如果您是中文开发者，强烈建议您阅读 [中文文档](README_zh-CN.md) 以获得更详细的说明和重要的开源协议信息。**

<p align="right">
  <a href="README_zh-CN.md">中文</a>
</p>

# Easy Mic for Unity 🎤

<p align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="Easy Mic Logo" width="200"/>
</p>

**Easy Mic** is a high-performance, low-latency audio recording plugin for Unity. It provides direct access to raw microphone data and introduces a programmable audio processing pipeline, enabling developers to build real-time recording, playback, speech recognition, and voice interaction workflows with a clean Unity-first API.

> **Package scope:** this repository contains the open-source Easy Mic core package. It does **not** include AEC, AGC, or ANS. Those features belong to **EasyMic APM**, a separate paid extension package. If you need acoustic echo cancellation, automatic gain control, or automatic noise suppression, please contact the author separately.

## At a Glance

| Area | Included in this repository | Notes |
| --- | --- | --- |
| Microphone capture | Yes | Low-latency native capture with raw buffer access. |
| Processing pipeline | Yes | Compose runtime processors such as capture, downmix, gate, and loopback. |
| Unity components | Yes | Scene-friendly components for recording, playback, ASR orchestration, and TTS playback. |
| Sherpa ONNX integration | Optional | Requires [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity). |
| AEC / AGC / ANS | No | Provided only by the paid EasyMic APM extension package. |

## ✨ Core Features

*   **🎤 Ultra Low-Latency Recording**: Captures microphone audio with minimal delay using optimized native backend libraries, perfect for real-time applications and interactive experiences.
*   **🔊 Raw Audio Buffer Access**: Direct access to unprocessed audio data from the microphone, giving you complete control over audio manipulation and processing.
*   **⛓️ Programmable Processing Pipeline**: The heart of Easy Mic - dynamically build, modify, and optimize chains of audio processors. Add, remove, or reorder processors in real-time without interrupting the audio stream.
*   **💻 True Cross-Platform Support**: Unified API across Windows, macOS, Linux, Android, and iOS with platform-optimized native implementations.
*   **🧩 Practical Built-in Processor Library**: Includes common processors such as capture, downmix, volume gate, and loopback monitoring.
*   **🔌 Extensible Architecture**: Designed for custom processors, third-party integrations, and optional add-on packages.

## 🚀 The Audio Processing Pipeline

Easy Mic's revolutionary approach centers around its flexible, programmable audio pipeline. When recording is active, audio data flows through a customizable chain of processors you define:

```
🎙️ Mic Input → [Processor A] → [Processor B] → [Processor C] → 🔊 Final Output
```

This modular architecture allows you to:
- **Mix and match** processors to create custom audio workflows
- **Real-time modification** of the processing chain during recording
- **Performance optimization** by only using necessary processors
- **Easy debugging** by isolating specific processing stages

## 🛠️ Built-in Audio Processors

Easy Mic ships with a comprehensive suite of audio processors:

### Core Processors
*   **📼 `AudioCapturer`**: High-performance audio capture to memory buffers or direct file output with multiple format support.
*   **🔄 `AudioDownmixer`**: Intelligent multi-channel to mono conversion with configurable mixing algorithms.
*   **🔇 `VolumeGateFilter`**: Advanced noise gate with customizable threshold, attack, and release parameters.
*   **🔁 `LoopbackPlayer`**: Real-time audio loopback for monitoring and testing applications.

### AI Integration
*   **🗣️ `SherpaRealtimeSpeechRecognizer`**: Cutting-edge real-time speech-to-text using the Sherpa-ONNX engine. **Requires:** [com.eitan.sherpa-onnx-unity](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity)
*   **👂 `SherpaKeywordDetector`**: Keyword/wake-word detector for creating voice activation features. **Requires:** [com.eitan.sherpa-onnx-unity](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity)

### Optional Paid Audio Enhancement 💎

For production applications that require voice communication cleanup, consider **EasyMic Audio Processing Module (APM)**.

**Important:** EasyMic APM is a separate paid extension package. This open-source repository does **not** contain AEC, AGC, or ANS implementation code, binaries, samples, or licenses.

*   **🚫 AEC (Acoustic Echo Cancellation)**: Eliminates acoustic echoes for crystal-clear voice communication
*   **🔇 ANS (Automatic Noise Suppression)**: Removes background noise while preserving speech quality  
*   **📊 AGC (Automatic Gain Control)**: Maintains consistent audio levels automatically

**Recommended for AI Digital Humans & Virtual Anchors**: Solves the common echo problem in Unity-based conversational AI applications where system output is captured again by the microphone.

💰 EasyMic APM is a paid add-on. Please contact the author to purchase a license.<br>
📧 Contact: [unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)<br>
🛒 A third-party store is coming soon for convenient purchase and licensing.

## 📦 Installation

### Method 1: Unity Package Manager (Recommended)
1. Open Unity Package Manager (`Window > Package Manager`)
2. Click the `+` button → `Add package from git URL...`
3. Enter: `https://github.com/EitanWong/com.eitan.easymic.git#upm`
4. Click `Add`

### Method 2: Manual Installation
1. Download the latest release from [GitHub Releases](https://github.com/EitanWong/com.eitan.easymic/releases)
2. Extract and place in your project's `Packages` folder
3. Unity will automatically detect and import the package

## ▶️ Quick Start Guide

### Component Workflow (Mono)

If you prefer a scene-component workflow, use the built-in Unity components under `Runtime/Unity/Components`:

- `EasyMicrophone` for microphone capture and recording output
- `VoiceMicrophone` for ASR + keyword/turn-detection orchestration
- `PlaybackAudioSourceBehaviour` for low-latency clip/stream playback
- `SpeechSynthesizer` for queued TTS playback

Detailed guide: [Documentation~/en/components.md](Documentation~/en/components.md)  
中文说明: [Documentation~/zh-CN/components.md](Documentation~/zh-CN/components.md)

### Basic Recording Example
```csharp
using Eitan.EasyMic.Runtime;
using UnityEngine;

public class SimpleRecorder : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioWorkerBlueprint _bpCapture;

    void Start()
    {
        if (!PermissionUtils.HasPermission()) return;
        EasyMicAPI.Refresh();
        var devs = EasyMicAPI.Devices;
        if (devs.Length == 0) return;

        _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture");
        _handle = EasyMicAPI.StartRecording(devs[0].Name, SampleRate.Hz48000, devs[0].GetDeviceChannel(), new[]{ _bpCapture });
        Invoke(nameof(StopRecording), 5f);
    }

    void StopRecording()
    {
        if (!_handle.IsValid) return;
        EasyMicAPI.StopRecording(_handle);
        var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_handle, _bpCapture);
        var clip = capturer?.GetCapturedAudioClip();
        if (clip != null) GetComponent<AudioSource>()?.PlayOneShot(clip);
        _handle = default;
    }
}
```

### Advanced Pipeline Example
```csharp
using Eitan.EasyMic.Runtime;
using UnityEngine;

public class AdvancedAudioPipeline : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioWorkerBlueprint _bpGate, _bpDownmix, _bpCapture;

    void Start()
    {
        if (!PermissionUtils.HasPermission()) return;
        EasyMicAPI.Refresh();
        var d = EasyMicAPI.Devices;
        if (d.Length == 0) return;

        _bpGate    = new AudioWorkerBlueprint(() => new VolumeGateFilter { ThresholdDb = -35 }, key: "gate");
        _bpDownmix = new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmix");
        _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture");

        _handle = EasyMicAPI.StartRecording(d[0].Name, SampleRate.Hz44100, d[0].GetDeviceChannel(),
            new[]{ _bpGate, _bpDownmix, _bpCapture });
    }
}
```

## 🧪 Sample Projects Overview

EasyMic includes ready-to-run samples under `Samples~/` so developers can quickly validate workflows.

| Sample | Purpose | Best For |
| --- | --- | --- |
| `Recording Example` | Basic microphone recording flow and WAV persistence. | First-time integration and device/permission checks. |
| `Playback Example` | Core playback flow using EasyMic playback stack. | Verifying low-latency output and playback controls. |
| `AudioPlayback API Example` | Programmatic playback API usage and queue-style audio feeding. | Building custom runtime audio playback logic. |
| `SherpaONNXUnity ASR Example` | Real-time speech recognition pipeline with Sherpa ONNX + EasyMic input. | Speech-to-text applications and voice command prototypes. |
| `SherpaONNXUnity KWS Example` | Keyword spotting / wake-word workflow with Sherpa ONNX. | Wake-word activation and always-listening assistants. |
| `AIChat Example` | End-to-end AI voice chat sample (ASR + LLM + TTS + playback orchestration). | **Direct starting point for digital human / AI avatar apps.** |

### AIChat Sample Notes

- The `AIChat Example` is designed as a production-oriented reference pipeline for conversational digital humans.
- It demonstrates end-to-end flow from microphone input to speech recognition, LLM response generation, and speech synthesis playback.
- Install [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity) before importing/running this sample.
- Echo cancellation, gain control, and noise suppression are not included in this repository. For those capabilities, use the separate paid EasyMic APM extension.

## 🎯 Use Cases

### 🤖 AI & Virtual Characters
- **Digital Human Conversations**: Crystal-clear voice interaction without echo interference
- **Voice-Controlled NPCs**: Real-time speech recognition for game characters
- **Virtual Streamers**: Professional-quality voice capture for virtual influencers

### 🎮 Gaming Applications  
- **Voice Chat Systems**: Low-latency communication for multiplayer games
- **Voice Commands**: Responsive voice control for game mechanics
- **Audio Recording**: In-game voice message and replay systems

### 📱 Interactive Applications
- **Voice Assistants**: Building custom voice AI applications
- **Language Learning**: Pronunciation practice and feedback systems
- **Audio Production**: Real-time audio processing and effects

## 🔧 System Requirements

- **Unity**: 2021.3 LTS or later
- **Platforms**: Windows, macOS, Linux, Android, iOS
- **Microphone**: Any system-recognized audio input device
- **Memory**: Minimal overhead, efficient native implementation

## 📚 Documentation & Support

- 📖 **[Full Documentation](Documentation~/README.md)**: Comprehensive guides and API reference
- 🧩 **[Mono Components Guide](Documentation~/en/components.md)**: Component usage for microphone, ASR, playback, and TTS
- 💻 **[Sample Projects](Samples~/)**: Ready-to-run examples and tutorials  
- 💎 **EasyMic APM**: Paid extension for AEC, AGC, and ANS. Contact [unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)
- 🐛 **[Issue Tracker](https://github.com/EitanWong/com.eitan.easymic/issues)**: Bug reports and feature requests
- 💬 **[Discussions](https://github.com/EitanWong/com.eitan.easymic/discussions)**: Community support and tips

## 🤝 Contributing

We welcome contributions! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md).

## 📄 License

This project is licensed under the **GPLv3 License**. See [LICENSE.md](LICENSE.md) for details.

EasyMic APM is not part of this repository and is distributed separately as a paid extension under its own commercial licensing terms.

### Key License Points:
- ✅ **Free to use** for personal and commercial projects
- ✅ **Modify and distribute** under the same license terms  
- ✅ **Include in open-source** projects without restrictions
- ⚠️ **Copyleft requirement**: Derivative works must also be open-source under GPLv3

---

**Made with ❤️ by [Eitan](https://github.com/EitanWong)**

*Empowering developers to create amazing audio experiences in Unity*
