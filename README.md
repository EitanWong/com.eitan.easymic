<div align="center">
  <img src="./EasyMic/Packages/com.eitan.easymic/Documentation~/images/easymic-logo.png" alt="Easy Mic Icon" width="112" height="112">
  
  # Easy Mic for Unity
  
  **External audio capture, playback, and processing for Unity**
  
  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/package.json"><img src="https://img.shields.io/badge/version-0.1.3--exp.3-1f6feb.svg" alt="Version 0.1.3-exp.3"></a>
    <a href="https://unity3d.com/get-unity/download"><img src="https://img.shields.io/badge/Unity-2021.3%2B-222222.svg" alt="Unity 2021.3+"></a>
    <a href="LICENSE.md"><img src="https://img.shields.io/badge/License-GPLv3-2ea043.svg" alt="GPLv3 License"></a>
    <a href="#system-requirements"><img src="https://img.shields.io/badge/Platforms-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android%20%7C%20iOS-6e7681.svg" alt="Supported platforms"></a>
  </p>
  
  <p align="center">
    <a href="README_zh-CN.md">🇨🇳 中文版</a> | 
    <strong>🇺🇸 English</strong>
  </p>

  <p align="center">
    <strong>Release:</strong> <code>0.1.3-exp.3</code> · <span>2026-05-12</span>
  </p>

  <p align="center">
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/getting-started.md"><img src="https://img.shields.io/badge/Read_the_docs-Getting_Started-1f6feb.svg?style=for-the-badge" alt="Read the docs"></a>
    <a href="#-sample-projects-overview"><img src="https://img.shields.io/badge/Open_samples-Unity_Package-2ea043.svg?style=for-the-badge" alt="Open samples"></a>
    <a href="EasyMic/Packages/com.eitan.easymic/CHANGELOG.md"><img src="https://img.shields.io/badge/Changelog-0.1.3--exp.3-6e7681.svg?style=for-the-badge" alt="View changelog"></a>
  </p>
  <p align="center">
    <em>Built for voice interaction, digital humans, custom playback, and realtime audio diagnostics.</em>
  </p>
</div>

---

> **Repository scope:** this repository contains the open-source Easy Mic core package. It does **not** include AEC, AGC, or ANS. Those features are provided by **EasyMic APM**, a separate paid extension package. If you need acoustic echo cancellation, automatic gain control, or automatic noise suppression, please contact the author separately.

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        <strong>Included</strong><br>
        Microphone capture<br>
        Raw audio buffers
      </td>
      <td align="center" width="25%">
        <strong>Included</strong><br>
        Runtime audio pipeline<br>
        Built-in processors
      </td>
      <td align="center" width="25%">
        <strong>Optional</strong><br>
        Sherpa ONNX integration<br>
        ASR / KWS workflows
      </td>
      <td align="center" width="25%">
        <strong>Paid Add-on</strong><br>
        EasyMic APM<br>
        AEC / AGC / ANS
      </td>
    </tr>
  </table>
</div>

---

<div align="center">
  <h2>Audio I/O for interactive Unity applications</h2>
  
  <p><strong>Easy Mic</strong> provides miniaudio-backed microphone capture, external playback, processor pipelines, latency profiles, and diagnostics outside Unity's built-in audio path.</p>
</div>

<table align="center">
  <tr>
    <td align="center" width="25%">
      <br>
      <strong>Capture</strong><br>
      <em>Low-latency microphone input with device selection and transport buffering.</em>
      <br><br>
    </td>
    <td align="center" width="25%">
      <br>
      <strong>Playback</strong><br>
      <em>Streaming and clip playback through EasyMic's external audio system.</em>
      <br><br>
    </td>
    <td align="center" width="25%">
      <br>
      <strong>Processing</strong><br>
      <em>Composable audio processors with explicit realtime and threading contracts.</em>
      <br><br>
    </td>
    <td align="center" width="25%">
      <br>
      <strong>Diagnostics</strong><br>
      <em>Telemetry for underruns, overflows, callback health, queue depth, and workers.</em>
      <br><br>
    </td>
  </tr>
</table>

<div align="center">
  <p><em>Designed for voice interaction, AI characters, custom audio tools, and realtime audio workflows.</em></p>
</div>

---

## 🎬 See It In Action

