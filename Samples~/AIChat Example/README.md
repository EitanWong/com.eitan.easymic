# EasyMic AI Chat Example

English | [简体中文](README_zh-CN.md)

`Samantha` is a working real-time AI voice conversation sample and a practical scaffold for building your own voice agent. It combines local speech recognition, an OpenAI-compatible LLM endpoint, sentence-level speech synthesis, interruption handling, UI state, and avatar feedback in one scene.

Entry scene: `Samantha/Samantha.unity`

## Start Here

1. Import **AIChat Example** from the EasyMic package samples.
2. In Unity, open `Tools > EasyMic > AI Chat > Setup`.
3. Install `com.eitan.sherpa-onnx-unity` when the setup window reports it missing.
4. Open `Samantha/Samantha.unity` from the setup window.
5. Open `Tools > EasyMic > AI Chat > Provider Setup`.
6. Choose `SiliconFlow` or `OpenAI Compatible / Custom`. The imported scene and presets already use public, current defaults.
7. Enter the endpoint, model names, voice, and API key, then click **Apply Scene + Save Device Config**. Save the scene with `Cmd/Ctrl+S`.
8. Enter Play Mode and speak into the selected microphone. The shipped scene starts with a proactive greeting, so the first LLM/TTS request may happen before you speak.

The first setup window is intentionally dependency-free, so it remains available immediately after import. Provider Setup appears after the Sherpa dependency compiles.

## Requirements

- EasyMic and this AIChat sample.
- `com.eitan.sherpa-onnx-unity` for ASR and optional local TTS.
- Microphone permission for the target platform.
- An endpoint and API key for the remote LLM.
- A provider with OpenAI-style remote TTS only when `Use Local TTS` is disabled.

Install the Sherpa package from the Git URL below, or copy this entry into `Packages/manifest.json`:

```json
"com.eitan.sherpa-onnx-unity": "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm"
```

When the dependency is absent, the setup entry remains available, but AIChat runtime scripts, ASR, and local TTS do not compile.

## Provider Setup

The sample uses one `ApiBaseUrl` and API key for both the LLM and remote TTS. The setup window writes provider defaults to `AIChatConfigurationPolicy`, then saves the final Provider and device settings to the runtime configuration file. It marks the scene dirty but deliberately leaves the final scene save under source-control-aware developer control.

### SiliconFlow

Choose `SiliconFlow` in Provider Setup to prefill the sample defaults:

| Setting | Default |
| --- | --- |
| API Base URL | `https://api.siliconflow.cn/v1/` |
| LLM model | `Qwen/Qwen3.5-9B` |
| Remote TTS model | `FunAudioLLM/CosyVoice2-0.5B` |
| Remote TTS voice | `FunAudioLLM/CosyVoice2-0.5B:alex` |

Pick another TTS voice only when your SiliconFlow account or product requires it. The shipped scene uses the same public voice as the preset.

The `SiliconFlowExpressiveTtsInputPlugin` is optional. It only activates for a SiliconFlow host with a CosyVoice model and formats expressive TTS input for that combination.

### Other OpenAI-Compatible Providers

Choose `OpenAI Compatible / Custom`, then replace every provider field with values from that provider's dashboard. Use HTTPS and include the provider's API prefix, for example `https://HOST/v1/`. A URL with no path defaults to `v1/`; any non-empty path is treated as the authoritative prefix. Plain HTTP is accepted only for loopback development hosts such as `localhost`.

For custom hosts, the client uses streaming Chat Completions directly. Official OpenAI hosts use the Responses API, while SiliconFlow uses its dedicated Chat Completions adapter. The current OpenAI preset is `gpt-5.4` with `gpt-4o-mini-tts` and the `marin` voice. Authentication is `Authorization: Bearer <key>` and the expected paths are `chat/completions` or `responses`, plus `audio/speech` for remote speech.

