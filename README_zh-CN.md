
<p align="right">
  <a href="README.md">English</a>
</p>

# Easy Mic for Unity

<p align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="Easy Mic Logo" width="200"/>
</p>

**Easy Mic** 是一款用于 Unity 的高性能、低延迟音频录制插件。它提供对原始麦克风数据的直接访问，并引入了可编程的音频处理流水线，让开发者可以串联使用内置的模块来创建复杂的实时音频工作流。

## ✨ 核心功能

*   **🎤 低延迟音频录制**: 通过利用原生后端库，以最小的延迟捕获麦克风音频，非常适合实时应用。
*   **🔊 原始音频数据**: 直接访问来自麦克风的原始音频缓冲区，完全控制声音数据。
*   **⛓️ 可编程处理流水线**: Easy Mic 的核心功能。动态构建音频处理链。您可以随时添加、删除或重新排序处理器。
*   **💻 跨平台支持**: 为包括 Windows、macOS、Linux、Android 和 iOS 在内的主要平台提供统一的 API。
*   **🧩 内置处理器**: 自带一套预置的处理器，用于常见的音频任务。

## 🚀 音频处理流水线

Easy Mic 的强大之处在于其可编程的流水线。当麦克风激活时，它会将音频数据流经您定义的一系列“处理器”。每个处理器接收音频数据，执行操作，然后将修改后的数据传递给链中的下一个处理器。

`麦克风输入 -> [处理器 A] -> [处理器 B] -> [处理器 C] -> 最终输出`

这使您可以轻松地组合提供的模块以满足您的需求。

## 🛠️ 内置处理器

Easy Mic 目前包含以下开箱即用的模块：

*   **`AudioCapturer`**: 将输入的音频数据捕获到缓冲区或保存到文件。
*   **`AudioDownmixer`**: 将多通道音频（例如立体声）转换为单通道音频（单声道）。
*   **`VolumeGateFilter`**: 一个噪声门，只有当音量高于特定阈值时才允许音频通过。
*   **`SherpaRealtimeSpeechRecognizer`**: 使用 Sherpa-ONNX 引擎提供实时的语音转文本功能。**注意：** 此处理器需要先安装 [com.eitan.sherpa-onnx-unity](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity) 插件。

*（注意：创建自定义处理器的功能计划在未来版本中开放。）*

## 📦 安装

1.  打开 Unity 包管理器 (`Window > Package Manager`)。
2.  点击左上角的 `+` 按钮，选择 "Add package from git URL..."。
3.  输入仓库 URL: `https://github.com/EitanWong/com.eitan.easymic.git`
4.  点击 "Add"。

## ▶️ 快速入门

这是一个如何录制一个5秒音频片段的基本示例。

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
        // 刷新设备列表以确保其为最新
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        if (devices.Length == 0)
        {
            Debug.LogError("未找到麦克风设备。");
            return;
        }

        // 1. 使用默认设备开始录制并获取一个句柄
        _recordingHandle = EasyMicAPI.StartRecording(devices[0].Name);

        if (!_recordingHandle.IsValid)
        {
            Debug.LogError("开始录制失败。");
            return;
        }

        // 2. 创建一个 AudioCapturer 处理器来捕获音频数据
        _audioCapturer = new AudioCapturer(); 
        EasyMicAPI.AddProcessor(_recordingHandle, _audioCapturer);

        Debug.Log("开始录制5秒钟...");

        // 在此示例中，5秒后停止录制
        Invoke(nameof(StopRecording), 5f);
    }

    void StopRecording()
    {
        if (!_recordingHandle.IsValid) return;

        // 3. 通过句柄停止录制
        EasyMicAPI.StopRecording(_recordingHandle);

        // 4. 从处理器中获取捕获到的音频片段
        _recordedClip = _audioCapturer.GetCapturedAudioClip();

        if (_recordedClip != null)
        {
            Debug.Log($"录制完成。创建的 AudioClip 长度为: {_recordedClip.length}s");
            // 现在你可以用一个 AudioSource 来播放这个片段了
            // GetComponent<AudioSource>().PlayOneShot(_recordedClip);
        }
        
        // 使句柄无效
        _recordingHandle = default;
    }

    void OnDestroy()
    {
        // 确保在对象销毁时停止录制
        if (_recordingHandle.IsValid)
        {
            EasyMicAPI.StopRecording(_recordingHandle);
        }
    }
}
```

## 📄 许可证

该项目采用 [GPLv3 许可证](LICENSE.md) 授权。

### 为什么遵守开源协议如此重要？

在开源的世界里，代码的自由共享和修改是建立在相互尊重和信任的基础之上的。GPLv3 协议就是这种信任的法律保障。它赋予了你使用、修改和分享本软件的自由，但同时也设定了一些基本规则，以确保这种自由能够传递给每一个使用者。

**简单来说，GPLv3 的核心要求是：**

1.  **代码共享**：如果你修改了本项目的代码，并将其用于公开发布的产品或软件中，你也必须将你的修改同样以 GPLv3 协议开源。这保证了社区的贡献能够回馈给整个社区。
2.  **责任与义务**：这不仅仅是“免费使用”。当你选择使用本软件时，你就选择了接受 GPLv3 协议的条款。这是一种责任，也是对原作者和其他贡献者辛勤工作的尊重。

我之所以在此特别强调，是因为许多开发者可能无意中忽略了开源协议的重要性。不遵守协议不仅可能引发法律风险，更重要的是，它会损害整个开源社区的健康发展。一个健康的社区需要每一位参与者的共同维护。

**请务必认真阅读并遵守 GPLv3 协议。这不仅是对我工作的尊重，也是维护一个健康、繁荣的开源生态环境的重要一环。感谢你的理解与合作！**
