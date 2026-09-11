# EasyMic AI 语音对话案例

[English](README.md) | 简体中文

`Samantha` 是一个可运行的实时 AI 语音对话案例，也可以直接作为语音 Agent 的项目脚手架。它在一个场景中串联了本地语音识别、OpenAI API 兼容的 LLM、按句语音合成、对话打断、UI 状态和角色动画反馈。

入口场景：`Samantha/Samantha.unity`

## 快速开始

1. 从 EasyMic Package Samples 导入 **AIChat Example**。
2. 在 Unity 中打开 `Tools > EasyMic > AI Chat > Setup`。
3. 如果窗口提示缺少依赖，安装 `com.eitan.sherpa-onnx-unity`。
4. 通过 Setup 窗口打开 `Samantha/Samantha.unity`。
5. 打开 `Tools > EasyMic > AI Chat > Provider Setup`。
6. 选择 `SiliconFlow` 或 `OpenAI 兼容 / 自定义`。导入场景和预设已经使用公共的当前默认值。
7. 填写 API 地址、模型、音色和 API Key，点击 **应用场景并保存设备配置**，再使用 `Cmd/Ctrl+S` 保存场景。
8. 进入 Play Mode 后对麦克风说话。案例默认启用了主动问候，因此第一次 LLM/TTS 请求可能在你开口前就开始。

第一个 Setup 窗口使用独立程序集，不依赖 Sherpa，因此案例刚导入、语音依赖尚未安装时也能打开。依赖安装并编译完成后，Provider Setup 才会出现。

## 环境要求

- EasyMic 和当前 AIChat Example。
- `com.eitan.sherpa-onnx-unity`，用于 ASR 和可选的本地 TTS。
- 目标平台的麦克风权限。
- 远程 LLM 的 API 地址和 API Key。
- 关闭 `Use Local TTS` 时，同一 Provider 还需要支持兼容的远程 TTS 接口。

可以使用以下 Git URL 安装 Sherpa Package，或将条目加入 `Packages/manifest.json`：

```json
"com.eitan.sherpa-onnx-unity": "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm"
```

缺少依赖时，Setup 入口仍然可用，但 AIChat 运行时代码、ASR 和本地 TTS 不会编译。

## Provider 设置

案例使用同一个 `ApiBaseUrl` 和 API Key 访问 LLM 与远程 TTS。Provider Setup 会把 Provider 默认值写入 `AIChatConfigurationPolicy`，再把最终 Provider 与设备设置保存到运行时配置文件。工具只会把场景标记为已修改，场景文件仍由开发者自行保存，便于检查版本控制差异。

### SiliconFlow

在 Provider Setup 中选择 `SiliconFlow` 并应用预设，会填入以下默认值：

| 设置 | 默认值 |
| --- | --- |
| API Base URL | `https://api.siliconflow.cn/v1/` |
| LLM 模型 | `Qwen/Qwen3.5-9B` |
| 远程 TTS 模型 | `FunAudioLLM/CosyVoice2-0.5B` |
| 远程 TTS 音色 | `FunAudioLLM/CosyVoice2-0.5B:alex` |

只有在 SiliconFlow 账号或产品需要其他音色时才需要修改。导入场景与预设使用相同的公共音色。

`SiliconFlowExpressiveTtsInputPlugin` 是可选插件。只有当 Host 是 SiliconFlow 且模型属于 CosyVoice 时，它才会格式化带有表现力指令的 TTS 输入。

### 其他 OpenAI API 兼容 Provider

选择 `OpenAI 兼容 / 自定义`，再用 Provider 控制台中的值替换所有字段。请使用 HTTPS，并在 Base URL 中包含 API 前缀，例如 `https://HOST/v1/`。没有路径时，Client 默认使用 `v1/`；只要配置了非空路径，就会把它作为权威前缀。只有 `localhost` 等本机回环开发地址可以使用明文 HTTP。

自定义 Host 会直接使用流式 Chat Completions。官方 OpenAI Host 使用 Responses API，SiliconFlow 则使用专用的 Chat Completions Adapter。当前 OpenAI 预设为 `gpt-5.4`、`gpt-4o-mini-tts` 和 `marin` 音色。鉴权格式固定为 `Authorization: Bearer <key>`；在配置的 Base URL 下使用 `chat/completions` 或 `responses`，远程语音使用 `audio/speech`。

