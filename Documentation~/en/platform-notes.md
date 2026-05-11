# Platform Notes

Always validate your target platform with real devices. Editor behavior is not a substitute for IL2CPP player and mobile testing.

## Desktop

Windows and macOS desktop builds can usually start with `LowLatency`. Use `Balanced` if scene loading, CPU spikes, or processor work causes underruns or capture drops.

Linux backend behavior depends on the audio stack and device configuration. Test ALSA/PulseAudio/PipeWire setups that match your users.

## Android

Android should default to `Balanced` unless you explicitly opt into lower latency and test on target devices. The playback API already defaults to `Balanced` on Android players.

Add microphone permission:

```xml
<uses-permission android:name="android.permission.RECORD_AUDIO" />
```

Android routing, Bluetooth, AAudio/OpenSL behavior, and device-specific buffer sizes vary. Test real devices before release.

## iOS

iOS requires microphone permission and correct app usage descriptions. Audio session behavior can affect route, sample rate, and interruptions. Validate capture/playback in a player build, not only in the Editor.

## IL2CPP

EasyMic uses static reverse P/Invoke callbacks internally and annotates callback entry points for AOT platforms where required. Do not replace these with instance callbacks from user code.

## Unity Lifecycle

- Stop recordings before destroying objects that own processor state.
- Dispose `PlaybackHandle` values you keep beyond auto-disposed clip playback.
- Expect subsystem registration and domain reload to reset static EasyMic state.
- Avoid starting/stopping devices repeatedly inside tight gameplay loops.

## Validation Status

The package is structured for Windows, macOS, Linux, Android, and iOS, but platform behavior depends on native plugin availability, permissions, OS audio backend, and device drivers. Treat every release target as needing its own capture/playback validation pass.
