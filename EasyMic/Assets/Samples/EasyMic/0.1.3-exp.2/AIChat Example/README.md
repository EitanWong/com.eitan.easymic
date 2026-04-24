# AIChat Sample Dependency Notes

The `Assets/Samples/AIChat` sample depends on:

- `com.eitan.sherpa-onnx-unity`

When this package is missing, the sample now stays import-safe across Unity versions and platforms by keeping scene/component references alive in a compatibility mode. In that mode:

- scene scripts are preserved instead of degrading into `Missing Script`
- AI Chat runtime components report the missing dependency at runtime
- ASR and local TTS features remain unavailable until `com.eitan.sherpa-onnx-unity` is installed

## Developer Guidance

Use:

- `Tools/EasyMic/AI Chat/Dependency Guide`

From this guide you can:

- Open Package Manager
- Copy the `manifest.json` dependency entry
- Open `Packages/manifest.json`
- Recheck dependency status

## Install via Git URL

Package URL:

`https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm`

`manifest.json` entry:

`"com.eitan.sherpa-onnx-unity": "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm"`