The generic remote-TTS request contains the standard `model`, `input`, `voice`, `response_format`, and `speed` fields. This scaffold requests mono PCM and expects either raw PCM16 at 24 kHz or a WAV payload carrying its own format metadata. SiliconFlow receives its additional `sample_rate`, `gain`, and `stream` fields through the dedicated adapter. Providers with different request fields, SSE events, or audio formats need an `IOpenAIProviderAdapter`/resolver extension. Different authentication schemes also require extending the client's credential/header handling; never put a key in the URL.

If the provider offers an LLM but no compatible remote TTS endpoint, enable **Use Local TTS** and configure the `SpeechSynthesizer` instead. Local TTS changes only speech output; the LLM still needs the remote API base URL and key. Using separate remote providers for LLM and TTS is not a first-class configuration in this sample.

## Credentials And Configuration Priority

The API key is not stored in the scene or in `AIChatConfigurationPolicy` by Provider Setup. It is written as plain text to:

```text
Application.persistentDataPath/<AIChatControllerConfig.RuntimeConfigFileName>
```

The default file name is `ai_chat_config.json`.

This is per-device application storage. Do not copy that file into source control, screenshots, or build artifacts. For a production application, inject credentials through your own authenticated backend or startup flow with `AIChatController.SetApiKey()`.

Runtime configuration uses schema v4 and loads only that exact contract. Files with a different schema are replaced by current defaults instead of being converted. Provider Setup and the runtime settings panel are the explicit persistence paths; keys injected with `SetApiKey()` remain transient.

At startup, the effective configuration is resolved in this order:

1. `AIChatControllerConfig` in the scene supplies baseline references and defaults.
2. An enabled `AIChatConfigurationPolicy` applies repeatable scene and deployment defaults.
3. `ai_chat_config.json` applies the final device-specific API key, Provider, model, microphone, ASR, and local-TTS settings.
4. A non-empty key injected with `SetApiKey()` before `Awake` remains the final in-memory credential and is not written to JSON.
5. The controller initializes the API client, ASR, and TTS pipeline from that resolved configuration.

The scene Policy therefore remains a reusable default, while settings saved through `AIChatRuntimeConfigPanel` survive the next startup. The runtime panel writes the JSON and applies it immediately; it does not reload the scene or require the scene in Build Settings. If Save is triggered again while a local model is loading, the latest settings win and the earlier apply operation is canceled.

## Editor Tools

| Menu or inspector | Use it for |
| --- | --- |
| `AIChatSampleReadme.asset` | Localized ScriptableObject README shown directly in the Inspector, with scene, Setup, Provider, and full-document actions. |
| `Tools > EasyMic > AI Chat > Setup` | Dependency check, package-install shortcut, open the matching imported scene, and open the localized README. Available before Sherpa is installed. |
| `Tools > EasyMic > AI Chat > Provider Setup` | SiliconFlow/custom Provider profile, scene policy, device configuration, API key save/clear, and automatic component reference discovery. |
| `AIChatController` inspector | Main scene configuration, validation messages, runtime-config path, and Play Mode diagnostics. |
| `AIChatConfigurationPolicy` inspector | Reusable OpenAI, SiliconFlow, Local TTS, or field-level override policy. |
| In-game `AIChatRuntimeConfigPanel` | End-user/device settings: API key, endpoint, models, microphone, ASR mode/models, and local TTS setup. Save writes schema-v4 JSON and applies it immediately. |

The runtime panel deliberately does not manage every scene-level behavior. System prompt, history size, streaming-TTS behavior, interruption behavior, and diagnostics remain in the controller inspector or configuration policy.

## Local Speech Volume And Debugging