通用远程 TTS 请求只包含标准字段：`model`、`input`、`voice`、`response_format` 和 `speed`。脚手架请求单声道 PCM，并接受以下响应之一：

- 24 kHz 的裸 PCM16 数据。
- 自带格式信息的 WAV 数据。

SiliconFlow 专用 Adapter 会额外发送 `sample_rate`、`gain` 和 `stream`。如果 Provider 使用不同的请求字段、SSE 事件或音频格式，需要扩展 `IOpenAIProviderAdapter` 和 `OpenAIProviderAdapterResolver`。不同鉴权方式还需要扩展 Client 的凭证/Header 处理；不要把 Key 放入 URL。

如果 Provider 只有 LLM、没有兼容的远程 TTS，请启用 **Use Local TTS** 并配置 `SpeechSynthesizer`。本地 TTS 只替换语音输出，LLM 仍然需要远程 API 地址和 Key。当前脚手架没有把 LLM 与远程 TTS 拆成两个独立 Provider。

## 凭证与配置优先级

Provider Setup 不会把 API Key 写入场景或 `AIChatConfigurationPolicy`，而是以明文写入：

```text
Application.persistentDataPath/<AIChatControllerConfig.RuntimeConfigFileName>
```

默认文件名是 `ai_chat_config.json`。这是当前设备的应用数据目录，不应把该文件复制到版本控制、截图或构建产物中。生产项目应让后端代理持有 Provider Key，或在 Controller 初始化前注入短期凭证，并替换案例中的持久化策略。

运行时配置使用 schema v4，并且只读取这一精确契约。其他 schema 的文件会由当前默认值替换，不进行转换。Provider Setup 和运行时设置面板是显式持久化入口；通过 `SetApiKey()` 注入的 Key 保持为临时值。

启动时按以下顺序解析最终配置：

1. 场景中的 `AIChatControllerConfig` 提供组件引用和基线默认值。
2. 启用的 `AIChatConfigurationPolicy` 应用可复用的场景和部署默认值。
3. 设备 JSON 最后应用 API Key、Provider、模型、麦克风、ASR 和本地 TTS 等设备设置。
4. 在 `Awake` 前通过 `SetApiKey()` 注入的非空 Key 是最终内存凭证，并且不会写入 JSON。
5. Controller 使用最终结果初始化 API Client、ASR 和 TTS Pipeline。

因此场景 Policy 保持为可复用默认值，而 `AIChatRuntimeConfigPanel` 保存的设备设置在下次启动后仍然有效。运行时面板会写入 JSON 并立即应用，不依赖场景重载或 Build Settings。如果本地模型仍在加载时再次保存，将以最新设置为准，并取消前一次应用操作。

## 编辑器工具

Setup 和 Provider Setup 会自动读取 Unity Editor 的语言。简体中文编辑器显示中文，其他语言回退英文。

| 菜单或 Inspector | 主要用途 |
| --- | --- |
| `AIChatSampleReadme.asset` | 在 Inspector 中显示本地化的 ScriptableObject README，并提供场景、Setup、Provider 和完整文档入口。 |
| `Tools > EasyMic > AI Chat > Setup` | 检查依赖、复制安装地址、打开匹配的导入场景和本地化 README。未安装 Sherpa 时也可用。 |
| `Tools > EasyMic > AI Chat > Provider Setup` | 配置 SiliconFlow/自定义 Provider、重新应用预设、场景 Policy、设备 JSON、API Key 和组件引用。 |
| `AIChatController` Inspector | 配置场景基线、查看校验信息、定位运行时配置，并在 Play Mode 查看诊断数据。 |
| `AIChatConfigurationPolicy` Inspector | 配置 OpenAI、SiliconFlow、Local TTS 或字段级覆盖策略。 |
| 游戏内 `AIChatRuntimeConfigPanel` | 配置设备 API、模型、麦克风、ASR 和本地 TTS；保存时写入 schema v4 JSON 并立即应用。 |

运行时面板不会管理全部场景行为。System Prompt、历史轮数、流式 TTS、打断策略和诊断选项仍由 Controller Inspector 或 Configuration Policy 管理。

## 本地语音音量与调试

