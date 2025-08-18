← [Built-in Processors](processors.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/api-reference.md) →

# ⚡ API Reference

Complete reference documentation for Easy Mic's public API.

## 🎪 EasyMicAPI

The main facade providing simple access to all Easy Mic functionality.

### Static Properties

#### `Devices`
```csharp
public static MicDevice[] Devices { get; }
```
Gets the list of available microphone devices. Call `Refresh()` first to update the list.

**Returns:** Array of `MicDevice` structures representing available microphones.

**Example:**
```csharp
EasyMicAPI.Refresh();
var devices = EasyMicAPI.Devices;
foreach (var device in devices)
{
    Debug.Log($"Device: {device.Name}, Default: {device.IsDefault}");
}
```

#### `IsRecording`
```csharp
public static bool IsRecording { get; }
```
Gets whether any recording sessions are currently active.

**Returns:** `true` if any recordings are active, `false` otherwise.

### Static Methods

#### `Refresh()`
```csharp
public static void Refresh()
```
Refreshes the list of available microphone devices. Call this before accessing `Devices`.

**Permissions:** Requires microphone permission.

**Example:**
```csharp
EasyMicAPI.Refresh();
Debug.Log($"Found {EasyMicAPI.Devices.Length} microphone devices");
```

---

#### `StartRecording()` - Default Device
```csharp
public static RecordingHandle StartRecording(SampleRate sampleRate = SampleRate.Hz16000)
```
Starts recording using the default microphone device.

**Parameters:**
- `sampleRate` - Audio sample rate (default: 16kHz)

**Returns:** `RecordingHandle` for managing the recording session.

**Example:**
```csharp
var handle = EasyMicAPI.StartRecording(SampleRate.Hz48000);
if (handle.IsValid)
{
    Debug.Log("Recording started with default device");
}
```

---

#### `StartRecording()` - By Name
```csharp
public static RecordingHandle StartRecording(string deviceName, 
    SampleRate sampleRate = SampleRate.Hz16000, 
    Channel channel = Channel.Mono)
```
Starts recording using a specific microphone device by name.

**Parameters:**
- `deviceName` - Name of the microphone device
- `sampleRate` - Audio sample rate (default: 16kHz)
- `channel` - Channel configuration (default: Mono)

**Returns:** `RecordingHandle` for managing the recording session.

**Example:**
```csharp
var handle = EasyMicAPI.StartRecording(
    "Built-in Microphone",
    SampleRate.Hz44100,
    Channel.Stereo
);
```

---

#### `StartRecording()` - By Device
```csharp
public static RecordingHandle StartRecording(MicDevice device, 
    SampleRate sampleRate = SampleRate.Hz16000, 
    Channel channel = Channel.Mono)
```
Starts recording using a specific microphone device.

**Parameters:**
- `device` - The microphone device to use
- `sampleRate` - Audio sample rate (default: 16kHz)
- `channel` - Channel configuration (default: Mono)

**Returns:** `RecordingHandle` for managing the recording session.

**Example:**
```csharp
var devices = EasyMicAPI.Devices;
var defaultDevice = devices.FirstOrDefault(d => d.IsDefault);
var handle = EasyMicAPI.StartRecording(defaultDevice, SampleRate.Hz48000);
```

---

#### `StartRecording()` - With Worker Blueprints (default device)
```csharp
public static RecordingHandle StartRecording(
    SampleRate sampleRate,
    IEnumerable<AudioWorkerBlueprint> workers)
```
Starts recording using the default device and binds a pipeline built from the provided worker blueprints.

#### `StartRecording()` - With Worker Blueprints (by name/device)
```csharp
public static RecordingHandle StartRecording(
    string deviceName, SampleRate sampleRate, Channel channel,
    IEnumerable<AudioWorkerBlueprint> workers)

public static RecordingHandle StartRecording(
    MicDevice device, SampleRate sampleRate, Channel channel,
    IEnumerable<AudioWorkerBlueprint> workers)
```