<div align="center">
  <a href="https://www.bilibili.com/video/BV18hE46rEzw/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855">
    <img src="https://img.shields.io/badge/🎥_Watch_Demo-Bilibili-ff69b4.svg?style=for-the-badge" alt="Watch Demo">
  </a>
  
  <p><strong>Unity Digital Human Microphone Recording Plugin</strong><br>
  <em>Easy Mic core + optional EasyMic APM workflow for conversational AI</em></p>
  
  <p>This video demonstrates a conversational AI audio workflow. Echo cancellation, gain control, and noise suppression are not included in this repository; they require the separate paid EasyMic APM extension.</p>
</div>

---

## ✨ Key Features

<div align="center">
  <table>
    <tr>
      <td align="left" width="50%">
        <h3>Low-latency recording</h3>
        <ul align="left">
          <li>miniaudio-backed device access</li>
          <li>capture transport worker and unmanaged ring buffer</li>
          <li>diagnostics for overflows, drops, and callback health</li>
        </ul>
      </td>
      <td align="left" width="50%">
        <h3>External playback</h3>
        <ul align="left">
          <li>stream and clip playback APIs</li>
          <li>render worker with watermark scheduling</li>
          <li>underrun zero-fill and telemetry</li>
        </ul>
      </td>
    </tr>
    <tr>
      <td align="left" width="50%">
        <h3>Processor contracts</h3>
        <ul align="left">
          <li>transport-safe processor marker interfaces</li>
          <li>main-thread and realtime-forbidden separation</li>
          <li>clear Unity API threading guidance</li>
        </ul>
      </td>
      <td align="left" width="50%">
        <h3>Developer visibility</h3>
        <ul align="left">
          <li>latency profiles for different stability targets</li>
          <li>pipeline visualization and project settings tooling</li>
          <li>bilingual documentation and practical samples</li>
        </ul>
      </td>
    </tr>
  </table>
</div>

---

## 💎 EasyMic APM Extension - Paid Professional 3A Audio Processing

<div align="center">
  <img src="https://img.shields.io/badge/🔊_Solve_AI_Conversation_Interruption-Professional_Solution-gold.svg?style=for-the-badge" alt="APM Solution">
  
  <p>For developers working on <strong>Unity AI digital human projects</strong>, <strong>EasyMic APM (Audio Processing Module)</strong> is available as a <strong>separate paid extension package</strong>.</p>
  <p><strong>This repository does not contain AEC, AGC, or ANS implementation code, binaries, samples, or licenses.</strong></p>
</div>

<div align="center">
  <table>
    <tr>
      <td align="center" width="33%">
        🔇<br>
        <strong>AEC</strong><br>
        <em>Acoustic Echo Cancellation</em><br>
        Eliminates echo and feedback
      </td>
      <td align="center" width="33%">
        📢<br>
        <strong>AGC</strong><br>
        <em>Automatic Gain Control</em><br>
        Maintains consistent audio levels
      </td>
      <td align="center" width="33%">
        🎯<br>
        <strong>ANS</strong><br>
        <em>Acoustic Noise Suppression</em><br>
        Reduces background noise
      </td>
    </tr>
  </table>
</div>

<div align="center">
  <p><strong>📧 Contact:</strong> <a href="mailto:unease-equity-5c@icloud.com">unease-equity-5c@icloud.com</a> | <strong>💬 Bilibili:</strong> Send private message</p>
  <p><em>Please contact separately if your project needs AEC, AGC, or ANS.</em></p>
  
  <a href="https://www.bilibili.com/video/BV18hE46rEzw/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855">
    <img src="https://img.shields.io/badge/🎥_Demo_Video-Watch_on_Bilibili-ff69b4.svg" alt="Demo Video">
  </a>
</div>

---

## 🚀 Quick Start

<div align="left">
  <h3>📦 Installation</h3>
    <ol align="left">
      <li>Open Unity Package Manager</li>
      <li>Click <code>+</code> → <code>Add package from git URL...</code></li>
      <li>Enter: <code>https://github.com/EitanWong/com.eitan.easymic.git#upm</code></li>
      <li>Click <code>Add</code></li>
    </ol>
  <h3>📋 Import Sample Scene</h3>
    <ol align="left">
      <li>After importing Easy Mic, go to <strong>Package Manager</strong></li>
      <li>Find <strong>EasyMic</strong> in "In Project" packages</li>
      <li>Expand <strong>Samples</strong> section</li>
      <li>Click <strong>Import</strong> next to "Recording Example"</li>
      <li>Open the imported scene to see microphone recording demo</li>
    </ol>
    
  <div align="center">
    <img src="./EasyMic/Packages/com.eitan.easymic/Documentation~/images/how-to-import-samples.png" alt="How to Import Samples" width="600">
    <p><em>Import the Recording Example sample scene via Package Manager</em></p>
  </div>
  
  <h3>⚡ Basic Usage</h3>
    <div align="left">
      <pre><code>// Ensure permission (Android triggers system request)
