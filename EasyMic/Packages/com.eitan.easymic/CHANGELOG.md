# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.2-exp.1] - 2025-09-22

### Added
- Added `SherpaKeywordDetector` processor, a keyword/wake-word detector based on the [com.eitan.sherpa-onnx-unity](https://github.com/EitanWong/com.eitan.sherpa-onnx-unity) plugin, for implementing wake-word functionality.

### Changed
- Optimized Easy Mic's code by removing the `soundio` dependency for desktop platforms.
- Refactored and optimized code for better performance.
- Enhanced detailed recording device information retrieval (channel count and supported sample rates) on both mobile and desktop platforms.
- Optimized the `AudioPlayback` system to use a background audio thread, ensuring continuous audio playback even when the application is not in focus.


### Added
- Enhanced README documentation with professional visual layout
- Comprehensive bilingual documentation (English/Chinese) in Documentation~ folder
- Third-party notices for libsoundio and miniaudio dependencies
- Professional shields and badges for GitHub repository presentation
- APM extension package marketing and contact information

### Changed
- Updated README files with centered layout, professional styling, and improved visual hierarchy
- Simplified technical backend explanations for better accessibility
- Enhanced documentation structure with grid-based navigation
- Improved Quick Start section with side-by-side installation and usage examples

## [0.1.1-exp] - 2025-08-24

### Added
- New low-latency playback and mixing subsystem under `Runtime/Core/AudioPlayback`:
  - `AudioSystem`, `AudioMixer`, `PlaybackAudioSource` for high‑performance additive mixing, resampling, and per‑source pipelines/meters.
  - `PlaybackAudioSourceBehaviour` MonoBehaviour wrapper for easy scene integration.
- New sample scene: `Samples~/Playback Example` demonstrating playback via `PlaybackAudioSourceBehaviour`.
- Documentation callouts referencing APM’s AEC playback requirement.

### Changed
- Refactored the recording subsystem for non‑blocking, high‑performance capture:
  - Lock‑free SPSC ring buffers on the audio callback path to avoid stalls.
  - Zero‑allocation hot path and improved latency/stability across backends.
  - More robust device selection fallback via `EasyMicAPI`.
- Clarified AEC integration guidance: playback must use `PlaybackAudioSource`/`PlaybackAudioSourceBehaviour` for echo cancellation to work (see APM package docs).

## [0.1.0-exp.1] - 2025-07-26

### Added
- Repository privacy controls via .gitignore updates
- Exclusion of private APM package (`com.eitan.easymic.apm`) from public repository
- Exclusion of large demo assets (`EasyMic/Assets/Demo/Samples/AudioProcessing`) from version control
- Proper open-source repository structure while maintaining private components

### Changed
- Updated .gitignore to ensure clean public repository structure
- Separated public and private package components for proper licensing compliance

## [0.1.0-exp.0] - 2025-07-17

### Added
- Initial release of Easy Mic for Unity
- Cross-platform real-time audio recording capabilities
- Native audio backends using libsoundio and miniaudio
- Modular audio processing pipeline with Chain of Responsibility pattern
- Type-safe audio processor architecture (AudioReader/AudioWriter)
- Lock-free circular buffer implementation for SPSC scenarios
- Built-in audio processors:
  - AudioCapturer for recording audio to Unity AudioClip
  - AudioDownmixer for channel conversion (stereo to mono)
  - VolumeGateFilter for noise gate functionality
  - SherpaOnnxUnity integration for speech recognition
- Cross-platform native plugin support:
  - Windows (x86, x86_64, ARM64)
  - macOS (x86_64, ARM64)  
  - Linux (x86_64, ARM, ARM64)
  - Android (armeabi-v7a, arm64-v8a, x86_64)
  - iOS (Universal Framework)
- Unity Package Manager integration with UPM branch
- Complete API documentation and usage examples
- Sample scenes demonstrating core functionality
- Editor integration with optional dependency management
- Thread-safe operations for reliable multi-threaded applications
- Zero-GC audio processing for maximum performance
- GPL v3 licensing with commercial licensing options
- Comprehensive test suite for quality assurance

### Technical Implementation
- EasyMicAPI facade pattern for simplified public interface
- MicSystem manager for device enumeration and session lifecycle
- RecordingHandle struct for type-safe session identification
- AudioPipeline for dynamic processor chain management
- AudioState context passing for format-aware processing
- Native interop layer with platform-specific implementations
- Permission handling utilities for microphone access
- Device enumeration and management utilities
- Audio format conversion and extension utilities

### Documentation
- Getting started guide with installation instructions
- Core concepts documentation explaining architecture
- Audio pipeline deep dive with processing flow diagrams
- Built-in processors reference with usage examples
- API reference with complete method documentation
- Best practices guide for performance optimization
- Troubleshooting guide for common issues
- Real-world examples and use cases
- Cross-platform deployment instructions

### Platform Support
- Unity 2021.3 LTS and higher
- .NET Standard 2.1 compatibility
- Full cross-platform native plugin support
- Microphone permission handling per platform
- Platform-specific audio backend optimizations

---

## Version History Notes

- **0.1.0-exp.1**: Repository structure and privacy improvements
- **0.1.0-exp.0**: Initial feature-complete release with full cross-platform support

---

## Contributors

- **Eitan Wong** - Project Creator and Maintainer
  - GitHub: [@EitanWong](https://github.com/EitanWong)
  - Email: unease-equity-5c@icloud.com

---

## License

This project is licensed under the GNU General Public License v3.0 (GPLv3).
For commercial licensing options, please contact the maintainer.

## Third-Party Dependencies

- **libsoundio** - MIT License (Copyright © 2015 Andrew Kelley)
- **miniaudio** - Public Domain (Unlicense) OR MIT No Attribution License (Copyright 2025 David Reid)

For detailed license information, see [THIRD PARTY NOTICES.md](THIRD%20PARTY%20NOTICES.md).
