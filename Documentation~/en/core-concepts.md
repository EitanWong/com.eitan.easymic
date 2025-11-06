← [Getting Started](getting-started.md) | [Documentation Home](../README.md) | [中文版本](../zh-CN/core-concepts.md) →

# 🏗️ Core Concepts

Understanding these fundamental concepts will help you make the most of Easy Mic's powerful and flexible architecture.

## 🎯 Design Philosophy

Easy Mic is built around three core principles:

1. **🚀 Performance First**: Ultra-low latency audio processing with zero-GC operations
2. **🔧 Modular Design**: Composable audio processors that can be mixed and matched
3. **🛡️ Type Safety**: Compile-time guarantees prevent common audio processing mistakes

## 🏛️ Architecture Overview

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   EasyMicAPI    │───▶│    MicSystem     │───▶│ RecordingSession│
│   (Facade)      │    │   (Manager)      │    │   (Instance)    │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                                         │
                                                         ▼
                                               ┌─────────────────┐
                                               │ AudioPipeline   │
                                               │ ┌─────────────┐ │
                                               │ │ Processor A │ │
                                               │ ├─────────────┤ │
                                               │ │ Processor B │ │
                                               │ ├─────────────┤ │
                                               │ │ Processor C │ │
                                               │ └─────────────┘ │
                                               └─────────────────┘
```

## 🎭 Key Components

### 🎪 EasyMicAPI (Facade Pattern)

The main entry point for all EasyMic operations. Provides a simple, thread-safe interface:

```csharp
// Ensure permission (especially on mobile)
if (!PermissionUtils.HasPermission()) return;

// Get available devices
EasyMicAPI.Refresh();
var devices = EasyMicAPI.Devices;

// Start recording (auto-select default if needed)
var handle = EasyMicAPI.StartRecording(SampleRate.Hz16000);

// Add processors via blueprints
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture");
EasyMicAPI.AddProcessor(handle, bpCapture);

// Stop recording
EasyMicAPI.StopRecording(handle);
```

### 🎛️ MicSystem (Manager)

Manages multiple concurrent recording sessions and handles native audio backend operations:

- **Device Management**: Enumerates and manages microphone devices
- **Session Lifecycle**: Creates and destroys recording sessions
- **Resource Management**: Handles native memory and thread safety
- **Permission Handling**: Manages microphone permissions across platforms

### 🎫 RecordingHandle (Identifier)

A lightweight, type-safe identifier for recording sessions:

```csharp
public struct RecordingHandle
{
    public int Id { get; }
    public MicDevice Device { get; }
    public bool IsValid => Id > 0;
}
```

**Benefits:**

- ✅ **Type Safety**: Cannot accidentally mix up recording sessions
- ✅ **Resource Safety**: Invalid handles are automatically detected
- ✅ **Thread Safety**: Can be safely passed between threads

### 🔗 AudioPipeline (Chain of Responsibility)

The heart of EasyMic's processing system. Manages an ordered chain of audio processors:

```csharp
// Pipeline processes audio in order:
// Raw Mic → VolumeGate → Downmixer → Capturer → Output
var handle = EasyMicAPI.StartRecording(SampleRate.Hz16000);
var bpGate    = new AudioWorkerBlueprint(() => new VolumeGateFilter(), key: "gate");
var bpDownmix = new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "downmix");
var bpCapture = new AudioWorkerBlueprint(() => new AudioCapturer(), key: "capture");
EasyMicAPI.AddProcessor(handle, bpGate);
EasyMicAPI.AddProcessor(handle, bpDownmix);
EasyMicAPI.AddProcessor(handle, bpCapture);
```

**Key Features:**

- 🔄 Dynamic modification (add/remove during recording)
- 🧵 Thread-safe: lock‑free snapshots on the RT path
- 🎯 Strict order: stages run in insertion order
- 🧠 Zero‑GC on the audio thread

## 🎨 Processor Types

Easy Mic uses a type-safe design to prevent common audio processing mistakes:

### 📖 AudioReader (Async, Read-Only)

For analysis and monitoring without modifying the audio. AudioReader pushes frames into a lock‑free SPSC ring buffer on the audio thread and processes them on a dedicated worker thread via `OnAudioReadAsync`.

```csharp
public abstract class AudioReader : AudioWorkerBase
{
    // Audio thread: enqueue only, non‑blocking
    public sealed override void OnAudioPass(Span<float> buffer, AudioContext state)
    {
        // internal: SPSC write + signal
    }

    // Worker thread: your heavy work here
    protected abstract void OnAudioReadAsync(ReadOnlySpan<float> buffer);
}
```

Examples: meters, file writers, ASR front‑ends, VAD, streaming to network, etc.

### ✏️ AudioWriter (Read-Write)

For processing that modifies the audio:

```csharp
public abstract class AudioWriter : AudioWorkerBase
{
    public sealed override void OnAudioPass(Span<float> buffer, AudioContext state)
    {
        OnAudioWrite(buffer, state); // Span<float> - can modify!
    }

