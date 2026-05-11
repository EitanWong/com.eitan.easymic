# Getting Started

This page shows the shortest path to recording and playback with the current EasyMic API.

## Requirements

- Unity `2021.3` or newer.
- Package name: `com.eitan.easymic`.
- Microphone permission before recording on Android, iOS, and sandboxed desktop targets.

## Install

Unity Package Manager:

```text
https://github.com/EitanWong/com.eitan.easymic.git#upm
```

OpenUPM:

```bash
openupm add com.eitan.easymic
```

Samples can be imported from the package entry in Unity Package Manager.

## Record With the API

This example records from the default device, keeps a `Capturer` processor alive while recording, and reads the captured clip before stopping the session.

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

## Record With a Component

Use `EasyMicrophone` for scene-driven recording, device selection UI, temporary WAV recording, and Unity events.

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

## Play a Clip

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

## Stream PCM Playback

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

## Choose a Latency Profile

Use `LowLatency` for most desktop interactive work. Use `Balanced` for mobile or when Unity work can spike. Use `UltraLowLatency` only after measuring on target hardware. Use `Stable` / `SafeStreaming` when continuity matters more than minimum delay.

Android playback defaults to `Balanced`; other playback defaults use `LowLatency`.

## Check Diagnostics

Capture:

```csharp
var info = EasyMicAPI.GetRecordingInfo(handle);
Debug.Log($"Callbacks={info.Telemetry.CallbackCount}, dropped={info.Telemetry.FramesDropped}, overrun={info.Telemetry.TransportOverruns}");
```

Playback:

```csharp
var audioSystem = AudioSystem.Instance;
var telemetry = audioSystem.Telemetry;
Debug.Log($"Underruns={telemetry.TransportUnderruns}, zeroFill={telemetry.ZeroFilledFrames}");
```

## Next

- [Recording](recording.md)
- [Playback](playback.md)
- [Architecture](architecture.md)
- [Diagnostics](diagnostics.md)
