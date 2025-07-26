## 中文开发者请注意 📝
**如果您是中文开发者，强烈建议您阅读 [中文文档](README_zh-CN.md) 以获得更详细的说明和重要的开源协议信息。**

<p align="right">
  <a href="README_zh-CN.md">中文</a>
</p>

# Easy Mic for Unity 🎤

<p align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="Easy Mic Logo" width="200"/>
</p>

**Easy Mic** is a high-performance, low-latency audio recording plugin for Unity that revolutionizes audio capture and processing. It provides direct access to raw microphone data and introduces a powerful programmable audio processing pipeline, enabling developers to create sophisticated real-time audio workflows with ease.

## ✨ Core Features

*   **🎤 Ultra Low-Latency Recording**: Captures microphone audio with minimal delay using optimized native backend libraries, perfect for real-time applications and interactive experiences.
*   **🔊 Raw Audio Buffer Access**: Direct access to unprocessed audio data from the microphone, giving you complete control over audio manipulation and processing.
*   **⛓️ Programmable Processing Pipeline**: The heart of Easy Mic - dynamically build, modify, and optimize chains of audio processors. Add, remove, or reorder processors in real-time without interrupting the audio stream.
*   **💻 True Cross-Platform Support**: Unified API across Windows, macOS, Linux, Android, and iOS with platform-optimized native implementations.
*   **🧩 Rich Built-in Processor Library**: Comprehensive collection of pre-built processors for common audio tasks, ready to use out of the box.
*   **🔌 Extensible Architecture**: Designed for future expansion with custom processor support and third-party integrations.

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

### Professional Audio Enhancement 💎
For production-ready applications requiring studio-quality audio, consider the **EasyMic Audio Processing Module (APM)**:

*   **🚫 AEC (Acoustic Echo Cancellation)**: Eliminates acoustic echoes for crystal-clear voice communication
*   **🔇 ANS (Automatic Noise Suppression)**: Removes background noise while preserving speech quality  
*   **📊 AGC (Automatic Gain Control)**: Maintains consistent audio levels automatically

**Perfect for AI Digital Humans & Virtual Anchors**: Solves the critical echo problem in Unity-based conversational AI applications where system output interferes with microphone input.

📧 **Interested in APM?** Contact: [unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)  
🛒 **Third-party store coming soon** for easy purchase and licensing.

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

### Basic Recording Example
```csharp
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Core.Processors;
using UnityEngine;

public class SimpleRecorder : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private AudioCapturer _audioCapturer;
    private AudioClip _recordedClip;

    void Start()
    {
        // Initialize and check for available devices
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        
        if (devices.Length == 0)
        {
            Debug.LogError("No microphone devices found.");
            return;
        }

        // Start recording with optimal settings
        _recordingHandle = EasyMicAPI.StartRecording(
            devices[0].Name, 
            SampleRate.Hz48000,  // High quality sample rate
            Channel.Mono        // Mono for efficiency
        );

        if (!_recordingHandle.IsValid)
        {
            Debug.LogError("Failed to start recording.");
            return;
        }

        // Create and configure audio capturer
        _audioCapturer = new AudioCapturer(); 
        EasyMicAPI.AddProcessor(_recordingHandle, _audioCapturer);

        Debug.Log("🎙️ Recording started for 5 seconds...");
        
        // Auto-stop after 5 seconds
        Invoke(nameof(StopRecording), 5f);
    }

    void StopRecording()
    {
        if (!_recordingHandle.IsValid) return;

        // Stop recording and retrieve audio
        EasyMicAPI.StopRecording(_recordingHandle);
        _recordedClip = _audioCapturer.GetCapturedAudioClip();

        if (_recordedClip != null)
        {
            Debug.Log($"✅ Recording complete! Duration: {_recordedClip.length:F2}s");
            
            // Optional: Play the recorded audio
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
                audioSource.PlayOneShot(_recordedClip);
        }
        
        _recordingHandle = default;
    }

    void OnDestroy()
    {
        // Cleanup resources
        if (_recordingHandle.IsValid)
            EasyMicAPI.StopRecording(_recordingHandle);
    }
}
```

### Advanced Pipeline Example
```csharp
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Core.Processors;
using UnityEngine;

public class AdvancedAudioPipeline : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private VolumeGateFilter _noiseGate;
    private AudioDownmixer _downmixer;
    private AudioCapturer _capturer;

    void Start()
    {
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        
        // Start recording in stereo
        _recordingHandle = EasyMicAPI.StartRecording(
            devices[0].Name, 
            SampleRate.Hz44100, 
            Channel.Stereo
        );

        if (!_recordingHandle.IsValid) return;

        // Build processing pipeline
        _noiseGate = new VolumeGateFilter { Threshold = 0.01f };
        _downmixer = new AudioDownmixer();
        _capturer = new AudioCapturer();

        // Add processors in order
        EasyMicAPI.AddProcessor(_recordingHandle, _noiseGate);   // 1. Remove noise
        EasyMicAPI.AddProcessor(_recordingHandle, _downmixer);  // 2. Convert to mono
        EasyMicAPI.AddProcessor(_recordingHandle, _capturer);   // 3. Capture result

        Debug.Log("🔧 Advanced pipeline active with noise gate and downmixing");
    }
    
    // ... rest of implementation
}
```

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
- 💻 **[Sample Projects](Samples~/)**: Ready-to-run examples and tutorials  
- 🐛 **[Issue Tracker](https://github.com/EitanWong/com.eitan.easymic/issues)**: Bug reports and feature requests
- 💬 **[Discussions](https://github.com/EitanWong/com.eitan.easymic/discussions)**: Community support and tips

## 🤝 Contributing

We welcome contributions! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md).

## 📄 License

This project is licensed under the **GPLv3 License**. See [LICENSE.md](LICENSE.md) for details.

### Key License Points:
- ✅ **Free to use** for personal and commercial projects
- ✅ **Modify and distribute** under the same license terms  
- ✅ **Include in open-source** projects without restrictions
- ⚠️ **Copyleft requirement**: Derivative works must also be open-source under GPLv3

---

**Made with ❤️ by [Eitan](https://github.com/EitanWong)**

*Empowering developers to create amazing audio experiences in Unity*