Note: Each recording session creates fresh worker instances from the blueprints. Use the same blueprint key to retrieve the instance later.

---

#### `DefaultWorkers`
```csharp
public static List<AudioWorkerBlueprint> DefaultWorkers { get; set; }
```
Optional global defaults applied by `StartRecording(...)` overloads that don’t explicitly pass `workers`.

---

#### `StopRecording()`
```csharp
public static void StopRecording(RecordingHandle handle)
```
Stops a specific recording session.

**Parameters:**
- `handle` - The recording handle to stop

**Example:**
```csharp
EasyMicAPI.StopRecording(recordingHandle);
```

---

#### `StopAllRecordings()`
```csharp
public static void StopAllRecordings()
```
Stops all active recording sessions.

**Example:**
```csharp
EasyMicAPI.StopAllRecordings();
```

---

#### `AddProcessor()`
```csharp
public static void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
```
Adds an audio processor (created from the blueprint) to a session at runtime. If a worker with the same blueprint key already exists, it is ignored.

**Parameters:**
- `handle` - The recording handle
- `processor` - The audio processor to add

**Example:**
```csharp
var capturer = new AudioCapturer(10);
EasyMicAPI.AddProcessor(recordingHandle, capturer);
```

---

#### `RemoveProcessor()`
```csharp
public static void RemoveProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint)
```
Removes an audio processor (by blueprint key) from a session's pipeline and disposes it.

**Parameters:**
- `handle` - The recording handle
- `processor` - The audio processor to remove

**Example:**
```csharp
EasyMicAPI.RemoveProcessor(recordingHandle, noisegateProcessor);
```

---

#### `GetRecordingInfo()`
```csharp
public static RecordingInfo GetRecordingInfo(RecordingHandle handle)
```
Gets information about a recording session.

**Parameters:**
- `handle` - The recording handle

**Returns:** `RecordingInfo` containing session details.

**Example:**
```csharp
var info = EasyMicAPI.GetRecordingInfo(recordingHandle);
Debug.Log($"Recording: {info.SampleRate}Hz, {info.ChannelCount} channels, {info.ProcessorCount} processors");
```

---

#### `Cleanup()`
```csharp
public static void Cleanup()
```
Cleans up all Easy Mic resources. Call this when shutting down your application.

**Example:**
```csharp
void OnApplicationQuit()
{
    EasyMicAPI.Cleanup();
}
```

---

#### `GetProcessor<T>()`
```csharp
public static T GetProcessor<T>(RecordingHandle handle, AudioWorkerBlueprint blueprint)
    where T : class, IAudioWorker
```
Gets the concrete worker instance bound to the given session that was created from the blueprint. Returns `null` if not found.

---

## 🎫 RecordingHandle

Lightweight identifier for recording sessions.

### Structure
```csharp
public struct RecordingHandle
{
    public int Id { get; }
    public MicDevice Device { get; }
    public bool IsValid => Id > 0;
}
```

### Properties

#### `Id`
```csharp
public int Id { get; }
```
Unique identifier for the recording session.

#### `Device`
```csharp
public MicDevice Device { get; }
```
The microphone device associated with this recording.

#### `IsValid`
```csharp
public bool IsValid { get; }
```
Whether this handle represents a valid recording session.

**Example:**
```csharp
if (recordingHandle.IsValid)
{
    Debug.Log($"Recording {recordingHandle.Id} using {recordingHandle.Device.Name}");
}
```

---

## 🎤 MicDevice

Represents a microphone device.

### Structure
```csharp
public struct MicDevice
{
    public string Name { get; }
    public string Id { get; }
    public bool IsDefault { get; }
    public int MaxChannels { get; }
    public int MinSampleRate { get; }
    public int MaxSampleRate { get; }
}
```

### Properties

#### `Name`
```csharp
public string Name { get; }
```
Human-readable name of the microphone device.

