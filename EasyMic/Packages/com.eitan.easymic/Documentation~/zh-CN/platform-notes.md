# 平台说明

始终在真实设备上验证目标平台。Editor 行为不能替代 IL2CPP player 和移动端测试。

## 桌面端

Windows 和 macOS 桌面 build 通常可以从 `LowLatency` 开始。如果场景加载、CPU 尖峰或处理器工作导致 underruns 或 capture drops，请使用 `Balanced`。

Linux backend 行为取决于音频栈和设备配置。请测试与用户环境匹配的 ALSA/PulseAudio/PipeWire 设置。

## Android

Android 应默认使用 `Balanced`，除非你明确选择更低延迟并在目标设备上测试。播放 API 在 Android player 上已经默认使用 `Balanced`。

添加麦克风权限：

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

Android routing、Bluetooth、AAudio/OpenSL 行为和设备特定 buffer size 差异很大。发布前请测试真实设备。

## iOS

iOS 需要麦克风权限和正确的 usage description。Audio session 行为会影响 route、sample rate 和 interruption。请在 player build 中验证采集/播放，不要只在 Editor 中验证。

## IL2CPP

EasyMic 内部使用静态 reverse P/Invoke callbacks，并在需要的 AOT 平台上为 callback entry points 标注 attribute。不要用用户代码中的 instance callback 替换这些回调。

## Unity 生命周期

- 销毁持有处理器状态的对象前停止录音。
- 释放那些不会自动释放的 `PlaybackHandle`。
- 预期 subsystem registration 和 domain reload 会重置静态 EasyMic 状态。
- 避免在紧密 gameplay 循环中反复启动/停止设备。

## 验证状态

包结构面向 Windows、macOS、Linux、Android 和 iOS，但平台行为取决于原生 plugin 可用性、权限、OS audio backend 和设备驱动。每个发布目标都应进行独立的采集/播放验证。