1. Choose local speech in Provider Setup and select SpeechSynthesizer to configure the model. Check VoiceMicrophone input, ASR and permissions; enable APM/AEC when using speakers.
2. Keep **Normalize Output** enabled and start **Playback Volume** at 1 (0 mutes; 2 adds up to 6 dB). Both controls are also available in the SpeechSynthesizer inspector.
3. Use the model's native rate, e.g. 44100 Hz for vits-melo-tts-zh_en. Playback converts to the device format; lowering the configured rate does not accelerate inference.
4. Apply and save device settings, save the scene, and enter Play Mode. Wait for model loading. First remain quiet through the greeting, then speak during playback to verify interruption.
5. Enable **Debug Mode** to inspect F12 pipeline timing and interruption history. Select SpeechSynthesizer for input RMS, output peak and gain. Opt into Verbose Streaming Log or TTS Diagnostics separately in the Controller inspector when needed.
6. Disable Debug Mode after testing. Component debug logs, pipeline collection and the F12 panel turn off; actionable errors remain reported. Save in Provider Setup to keep these settings on the next launch, since device settings override scene defaults.

The leveler uses a -20 dBFS RMS target, 24 dB maximum boost, -55 dBFS silence gate, faster gain reduction than recovery and a -1 dBFS sample-peak limiter. Peak protection remains active with normalization off. Its bounded-gain and limiting design is informed by [WebRTC adaptive digital gain control](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/audio_processing/agc2/adaptive_digital_gain_controller.cc) and its [limiter](https://webrtc.googlesource.com/src/+/refs/heads/main/modules/audio_processing/agc2/limiter.cc).

Existing 20 ms blocks are processed in place with no extra queue or whole-utterance analysis. Playback and AEC receive the same processed signal. This reduces model level differences; it is not LUFS or true-peak normalization and cannot repair already clipped model output. Check the final mix and listening level on each platform. Older schema-v4 files without the additive fields preserve scene defaults.

## Module Map

| Module | Main responsibility |
| --- | --- |
| `AIChatController/` | Composition root. Starts microphone services, accepts transcribed input, coordinates LLM turns, handles cancel/barge-in, and publishes chat state. |
| `AIChatControllerConfig` | Inspector-visible baseline configuration: component references, endpoint, model, TTS mode, conversation behavior, and runtime config file name. |
| `AIChatConfigurationPolicy` | Scene provider/preset defaults for repeatable platform or deployment setup. Device runtime settings are applied after this layer. |
| `AIChatProviderPreset` | Shared SiliconFlow and OpenAI defaults used by policy and editor setup, preventing model/endpoint drift. |
| `RuntimeConfig/` | `AIChatRuntimeConfig` and `JsonAIChatRuntimeConfigStore` validate and persist schema-v4 per-device API and speech settings. |
| `OpenAI/` | OpenAI-compatible HTTP client, Server-Sent Events reader, models, and provider adapters. SiliconFlow receives a dedicated adapter; other hosts use the generic adapter. |
| `ChatTtsPipeline/` | Converts streamed assistant text into sentence jobs, generates local or remote audio, queues playback, and adapts its buffer. |
| `StreamingSentenceAssembler` | Splits LLM streaming text into speakable sentences to reduce time to first audio. |
| `Plugins/` | Optional features such as proactive conversation and SiliconFlow expressive TTS input formatting. |
| `UI/` | Loading, error, runtime-settings, and speech-visualization presentation. Replace this layer freely in a product. |
| `Animator/` | Maps controller/audio state to the Samantha avatar. It is presentation-only. |
| `Diagnostics/` | Pipeline metrics and debugging helpers. |
| `Prompts/` | Reusable system, greeting, and idle prompt profiles. |
| `Code/Editor/Bootstrap/` | Dependency-free Setup entry, shared Editor styling/localization, and the ScriptableObject README Inspector. |

## Conversation Flow

```text
VoiceMicrophone
  -> ASR transcription submit
  -> AIChatController
  -> AIChatRequestOrchestrator + OpenAICompatibleClient
  -> StreamingSentenceAssembler
  -> ChatTtsPipeline
  -> local or remote playback
  -> UI / Animator feedback
```

`AIChatController` is the integration boundary for a product UI. Typical hooks are `SubmitUserMessage`, `CancelResponseAsync`, `OnChatStateChanged`, and `OnAssistantAudioQueued`.

## Turn This Into Your Scaffold

1. Duplicate `Samantha/Samantha.unity` and its companion assets into your own sample or game folder.
2. Keep `AIChatController`, `VoiceMicrophone`, and the relevant playback component. Keep `SpeechSynthesizer` when using local TTS.
3. Run Provider Setup on the copied controller so the configuration policy and device runtime storage target the new scene setup.
4. Replace `AIChatUIView` and `SamanthaAnimator` with your UI, avatar, or gameplay listeners. They are not required by the LLM pipeline itself.
5. Replace the assigned `PromptProfile` with your product system prompt and tune `MaxHistoryTurns`, turn detection delay, and barge-in behavior in the controller inspector.
6. Add an `IAIChatPlugin` implementation when you need application-specific behavior without changing the controller flow.
7. Keep the sample assembly definitions and the Sherpa dependency while you reuse its ASR/local-TTS components.

Use this component split when extracting the scaffold:

- Required: `AIChatController`, `AIChatControllerConfig.Microphone` -> `VoiceMicrophone`, and a valid LLM endpoint/key/model.
- Required for local TTS: `SpeechSynthesizer` and its configured playback source.
- Recommended: a `PromptProfile`; without one the conversation still runs without a system prompt.
- Optional: `AIChatConfigurationPolicy`, runtime panel, UI, animator, diagnostics, proactive conversation, and expressive-TTS plugins.

Remote TTS creates its EasyMic playback stream automatically, so it does not require a scene playback source.

Provider Setup fills missing microphone and synthesizer references from the controller GameObject, its children, or its parents. It does not create speech components or model assets; add those through the EasyMic/Sherpa workflow first.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| `Setup` menu is missing | Reimport the AIChat Example under `Assets`, then let Unity finish compiling. |
| Provider Setup is unavailable | Install `com.eitan.sherpa-onnx-unity` and wait for script compilation. |
| API key error | Save a key through Provider Setup or the runtime settings panel. Confirm the file path in the `AIChatController` inspector. |
| LLM request fails | Verify base URL, model name, key, and the selected Provider contract: Responses for official OpenAI, Chat Completions for SiliconFlow/custom hosts. |
| Assistant text arrives but no speech plays | For remote TTS, verify model/voice and `audio/speech` support. For local TTS, assign/configure `SpeechSynthesizer`. |
| No transcription | Verify OS microphone permission, selected device, ASR models, and `VoiceMicrophone` assignment. |
| Runtime settings do not update | Confirm the panel references the active `AIChatController`, then check the Console for the saved path or validation error. |
| A request starts before you speak | The sample enables `ProactiveConversationPlugin` and sends a greeting after initialization. Disable that plugin behavior when your product should wait for input. |
| Custom Provider returns `400`/`404` | Verify the configured API prefix, Bearer authentication, streaming Chat Completions path, and model. Custom hosts do not require the Responses API. |
| Assistant interrupts itself | Use headphones or AEC, tune `BargeInEchoGuardSeconds`, or disable `InterruptAssistantOnUserSpeech` while debugging acoustic feedback. |
| First ASR/local-TTS start is slow | Sherpa models may need download/extraction time, network access, and disk space on first use. Watch the loading progress. |

## Before Shipping

- Move API-key ownership out of local JSON and into your application/backend credential flow.
- Inject credentials before the controller initializes, or replace the sample persistence flow so production credentials are not written back to device JSON.
- Disable `LogStreamingChunks` and TTS diagnostics in production. Console output, diagnostic payloads, and generated WAV names may contain transcripts, prompts, or responses.
- Test microphone permission and device selection on every target platform.
- Test interruption, network failures, long answers, and both local and remote TTS modes with the actual provider account and models.
- Replace sample visual/UI assets and review all prompt text for the product's intended behavior.
