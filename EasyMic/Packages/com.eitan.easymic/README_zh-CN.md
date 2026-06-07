<div align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="Easy Mic Icon" width="112" height="112">

  # Easy Mic for Unity

  **面向 Unity 的外部音频采集、播放、处理与 SherpaONNXUnity 集成**

  <p>
    <img src="https://img.shields.io/badge/version-0.1.3--exp.3-1f6feb.svg" alt="Version 0.1.3-exp.3">
    <img src="https://img.shields.io/badge/Unity-2021.3%2B-222222.svg" alt="Unity 2021.3+">
    <img src="https://img.shields.io/badge/License-GPLv3-2ea043.svg" alt="GPLv3 License">
    <img src="https://img.shields.io/badge/Platforms-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android%20%7C%20iOS-6e7681.svg" alt="支持平台">
  </p>

  <p>
    <a href="README.md">English</a> | <strong>中文</strong>
  </p>

  <p>
    <strong>版本：</strong><code>0.1.3-exp.3</code> · <span>2026-05-12</span>
  </p>
</div>

---

> **包范围说明：** 本包包含开源的 Easy Mic 核心包，以及 EasyMic 侧可选的 SherpaONNXUnity 集成层。本包**不包含 AEC、AGC、ANS**。这些能力属于单独付费提供的 **EasyMic APM** 扩展包。

## Easy Mic 提供什么

| 能力 | 是否包含 | 说明 |
| --- | --- | --- |
| 麦克风采集 | 包含 | 基于 miniaudio 的低延迟设备采集，可访问原始 PCM。 |
| 外部播放 | 包含 | 通过 EasyMic 播放传输链路进行流式播放和片段播放。 |
| 音频处理流水线 | 包含 | 基于 worker 的处理链路，明确实时线程和普通线程边界。 |
| 诊断 | 包含 | 观测回调状态、队列深度、underrun、overflow 和丢帧。 |
| SherpaONNXUnity 集成 | 可选 | 需要安装 [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm)。 |
| AEC / AGC / ANS | 不包含 | 仅在单独付费的 EasyMic APM 扩展包中提供。 |

## 核心能力

- **低延迟采集：** 麦克风输入、设备选择、延迟配置、传输缓冲和原始 PCM 访问。
- **外部播放：** 片段和流式播放 API，包含队列、underrun 处理和播放遥测。
- **可组合处理器：** 录制、降混、音量门、播放和集成 worker 均遵循明确的处理器契约。
- **实时安全架构：** 设备回调只负责轻量传输，高层处理不阻塞音频热路径。
- **编辑器优先体验：** Inspector、Project Settings、诊断窗口、Recipes、本地化 UI 和组件图标。
- **可选 Sherpa 集成：** EasyMic 可作为 Sherpa 原生组件的麦克风输入源，避免重复采集。

## 安装

### Unity Package Manager

1. 打开 `Window > Package Manager`。
2. 点击 `+` -> `Add package from git URL...`。
3. 输入：

```text
https://github.com/EitanWong/com.eitan.easymic.git#upm
```

4. 点击 `Add`。

### 可选 SherpaONNXUnity 依赖

仅当你需要 ASR、关键词检测、VAD、音频标签、说话人分离、语音增强、源分离、语种识别或 TTS 工作流时安装 SherpaONNXUnity：

```text
https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm
```

依赖可用后，EasyMic 会启用可选的 `Eitan.EasyMic.Integration.SherpaONNXUnity` 集成层。

## 快速开始

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

## SherpaONNXUnity 集成

EasyMic 现在提供更完整的可选 SherpaONNXUnity 桥接层，同时保持 EasyMic 和 SherpaONNXUnity 两个 package 都可以独立使用。

### 推荐的 Sherpa 组件路径

当你希望 Sherpa 原生 MonoBehaviour 组件直接消费 EasyMic 麦克风采集时，使用 `EasyMicSherpaAudioInputSource`：

- 通过 `GameObject > SherpaONNX > Audio > EasyMic Audio Input Source` 创建。
- 在输入源 Inspector 中点击 `添加 Sherpa 组件` 添加需要的 Sherpa 组件。
- 该输入源只启动一个 EasyMic 录音 session，并向绑定的 Sherpa 组件输出单声道 PCM chunk。
- 避免重复麦克风采集，并保证 `ChunkReady` 在 Unity 主线程派发。

当前 EasyMic 提供的 Sherpa 组合菜单包括：

- 使用 EasyMic 输入的实时语音识别。
- 使用 EasyMic VAD 的离线语音识别。
- 使用 EasyMic 输入的关键词检测。
- 使用 EasyMic 输入的语音活动检测。
- 使用 EasyMic 输入的音频标签。
- 使用 EasyMic 播放链路的语音合成组件。

### EasyMic Pipeline 路径

如果你希望在 EasyMic pipeline 或服务层中使用 Sherpa 能力，可使用 EasyMic 侧 worker/facade：

- `SherpaRealtimeSpeechRecognizer`
- `SherpaOfflineSpeechRecognizer`
- `SherpaKeywordDetector`
- `SherpaVoiceFilter`
- `SherpaAudioTagger`
- `SherpaSpeechEnhancementFilter`
- `SherpaSourceSeparator`
- `SherpaSpeakerDiarizer`
- `SherpaSpokenLanguageIdentifier`