#### `Id`
```csharp
public string Id { get; }
```
System identifier for the device.

#### `IsDefault`
```csharp
public bool IsDefault { get; }
```
Whether this is the system's default microphone.

#### `MaxChannels`
```csharp
public int MaxChannels { get; }
```
Maximum number of channels supported by the device.

#### `MinSampleRate` / `MaxSampleRate`
```csharp
public int MinSampleRate { get; }
public int MaxSampleRate { get; }
```
Supported sample rate range for the device.

### Extension Methods

#### `GetDeviceChannel()`
```csharp
public static Channel GetDeviceChannel(this MicDevice device)
```
Gets the recommended channel configuration for the device.

**Returns:** `Channel.Mono` for single-channel devices, `Channel.Stereo` for multi-channel devices.

**Example:**
```csharp
var device = EasyMicAPI.Devices[0];
var recommendedChannels = device.GetDeviceChannel();
Debug.Log($"Recommended channels for {device.Name}: {recommendedChannels}");
```

---

## 📊 AudioState

Carries information about the current audio format.

### Structure
```csharp
public class AudioState
{
    public int ChannelCount { get; set; }
    public int SampleRate { get; set; }
    public int Length { get; set; }
}
```

### Properties

#### `ChannelCount`
```csharp
public int ChannelCount { get; set; }
```
Number of audio channels (1 = mono, 2 = stereo).

#### `SampleRate`
```csharp
public int SampleRate { get; set; }
```
Sample rate in Hz (e.g., 44100, 48000).

#### `Length`
```csharp
public int Length { get; set; }
```
Current buffer length in samples.

### Constructor
```csharp
public AudioState(int channelCount, int sampleRate, int length)
```

**Example:**
```csharp
var state = new AudioState(2, 48000, 1024);
Debug.Log($"Audio format: {state.SampleRate}Hz, {state.ChannelCount} channels");
```

---

## 🎛️ IAudioWorker

Base interface for all audio processors.

### Interface
```csharp
public interface IAudioWorker : IDisposable
{
    void Initialize(AudioState state);
    void OnAudioPass(Span<float> buffer, AudioState state);
}
```

### Methods

#### `Initialize()`
```csharp
void Initialize(AudioState state)
```
Called when the processor is added to an active recording or when recording starts.

**Parameters:**
- `state` - Current audio format information

#### `OnAudioPass()`
```csharp
void OnAudioPass(Span<float> buffer, AudioState state)
```
Called for each audio buffer during recording.

**Parameters:**
- `buffer` - Audio data buffer
- `state` - Current audio format information

---

## 📖 AudioReader

Abstract base class for read-only audio processors.

### Class
```csharp
public abstract class AudioReader : AudioWorkerBase
{
    protected abstract void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state);
}
```

### Methods

#### `OnAudioRead()`
```csharp
protected abstract void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
```
Override this method to analyze audio without modifying it.

**Parameters:**
- `buffer` - Read-only audio data buffer
- `state` - Current audio format information

**Example Implementation:**
```csharp
public class VolumeMonitor : AudioReader
{
    private float _currentVolume;
    
    protected override void OnAudioRead(ReadOnlySpan<float> buffer, AudioState state)
    {
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i] * buffer[i];
        _currentVolume = MathF.Sqrt(sum / buffer.Length);
    }
    
    public float GetCurrentVolume() => _currentVolume;
}
```

---

## ✏️ AudioWriter

Abstract base class for audio processors that modify audio.

### Class
```csharp
public abstract class AudioWriter : AudioWorkerBase
{
    protected abstract void OnAudioWrite(Span<float> buffer, AudioState state);
}
```

### Methods

#### `OnAudioWrite()`
```csharp
protected abstract void OnAudioWrite(Span<float> buffer, AudioState state)
```
Override this method to process and modify audio data.

**Parameters:**
- `buffer` - Mutable audio data buffer
- `state` - Current audio format information

