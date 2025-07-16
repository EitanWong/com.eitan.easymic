<p align="right">
  <a href="README_zh-CN.md">中文</a>
</p>

# Easy Mic for Unity

<p align="center">
  <img src="EasyMic/Packages/com.eitan.easymic/Documentation~/images/easymic-logo.png" alt="Easy Mic Logo" width="200"/>
</p>

**Easy Mic** is a high-performance, low-latency audio recording plugin for Unity. It provides direct access to raw microphone data and introduces a programmable audio processing pipeline, allowing developers to chain built-in modules to create complex real-time audio workflows.

## ✨ Core Features

*   **🎤 Low-Latency Audio Recording**: Captures microphone audio with minimal delay by leveraging native backend libraries, ideal for real-time applications.
*   **🔊 Raw Audio Data**: Get direct access to the raw audio buffers from the microphone for full control over the sound data.
*   **⛓️ Programmable Processing Pipeline**: The core feature of Easy Mic. Build audio processing chains dynamically. You can add, remove, or reorder processors at any time.
*   **💻 Cross-Platform Support**: A unified API for major platforms including Windows, macOS, Linux, Android, and iOS.
*   **🧩 Built-in Processors**: Comes with a set of pre-built processors for common audio tasks.

## 🚀 Future Roadmap

Plan to integrate the following modules natively into Easy Mic. Contributions are welcome!

### Core Audio Processing
*   [ ] Acoustic Noise Suppression (ANS)
*   [ ] Automatic Gain Control (AGC)
*   [ ] Acoustic Echo Cancellation (AEC)
*   [ ] Voice Activity Detection (VAD)

### Advanced Voice AI
*   [ ] Wake-word Activation (based on sherpa-onnx)
*   [ ] Audio Separation (Source Separation)

### Microphone Array & Spatial Audio
*   [ ] Sound Source Localization (SSL)
*   [ ] Beamforming

### Platform Expansion
*   [ ] Support for Web Platforms (WebGL)

## 📦 Installation

To add Easy Mic to your Unity project:

1.  Open the Unity Package Manager (`Window > Package Manager`).
2.  Click the `+` button in the top-left corner and select "Add package from git URL...".
3.  Enter the repository URL: `https://github.com/EitanWong/com.eitan.easymic.git`
4.  Click "Add".

For more detailed documentation and examples, please refer to the [package's README file](EasyMic/Packages/com.eitan.easymic/README.md).

## 📄 License

This project is licensed under the [GPLv3 License](LICENSE.md). Please read it carefully. Your use of this software constitutes acceptance of its terms.