    protected abstract void OnAudioWrite(Span<float> buffer, AudioContext state);
}
```

**Examples:** Filters, effects, format conversion, noise gates

### 🛠️ IAudioWorker (Interface)

The base interface all processors implement:

```csharp
public interface IAudioWorker : IDisposable
{
    void Initialize(AudioContext state);
    void OnAudioPass(Span<float> buffer, AudioContext state);
}
```

## 📊 AudioContext (Context)

Carries information about the current audio format:

```csharp
public class AudioContext
{
    public int ChannelCount { get; set; }  // 1 = Mono, 2 = Stereo
    public int SampleRate { get; set; }    // e.g., 44100, 48000
    public int Length { get; set; }        // Current buffer length
}
```

**Usage:**

- Processors use this to adapt to different audio formats
- Automatically passed to each processor in the pipeline
- Can change during recording (e.g., if format switches)

## 🔄 AudioBuffer (Lock-Free Circular Buffer)

High-performance, lock-free buffer designed for Single Producer Single Consumer (SPSC) scenarios:

```csharp
var buffer = new AudioBuffer(48000); // 1 second at 48kHz

// Producer thread (audio callback)
buffer.Write(audioData);

// Consumer thread (your code)
buffer.Read(outputArray);
```

**Key Features:**

- 🚀 **Zero-Lock Performance**: No mutex overhead
- 🗑️ **Zero-GC Operations**: No allocations during read/write
- 🔄 **Circular Design**: Efficient memory usage
- ⚡ **Memory Barriers**: Proper threading with `Volatile.Read/Write`

**⚠️ Important**: Only safe for SPSC scenarios!

## 🎪 Processing Flow

Here's how audio flows through the system:

```
1. Microphone Hardware
   ↓ (Native Audio Thread)
2. Native Audio Backend (libsoundio)
   ↓ (Audio Callback)
3. MicSystem.RecordingSession
   ↓ (AudioPipeline.OnAudioPass)
4. Processor 1 (e.g., VolumeGate)
   ↓ (Modified Buffer)
5. Processor 2 (e.g., Downmixer)
   ↓ (Modified Buffer)
6. Processor 3 (e.g., AudioCapturer)
   ↓ (Captured to Buffer)
7. Your Application Code
```

**Key Points:**

- Audio processing happens on a **dedicated audio thread**
- Each processor gets the **output of the previous processor**
- The pipeline is **lock-free** for maximum performance
- Errors in one processor **don't crash the entire pipeline**

## 🛡️ Thread Safety

Easy Mic is designed to be thread-safe at the API level:

### ✅ Thread-Safe Operations

- Adding/removing processors from active recordings
- Starting/stopping recordings
- Accessing device information
- Creating/disposing processors

### ⚠️ Thread-Unsafe Areas

- Modifying processor properties during processing
- Accessing processor state from multiple threads
- Manual buffer operations outside the pipeline

### 🧵 Threading Model

```
Main Thread (Unity)     Audio Thread (Native)    Background Thread (Optional)
     │                        │                           │
     │ StartRecording()       │                           │
     ├────────────────────────▶                           │
     │                        │ OnAudioPass()            │
     │                        ├──────────────────────────▶
     │ AddProcessor()         │                           │ Process Audio
     ├────────────────────────▶                           ├─────────────▶
     │                        │                           │
     │ StopRecording()        │                           │
     ├────────────────────────▶                           │
```

## 💡 Best Practices

### 🎯 Performance

- **Minimize allocations** in audio processors
- **Keep processing lightweight** - audio thread is real-time critical
- **Use appropriate buffer sizes** - balance latency vs. performance
- **Avoid complex operations** in the audio callback

### 🔧 Architecture

- **Compose simple processors** rather than creating complex ones
- **Use AudioReader for analysis** that doesn't modify audio
- **Use AudioWriter for effects** that modify audio
- **Handle errors gracefully** - don't crash the audio thread

### 🧹 Resource Management

- **Always dispose processors** when done
- **Stop recordings** before destroying GameObjects
- **Use using statements** for automatic cleanup
- **Monitor memory usage** in long-running applications

## 🔍 What's Next?

Now that you understand the core concepts, dive deeper into:

- **[Audio Pipeline](audio-pipeline.md)** - Master the processing pipeline
- **[Built-in Processors](processors.md)** - Learn about all available processors
- **[API Reference](api-reference.md)** - Complete API documentation
- **[Best Practices](best-practices.md)** - Tips for optimal performance

---

← [Getting Started](getting-started.md) | **Next: [Audio Pipeline](audio-pipeline.md)** →