if (!PermissionUtils.HasPermission()) return;

// Refresh device list
EasyMicAPI.Refresh();

// Define processor blueprints
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(10), key: "capture");
var bpDownmix = new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmix");

// Start recording (auto-selects default device/channel)
var handle = EasyMicAPI.StartRecording(SampleRate.Hz16000);

// Attach processors
EasyMicAPI.AddProcessor(handle, bpDownmix);
EasyMicAPI.AddProcessor(handle, bpCapture);

// ... later: stop and get captured clip
EasyMicAPI.StopRecording(handle);
var clip = EasyMicAPI.GetProcessor<AudioCapturer>(handle, bpCapture)?.GetCapturedAudioClip();</code></pre>
    </div>
</div>

---

## 🧪 Sample Projects Overview

EasyMic includes ready-to-run samples under `EasyMic/Packages/com.eitan.easymic/Samples~/` so developers can quickly validate workflows.

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
- AEC, AGC, and ANS are not included in this repository. Use the separate paid EasyMic APM extension for those capabilities.

---

## 📚 Documentation

<div align="center">
  <h3>Documentation Map</h3>
  <p><em>Start with setup, then follow the capture/playback path into architecture, latency, diagnostics, and processor rules.</em></p>
  
  <table>
    <tr>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/getting-started.md">
          <strong>Getting Started</strong><br>
          <em>Installation & first steps</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/recording.md">
          <strong>Recording</strong><br>
          <em>Microphone capture flow</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/playback.md">
          <strong>Playback</strong><br>
          <em>Output and streaming</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/architecture.md">
          <strong>Architecture</strong><br>
          <em>Audio chain diagrams</em>
        </a>
      </td>
    </tr>
    <tr>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/latency-profiles.md">
          <strong>Latency Profiles</strong><br>
          <em>Tradeoffs and defaults</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/diagnostics.md">
          <strong>Diagnostics</strong><br>
          <em>Telemetry and counters</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/processors.md">
          <strong>Processors</strong><br>
          <em>Threading rules</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/troubleshooting.md">
          <strong>Troubleshooting</strong><br>
          <em>Common solutions</em>
        </a>
      </td>
    </tr>
  </table>

  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/api-overview.md">API Overview</a> ·
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/platform-notes.md">Platform Notes</a> ·
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/index.md">Documentation Index</a>
  </p>
  
  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/index.md">
      <img src="https://img.shields.io/badge/🇨🇳_中文文档-Complete_Chinese_Documentation-red.svg" alt="Chinese Documentation">
    </a>
  </p>
</div>

---

## 🎯 Use Cases

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        🤖<br>
        <strong>AI Digital Humans</strong>
        <ul align="left">
          <li>Real-time voice interaction</li>
          <li>Conversation AI workflows</li>
          <li>Optional APM add-on for echo cancellation</li>
          <li>Natural language processing</li>
        </ul>
      </td>
      <td align="center" width="25%">
        🎮<br>
        <strong>Gaming Applications</strong>
        <ul align="left">
          <li>Voice chat in multiplayer</li>
          <li>Voice commands for control</li>
          <li>Real-time audio effects</li>
        </ul>
      </td>
      <td align="center" width="25%">
        📞<br>
        <strong>Communication Apps</strong>
        <ul align="left">
          <li>VoIP applications</li>
          <li>Video conferencing tools</li>
          <li>Real-time audio streaming</li>
        </ul>
      </td>
      <td align="center" width="25%">
        🎙️<br>
        <strong>Content Creation</strong>
        <ul align="left">
          <li>Podcast recording tools</li>
          <li>Voice-over applications</li>
          <li>Audio content workflows</li>
        </ul>
      </td>
    </tr>
  </table>
</div>

---

