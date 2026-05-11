# 快速入门

本页展示使用当前 EasyMic API 进行录音和播放的最短路径。

## 要求

- Unity `2021.3` 或更新版本。
- 包名：`com.eitan.easymic`。
- Android、iOS 和 sandboxed desktop 目标上，录音前需要麦克风权限。

## 安装

Unity Package Manager：

```text
https://github.com/EitanWong/com.eitan.easymic.git#upm
```

OpenUPM：

```bash
openupm add com.eitan.easymic
```

Samples 可以从 Unity Package Manager 中的包条目导入。

## 使用 API 录音

这个示例从默认设备录音，在录音期间保持 `Capturer` 处理器存活，并在停止 session 前读取 captured clip。

```csharp
using Eitan.EasyMic;
using Eitan.EasyMic.Runtime;
using UnityEngine;

public sealed class EasyMicQuickRecord : MonoBehaviour
{
    private RecordingHandle _handle;
    private AudioWorkerBlueprint _captureBlueprint;

    private void Start()
    {
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogWarning("Microphone permission is not granted yet.");
            return;
        }

        EasyMicAPI.Refresh();
        if (EasyMicAPI.Devices.Length == 0)
        {
            Debug.LogWarning("No microphone devices found.");
            return;
        }

        _captureBlueprint = new AudioWorkerBlueprint(() => new Capturer(), "capture");
        _handle = EasyMicAPI.StartRecording(
            EasyMicAPI.Default,
            SampleRate.Hz48000,
            Channel.Mono,
            new[] { _captureBlueprint },
            EasyMicLatencyProfile.LowLatency);

        Invoke(nameof(FinishRecording), 3f);
    }

    private void FinishRecording()
    {
        if (!_handle.IsValid)
        {
            return;
        }

        var capturer = EasyMicAPI.GetProcessor<Capturer>(_handle, _captureBlueprint);
        AudioClip clip = capturer != null ? capturer.GetCapturedAudioClip() : null;

        EasyMicAPI.StopRecording(_handle);
        _handle = default;

        if (clip != null)
        {
            Debug.Log($"Captured {clip.length:0.00}s at {clip.frequency} Hz.");
        }
    }
}
```

## 使用组件录音

使用 `EasyMicrophone` 可进行 scene-driven 录音、设备选择 UI、临时 WAV 录音和 Unity 事件集成。

```csharp
using Eitan.EasyMic.Runtime.Mono;
using UnityEngine;

public sealed class ComponentRecordButton : MonoBehaviour
{
    [SerializeField] private EasyMicrophone microphone;

    public void ToggleRecording()
    {
        if (microphone.IsRecording)
        {
            microphone.StopRecording();
            AudioClip clip = microphone.LatestRecordingClip;
            Debug.Log(clip != null ? $"Recorded {clip.length:0.00}s." : "No clip captured.");
        }
        else
        {
            microphone.Init();
            microphone.StartRecording();
        }
    }
}
```

## 播放 Clip

```csharp
using Eitan.EasyMic.Runtime;
using UnityEngine;

public sealed class EasyMicPlayClip : MonoBehaviour
{
    [SerializeField] private AudioClip clip;
    private PlaybackHandle _handle;

    public void Play()
    {
        _handle = AudioPlayback.PlayClip(
            clip,
            loop: false,
            volume: 1f,
            autoDisposeOnComplete: true,
            latencyProfile: EasyMicLatencyProfile.LowLatency);
    }

    private void OnDestroy()
    {
        if (_handle.IsValid)
        {
            _handle.Dispose();
        }
    }
}
```

## 流式播放 PCM

```csharp
using Eitan.EasyMic.Runtime;
using UnityEngine;

public sealed class EasyMicStreamPlayback : MonoBehaviour
{
    private PlaybackHandle _stream;

    public void Begin()
    {
        _stream = AudioPlayback.CreateStream(1f, EasyMicLatencyProfile.Balanced);
    }

    public void Push(float[] interleavedSamples, int count, int channels, int sampleRate)
    {
        var result = _stream.TryEnqueue(interleavedSamples, count, channels, sampleRate);
        if (!result.Success)
        {
            Debug.LogWarning($"Playback enqueue status: {result.Status}");
        }
    }

    public void End()
    {
        _stream.CompleteStream();
    }
}
```

## 选择 Latency Profile

大多数桌面交互工作使用 `LowLatency`。移动端或 Unity 工作可能出现尖峰时使用 `Balanced`。只有在目标硬件上测量后才使用 `UltraLowLatency`。当连续性比最小延迟更重要时，使用 `Stable` / `SafeStreaming`。

Android 播放默认使用 `Balanced`；其他播放默认使用 `LowLatency`。

## 检查诊断

采集：

```csharp
var info = EasyMicAPI.GetRecordingInfo(handle);
Debug.Log($"Callbacks={info.Telemetry.CallbackCount}, dropped={info.Telemetry.FramesDropped}, overrun={info.Telemetry.TransportOverruns}");
```

播放：

```csharp
var audioSystem = AudioSystem.Instance;
var telemetry = audioSystem.Telemetry;
Debug.Log($"Underruns={telemetry.TransportUnderruns}, zeroFill={telemetry.ZeroFilledFrames}");
```

## 下一步

- [录音](recording.md)
- [播放](playback.md)
- [架构](architecture.md)
- [诊断](diagnostics.md)