**Example Implementation:**
```csharp
public class GainProcessor : AudioWriter
{
    public float Gain { get; set; } = 1.0f;
    
    protected override void OnAudioWrite(Span<float> buffer, AudioState state)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] *= Gain;
    }
}
```

---

## 📦 Enumerations

### `SampleRate`
```csharp
public enum SampleRate
{
    Hz8000 = 8000,
    Hz16000 = 16000,
    Hz22050 = 22050,
    Hz44100 = 44100,
    Hz48000 = 48000,
    Hz96000 = 96000
}
```
Common sample rates for audio recording.

### `Channel`
```csharp
public enum Channel
{
    Mono = 1,
    Stereo = 2
}
```
Audio channel configurations.

---

## 🔄 AudioBuffer

High-performance lock-free circular buffer for SPSC scenarios.

### Class
```csharp
public class AudioBuffer
{
    public int Capacity { get; }
    public int ReadableCount { get; }
    public int WritableCount { get; }
}
```

### Constructor
```csharp
public AudioBuffer(int capacity)
```
Creates a new audio buffer with the specified capacity.

**Parameters:**
- `capacity` - Maximum number of samples the buffer can hold

### Methods

#### `Write()`
```csharp
public int Write(ReadOnlySpan<float> data)
```
Writes audio data to the buffer (producer thread).

**Parameters:**
- `data` - Audio data to write

**Returns:** Number of samples actually written.

#### `Read()`
```csharp
public int Read(Span<float> destination)
```
Reads audio data from the buffer (consumer thread).

**Parameters:**
- `destination` - Buffer to receive the audio data

**Returns:** Number of samples actually read.

#### `Clear()`
```csharp
public void Clear()
```
Clears all data from the buffer.

**Example:**
```csharp
var buffer = new AudioBuffer(48000); // 1 second at 48kHz

// Producer thread
float[] inputData = GetAudioFromMicrophone();
int written = buffer.Write(inputData);

// Consumer thread  
float[] outputData = new float[1024];
int read = buffer.Read(outputData);
```

---

## 📋 RecordingInfo

Information about a recording session.

### Structure
```csharp
public struct RecordingInfo
{
    public MicDevice Device { get; }
    public SampleRate SampleRate { get; }
    public Channel Channel { get; }
    public bool IsActive { get; }
    public int ProcessorCount { get; }
}
```

### Properties

#### `Device`
```csharp
public MicDevice Device { get; }
```
The microphone device being used.

#### `SampleRate`
```csharp
public SampleRate SampleRate { get; }
```
Current sample rate of the recording.

#### `Channel`
```csharp
public Channel Channel { get; }
```
Current channel configuration.

#### `IsActive`
```csharp
public bool IsActive { get; }
```
Whether the recording is currently active.

#### `ProcessorCount`
```csharp
public int ProcessorCount { get; }
```
Number of processors in the pipeline.

---

## 🛠️ Utility Classes

### `PermissionUtils`
```csharp
public static class PermissionUtils
{
    public static bool HasPermission()
    public static void RequestPermission(Action<bool> callback)
}
```

Platform-specific microphone permission handling.

### `MicDeviceUtils`
```csharp
public static class MicDeviceUtils
{
    public static MicDevice GetDefaultDevice()
    public static MicDevice FindDeviceByName(string name)
}
```

Utility methods for working with microphone devices.

### `AudioExtension`
```csharp
public static class AudioExtension
{
    public static float[] ConvertToMono(float[] stereoData)
    public static AudioClip CreateAudioClip(float[] data, int sampleRate, int channels, string name)
}
```

Audio processing utility methods.

---

## 🔍 What's Next?

Explore practical usage:

- **[Best Practices](best-practices.md)** - Optimization techniques and patterns
- **[Examples](examples.md)** - Real-world usage examples
- **[Troubleshooting](troubleshooting.md)** - Common issues and solutions

---

← [Built-in Processors](processors.md) | **Next: [Best Practices](best-practices.md)** →