1. 在 Provider Setup 选择本地语音，点击 SpeechSynthesizer 定位模型组件；确认 VoiceMicrophone 输入设备、ASR 模型和权限。外放时启用 APM/AEC。
2. 保持 **自动均衡音量 / Normalize Output** 开启，**播放音量 / Playback Volume** 从 1 开始（0 静音，2 最多增加约 6 dB）。这两个选项也能在 SpeechSynthesizer Inspector 调整。
3. 采用模型原生采样率，例如 vits-melo-tts-zh_en 为 44100 Hz；播放链路负责设备格式转换。降低配置的采样率不会加速模型推理。
4. 点击 **应用场景并保存设备配置**，保存场景，进入 Play Mode 等待模型加载完成。先保持安静，确认主动问候完整播完，再在播放期间说话，确认可以及时打断。
5. 排查问题时开启 **开发者调试模式 / Debug Mode**，按 F12 查看流水线延迟与打断；在 SpeechSynthesizer Inspector 查看输入 RMS、输出峰值和实际增益。需要逐字转写或远程音频诊断时，在 Controller Inspector 分别开启 Verbose Streaming Log 或 TTS Diagnostics。
6. 排查后关闭 Debug Mode。调试日志、流水线采集及 F12 面板随之关闭；实际故障与配置错误仍会报告。设备配置优先于场景默认值，要保持下次启动的设置，请通过 Provider Setup 保存。

均衡算法采用受限 RMS 自动增益：目标 -20 dBFS、最大提升 24 dB、-55 dBFS 静音门限、快降慢升的增益平滑，最后通过 -1 dBFS 采样峰值限制。关闭均衡后峰值保护仍有效。算法参考 [WebRTC 自适应数字增益控制](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/audio_processing/agc2/adaptive_digital_gain_controller.cc) 和 [Limiter](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/audio_processing/agc2/limiter.cc) 的增益约束与限制思路。

处理在已有 20 ms 播放块上原地执行，不增加整句分析或额外排队；处理后的同一信号供播放和 AEC 参考。它减少模型间电平差异，不是 LUFS/真峰值归一化，也不会修复模型本身已产生的削波失真。最终混音和设备音量仍需在目标平台试听。旧 schema v4 缺少新增字段时保留场景默认值。

## 模块职责

| 模块 | 主要职责 |
| --- | --- |
| `AIChatController/` | 项目组合入口。启动麦克风、接收识别文本、协调 LLM 回合、处理取消和 Barge-in，并发布对话状态。 |
| `AIChatControllerConfig` | Inspector 中可编辑的基线配置，包括组件引用、端点、模型、TTS 模式、对话行为和运行时文件名。 |
| `AIChatConfigurationPolicy` | 可复用的场景级 Provider/部署默认值；设备运行时配置会在其后应用。 |
| `AIChatProviderPreset` | Setup 与 Policy 共用的 SiliconFlow/OpenAI 默认值，避免端点和模型配置漂移。 |
| `RuntimeConfig/` | `AIChatRuntimeConfig` 与 `JsonAIChatRuntimeConfigStore`，负责 schema v4 设备设置的校验、读取和保存。 |
| `OpenAI/` | OpenAI API 兼容 HTTP Client、SSE Reader、请求模型和 Provider Adapter。 |
| `ChatTtsPipeline/` | 把流式回答转换为句子任务，并行生成本地或远程语音，管理缓冲与播放。 |
| `StreamingSentenceAssembler` | 把 LLM Token 流拆成可朗读句子，降低首句音频延迟。 |
| `Plugins/` | 主动对话、SiliconFlow 表现力 TTS，以及业务扩展点。 |
| `Prompts/` | 可复用的 System、Greeting 和 Idle Prompt Profile。 |
| `UI/` | Loading、错误、运行时设置和语音可视化。产品项目可以整体替换。 |
| `Animator/` | 把 Controller/音频状态映射到 Samantha 角色，只负责表现。 |
| `Diagnostics/` | Pipeline 指标和调试工具。 |
| `Code/Editor/Bootstrap/` | 不依赖 Sherpa 的 Setup 入口、共享 Editor 样式/本地化，以及 ScriptableObject README Inspector。 |

## 对话数据流

```text
VoiceMicrophone
  -> ASR 转写提交
  -> AIChatController
  -> AIChatRequestOrchestrator + OpenAICompatibleClient
  -> StreamingSentenceAssembler
  -> ChatTtsPipeline
  -> 本地或远程语音播放
  -> UI / Animator 反馈
```

`AIChatController` 是产品 UI 的主要集成边界。常用接口包括 `SubmitUserMessage`、`CancelResponseAsync`、`OnChatStateChanged` 和 `OnAssistantAudioQueued`。