## 📋 System Requirements

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        <strong>Unity</strong><br>
        2021.3 LTS or higher
      </td>
      <td align="center" width="25%">
        <strong>Platforms</strong><br>
        Windows, macOS, Linux<br>
        Android, iOS
      </td>
      <td align="center" width="25%">
        <strong>Dependencies</strong><br>
        .NET Standard 2.1+
      </td>
      <td align="center" width="25%">
        <strong>Permissions</strong><br>
        Microphone access required
      </td>
    </tr>
  </table>
</div>

---

## 📄 License

<div align="center">
  <p>This project is licensed under the <strong>GPLv3 License</strong> - see the <a href="LICENSE.md">LICENSE.md</a> file for details.</p>
  <p><strong>EasyMic APM is not part of this repository.</strong> It is distributed separately as a paid extension under its own commercial licensing terms.</p>
  
  <table>
    <tr>
      <td align="center" width="50%">
        <h4>✅ Open Source Friendly</h4>
        <ul align="left">
          <li>Free to use in open source projects</li>
          <li>Commercial use allowed with GPL compliance</li>
        </ul>
      </td>
      <td align="center" width="50%">
        <h4>⚠️ Commercial Projects</h4>
        <ul align="left">
          <li>Source code disclosure required</li>
          <li>GPL compliance mandatory for distribution</li>
        </ul>
      </td>
    </tr>
  </table>
  
</div>

---

## 🤝 Community & Support

<div align="center">
  <table>
    <tr>
      <td align="center" width="33%">
        🐛<br>
        <strong>Issues & Bug Reports</strong><br>
        <a href="https://github.com/EitanWong/com.eitan.easymic/issues">GitHub Issues</a><br>
        <em>Check <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/troubleshooting.md">Troubleshooting</a> first</em>
      </td>
      <td align="center" width="33%">
        💬<br>
        <strong>Community Discussion</strong><br>
        <a href="https://github.com/EitanWong/com.eitan.easymic/discussions">GitHub Discussions</a><br>
        <em>Share projects & get help</em>
      </td>
      <td align="center" width="33%">
        📧<br>
        <strong>Professional Support</strong><br>
        <a href="mailto:unease-equity-5c@icloud.com">Email</a> | Bilibili PM<br>
        <em>Technical support and EasyMic APM inquiries</em>
      </td>
    </tr>
  </table>
</div>

---

## Design Principles

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        <strong>Thin callback path</strong><br>
        <em>Keep device callbacks focused on transport work.</em>
      </td>
      <td align="center" width="25%">
        <strong>Worker-based processing</strong><br>
        <em>Run higher-level processing outside the device callback.</em>
      </td>
      <td align="center" width="25%">
        <strong>Observable behavior</strong><br>
        <em>Expose counters that help diagnose latency and glitches.</em>
      </td>
      <td align="center" width="25%">
        <strong>Practical Unity workflow</strong><br>
        <em>Keep samples, components, and docs close to the package.</em>
      </td>
    </tr>
  </table>
</div>

---

<div align="center">
  <h2>Start building with Easy Mic</h2>
  <p><em>Import the package, open a sample, and use diagnostics while validating on your target device.</em></p>
  
  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/getting-started.md">
      <img src="https://img.shields.io/badge/📘_Get_Started_Now-blue.svg?style=for-the-badge" alt="Get Started">
    </a>
    <a href="#-sample-projects-overview">
      <img src="https://img.shields.io/badge/🚀_View_Samples-green.svg?style=for-the-badge" alt="View Samples">
    </a>
    <a href="mailto:unease-equity-5c@icloud.com">
      <img src="https://img.shields.io/badge/💎_Contact_for_APM-gold.svg?style=for-the-badge" alt="Contact APM">
    </a>
  </p>
  
  <hr>
  
  <p>
    <strong>Made with ❤️ by <a href="https://github.com/EitanWong">Eitan</a></strong><br>
    <em>Star ⭐ this repo if Easy Mic helps your project!</em>
  </p>
  
  <p>
    <a href="https://github.com/EitanWong/com.eitan.easymic/stargazers">
      <img src="https://img.shields.io/github/stars/EitanWong/com.eitan.easymic?style=social" alt="GitHub stars">
    </a>
    <a href="https://github.com/EitanWong/com.eitan.easymic/network/members">
      <img src="https://img.shields.io/github/forks/EitanWong/com.eitan.easymic?style=social" alt="GitHub forks">
    </a>
  </p>
</div>
