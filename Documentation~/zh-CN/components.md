← [入门指南](getting-started.md) | [文档首页](../README.md) | [English Version](../en/components.md) →

# 🧩 Mono 组件指南

本页聚焦 `Runtime/Unity/Components` 下的组件化工作流。

## 范围

- `EasyMicrophone` (`Eitan.EasyMic.Runtime.Mono`)
- `VoiceMicrophone` (`Eitan.EasyMic.Runtime.Mono.Components.ASR`)
- `PlaybackAudioSourceBehaviour` (`Eitan.EasyMic.Runtime.Mono.Components`)
- `SpeechSynthesizer` (`Eitan.EasyMic.Runtime.Mono.Components.TTS`)

## 1) EasyMicrophone

`EasyMicrophone` 是基于 GameObject 的基础录音组件。

### 主要能力

- 设备枚举：`AvailableDevices`
- 启停录音：`StartRecording`、`StopRecording`
- 运行时热插拔处理器：`AppendProcessor`、`RemoveProcessor`
- 保存最近一次临时录音：`TrySaveLatestRecording`

### 关键事件

- `OnMicrophoneInitialized(bool initialized)`
- `OnRecordingStateChanged(bool isRecording)`

### 典型用法

```csharp
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono;
using UnityEngine;

public class MicRecorderExample : MonoBehaviour
{
    [SerializeField] private EasyMicrophone microphone;

    private void Awake()
    {
        microphone.OnMicrophoneInitialized += initialized =>
            Debug.Log($"麦克风初始化: {initialized}");
        microphone.OnRecordingStateChanged += isRecording =>
            Debug.Log($"录音状态: {isRecording}");
    }

    public void BeginRecording() => microphone.StartRecording();

    public void EndRecordingAndSave()
    {
        microphone.StopRecording();
        microphone.TrySaveLatestRecording($"{Application.persistentDataPath}/voice.wav");
    }
}
```

## 2) VoiceMicrophone（ASR）

`VoiceMicrophone` 继承自 `EasyMicrophone`，负责 Sherpa-ONNX ASR 的模型生命周期和麦克风驱动识别流程。

> 需要 `EITAN_SHERPA_ONNX_UNITY_PRESENT` 编译宏以及 `com.eitan.sherpa-onnx-unity`。

### 主要能力

- 预设化 ASR 配置（`AsrConfig`、`TrySetActivePreset`）
- 实时/最终识别事件
- 语音活动与关键词状态事件
- 模型生命周期控制（`ReloadModels`、`DisposeModels`）

### 关键事件

- `OnASRTranscriptionStreaming(string text)`
- `OnASRTranscriptionSubmit(string text)`
- `OnVoiceActivityChanged(bool isActive)`
- `OnKeywordActivityChanged(string keyword, bool active)`
- `OnLoadingProgressFeedback(string message, float progress)`

### 典型用法

```csharp
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using UnityEngine;

public class VoiceMicExample : MonoBehaviour
{
    [SerializeField] private VoiceMicrophone voiceMic;

    private void OnEnable()
    {
        voiceMic.OnASRTranscriptionStreaming += partial =>
            Debug.Log($"[实时] {partial}");
        voiceMic.OnASRTranscriptionSubmit += finalText =>
            Debug.Log($"[最终] {finalText}");
    }

    public void StartVoiceInput()
    {
        if (!voiceMic.Initialized)
        {
            voiceMic.Init();
        }
        voiceMic.StartRecording();
    }

    public void StopVoiceInput() => voiceMic.StopRecording();
}
```

## 3) PlaybackAudioSourceBehaviour

面向 Unity 组件层的播放封装，底层基于 `PlaybackAudioSession`。

### 主要能力

- 音频片段播放：`PlayClip`、`Play`、`Pause`、`Stop`
- 流式入队：`Enqueue`、`CompleteStream`
- 运行时控制：`Volume`、`Mute`、`Solo`、`Loop`

### 关键事件

- `OnAudioPlaybackRead(float[] data, int channels, int sampleRate)`
- `OnPlaybackCompleted(PlaybackAudioSourceBehaviour source)`

### 流式播放模式

1. 先 `Play()`，确保播放会话已激活
2. 持续 `Enqueue(...)` 推入PCM数据
3. 上游流结束时调用 `CompleteStream()`

## 4) SpeechSynthesizer（TTS）

队列化 TTS 组件，会将文本合成并推流到 `PlaybackAudioSourceBehaviour`。

> 需要 `EITAN_SHERPA_ONNX_UNITY_PRESENT` 编译宏以及 `com.eitan.sherpa-onnx-unity`。

### 主要能力

- 模型初始化（`Init`、`OnSynthesizerInitialized`）
- 入队接口（`EnqueueSentence`、`EnqueueText`）
- 会话停止/取消（`Stop`、`StopAndWaitAsync`）
- 通过 `SpeechSynthesizerConfiguration` 管理预设

### 典型用法

```csharp
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;

public class TtsExample : MonoBehaviour
{
    [SerializeField] private SpeechSynthesizer synthesizer;

    private void Start()
    {
        synthesizer.OnSynthesizerInitialized += ok =>
            Debug.Log($"TTS 初始化: {ok}");

        synthesizer.Init();
    }

    public void Speak(string text)
    {
        if (!synthesizer.Initialized) return;
        synthesizer.EnqueueText(text);
    }
}
```

## 组件选型建议

- 只需要录音和本地捕获：用 `EasyMicrophone`
- 需要 ASR/关键词/轮次检测：用 `VoiceMicrophone`
- 需要低延迟片段或流式播放：用 `PlaybackAudioSourceBehaviour`
- 需要场景内队列化 TTS：用 `SpeechSynthesizer`
