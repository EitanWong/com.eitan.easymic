<div align="center">
  <img src="./EasyMic/Packages/com.eitan.easymic/Documentation~/images/easymic-logo.png" alt="Easy Mic Icon" width="112" height="112">
  
  # Easy Mic for Unity
  
  **面向 Unity 的外部音频采集、播放与处理系统**
  
  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/package.json"><img src="https://img.shields.io/badge/version-0.1.3--exp.3-1f6feb.svg" alt="Version 0.1.3-exp.3"></a>
    <a href="https://unity3d.com/get-unity/download"><img src="https://img.shields.io/badge/Unity-2021.3%2B-222222.svg" alt="Unity 2021.3+"></a>
    <a href="LICENSE.md"><img src="https://img.shields.io/badge/License-GPLv3-2ea043.svg" alt="GPLv3 License"></a>
    <a href="#系统要求"><img src="https://img.shields.io/badge/Platforms-Windows%20%7C%20macOS%20%7C%20Linux%20%7C%20Android%20%7C%20iOS-6e7681.svg" alt="支持平台"></a>
  </p>
  
  <p align="center">
    <strong>🇨🇳 中文版</strong> | 
    <a href="README.md">🇺🇸 English</a>
  </p>

  <p align="center">
    <strong>版本：</strong><code>0.1.3-exp.3</code> · <span>2026-05-12</span>
  </p>

  <p align="center">
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/getting-started.md"><img src="https://img.shields.io/badge/阅读文档-入门指南-1f6feb.svg?style=for-the-badge" alt="阅读文档"></a>
    <a href="#-示例项目总览"><img src="https://img.shields.io/badge/打开示例-Unity_Package-2ea043.svg?style=for-the-badge" alt="打开示例"></a>
    <a href="EasyMic/Packages/com.eitan.easymic/CHANGELOG.md"><img src="https://img.shields.io/badge/更新日志-0.1.3--exp.3-6e7681.svg?style=for-the-badge" alt="查看更新日志"></a>
  </p>
  <p align="center">
    <em>适用于语音交互、AI 数字人、自定义播放链路和实时音频诊断。</em>
  </p>
</div>

---

> **仓库范围说明：** 本仓库仅包含开源的 Easy Mic 核心包，**不包含 AEC、AGC、ANS 功能**。这些能力属于 **EasyMic APM**，它是单独提供的付费扩展包。如需声学回声消除、自动增益控制或自动噪声抑制，请单独联系作者获取。

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        <strong>本仓库包含</strong><br>
        麦克风采集<br>
        原始音频缓冲区
      </td>
      <td align="center" width="25%">
        <strong>本仓库包含</strong><br>
        运行时音频流水线<br>
        内置处理器
      </td>
      <td align="center" width="25%">
        <strong>可选集成</strong><br>
        Sherpa ONNX<br>
        ASR / KWS 工作流
      </td>
      <td align="center" width="25%">
        <strong>付费扩展</strong><br>
        EasyMic APM<br>
        AEC / AGC / ANS
      </td>
    </tr>
  </table>
</div>

---

<div align="center">
  <h2>面向交互式 Unity 应用的音频 I/O</h2>
  
  <p><strong>Easy Mic</strong> 提供基于 miniaudio 的麦克风采集、外部播放、处理器流水线、延迟配置和诊断能力，不依赖 Unity 内置音频路径完成核心传输。</p>
</div>

<table align="center">
  <tr>
    <td align="center" width="25%">
      <br>
      <strong>采集</strong><br>
      <em>低延迟麦克风输入、设备选择和传输缓冲。</em>
      <br><br>
    </td>
    <td align="center" width="25%">
      <br>
      <strong>播放</strong><br>
      <em>通过 EasyMic 外部音频系统进行流式播放和片段播放。</em>
      <br><br>
    </td>
    <td align="center" width="25%">
      <br>
      <strong>处理</strong><br>
      <em>可组合音频处理器，明确区分实时和线程约束。</em>
      <br><br>
    </td>
    <td align="center" width="25%">
      <br>
      <strong>诊断</strong><br>
      <em>观测 underrun、overflow、回调状态、队列深度和 worker 状态。</em>
      <br><br>
    </td>
  </tr>
</table>

<div align="center">
  <p><em>适用于语音交互、AI 角色、自定义音频工具和实时音频工作流。</em></p>
</div>

---

## 🎬 实际演示