集成层还包含 EasyMic 侧模型服务注册表，用于 worker/facade/session 的模型复用。Sherpa 原生组件仍由 SherpaONNXUnity 自己管理模型，因为本包不会修改 SherpaONNXUnity。

### 编辑器体验

- `Project Settings > Easy Mic > Integrations > SherpaONNXUnity` 展示依赖和集成状态。
- 如果尚未安装 SherpaONNXUnity，可以在该设置页使用 git URL 一键安装。
- `Window > Easy Mic > SherpaONNXUnity Diagnostics` 提供集成诊断。
- EasyMic Sherpa 组件和通过 Recipe 创建的 Sherpa 组件带有专属图标。
- EasyMic 音频输入源 Inspector 会提示重复麦克风输入，并显示运行时计数器。

## 示例

安装包后可从 Unity Package Manager 导入示例。

| 示例 | 作用 | 适用场景 |
| --- | --- | --- |
| `Recording Example` | 基础麦克风录音和 WAV 保存。 | 首次接入、设备检测、权限验证。 |
| `Playback Example` | 使用 EasyMic 播放栈播放音频。 | 低延迟输出验证。 |
| `AudioPlayback API Example` | 代码式播放和队列式音频喂入。 | 自定义运行时播放系统。 |
| `SherpaONNXUnity ASR Example` | SherpaONNXUnity + EasyMic 实时语音识别。 | 语音转文字、语音指令原型。 |
| `SherpaONNXUnity KWS Example` | 关键词/唤醒词流程。 | 唤醒词触发、常驻监听助手。 |
| `SherpaONNXUnity AudioTagging Input Example` | EasyMic 输入源喂给 Sherpa 音频标签组件。 | 避免重复采集的 Sherpa 组件集成。 |
| `AIChat Example` | ASR + LLM + TTS + 播放编排。 | 数字人和 AI 语音助手原型。 |

## 文档

- [文档索引](Documentation~/README.md)
- [概览](Documentation~/zh-CN/index.md)
- [快速入门](Documentation~/zh-CN/getting-started.md)
- [录音](Documentation~/zh-CN/recording.md)
- [播放](Documentation~/zh-CN/playback.md)
- [架构](Documentation~/zh-CN/architecture.md)
- [延迟配置](Documentation~/zh-CN/latency-profiles.md)
- [诊断](Documentation~/zh-CN/diagnostics.md)
- [处理器契约](Documentation~/zh-CN/processors.md)
- [平台说明](Documentation~/zh-CN/platform-notes.md)
- [故障排除](Documentation~/zh-CN/troubleshooting.md)
- [API 概览](Documentation~/zh-CN/api-overview.md)
- [SherpaONNXUnity 使用指南](Documentation~/zh-CN/sherpa-onnx-unity-usage.md)
- [SherpaONNXUnity 集成计划](Documentation~/zh-CN/sherpa-onnx-unity-integration-plan.md)

English documentation:

- [English Overview](Documentation~/en/index.md)
- [Getting Started](Documentation~/en/getting-started.md)

## 常见使用场景

- AI 数字人和虚拟助手。
- 语音命令和唤醒词工作流。
- 实时录音和播放工具。
- 游戏内语音消息和通信功能。
- 音频诊断和平台采集验证。
- 通过单个 EasyMic 采集 session 接入 SherpaONNXUnity ASR/KWS/VAD/音频标签。

## 系统要求

- Unity 2021.3 LTS 或更高版本。
- 兼容 .NET Standard 2.1 的 Unity 运行时。
- 目标平台需要麦克风权限。
- 可选：Sherpa 工作流需要 [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm)。

## EasyMic APM

EasyMic APM 是单独付费提供的生产级语音清理扩展：

- AEC：声学回声消除。
- AGC：自动增益控制。
- ANS：自动噪声抑制。

[B 站演示视频：Unity 数字人语音交互中的回声、音量和噪声处理](https://www.bilibili.com/video/BV18hE46rEzw/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855)

本仓库不包含 EasyMic APM 的实现代码、二进制文件、示例或授权。

联系方式：[unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)

## 许可证

Easy Mic 使用 GPLv3 授权。详情见 [LICENSE.md](LICENSE.md)。

EasyMic APM 不属于本仓库内容，它作为付费扩展包单独分发，并适用独立的商业授权条款。

### 为什么严格遵守 GPLv3 很重要

GPLv3 不是建议，而是法律许可条款。如果你分发包含或链接 GPLv3 代码的软件，需要遵守 GPLv3 的源代码开放和协议传递要求。闭源商业软件、只开放部分代码、或试图规避 GPLv3 义务都可能带来版权和合规风险。

如果 GPLv3 的 Copyleft 要求与你的商业需求不匹配，可以联系作者讨论商业许可方案：

[unease-equity-5c@icloud.com](mailto:unease-equity-5c@icloud.com)

---

<div align="center">
  <strong>由 <a href="https://github.com/EitanWong">Eitan</a> 制作</strong>
</div>
