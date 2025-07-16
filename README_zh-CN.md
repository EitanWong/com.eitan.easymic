<p align="right">
  <a href="README.md">English</a>
</p>

# Easy Mic for Unity

<p align="center">
  <img src="EasyMic/Packages/com.eitan.easymic/Documentation~/images/easymic-logo.png" alt="Easy Mic Logo" width="200"/>
</p>

**Easy Mic** 是一款用于 Unity 的高性能、低延迟音频录制插件。它提供对原始麦克风数据的直接访问，并引入了可编程的音频处理流水线，让开发者可以串联使用内置的模块来创建复杂的实时音频工作流。

## ✨ 核心功能

*   **🎤 低延迟音频录制**: 通过利用原生后端库，以最小的延迟捕获麦克风音频，非常适合实时应用。
*   **🔊 原始音频数据**: 直接访问来自麦克风的原始音频缓冲区，完全控制声音数据。
*   **⛓️ 可编程处理流水线**: Easy Mic 的核心功能。动态构建音频处理链。您可以随时添加、删除或重新排序处理器。
*   **💻 跨平台支持**: 为包括 Windows、macOS、Linux、Android 和 iOS 在内的主要平台提供统一的 API。
*   **🧩 内置处理器**: 自带一套预置的处理器，用于常见的音频任务。

## 🚀 未来路线图

计划在未来将以下模块原生集成到 Easy Mic 中。欢迎社区贡献！

### 核心音频处理
*   [ ] 声学噪声抑制 (ANS)
*   [ ] 自动增益控制 (AGC)
*   [ ] 回声消除 (AEC)
*   [ ] 语音活性检测 (VAD)

### 高级语音 AI
*   [ ] 唤醒词激活 (基于 sherpa-onnx)
*   [ ] 音频分离 (声源分离)

### 麦克风阵列与空间音频
*   [ ] 声源定位 (SSL)
*   [ ] 波束赋形

### 平台扩展
*   [ ] 支持 Web 平台 (WebGL)

## 📦 安装

要将 Easy Mic 添加到您的 Unity 项目中：

1.  打开 Unity 包管理器 (`Window > Package Manager`)。
2.  点击左上角的 `+` 按钮，选择 "Add package from git URL...".
3.  输入仓库 URL: `https://github.com/EitanWong/com.eitan.easymic.git`
4.  点击 "Add"。

更详细的文档和示例，请参考[包内的 README 文件](EasyMic/Packages/com.eitan.easymic/README_zh-CN.md)。

## 📄 许可证

该项目采用 [GPLv3 许可证](LICENSE.md) 授权。请务必仔细阅读。使用本软件即表示您接受其条款。