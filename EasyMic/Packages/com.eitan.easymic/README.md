<p align="right">
  <a href="README_zh-CN.md">中文</a>
</p>

# Easy Mic for Unity

<p align="center">
  <img src="Documentation~/images/easymic-logo.png" alt="Easy Mic Logo" width="200"/>
</p>

**Easy Mic** is a high-performance, low-latency audio recording plugin for Unity. It provides direct access to raw microphone data and introduces a programmable audio processing pipeline, allowing developers to chain built-in modules to create complex real-time audio workflows.

## ✨ Key Features

*   **🎤 Low-Latency Audio Recording**: Captures microphone audio with minimal delay by leveraging native backend libraries, ideal for real-time applications.
*   **🔊 RAW Audio Data**: Get direct access to the raw audio buffer from the microphone for full control over the sound data.
*   **⛓️ Programmable Processing Pipeline**: The core feature of Easy Mic. Dynamically build a chain of audio processors. You can add, remove, or reorder processors at any time.
*   **💻 Cross-Platform**: Unified API for major platforms including Windows, macOS, Linux, Android, and iOS.
*   **🧩 Built-in Processors**: Comes with a set of pre-built processors for common audio tasks.

## 🚀 The Audio Pipeline

Easy Mic's power comes from its programmable pipeline. When the microphone is active, it streams audio data through a series of "processors" that you define. Each processor receives the audio data, performs an operation, and passes the modified data to the next processor in the chain.

`Mic Input -> [Processor A] -> [Processor B] -> [Processor C] -> Final Output`

This allows you to easily combine the provided modules to fit your needs.

## 🛠️ Built-in Processors

Easy Mic includes the following modules out-of-the-box:

*   **`AudioCapturer`**: Captures the incoming audio data into a buffer or saves it to a file.
*   **`AudioDownmixer`**: Converts multi-channel audio (e.g., stereo) into single-channel audio (mono).
*   **`VolumeGateFilter`**: A noise gate that only allows audio to pass through if its volume is above a certain threshold.
*   **`SherpaRealtimeSpeechRecognizer`**: Provides real-time speech-to-text functionality using the Sherpa-ONNX engine. **Note:** This processor requires the [com.eitan.sherpa-onnx-unity](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity) plugin to be installed.

*(Note: The ability to create your own custom processors is planned for a future release.)*

## 📦 Installation

1.  Open the Unity Package Manager (`Window > Package Manager`).
2.  Click the `+` button in the top-left corner and select "Add package from git URL...".
3.  Enter the repository URL: `https://github.com/EitanWong/com.eitan.easymic.git`
4.  Click "Add".

## ▶️ Quick Start

Here is a basic example of how to record a 5-second audio clip.

```csharp
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Core.Processors;
using UnityEngine;

public class SimpleRecorder : MonoBehaviour
{
    private RecordingHandle _recordingHandle;
    private AudioCapturer _audioCapturer;
    private AudioClip _recordedClip;

    void Start()
    {
        // Refresh the device list to ensure it's up to date
        EasyMicAPI.Refresh();
        var devices = EasyMicAPI.Devices;
        if (devices.Length == 0)
        {
            Debug.LogError("No microphone devices found.");
            return;
        }

        // 1. Start recording with the default device and get a handle
        _recordingHandle = EasyMicAPI.StartRecording(devices[0].Name);

        if (!_recordingHandle.IsValid)
        {
            Debug.LogError("Failed to start recording.");
            return;
        }

        // 2. Create an AudioCapturer processor to capture the audio data
        _audioCapturer = new AudioCapturer(); 
        EasyMicAPI.AddProcessor(_recordingHandle, _audioCapturer);

        Debug.Log("Recording for 5 seconds...");

        // Stop recording after 5 seconds for this example
        Invoke(nameof(StopRecording), 5f);
    }

    void StopRecording()
    {
        if (!_recordingHandle.IsValid) return;

        // 3. Stop the recording via its handle
        EasyMicAPI.StopRecording(_recordingHandle);

        // 4. Get the captured audio clip from the processor
        _recordedClip = _audioCapturer.GetCapturedAudioClip();

        if (_recordedClip != null)
        {
            Debug.Log($"Recording finished. AudioClip created with length: {_recordedClip.length}s");
            // You can now play this clip with an AudioSource
            // GetComponent<AudioSource>().PlayOneShot(_recordedClip);
        }
        
        // Invalidate the handle
        _recordingHandle = default;
    }

    void OnDestroy()
    {
        // Ensure the recording is stopped when the object is destroyed
        if (_recordingHandle.IsValid)
        {
            EasyMicAPI.StopRecording(_recordingHandle);
        }
    }
}
```

## 📄 License

This project is licensed under the [GPLv3 License](LICENSE.md).