<div align="center">
  <a href="https://www.bilibili.com/video/BV1qxb9zKEQN/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855">
    <img src="https://img.shields.io/badge/🎥_观看演示-B站-ff69b4.svg?style=for-the-badge" alt="观看演示">
  </a>
  
  <p><strong>Unity 数字人麦克风录音插件</strong><br>
  <em>Easy Mic 核心包 + 可选 EasyMic APM 的对话式 AI 工作流</em></p>
  
  <p>这个视频演示了对话式 AI 音频工作流。回声消除、增益控制、噪声抑制不包含在本仓库中；这些能力需要单独付费的 EasyMic APM 扩展包。</p>
</div>

---

## ✨ 核心功能

<div align="center">
  <table>
    <tr>
      <td align="left" width="50%">
        <h3>低延迟录音</h3>
        <ul align="left">
          <li>基于 miniaudio 的设备访问</li>
          <li>capture transport worker 与 unmanaged ring buffer</li>
          <li>观测 overflow、drop 和 callback 状态</li>
        </ul>
      </td>
      <td align="left" width="50%">
        <h3>外部播放</h3>
        <ul align="left">
          <li>stream 和 clip 播放 API</li>
          <li>带水位调度的 render worker</li>
          <li>underrun zero-fill 与遥测计数</li>
        </ul>
      </td>
    </tr>
    <tr>
      <td align="left" width="50%">
        <h3>处理器契约</h3>
        <ul align="left">
          <li>transport-safe processor 标记接口</li>
          <li>区分主线程和 realtime-forbidden 处理器</li>
          <li>明确 Unity API 的线程使用边界</li>
        </ul>
      </td>
      <td align="left" width="50%">
        <h3>开发者可观测性</h3>
        <ul align="left">
          <li>面向不同稳定性目标的 latency profiles</li>
          <li>pipeline visualizer 和 project settings 工具</li>
          <li>中英文文档与实用示例</li>
        </ul>
      </td>
    </tr>
  </table>
</div>

---

## 💎 EasyMic APM 扩展包 - 付费专业 3A 音频处理

<div align="center">
  <img src="https://img.shields.io/badge/🔊_解决_AI_对话打断问题-专业解决方案-gold.svg?style=for-the-badge" alt="APM解决方案">
  
  <p>对于从事 <strong>Unity AI 数字人项目</strong>的开发者，<strong>EasyMic APM（Audio Processing Module）</strong>作为<strong>单独付费扩展包</strong>提供。</p>
  <p><strong>本仓库不包含 AEC、AGC、ANS 的实现代码、二进制文件、示例或授权。</strong></p>
</div>

<div align="center">
  <table>
    <tr>
      <td align="center" width="33%">
        🔇<br>
        <strong>AEC</strong><br>
        <em>声学回声消除</em><br>
        消除回声和反馈
      </td>
      <td align="center" width="33%">
        📢<br>
        <strong>AGC</strong><br>
        <em>自动增益控制</em><br>
        维持一致的音频电平
      </td>
      <td align="center" width="33%">
        🎯<br>
        <strong>ANS</strong><br>
        <em>声学噪声抑制</em><br>
        减少背景噪音
      </td>
    </tr>
  </table>
</div>

<div align="center">
  <p><strong>📧 联系：</strong> <a href="mailto:unease-equity-5c@icloud.com">unease-equity-5c@icloud.com</a> | <strong>💬 B站：</strong> 发送私信</p>
  <p><em>如项目需要 AEC、AGC、ANS，请单独联系获取 EasyMic APM。</em></p>
  
  <a href="https://www.bilibili.com/video/BV1qxb9zKEQN/?share_source=copy_web&vd_source=06d081c8a7b3c877a41f801ce5915855">
    <img src="https://img.shields.io/badge/🎥_演示视频-在B站观看-ff69b4.svg" alt="演示视频">
  </a>
</div>

---

## 🚀 快速开始

<div align="left">
  <h3>📦 安装</h3>
    <ol align="left">
      <li>打开 Unity Package Manager</li>
      <li>点击 <code>+</code> → <code>Add package from git URL...</code></li>
      <li>输入: <code>https://github.com/EitanWong/com.eitan.easymic.git#upm</code></li>
      <li>点击 <code>Add</code></li>
    </ol>
  <h3>📋 导入示例场景</h3>
    <ol align="left">
      <li>导入 Easy Mic 后，前往 <strong>Package Manager</strong></li>
      <li>在 "In Project" 包中找到 <strong>EasyMic</strong></li>
      <li>展开 <strong>Samples</strong> 部分</li>
      <li>点击 "Recording Example" 旁边的 <strong>Import</strong></li>
      <li>打开导入的场景查看麦克风录制演示</li>
    </ol>
    
  <div align="center">
    <img src="./EasyMic/Packages/com.eitan.easymic/Documentation~/images/how-to-import-samples.png" alt="如何导入示例" width="600">
    <p><em>通过 Package Manager 导入 Recording Example 示例场景</em></p>
  </div>
  
  <h3>⚡ 基本使用</h3>
    <div align="left">
      <pre><code>// 检查权限（Android 会弹系统授权）
