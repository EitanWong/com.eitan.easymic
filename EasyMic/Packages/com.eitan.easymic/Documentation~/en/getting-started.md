← [Documentation Home](../README.md) | [中文版本](../zh-CN/getting-started.md) →

# 🚀 Getting Started with Easy Mic

Welcome to Easy Mic! This guide will help you set up and start using Easy Mic in your Unity project.

## 📋 Prerequisites

- **Unity 2021.3 LTS** or higher
- **Supported Platforms**: Windows, macOS, Linux, Android, iOS
- **Microphone access** on your target platform

## 📦 Installation

### Method 1: Unity Package Manager (Recommended)

1. Open Unity Package Manager (`Window > Package Manager`)
2. Click the `+` button in the top-left corner
3. Select `Add package from git URL...`
4. Enter: `https://github.com/EitanWong/com.eitan.easymic.git#upm`
5. Click `Add`

### Method 2: OpenUPM

```bash
openupm add com.eitan.easymic
```

### Method 3: Manual Installation

1. Download the latest release from [GitHub Releases](https://github.com/EitanWong/com.eitan.easymic/releases)
2. Extract to your project's `Packages` folder
3. Unity will automatically detect and import the package

## 🎯 Your First Recording

Let's create a simple script that records 5 seconds of audio:

```csharp
using Eitan.EasyMic.Runtime;
using UnityEngine;

public class FirstRecording : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private AudioWorkerBlueprint _bpCapture;

    void Start()
    {
        // 0) Permission (especially on Android)
        if (!PermissionUtils.HasPermission())
        {
            Debug.LogError("❌ Microphone permission not granted.");
            return;
        }

        // 1) Devices
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        if (devices.Length == 0)
        {
            Debug.LogError("❌ No microphone devices found!");
            return;
        }

        // 2) Start with a simple pipeline via blueprints
        _bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(5), key: "capture");
        _recordingHandle = EasyMicAPI.StartRecording(
            devices[0].Name,
            SampleRate.Hz48000,
            devices[0].GetDeviceChannel(),
            new[]{ _bpCapture }
        );

        if (!_recordingHandle.IsValid)
        {
            Debug.LogError("❌ Failed to start recording!");
            return;
        }

        Debug.Log("🎙️ Recording started for 5 seconds...");
        Invoke(nameof(StopRecording), 5f);
    }

    void StopRecording()
    {
        if (!_recordingHandle.IsValid) return;
        EasyMicAPI.StopRecording(_recordingHandle);

        // Retrieve the concrete worker instance for this session
        var capturer = EasyMicAPI.GetProcessor<AudioCapturer>(_recordingHandle, _bpCapture);
        var clip = capturer?.GetCapturedAudioClip();
        if (clip != null)
        {
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null) audioSource.PlayOneShot(clip);
            Debug.Log($"✅ Recording complete! Duration: {clip.length:F2}s");
        }

        _recordingHandle = default;
    }
}
```

## 🎮 Setting Up the Scene

1. **Create a new GameObject** in your scene
2. **Add the script** `FirstRecording` to the GameObject
3. **Add an AudioSource component** (optional, for playback)
4. **Press Play** and speak into your microphone!

## 📱 Platform-Specific Setup

### 🖥️ Desktop (Windows/macOS/Linux)
No additional setup required. Unity will automatically request microphone permissions when needed.

### 📱 Mobile (Android/iOS)

#### Android
Add to your `AndroidManifest.xml`:
```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

#### iOS  
Easy Mic will automatically request microphone permissions. You can also add a usage description in your `Info.plist`:
```xml
<key>NSMicrophoneUsageDescription</key>
<string>This app needs microphone access to record audio.</string>
```

## 🔧 Troubleshooting

### No Microphone Devices Found
- Ensure your microphone is connected and enabled
- Check system permissions for microphone access
- Try refreshing with `EasyMicAPI.Refresh()`

### Recording Fails to Start
- Verify microphone permissions are granted
- Check if another application is using the microphone
- Try a different sample rate or channel configuration

### No Audio Captured
- Ensure you're speaking into the microphone
- Check system microphone levels
- Verify the `AudioCapturer` is added to the pipeline before recording starts

## 📖 What's Next?

Now that you have basic recording working, explore these topics:

- **[Core Concepts](core-concepts.md)** - Understand EasyMic's architecture
- **[Audio Pipeline](audio-pipeline.md)** - Learn about the processing pipeline
- **[Built-in Processors](processors.md)** - Discover all available processors
- **[Examples](examples.md)** - See more complex use cases

## 🆘 Need Help?

- 📖 Check the [Troubleshooting Guide](troubleshooting.md)
- 🐛 Report issues on [GitHub Issues](https://github.com/EitanWong/com.eitan.easymic/issues)
- 💬 Join discussions on [GitHub Discussions](https://github.com/EitanWong/com.eitan.easymic/discussions)

---

← [Documentation Home](../README.md) | **Next: [Core Concepts](core-concepts.md)** →
