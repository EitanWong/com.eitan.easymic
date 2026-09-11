#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class RuntimeConfigRoundTripTests
    {
        private const string FixtureApiKey = "fixture";
        private const string PersistedFixtureApiKey = "persisted";
        private const string InjectedFixtureApiKey = "injected";

        [Test]
        public void SaveAndLoad_ShouldRoundTripAllFields()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_{Guid.NewGuid():N}.json");

            try
            {
                var input = new AIChatRuntimeConfig
                {
                    ApiKey = FixtureApiKey,
                    ApiBaseUrl = "https://example.com/v1/",
                    LlmModel = "gpt-4.1-mini",
                    LlmTemperature = 0.3f,
                    TtsModel = "gpt-4o-mini-tts",
                    TtsVoice = "marin",
                    UseLocalTts = 1,
                    DebugMode = 1,
                    LocalTtsNormalizeOutput = 1,
                    LocalTtsPlaybackVolume = 1.4f,
                    MicrophoneDeviceName = "USB Microphone",
                    AsrRecognitionModeIndex = 2,
                    AsrStreamingModelId = "stream-model",
                    AsrOfflineModelId = "offline-model",
                    AsrVadModelId = "silero-vad-v5",
                    AsrEnablePunctuation = 1,
                    AsrPunctuationModelId = "punct-model",
                    LocalTtsModelId = "local-model",
                    LocalTtsVoiceId = 2,
                    LocalTtsSpeed = 1.2f,
                    LocalTtsSampleRate = 22050
                };

                bool saveOk = store.TrySave(path, input, out string saveError);
                Assert.IsTrue(saveOk, saveError);

                bool loadOk = store.TryLoad(path, out var output);
                Assert.IsTrue(loadOk);
                Assert.NotNull(output);
                Assert.AreEqual(input.ApiKey, output.ApiKey);
                Assert.AreEqual(input.ApiBaseUrl, output.ApiBaseUrl);
                Assert.AreEqual(input.LlmModel, output.LlmModel);
                Assert.AreEqual(input.LlmTemperature, output.LlmTemperature);
                Assert.AreEqual(input.TtsModel, output.TtsModel);
                Assert.AreEqual(input.TtsVoice, output.TtsVoice);
                Assert.AreEqual(input.UseLocalTts, output.UseLocalTts);
                Assert.AreEqual(input.DebugMode, output.DebugMode);
                Assert.AreEqual(input.LocalTtsNormalizeOutput, output.LocalTtsNormalizeOutput);
                Assert.AreEqual(input.LocalTtsPlaybackVolume, output.LocalTtsPlaybackVolume);
                Assert.AreEqual(input.MicrophoneDeviceName, output.MicrophoneDeviceName);
                Assert.AreEqual(input.AsrRecognitionModeIndex, output.AsrRecognitionModeIndex);
                Assert.AreEqual(input.AsrStreamingModelId, output.AsrStreamingModelId);
                Assert.AreEqual(input.AsrOfflineModelId, output.AsrOfflineModelId);
                Assert.AreEqual(input.AsrVadModelId, output.AsrVadModelId);
                Assert.AreEqual(input.AsrEnablePunctuation, output.AsrEnablePunctuation);
                Assert.AreEqual(input.AsrPunctuationModelId, output.AsrPunctuationModelId);
                Assert.AreEqual(input.LocalTtsModelId, output.LocalTtsModelId);
                Assert.AreEqual(input.LocalTtsVoiceId, output.LocalTtsVoiceId);
                Assert.AreEqual(input.LocalTtsSpeed, output.LocalTtsSpeed);
                Assert.AreEqual(input.LocalTtsSampleRate, output.LocalTtsSampleRate);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void Capture_ShouldExcludeTransientControllerApiKey()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            var config = new AIChatControllerConfig();
            config.SetApiKeyOverride(FixtureApiKey);

            AIChatRuntimeConfig snapshot = store.Capture(config);

            Assert.IsTrue(string.IsNullOrEmpty(snapshot.ApiKey));
        }

        [Test]
        public void OlderV4Config_PreservesNewSettingDefaults()
        {
            var path = Path.Combine(Path.GetTempPath(), $"ai_chat_old_v4_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(path, "{\"SchemaVersion\":4,\"ApiKey\":\"fixture\"}");
                var store = new JsonAIChatRuntimeConfigStore();
                Assert.IsTrue(store.TryLoad(path, out var config));
                Assert.AreEqual(-1, config.DebugMode);
                Assert.AreEqual(-1, config.LocalTtsNormalizeOutput);
                Assert.AreEqual(-1f, config.LocalTtsPlaybackVolume);
                Assert.AreEqual(FixtureApiKey, config.ApiKey);
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ApplyAndCapture_PreserveDebugAndLocalOutputSettings()
        {
            var owner = new UnityEngine.GameObject("TTS output config");
            owner.SetActive(false);
            try
            {
                var synthesizer = owner.AddComponent<Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.SpeechSynthesizer>();
                var controller = new AIChatControllerConfig { SpeechSynthesizer = synthesizer };
                var store = new JsonAIChatRuntimeConfigStore();
                store.Apply(new AIChatRuntimeConfig
                {
                    DebugMode = 1, LocalTtsNormalizeOutput = 0, LocalTtsPlaybackVolume = 1.4f
                }, controller);
                Assert.IsTrue(controller.DebugMode);
                Assert.IsFalse(synthesizer.NormalizeOutput);
                Assert.AreEqual(1.4f, synthesizer.PlaybackVolume);
                var captured = store.Capture(controller);
                Assert.AreEqual(1, captured.DebugMode);
                Assert.AreEqual(0, captured.LocalTtsNormalizeOutput);
                Assert.AreEqual(1.4f, captured.LocalTtsPlaybackVolume);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void Apply_WithEmptyPersistedKey_ShouldClearTransientControllerKey()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            var controllerConfig = new AIChatControllerConfig();
            controllerConfig.SetApiKeyOverride(FixtureApiKey);

            store.Apply(
                new AIChatRuntimeConfig { ApiKey = string.Empty },
                controllerConfig);

            Assert.IsTrue(string.IsNullOrEmpty(controllerConfig.ResolveApiKey()));
        }

        [Test]
        public void DebugMode_ControlsMicrophoneSynthesizerAndTrackerTogether()
        {
            var owner = new UnityEngine.GameObject("Debug settings");
            owner.SetActive(false);
            try
            {
                var controller = owner.AddComponent<AIChatController>();
                var synth = owner.AddComponent<Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.SpeechSynthesizer>();
                var mic = owner.AddComponent<Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR.VoiceMicrophone>();
                controller.CurrentConfig.Microphone = mic;
                controller.CurrentConfig.SpeechSynthesizer = synth;
                var tracker = new PipelineDebugTracker();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(AIChatController).GetField("_latencyTracker", flags).SetValue(controller, tracker);
                foreach (bool enabled in new[] { true, false })
                {
                    controller.CurrentConfig.DebugMode = enabled;
                    typeof(AIChatController).GetMethod("ApplyDebugSettings", flags).Invoke(controller, null);
                    Assert.AreEqual(enabled, mic.EnableLog);
                    Assert.AreEqual(enabled, synth.EnableLog);
                    Assert.AreEqual(enabled, tracker.Enabled);
                    Assert.AreEqual(enabled, tracker.LogEvents);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void RuntimePanelSave_PreservesOutputPreferencesWithoutControls()
        {
            var owner = new UnityEngine.GameObject("Runtime panel preferences");
            owner.SetActive(false);
            try
            {
                var controller = owner.AddComponent<AIChatController>();
                var synth = owner.AddComponent<Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS.SpeechSynthesizer>();
                var panel = owner.AddComponent<AIChatRuntimeConfigPanel>();
                controller.CurrentConfig.DebugMode = true;
                controller.CurrentConfig.SpeechSynthesizer = synth;
                synth.NormalizeOutput = false;
                synth.PlaybackVolume = 1.3f;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(AIChatRuntimeConfigPanel).GetField("_controller", flags).SetValue(panel, controller);
                var captured = (AIChatRuntimeConfig)typeof(AIChatRuntimeConfigPanel).GetMethod("ReadFields", flags).Invoke(panel, null);
                Assert.AreEqual(1, captured.DebugMode);
                Assert.AreEqual(0, captured.LocalTtsNormalizeOutput);
                Assert.AreEqual(1.3f, captured.LocalTtsPlaybackVolume);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [TestCase(3)]
        [TestCase(5)]
        public void TryLoad_ShouldRejectNonCurrentSchema(int schemaVersion)
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_schema_{Guid.NewGuid():N}.json");

            try
            {
                var unsupported = new AIChatRuntimeConfig
                {
                    SchemaVersion = schemaVersion,
                    ApiKey = string.Empty,
                    LlmModel = "unsupported-schema-model"
                };
                File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(unsupported, true));

                Assert.IsFalse(store.TryLoad(path, out AIChatRuntimeConfig loaded));
                Assert.IsNull(loaded);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void LoadOrCreate_ShouldReplaceNonCurrentSchemaWithCurrentDefaults()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_replace_{Guid.NewGuid():N}.json");

            try
            {
                var unsupported = new AIChatRuntimeConfig
                {
                    SchemaVersion = AIChatRuntimeConfig.CurrentSchemaVersion - 1,
                    ApiKey = string.Empty,
                    LlmModel = "unsupported-schema-model"
                };
                File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(unsupported, true));

                var controllerConfig = new AIChatControllerConfig
                {
                    LlmModel = AIChatProviderPresets.OpenAiLlmModel
                };

                AIChatRuntimeConfig current = store.LoadOrCreate(
                    path,
                    controllerConfig,
                    out bool createdDefault);

                Assert.IsTrue(createdDefault);
                Assert.AreEqual(AIChatRuntimeConfig.CurrentSchemaVersion, current.SchemaVersion);
                Assert.AreEqual(AIChatProviderPresets.OpenAiLlmModel, current.LlmModel);
                Assert.IsTrue(string.IsNullOrEmpty(current.ApiKey));
                Assert.IsTrue(store.TryLoad(path, out AIChatRuntimeConfig reloaded));
                Assert.AreEqual(AIChatRuntimeConfig.CurrentSchemaVersion, reloaded.SchemaVersion);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void StartupFlow_ShouldApplyDeviceConfigAfterScenePolicy()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_layers_{Guid.NewGuid():N}.json");
            var policyObject = new UnityEngine.GameObject("AI Chat Policy Test");

            try
            {
                var policy = policyObject.AddComponent<AIChatConfigurationPolicy>();
                var persisted = new AIChatRuntimeConfig
                {
                    ApiBaseUrl = "https://device-provider.example/v1/",
                    LlmModel = "device-model",
                    TtsModel = "device-tts",
                    TtsVoice = "device-voice",
                    UseLocalTts = 0
                };
                Assert.IsTrue(store.TrySave(path, persisted, out string saveError), saveError);

                var controllerConfig = new AIChatControllerConfig();
                AIChatRuntimeConfigurationFlow.ApplyStartupLayers(
                    policy,
                    store,
                    path,
                    controllerConfig,
                    loadRuntimeConfig: true);

                Assert.AreEqual(persisted.ApiBaseUrl, controllerConfig.ApiBaseUrl);
                Assert.AreEqual(persisted.LlmModel, controllerConfig.LlmModel);
                Assert.AreEqual(persisted.TtsModel, controllerConfig.TtsModel);
                Assert.AreEqual(persisted.TtsVoice, controllerConfig.TtsVoice);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(policyObject);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void StartupFlow_ShouldPreserveInjectedApiKeyAsFinalMemoryLayer()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_injected_key_{Guid.NewGuid():N}.json");

            try
            {
                Assert.IsTrue(
                    store.TrySave(
                        path,
                        new AIChatRuntimeConfig { ApiKey = PersistedFixtureApiKey },
                        out string saveError),
                    saveError);

                var controllerConfig = new AIChatControllerConfig();
                controllerConfig.SetApiKeyOverride(InjectedFixtureApiKey);

                AIChatRuntimeConfigurationFlow.ApplyStartupLayers(
                    null,
                    store,
                    path,
                    controllerConfig,
                    loadRuntimeConfig: true);

                Assert.AreEqual(InjectedFixtureApiKey, controllerConfig.ResolveApiKey());
                Assert.IsTrue(store.TryLoad(path, out AIChatRuntimeConfig persisted));
                Assert.AreEqual(PersistedFixtureApiKey, persisted.ApiKey);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void SetApiKey_BeforeAwake_ShouldStoreWithoutInitializingClient()
        {
            var target = new UnityEngine.GameObject("AI Chat Pre-Awake Key Test");
            target.SetActive(false);

            try
            {
                var controller = target.AddComponent<AIChatController>();
                controller.SetApiKey(InjectedFixtureApiKey);

                var clientField = typeof(AIChatController).GetField(
                    "_openAiClient",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.NotNull(clientField);
                Assert.AreEqual(InjectedFixtureApiKey, controller.CurrentConfig.ResolveApiKey());
                Assert.IsNull(clientField.GetValue(controller));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void InitializeOpenAiClient_WithMissingKey_ShouldRemainRecoverable()
        {
            var target = new UnityEngine.GameObject("AI Chat Missing Key Test");
            target.SetActive(false);

            try
            {
                var controller = target.AddComponent<AIChatController>();
                var initializeMethod = typeof(AIChatController).GetMethod(
                    "InitializeOpenAiClient",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var failedProperty = typeof(AIChatController).GetProperty(
                    "_initializationFailed",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                Assert.NotNull(initializeMethod);
                Assert.NotNull(failedProperty);
                initializeMethod.Invoke(controller, null);

                Assert.IsFalse((bool)failedProperty.GetValue(controller));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void SpeechSynthesizer_ShouldExposeRuntimeConfigurationReload()
        {
            Type synthesizerType = GetSpeechSynthesizerType();
            var reloadMethod = synthesizerType?.GetMethod("ReloadConfigurationAsync", Type.EmptyTypes);

            Assert.NotNull(reloadMethod);
            Assert.AreEqual(typeof(System.Threading.Tasks.Task), reloadMethod.ReturnType);

            var cancellableReloadMethod = synthesizerType?.GetMethod(
                "ReloadConfigurationAsync",
                new[] { typeof(CancellationToken) });
            Assert.NotNull(cancellableReloadMethod);
            Assert.AreEqual(typeof(Task), cancellableReloadMethod.ReturnType);
        }

        [Test]
        public void SpeechSynthesizer_DestroyHook_ShouldBeSynchronous()
        {
            Type synthesizerType = GetSpeechSynthesizerType();
            MethodInfo onDestroy = synthesizerType?.GetMethod(
                "OnDestroy",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(onDestroy);
            Assert.IsFalse(
                Attribute.IsDefined(onDestroy, typeof(AsyncStateMachineAttribute)),
                "Unity destruction cleanup must finish synchronously before the native object is released.");
        }

        [Test]
        public void SpeechSynthesizer_Disable_ShouldCancelConfigurationReloads()
        {
            Type synthesizerType = GetSpeechSynthesizerType();
            FieldInfo enabledCtsField = synthesizerType?.GetField(
                "_componentEnabledCts",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo initializingField = synthesizerType?.GetField(
                "_initializing",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo restartField = synthesizerType?.GetField(
                "_restartInitializationOnEnable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onDisable = synthesizerType?.GetMethod(
                "OnDisable",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(enabledCtsField);
            Assert.NotNull(initializingField);
            Assert.NotNull(restartField);
            Assert.NotNull(onDisable);

            var target = new UnityEngine.GameObject("Speech Synthesizer Disable Test");
            target.SetActive(false);
            try
            {
                UnityEngine.Component synthesizer = target.AddComponent(synthesizerType);
                var enabledCts = (CancellationTokenSource)enabledCtsField.GetValue(synthesizer);
                initializingField.SetValue(synthesizer, true);

                onDisable.Invoke(synthesizer, null);

                Assert.IsTrue(enabledCts.IsCancellationRequested);
                Assert.IsTrue((bool)restartField.GetValue(synthesizer));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void SpeechSynthesizer_ActiveReload_ShouldRenewCanceledEnableToken()
        {
            Type synthesizerType = GetSpeechSynthesizerType();
            FieldInfo enabledCtsField = synthesizerType?.GetField(
                "_componentEnabledCts",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo ensureEnabledCts = synthesizerType?.GetMethod(
                "EnsureComponentEnabledCancellationSource",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(enabledCtsField);
            Assert.NotNull(ensureEnabledCts);

            var target = new UnityEngine.GameObject("Speech Synthesizer Enable Token Test");
            try
            {
                UnityEngine.Component synthesizer = target.AddComponent(synthesizerType);
                var canceledCts = (CancellationTokenSource)enabledCtsField.GetValue(synthesizer);
                canceledCts.Cancel();

                ensureEnabledCts.Invoke(synthesizer, null);

                var renewedCts = (CancellationTokenSource)enabledCtsField.GetValue(synthesizer);
                Assert.AreNotSame(canceledCts, renewedCts);
                Assert.IsFalse(renewedCts.IsCancellationRequested);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void SetApiKey_ShouldCancelActiveRuntimeConfigurationRefresh()
        {
            FieldInfo refreshCtsField = typeof(AIChatController).GetField(
                "_runtimeConfigurationRefreshCts",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(refreshCtsField);

            var target = new UnityEngine.GameObject("AI Chat API Key Refresh Test");
            target.SetActive(false);
            var refreshCts = new CancellationTokenSource();
            try
            {
                var controller = target.AddComponent<AIChatController>();
                refreshCtsField.SetValue(controller, refreshCts);

                controller.SetApiKey(FixtureApiKey);

                Assert.IsTrue(refreshCts.IsCancellationRequested);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                refreshCts.Dispose();
            }
        }

        [Test]
        public void ControllerDisable_ShouldScheduleConfigurationRecovery()
        {
            FieldInfo runtimeStoreField = typeof(AIChatController).GetField(
                "_runtimeConfigStore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo restartField = typeof(AIChatController).GetField(
                "_restartConfigurationOnEnable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo onDisable = typeof(AIChatController).GetMethod(
                "OnDisable",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(runtimeStoreField);
            Assert.NotNull(restartField);
            Assert.NotNull(onDisable);

            var target = new UnityEngine.GameObject("AI Chat Disable Recovery Test");
            target.SetActive(false);
            try
            {
                var controller = target.AddComponent<AIChatController>();
                runtimeStoreField.SetValue(controller, new JsonAIChatRuntimeConfigStore());

                onDisable.Invoke(controller, null);

                Assert.IsTrue((bool)restartField.GetValue(controller));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static Type GetSpeechSynthesizerType()
            => typeof(AIChatControllerConfig)
                .GetField("SpeechSynthesizer")
                ?.FieldType;

        [Test]
        public void SaveFlow_ShouldPersistApplyAndRefreshWithoutSceneReload()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_save_{Guid.NewGuid():N}.json");

            try
            {
                var input = new AIChatRuntimeConfig
                {
                    ApiKey = FixtureApiKey,
                    ApiBaseUrl = "https://saved-provider.example/v1/",
                    LlmModel = "saved-model",
                    TtsModel = "saved-tts",
                    TtsVoice = "saved-voice",
                    UseLocalTts = 0
                };
                var controllerConfig = new AIChatControllerConfig();
                bool refreshed = false;

                bool saved = AIChatRuntimeConfigurationFlow.TrySaveAndApply(
                    store,
                    path,
                    input,
                    controllerConfig,
                    () => refreshed = true,
                    out string saveError);

                Assert.IsTrue(saved, saveError);
                Assert.IsTrue(refreshed);
                Assert.AreEqual(input.ApiBaseUrl, controllerConfig.ApiBaseUrl);
                Assert.AreEqual(input.LlmModel, controllerConfig.LlmModel);
                Assert.AreEqual(input.ApiKey, controllerConfig.ResolveApiKey());
                Assert.IsTrue(store.TryLoad(path, out AIChatRuntimeConfig persisted));
                Assert.AreEqual(input.LlmModel, persisted.LlmModel);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void SaveFlow_WhenPersistenceFails_ShouldNotApplyOrRefresh()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            var input = new AIChatRuntimeConfig
            {
                ApiBaseUrl = "https://unsaved-provider.example/v1/",
                LlmModel = "unsaved-model"
            };
            var controllerConfig = new AIChatControllerConfig();
            string initialModel = controllerConfig.LlmModel;
            bool refreshed = false;

            bool saved = AIChatRuntimeConfigurationFlow.TrySaveAndApply(
                store,
                string.Empty,
                input,
                controllerConfig,
                () => refreshed = true,
                out string saveError);

            Assert.IsFalse(saved);
            Assert.IsNotEmpty(saveError);
            Assert.IsFalse(refreshed);
            Assert.AreEqual(initialModel, controllerConfig.LlmModel);
        }

        [Test]
        public void SaveFlow_WhenRefreshFails_ShouldKeepSavedConfigAndReportApplyError()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_refresh_{Guid.NewGuid():N}.json");

            try
            {
                var input = new AIChatRuntimeConfig
                {
                    ApiBaseUrl = "https://saved-provider.example/v1/",
                    LlmModel = "saved-before-refresh-error"
                };

                bool completed = AIChatRuntimeConfigurationFlow.TrySaveAndApply(
                    store,
                    path,
                    input,
                    new AIChatControllerConfig(),
                    () => throw new InvalidOperationException("refresh failed"),
                    out string errorMessage);

                Assert.IsFalse(completed);
                StringAssert.Contains("saved", errorMessage.ToLowerInvariant());
                StringAssert.Contains("refresh failed", errorMessage);
                Assert.IsTrue(store.TryLoad(path, out AIChatRuntimeConfig persisted));
                Assert.AreEqual(input.LlmModel, persisted.LlmModel);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [UnityTest]
        public IEnumerator SaveFlow_WithRealControllerAndMissingKey_ShouldReportApplyFailure()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_real_refresh_{Guid.NewGuid():N}.json");
            var target = new UnityEngine.GameObject("AI Chat Real Refresh Test");
            target.SetActive(false);

            try
            {
                var controller = target.AddComponent<AIChatController>();
                var input = new AIChatRuntimeConfig
                {
                    ApiKey = string.Empty,
                    ApiBaseUrl = AIChatProviderPresets.OpenAiApiBaseUrl,
                    LlmModel = AIChatProviderPresets.OpenAiLlmModel
                };

                Task<AIChatRuntimeConfigurationResult> operation =
                    AIChatRuntimeConfigurationFlow.SaveAndApplyAsync(
                    store,
                    path,
                    input,
                    controller.CurrentConfig,
                    controller.RefreshRuntimeConfigurationAsync);
                while (!operation.IsCompleted)
                {
                    yield return null;
                }

                AIChatRuntimeConfigurationResult result = operation.GetAwaiter().GetResult();

                Assert.IsFalse(result.Succeeded);
                StringAssert.Contains("API key is missing", result.ErrorMessage);
                Assert.IsTrue(store.TryLoad(path, out AIChatRuntimeConfig persisted));
                Assert.AreEqual(input.LlmModel, persisted.LlmModel);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [UnityTest]
        public IEnumerator SaveFlowAsync_ShouldWaitForRuntimeRefresh()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_async_{Guid.NewGuid():N}.json");
            var refreshCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                Task<AIChatRuntimeConfigurationResult> operation =
                    AIChatRuntimeConfigurationFlow.SaveAndApplyAsync(
                        store,
                        path,
                        new AIChatRuntimeConfig { LlmModel = "async-model" },
                        new AIChatControllerConfig(),
                        () => refreshCompletion.Task);

                Assert.IsFalse(operation.IsCompleted);
                refreshCompletion.SetResult(true);
                while (!operation.IsCompleted)
                {
                    yield return null;
                }

                AIChatRuntimeConfigurationResult result = operation.GetAwaiter().GetResult();
                Assert.IsTrue(result.Succeeded, result.ErrorMessage);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

    }
}
#endif
