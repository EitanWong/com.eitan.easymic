# SherpaONNXUnity AudioTagging Input Example

This sample shows the recommended no-duplicate-capture setup:

1. Add `EasyMicSherpaAudioInputSource` to a GameObject.
2. Add SherpaONNXUnity `AudioTaggingComponent` to the same or another GameObject.
3. Bind the component to the EasyMic input source instead of adding `SherpaMicrophoneInput`.
4. Subscribe to `AudioTaggingComponent.TagsReadyEvent` or use `EasyMicSherpaAudioTaggingInputExample`.

The EasyMic input source owns one EasyMic recording session and emits mono chunks on Unity's main thread. Sherpa's audio tagging component keeps its own model lifecycle unchanged.

By default the example only binds the input source. Let Sherpa's `startCaptureWhenReady` start capture after the module is initialized. Enable `forceStartCaptureOnStart` only when you intentionally want to start the EasyMic source before the Sherpa component reports readiness.