if (!PermissionUtils.HasPermission()) return;

// 刷新设备列表
EasyMicAPI.Refresh();

// 定义处理器蓝图
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(10), key: "capture");
var bpDownmix = new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmix");

// 开始录音（自动选择默认设备/声道）
var handle = EasyMicAPI.StartRecording(SampleRate.Hz16000);

// 挂载处理器
EasyMicAPI.AddProcessor(handle, bpDownmix);
EasyMicAPI.AddProcessor(handle, bpCapture);

// …稍后：停止并获取录音结果
EasyMicAPI.StopRecording(handle);
var clip = EasyMicAPI.GetProcessor<AudioCapturer>(handle, bpCapture)?.GetCapturedAudioClip();</code></pre>
    </div>
</div>

---

## 🧪 示例项目总览

EasyMic 在 `EasyMic/Packages/com.eitan.easymic/Samples~/` 提供了可直接运行的示例，方便开发者快速验证完整流程。

| 示例 | 作用 | 适用场景 |
| --- | --- | --- |
| `Recording Example` | 演示基础麦克风录音与 WAV 保存流程。 | 首次接入、设备与权限联调。 |
| `Playback Example` | 演示 EasyMic 播放链路的基础能力。 | 验证低延迟播放与播放控制。 |
| `AudioPlayback API Example` | 演示代码式播放 API 与队列式音频喂入。 | 构建自定义运行时播放逻辑。 |
| `SherpaONNXUnity ASR Example` | 演示 Sherpa ONNX + EasyMic 的实时语音识别链路。 | 语音转文字、语音指令原型。 |
| `SherpaONNXUnity KWS Example` | 演示关键词/唤醒词识别流程。 | 唤醒词触发、常驻监听助手。 |
| `AIChat Example` | 演示端到端 AI 语音对话（ASR + LLM + TTS + 播放编排）。 | **可直接作为数字人 / AI 语音助手应用起点。** |

### AIChat 示例说明

- `AIChat Example` 以“可落地”的对话系统流程为目标，适合作为数字人项目脚手架。
- 示例覆盖从麦克风输入、语音识别、LLM 生成回复，到 TTS 合成播放的完整链路。
- 运行前请先安装 [`com.eitan.sherpa-onnx-unity`](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity)。
- 本仓库不包含 AEC、AGC、ANS。如需这些能力，请使用单独付费的 EasyMic APM 扩展包。

---

## 📚 文档

<div align="center">
  <h3>文档地图</h3>
  <p><em>建议从安装开始，再沿着采集、播放、架构、延迟、诊断和处理器规则逐步阅读。</em></p>
  
  <table>
    <tr>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/getting-started.md">
          <strong>入门指南</strong><br>
          <em>安装和第一步</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/recording.md">
          <strong>录音</strong><br>
          <em>麦克风采集链路</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/playback.md">
          <strong>播放</strong><br>
          <em>输出与流式播放</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/architecture.md">
          <strong>架构</strong><br>
          <em>音频链路图</em>
        </a>
      </td>
    </tr>
    <tr>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/latency-profiles.md">
          <strong>延迟配置</strong><br>
          <em>取舍与默认值</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/diagnostics.md">
          <strong>诊断</strong><br>
          <em>遥测与计数器</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/processors.md">
          <strong>处理器</strong><br>
          <em>线程规则</em>
        </a>
      </td>
      <td align="center" width="25%">
        <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/troubleshooting.md">
          <strong>故障排除</strong><br>
          <em>常见解决方案</em>
        </a>
      </td>
    </tr>
  </table>

  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/api-overview.md">API 概览</a> ·
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/platform-notes.md">平台说明</a> ·
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/index.md">文档索引</a>
  </p>
  
  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/en/getting-started.md">
      <img src="https://img.shields.io/badge/🇺🇸_English_Documentation-Complete_Documentation-blue.svg" alt="English Documentation">
    </a>
  </p>
</div>

---

## 🎯 使用场景

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        🤖<br>
        <strong>AI 数字人</strong>
        <ul align="left">
          <li>实时语音交互</li>
          <li>对话式 AI 工作流</li>
          <li>可选 APM 扩展用于回声消除</li>
          <li>自然语言处理</li>
        </ul>
      </td>
      <td align="center" width="25%">
        🎮<br>
        <strong>游戏应用</strong>
        <ul align="left">
          <li>多人游戏语音聊天</li>
          <li>语音控制命令</li>
          <li>实时音频效果</li>
        </ul>
      </td>
      <td align="center" width="25%">
        📞<br>
        <strong>通信应用</strong>
        <ul align="left">
          <li>VoIP 应用程序</li>
          <li>视频会议工具</li>
          <li>实时音频流</li>
        </ul>
      </td>
      <td align="center" width="25%">
        🎙️<br>
        <strong>内容创作</strong>
        <ul align="left">
          <li>播客录制工具</li>
          <li>配音应用程序</li>
          <li>音频内容工作流</li>
        </ul>
      </td>
    </tr>
  </table>
