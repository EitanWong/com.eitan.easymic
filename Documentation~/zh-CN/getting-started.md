← [文档首页](../README.md) | [English Version](../en/getting-started.md) →

# 🚀 Easy Mic 入门指南

欢迎使用 Easy Mic！本指南将帮助您在 Unity 项目中设置和开始使用 Easy Mic。

## 📋 前置条件

- **Unity 2021.3 LTS** 或更高版本
- **支持的平台**：Windows、macOS、Linux、Android、iOS
- 目标平台上的**麦克风访问权限**

## 📦 安装

### 方法 1：Unity Package Manager（推荐）

1. 打开 Unity Package Manager (`Window > Package Manager`)
2. 点击左上角的 `+` 按钮
3. 选择 `Add package from git URL...`
4. 输入：`https://github.com/EitanWong/com.eitan.easymic.git#upm`
5. 点击 `Add`

### 方法 2：OpenUPM

```bash
openupm add com.eitan.easymic
```

### 方法 3：手动安装

1. 从 [GitHub Releases](https://github.com/EitanWong/com.eitan.easymic/releases) 下载最新版本
2. 解压到项目的 `Packages` 文件夹
3. Unity 将自动检测并导入包

## 🎯 您的第一次录音

让我们创建一个录制 5 秒音频的简单脚本：

```csharp
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Core.Processors;
using UnityEngine;

public class FirstRecording : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private AudioCapturer _audioCapturer;

    void Start()
    {
        // 1. 初始化 EasyMic 并获取可用设备
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        
        if (devices.Length == 0)
        {
            Debug.LogError("❌ 未找到麦克风设备！");
            return;
        }

        Debug.Log($"🎤 找到 {devices.Length} 个麦克风设备");

        // 2. 使用高质量设置开始录音
        _recordingHandle = EasyMicAPI.StartRecording(
            devices[0].Name,           // 使用第一个可用设备
            SampleRate.Hz48000,        // 高质量采样率
            Channel.Mono              // 单声道，提高效率
        );

        if (!_recordingHandle.IsValid)
        {
            Debug.LogError("❌ 录音启动失败！");
            return;
        }

        // 3. 创建音频捕获器并添加到管道
        _audioCapturer = new AudioCapturer(5); // 最多 5 秒
        EasyMicAPI.AddProcessor(_recordingHandle, _audioCapturer);

        Debug.Log("🎙️ 开始录音 5 秒...");
        
        // 4. 5 秒后停止录音
        Invoke(nameof(StopRecording), 5f);
    }

    void StopRecording()
    {
        if (!_recordingHandle.IsValid) return;

        // 停止录音
        EasyMicAPI.StopRecording(_recordingHandle);
        
        // 将捕获的音频作为 Unity AudioClip 获取
        var audioClip = _audioCapturer.GetCapturedAudioClip();
        
        if (audioClip != null)
        {
            Debug.Log($"✅ 录音完成！时长：{audioClip.length:F2}s");
            
            // 播放回放（可选）
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.PlayOneShot(audioClip);
                Debug.Log("🔊 播放录制的音频...");
            }
        }
        else
        {
            Debug.LogError("❌ 未捕获到音频！");
        }
        
        // 清理
        _recordingHandle = default;
    }

    void OnDestroy()
    {
        // 对象销毁时始终清理
        if (_recordingHandle.IsValid)
            EasyMicAPI.StopRecording(_recordingHandle);
    }
}
```

## 🎮 设置场景

1. 在场景中**创建新的 GameObject**
2. 将 `FirstRecording` 脚本**添加到 GameObject**
3. **添加 AudioSource 组件**（可选，用于播放）
4. **按下 Play** 并对着麦克风说话！

## 📱 平台特定设置

### 🖥️ 桌面平台（Windows/macOS/Linux）
无需额外设置。Unity 会在需要时自动请求麦克风权限。

### 📱 移动平台（Android/iOS）

#### Android
添加到您的 `AndroidManifest.xml`：
```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

#### iOS  
Easy Mic 会自动请求麦克风权限。您也可以在 `Info.plist` 中添加使用说明：
```xml
<key>NSMicrophoneUsageDescription</key>
<string>此应用需要麦克风访问权限来录制音频。</string>
```

## 🔧 故障排除

### 未找到麦克风设备
- 确保麦克风已连接并启用
- 检查系统的麦克风访问权限
- 尝试使用 `EasyMicAPI.Refresh()` 刷新

### 录音启动失败
- 验证麦克风权限已授予
- 检查是否有其他应用正在使用麦克风
- 尝试不同的采样率或声道配置

### 未捕获到音频
- 确保您正在对着麦克风说话
- 检查系统麦克风音量
- 验证 `AudioCapturer` 在录音开始前已添加到管道

## 📖 下一步

现在您已经有基本的录音功能了，可以探索这些主题：

- **[核心概念](core-concepts.md)** - 了解 EasyMic 的架构
- **[音频管道](audio-pipeline.md)** - 学习处理管道
- **[内置处理器](processors.md)** - 发现所有可用的处理器
- **[示例代码](examples.md)** - 查看更复杂的用例

## 🆘 需要帮助？

- 📖 查看 [故障排除指南](troubleshooting.md)
- 🐛 在 [GitHub Issues](https://github.com/EitanWong/com.eitan.easymic/issues) 报告问题
- 💬 在 [GitHub Discussions](https://github.com/EitanWong/com.eitan.easymic/discussions) 参与讨论

---

← [文档首页](../README.md) | **下一步：[核心概念](core-concepts.md)** →