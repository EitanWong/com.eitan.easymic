# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Easy Mic is a Unity package for low-latency audio recording with a programmable processing pipeline. It provides direct access to raw microphone data and allows chaining audio processors for real-time audio workflows.

## Repository Structure

This is a Unity project containing a Unity package. The main package code is located at:
- `EasyMic/Packages/com.eitan.easymic/` - Main Unity package
- `EasyMic/Packages/com.eitan.easymic.apm/` - Additional audio processing modules (private package)

## Key Architecture Components

### Core API Layer
- `Runtime/API/EasyMicAPI.cs` - Main public API facade with singleton pattern
- Uses permission checks before allowing microphone access
- Thread-safe operations with proper locking

### Core System
- `Runtime/Core/MicSystem.cs` - Central manager for microphone recordings
- Manages multiple concurrent recording sessions
- Uses native interop through SoundIO library
- Implements proper disposal patterns for unmanaged resources

### Audio Pipeline
- `Runtime/Core/Structs/AudioPipeline.cs` - Manages chain of audio processors
- `Runtime/Core/Interfaces/IAudioWorker.cs` - Interface for all audio processors
- `Runtime/Core/Abstracts/Base/AudioWorkerBase.cs` - Base class for processors

### Built-in Processors
- `AudioCapturer` - Captures audio to buffer/file
- `AudioDownmixer` - Converts multi-channel to mono
- `VolumeGateFilter` - Noise gate filtering
- `LoopbackPlayer` - Audio loopback functionality
- `SherpaRealtimeSpeechRecognizer` - Speech-to-text (requires external dependency)

### Native Integration
- `Runtime/Core/Native.cs` - P/Invoke declarations for SoundIO library
- `Plugins/soundio/` - Native libraries for Windows, macOS, Linux
- Cross-platform audio backend using libsoundio

## Development Workflow

### Building and Testing
- Use Unity Editor for development and testing
- Project includes Unity Test Runner setup (`Tests/` directory)
- Assembly definitions configured for proper module separation
- Visual Studio solution file available for IDE integration

### Package Structure
- Uses Unity Package Manager (UPM) format
- Assembly definitions (`*.asmdef`) separate Runtime, Editor, and Tests
- Proper dependency management through package.json

### Testing
- Unit tests in `Tests/` directory using Unity Test Runner
- Test assembly: `Eitan.EasyMic.Tests.asmdef`
- Run tests through Unity Test Runner window

## Important Patterns

### Thread Safety
- All public API calls are thread-safe using locks
- Audio callbacks run on separate threads
- Proper GC handle management for native callbacks

### Resource Management
- Implements IDisposable pattern throughout
- Careful management of native memory allocation/deallocation
- Automatic cleanup on application shutdown

### Permission Handling
- Microphone permissions checked before operations
- Graceful degradation when permissions not granted
- Platform-specific permission utilities

## Development Commands

### Unity Editor
- Open project in Unity 2021.3+
- Use Unity Package Manager to manage dependencies
- Build through Unity's standard build pipeline

### Testing
- Open Unity Test Runner (`Window > General > Test Runner`)
- Run PlayMode and EditMode tests
- Tests are located in `EasyMic/Packages/com.eitan.easymic/Tests/`

## Notable Constraints

### IL2CPP Compatibility
- Uses `[MonoPInvokeCallback]` attribute for native callbacks
- Static callback methods to avoid IL2CPP issues
- Careful management of delegate instances

### Cross-Platform Support
- Native libraries for Windows, macOS, Linux
- Platform-specific implementations in utilities
- Conditional compilation for different platforms

## File Locations

### Core Runtime Files
- Main API: `EasyMic/Packages/com.eitan.easymic/Runtime/API/EasyMicAPI.cs`
- Core System: `EasyMic/Packages/com.eitan.easymic/Runtime/Core/MicSystem.cs`
- Audio Pipeline: `EasyMic/Packages/com.eitan.easymic/Runtime/Core/Structs/AudioPipeline.cs`

### Native Integration
- Native Bindings: `EasyMic/Packages/com.eitan.easymic/Runtime/Core/Native.cs`
- Native Libraries: `EasyMic/Packages/com.eitan.easymic/Plugins/soundio/`

### Assembly Definitions
- Runtime: `EasyMic/Packages/com.eitan.easymic/Runtime/Eitan.EasyMic.asmdef`
- Editor: `EasyMic/Packages/com.eitan.easymic/Editor/Eitan.EasyMic.Editor.asmdef`
- Tests: `EasyMic/Packages/com.eitan.easymic/Tests/Eitan.EasyMic.Tests.asmdef`