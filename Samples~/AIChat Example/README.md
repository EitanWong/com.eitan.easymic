# AIChat Sample Dependency Notes

The `Assets/Samples/AIChat` sample depends on:

- `com.eitan.sherpa-onnx-unity`

When this package is missing, the sample intentionally hides runtime scripts/components with compile guards (`EASYMIC_SHERPA_ONNX_INTEGRATION`) to keep the project compile-safe.

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