</div>

---

## 📋 系统要求

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        <strong>Unity</strong><br>
        2021.3 LTS 或更高版本
      </td>
      <td align="center" width="25%">
        <strong>平台</strong><br>
        Windows, macOS, Linux<br>
        Android, iOS
      </td>
      <td align="center" width="25%">
        <strong>依赖</strong><br>
        .NET Standard 2.1+
      </td>
      <td align="center" width="25%">
        <strong>权限</strong><br>
        需要麦克风访问权限
      </td>
    </tr>
  </table>
</div>

---

## 📄 许可证

<div align="center">
  <p>本项目采用 <strong>GPLv3 许可证</strong> - 详见 <a href="LICENSE.md">LICENSE.md</a> 文件。</p>
  <p><strong>EasyMic APM 不属于本仓库内容。</strong>它作为付费扩展包单独分发，并适用独立的商业授权条款。</p>
  
  <table>
    <tr>
      <td align="center" width="50%">
        <h4>✅ 开源友好</h4>
        <ul align="left">
          <li>开源项目免费使用</li>
          <li>允许商业使用（需遵守GPL）</li>
        </ul>
      </td>
      <td align="center" width="50%">
        <h4>⚠️ 商业项目</h4>
        <ul align="left">
          <li>需要公开源代码</li>
          <li>分发时必须遵守GPL</li>
        </ul>
      </td>
    </tr>
  </table>
  
</div>

---

## 🤝 社区与支持

<div align="center">
  <table>
    <tr>
      <td align="center" width="33%">
        🐛<br>
        <strong>问题与错误报告</strong><br>
        <a href="https://github.com/EitanWong/com.eitan.easymic/issues">GitHub Issues</a><br>
        <em>请先查看 <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/troubleshooting.md">故障排除</a></em>
      </td>
      <td align="center" width="33%">
        💬<br>
        <strong>社区讨论</strong><br>
        <a href="https://github.com/EitanWong/com.eitan.easymic/discussions">GitHub Discussions</a><br>
        <em>分享项目并获得帮助</em>
      </td>
      <td align="center" width="33%">
        📧<br>
        <strong>专业支持</strong><br>
        <a href="mailto:unease-equity-5c@icloud.com">邮件</a> | B站私信<br>
        <em>技术支持与 EasyMic APM 咨询</em>
      </td>
    </tr>
  </table>
</div>

---

## 设计原则

<div align="center">
  <table>
    <tr>
      <td align="center" width="25%">
        <strong>回调路径保持轻量</strong><br>
        <em>设备回调只承担必要的传输工作。</em>
      </td>
      <td align="center" width="25%">
        <strong>worker 承担处理工作</strong><br>
        <em>高层处理逻辑不运行在设备回调中。</em>
      </td>
      <td align="center" width="25%">
        <strong>行为可观测</strong><br>
        <em>提供用于分析延迟和 glitch 的诊断计数器。</em>
      </td>
      <td align="center" width="25%">
        <strong>贴近 Unity 工作流</strong><br>
        <em>示例、组件和文档与包结构保持一致。</em>
      </td>
    </tr>
  </table>
</div>

---

<div align="center">
  <h2>开始使用 Easy Mic</h2>
  <p><em>导入包、打开示例，并在目标设备上结合诊断数据完成验证。</em></p>
  
  <p>
    <a href="EasyMic/Packages/com.eitan.easymic/Documentation~/zh-CN/getting-started.md">
      <img src="https://img.shields.io/badge/📘_立即开始-blue.svg?style=for-the-badge" alt="立即开始">
    </a>
    <a href="#-示例项目总览">
      <img src="https://img.shields.io/badge/🚀_查看示例-green.svg?style=for-the-badge" alt="查看示例">
    </a>
    <a href="mailto:unease-equity-5c@icloud.com">
      <img src="https://img.shields.io/badge/💎_联系获取APM-gold.svg?style=for-the-badge" alt="联系APM">
    </a>
  </p>
  
  <hr>
  
  <p>
    <strong>Made with ❤️ by <a href="https://github.com/EitanWong">Eitan</a></strong><br>
    <em>如果 Easy Mic 对您的项目有帮助，请给我们一个 ⭐！</em>
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
