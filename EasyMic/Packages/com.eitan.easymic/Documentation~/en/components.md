← [Getting Started](getting-started.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/components.md) →

# 🧩 Mono Components Guide

This page focuses on component-based workflows under `Runtime/Mono/Components`.

## Scope

- `EasyMicrophone` (`Eitan.EasyMic.Runtime.Mono`)
- `VoiceMicrophone` (`Eitan.EasyMic.Runtime.Mono.Components.ASR`)
- `PlaybackAudioSourceBehaviour` (`Eitan.EasyMic.Runtime.Mono.Components`)
- `SpeechSynthesizer` (`Eitan.EasyMic.Runtime.Mono.Components.TTS`)

## 1) EasyMicrophone

`EasyMicrophone` is the base recorder component for GameObject workflows.

### Main capabilities

- Device discovery via `AvailableDevices`
- Start/stop capture (`StartRecording`, `StopRecording`)
- Runtime processor hot-plug (`AppendProcessor`, `RemoveProcessor`)
- Save latest temporary recording (`TrySaveLatestRecording`)

### Key events

- `OnMicrophoneInitialized(bool initialized)`
- `OnRecordingStateChanged(bool isRecording)`

### Typical usage

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
            Debug.Log($"Microphone initialized: {initialized}");
        microphone.OnRecordingStateChanged += isRecording =>
            Debug.Log($"Recording: {isRecording}");
    }

    public void BeginRecording() => microphone.StartRecording();

    public void EndRecordingAndSave()
    {
        microphone.StopRecording();
        microphone.TrySaveLatestRecording($"{Application.persistentDataPath}/voice.wav");
    }
}
```

## 2) VoiceMicrophone (ASR)

`VoiceMicrophone` extends `EasyMicrophone` and manages Sherpa-ONNX ASR model lifecycle + microphone-driven transcription.

> Requires `EASYMIC_SHERPA_ONNX_INTEGRATION` and `com.eitan.sherpa-onnx-unity`.

### Main capabilities

- Preset-based ASR config (`AsrConfig`, `TrySetActivePreset`)
- Streaming/final transcript events
- Voice activity + keyword activity events
- Model lifecycle controls (`ReloadModels`, `DisposeModels`)

### Key events

- `OnASRTranscriptionStreaming(string text)`
- `OnASRTranscriptionSubmit(string text)`
- `OnVoiceActivityChanged(bool isActive)`
- `OnKeywordActivityChanged(string keyword, bool active)`
- `OnLoadingProgressFeedback(string message, float progress)`

### Typical usage

```csharp
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using UnityEngine;

public class VoiceMicExample : MonoBehaviour
{
    [SerializeField] private VoiceMicrophone voiceMic;

    private void OnEnable()
    {
        voiceMic.OnASRTranscriptionStreaming += partial =>
            Debug.Log($"[Partial] {partial}");
        voiceMic.OnASRTranscriptionSubmit += finalText =>
            Debug.Log($"[Final] {finalText}");
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

Unity-facing playback wrapper around `PlaybackAudioSession`.

### Main capabilities

- Clip playback (`PlayClip`, `Play`, `Pause`, `Stop`)
- Streaming enqueue API (`Enqueue`, `CompleteStream`)
- Runtime controls (`Volume`, `Mute`, `Solo`, `Loop`)

### Key events

- `OnAudioPlaybackRead(float[] data, int channels, int sampleRate)`
- `OnPlaybackCompleted(PlaybackAudioSourceBehaviour source)`

### Streaming usage pattern

1. `Play()` to ensure session/audio source is active
2. Continuously `Enqueue(...)`
3. Call `CompleteStream()` when upstream stream ends

## 4) SpeechSynthesizer (TTS)

Queue-based TTS component that synthesizes text and streams it to `PlaybackAudioSourceBehaviour`.

> Requires `EASYMIC_SHERPA_ONNX_INTEGRATION` and `com.eitan.sherpa-onnx-unity`.

### Main capabilities

- Model initialization (`Init`, `OnSynthesizerInitialized`)
- Queue APIs (`EnqueueSentence`, `EnqueueText`)
- Session stop/cancel (`Stop`, `StopAndWaitAsync`)
- Preset config via `SpeechSynthesizerConfiguration`

### Typical usage

```csharp
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;

public class TtsExample : MonoBehaviour
{
    [SerializeField] private SpeechSynthesizer synthesizer;

    private void Start()
    {
        synthesizer.OnSynthesizerInitialized += ok =>
            Debug.Log($"TTS initialized: {ok}");

        synthesizer.Init();
    }

    public void Speak(string text)
    {
        if (!synthesizer.Initialized) return;
        synthesizer.EnqueueText(text);
    }
}
```

## Component Selection Tips

- Use `EasyMicrophone` if you only need recording and local capture.
- Use `VoiceMicrophone` if you need ASR/keyword/turn-detection orchestration.
- Use `PlaybackAudioSourceBehaviour` for low-latency clip/stream playback.
- Use `SpeechSynthesizer` for queued TTS playback in scene workflows.