## 作为项目脚手架使用

1. 把 `Samantha/Samantha.unity` 和需要的配套资源复制到自己的 Sample 或游戏目录。
2. 保留 `AIChatController`、`VoiceMicrophone` 和远程播放链路；使用本地 TTS 时保留 `SpeechSynthesizer`。
3. 对复制后的 Controller 运行 Provider Setup，让 Policy 和设备运行时配置指向新场景。
4. 用自己的 UI、角色或玩法监听器替换 `AIChatUIView` 和 `SamanthaAnimator`。它们不是 LLM Pipeline 的必需部分。
5. 替换 `PromptProfile`，并按产品需求调整 `MaxHistoryTurns`、Turn Detection Delay 和 Barge-in。
6. 通过实现 `IAIChatPlugin` 增加业务行为，避免直接修改 Controller 主流程。
7. 自定义脚本使用 asmdef 时，引用 `Eitan.EasyMic.Demo.AIChat.Samantha`。

抽取脚手架时按以下层级保留组件：

- 必需：`AIChatController`、`AIChatControllerConfig.Microphone -> VoiceMicrophone`，以及有效的 LLM 地址、Key 和模型。
- 本地 TTS 必需：`SpeechSynthesizer` 及其 Playback Source。
- 推荐：`PromptProfile`。缺少时仍可对话，但不会注入 System Prompt。
- 可选：`AIChatConfigurationPolicy`、运行时面板、UI、Animator、Diagnostics、主动对话和表现力 TTS 插件。

远程 TTS 会自动创建 EasyMic Playback Stream，不要求场景中预先放置 Playback Source。

Provider Setup 会从 Controller 自身、子对象和父对象中补齐缺失的 `VoiceMicrophone` 与 `SpeechSynthesizer` 引用。它不会创建语音组件或模型资源；这些内容仍需通过 EasyMic/Sherpa 工作流准备。

## 常见问题

| 现象 | 检查项 |
| --- | --- |
| 找不到 `Setup` 菜单 | 重新导入 AIChat Example，并等待 Unity 完成编译。 |
| Provider Setup 尚未出现 | 安装 `com.eitan.sherpa-onnx-unity`，等待脚本编译结束。 |
| API Key 错误 | 通过 Provider Setup 或运行时面板保存 Key，并在 Controller Inspector 中确认实际文件路径。 |
| LLM 请求失败 | 检查 Base URL、模型、Key 和当前 Provider 契约：官方 OpenAI 使用 Responses，SiliconFlow/自定义 Host 使用 Chat Completions。 |
| 有文字但没有声音 | 远程 TTS 检查模型、账号音色、`audio/speech` 以及 PCM/WAV 契约；本地 TTS 检查 `SpeechSynthesizer`。 |
| 没有转写 | 检查系统麦克风权限、设备选择、ASR 模型和 `VoiceMicrophone` 引用。 |
| 运行时设置没有更新 | 确认面板引用当前活动的 `AIChatController`，再在 Console 中检查保存路径或校验错误。 |
| 尚未说话就产生请求 | 案例启用了 `ProactiveConversationPlugin`，初始化后会发送主动问候。产品不需要时关闭该行为。 |
| 自定义 Provider 返回 `400`/`404` | 检查配置的 API 前缀、Bearer 鉴权、Chat Completions 路径和模型。自定义 Host 不要求 Responses API。 |
| 助手反复打断自己 | 确认 APM/AEC，关闭旁边其他设备声音，分别测试外放与耳机；开启 Debug Mode 查看 BargeIn 时刻、缓冲和采集丢帧，并保留真人打断测试。 |
| 首次 ASR/本地 TTS 很慢 | Sherpa 模型首次使用可能需要下载和解压，请预留网络、磁盘空间并观察加载进度。 |

## 发布前检查

- 把 Provider Key 移出本地 JSON，交给后端代理或产品自己的凭证系统。
- 在 Controller 初始化前注入凭证，或替换案例持久化流程，避免生产凭证再次写回设备 JSON。
- 在生产环境关闭 `DebugMode`。Console、诊断 Payload 和生成的 WAV 文件名可能包含转写、Prompt 或回答内容。
- 在每个目标平台测试麦克风权限和设备选择。
- 使用真实 Provider 账号与模型测试打断、网络失败、长回答，以及本地和远程两种 TTS 模式。
- 替换案例 UI/角色资源，并检查全部 Prompt 是否符合产品用途。
