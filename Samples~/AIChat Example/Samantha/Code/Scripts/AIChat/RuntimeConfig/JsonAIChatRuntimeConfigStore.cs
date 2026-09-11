#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.IO;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class JsonAIChatRuntimeConfigStore : IAIChatRuntimeConfigStore
    {
        public AIChatRuntimeConfig CreateDefault(AIChatControllerConfig controllerConfig)
        {
            var config = controllerConfig ?? new AIChatControllerConfig();
            var runtimeConfig = new AIChatRuntimeConfig
            {
                ApiBaseUrl = config.ApiBaseUrl,
                LlmModel = config.LlmModel,
                LlmTemperature = config.LlmTemperature,
                TtsModel = config.TtsModel,
                TtsVoice = config.TtsVoice,
                UseLocalTts = config.UseLocalTts ? 1 : 0,
                DebugMode = config.DebugMode ? 1 : 0,
                LocalTtsNormalizeOutput = config.SpeechSynthesizer == null || config.SpeechSynthesizer.NormalizeOutput ? 1 : 0,
                LocalTtsPlaybackVolume = config.SpeechSynthesizer != null ? config.SpeechSynthesizer.PlaybackVolume : 1f,
                MicrophoneDeviceName = config.Microphone != null
                    ? config.Microphone.DeviceOpts.DeviceName
                    : null,
                AsrRecognitionModeIndex = MapRecognitionModeToIndex(RecognitionMode.Streaming),
                AsrEnablePunctuation = 1,
                AsrStreamingModelId = AIChatRuntimeDefaults.DefaultAsrStreamingModelId,
                AsrOfflineModelId = AIChatRuntimeDefaults.DefaultAsrOfflineModelId,
                AsrVadModelId = AIChatRuntimeDefaults.DefaultAsrVadModelId,
                AsrTurnDetectionDelaySeconds = Mathf.Max(0.1f, config.AsrTurnDetectionDelaySeconds),
                AsrPunctuationModelId = AIChatRuntimeDefaults.DefaultAsrPunctuationModelId,
                LocalTtsModelId = AIChatRuntimeDefaults.DefaultLocalTtsModelId,
                LocalTtsVoiceId = AIChatRuntimeDefaults.DefaultLocalTtsVoiceId,
                LocalTtsSpeed = AIChatRuntimeDefaults.DefaultLocalTtsSpeed,
                LocalTtsSampleRate = AIChatRuntimeDefaults.DefaultLocalTtsSampleRate
            };

            if (config.Microphone != null && config.Microphone.AsrConfig != null)
            {
                var preset = config.Microphone.AsrConfig.ActivePresetConfiguration;
                runtimeConfig.AsrRecognitionModeIndex = MapRecognitionModeToIndex(preset.RecognitionMode);
                runtimeConfig.AsrStreamingModelId = preset.StreamingModelId;
                runtimeConfig.AsrOfflineModelId = preset.OfflineModelId;
                runtimeConfig.AsrVadModelId = preset.VadModelId;
                runtimeConfig.AsrEnablePunctuation = preset.EnablePunctuation ? 1 : 0;
                runtimeConfig.AsrPunctuationModelId = preset.PunctuationModelId;
            }

            return runtimeConfig;
        }

        public AIChatRuntimeConfig Capture(AIChatControllerConfig controllerConfig)
        {
            var snapshot = CreateDefault(controllerConfig);
            if (controllerConfig == null)
            {
                return snapshot;
            }

            snapshot.ApiBaseUrl = controllerConfig.ApiBaseUrl;
            snapshot.LlmModel = controllerConfig.LlmModel;
            snapshot.LlmTemperature = controllerConfig.LlmTemperature;
            snapshot.TtsModel = controllerConfig.TtsModel;
            snapshot.TtsVoice = controllerConfig.TtsVoice;
            snapshot.UseLocalTts = controllerConfig.UseLocalTts ? 1 : 0;
            snapshot.DebugMode = controllerConfig.DebugMode ? 1 : 0;
            if (controllerConfig.SpeechSynthesizer != null)
            {
                snapshot.LocalTtsNormalizeOutput = controllerConfig.SpeechSynthesizer.NormalizeOutput ? 1 : 0;
                snapshot.LocalTtsPlaybackVolume = controllerConfig.SpeechSynthesizer.PlaybackVolume;
            }
            snapshot.MicrophoneDeviceName = controllerConfig.Microphone != null
                ? controllerConfig.Microphone.DeviceOpts.DeviceName
                : null;
            snapshot.AsrTurnDetectionDelaySeconds = Mathf.Max(0.1f, controllerConfig.AsrTurnDetectionDelaySeconds);

            var mic = controllerConfig.Microphone;
            if (mic != null && mic.AsrConfig != null)
            {
                var preset = mic.AsrConfig.ActivePresetConfiguration;
                snapshot.AsrRecognitionModeIndex = MapRecognitionModeToIndex(preset.RecognitionMode);
                snapshot.AsrStreamingModelId = preset.StreamingModelId;
                snapshot.AsrOfflineModelId = preset.OfflineModelId;
                snapshot.AsrVadModelId = preset.VadModelId;
                snapshot.AsrTurnDetectionDelaySeconds = Mathf.Max(0.1f, preset.TurnDetectionOptions.MinDelaySeconds);
                snapshot.AsrEnablePunctuation = preset.EnablePunctuation ? 1 : 0;
                snapshot.AsrPunctuationModelId = preset.PunctuationModelId;
            }

            var synthesizer = controllerConfig.SpeechSynthesizer;
            if (synthesizer != null && synthesizer.TtsConfig != null)
            {
                var preset = synthesizer.TtsConfig.GetActivePreset();
                snapshot.LocalTtsModelId = preset.modelId;
                snapshot.LocalTtsVoiceId = preset.voiceId;
                snapshot.LocalTtsSpeed = preset.speed;
                snapshot.LocalTtsSampleRate = preset.sampleRates;
            }

            return snapshot;
        }

        public bool TryLoad(string path, out AIChatRuntimeConfig runtimeConfig)
        {
            runtimeConfig = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                // Preserve sentinel defaults for additive settings absent in older schema-v4 files.
                runtimeConfig = new AIChatRuntimeConfig { SchemaVersion = 0 };
                JsonUtility.FromJsonOverwrite(json, runtimeConfig);
                if (runtimeConfig == null ||
                    runtimeConfig.SchemaVersion != AIChatRuntimeConfig.CurrentSchemaVersion)
                {
                    runtimeConfig = null;
                    return false;
                }

                return true;
            }
            catch
            {
                runtimeConfig = null;
                return false;
            }
        }

        public AIChatRuntimeConfig LoadOrCreate(string path, AIChatControllerConfig controllerConfig, out bool createdDefault)
        {
            createdDefault = false;
            if (TryLoad(path, out var runtimeConfig))
            {
                return runtimeConfig;
            }

            runtimeConfig = CreateDefault(controllerConfig);
            createdDefault = true;
            TrySave(path, runtimeConfig, out _);
            return runtimeConfig;
        }

        public bool TrySave(string path, AIChatRuntimeConfig runtimeConfig, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                errorMessage = "Path is empty.";
                return false;
            }

            if (runtimeConfig == null ||
                runtimeConfig.SchemaVersion != AIChatRuntimeConfig.CurrentSchemaVersion)
            {
                errorMessage = $"Runtime config schema must be {AIChatRuntimeConfig.CurrentSchemaVersion}.";
                return false;
            }

            try
            {
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                string json = JsonUtility.ToJson(runtimeConfig, true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public void Apply(AIChatRuntimeConfig runtimeConfig, AIChatControllerConfig controllerConfig)
        {
            if (runtimeConfig == null || controllerConfig == null)
            {
                return;
            }

            controllerConfig.SetApiKeyOverride(runtimeConfig.ApiKey);

            if (!string.IsNullOrWhiteSpace(runtimeConfig.ApiBaseUrl))
            {
                controllerConfig.ApiBaseUrl = runtimeConfig.ApiBaseUrl;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.LlmModel))
            {
                controllerConfig.LlmModel = runtimeConfig.LlmModel;
            }

            if (runtimeConfig.LlmTemperature >= 0f)
            {
                controllerConfig.LlmTemperature = runtimeConfig.LlmTemperature;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.TtsModel))
            {
                controllerConfig.TtsModel = runtimeConfig.TtsModel;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.TtsVoice))
            {
                controllerConfig.TtsVoice = runtimeConfig.TtsVoice;
            }

            if (runtimeConfig.UseLocalTts >= 0)
            {
                controllerConfig.UseLocalTts = runtimeConfig.UseLocalTts > 0;
            }

            if (runtimeConfig.DebugMode >= 0)
            {
                controllerConfig.DebugMode = runtimeConfig.DebugMode > 0;
            }

            ApplyMicrophoneDevice(runtimeConfig, controllerConfig.Microphone);

            if (runtimeConfig.AsrTurnDetectionDelaySeconds > 0f)
            {
                controllerConfig.AsrTurnDetectionDelaySeconds = runtimeConfig.AsrTurnDetectionDelaySeconds;
            }

            ApplyAsrConfig(runtimeConfig, controllerConfig.Microphone);
            ApplyLocalTtsConfig(runtimeConfig, controllerConfig.SpeechSynthesizer);
        }

        private static void ApplyMicrophoneDevice(AIChatRuntimeConfig runtimeConfig, VoiceMicrophone microphone)
        {
            if (runtimeConfig == null || microphone == null || runtimeConfig.MicrophoneDeviceName == null)
            {
                return;
            }

            var options = microphone.DeviceOpts;
            options.DeviceName = runtimeConfig.MicrophoneDeviceName.Trim();
            microphone.ApplyDeviceOptions(options, restartRecording: false);
        }

        private static void ApplyAsrConfig(AIChatRuntimeConfig runtimeConfig, VoiceMicrophone microphone)
        {
            if (runtimeConfig == null || microphone == null)
            {
                return;
            }

            if (microphone.AsrConfig == null)
            {
                return;
            }

            var preset = microphone.AsrConfig.ActivePresetConfiguration;

            if (TryMapRecognitionMode(runtimeConfig.AsrRecognitionModeIndex, out var recognitionMode))
            {
                preset.RecognitionMode = recognitionMode;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrStreamingModelId))
            {
                preset.StreamingModelId = runtimeConfig.AsrStreamingModelId;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrOfflineModelId))
            {
                preset.OfflineModelId = runtimeConfig.AsrOfflineModelId;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrVadModelId))
            {
                preset.VadModelId = runtimeConfig.AsrVadModelId.Trim();
            }

            if (runtimeConfig.AsrEnablePunctuation >= 0)
            {
                preset.EnablePunctuation = runtimeConfig.AsrEnablePunctuation > 0;
            }

            if (!string.IsNullOrWhiteSpace(runtimeConfig.AsrPunctuationModelId))
            {
                preset.PunctuationModelId = runtimeConfig.AsrPunctuationModelId;
            }

            if (runtimeConfig.AsrTurnDetectionDelaySeconds > 0f)
            {
                float delay = Mathf.Max(0.1f, runtimeConfig.AsrTurnDetectionDelaySeconds);
                float maxDelay = Mathf.Clamp(delay * 2.5f, 0.6f, 1.2f);
                preset.TurnDetectionOptions = new TurnDetectionOptions(delay, Mathf.Max(delay, maxDelay));
            }

            preset.Id = AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId;
            var asrConfig = AutomaticSpeechRecognitionConfiguration.CreateDefault();
            asrConfig.AddPreset(preset, true);
            asrConfig.SetActivePreset(AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId);
            microphone.ApplyConfiguration(asrConfig);
        }

        private static void ApplyLocalTtsConfig(AIChatRuntimeConfig runtimeConfig, SpeechSynthesizer synthesizer)
        {
            if (runtimeConfig == null || synthesizer == null)
            {
                return;
            }

            if (synthesizer.TtsConfig == null)
            {
                return;
            }

            var preset = synthesizer.TtsConfig.GetActivePreset();

            if (!string.IsNullOrWhiteSpace(runtimeConfig.LocalTtsModelId))
            {
                preset.modelId = runtimeConfig.LocalTtsModelId;
            }

            if (runtimeConfig.LocalTtsVoiceId >= 0)
            {
                preset.voiceId = runtimeConfig.LocalTtsVoiceId;
            }

            if (runtimeConfig.LocalTtsSpeed > 0f)
            {
                preset.speed = runtimeConfig.LocalTtsSpeed;
            }

            if (runtimeConfig.LocalTtsSampleRate > 0)
            {
                preset.sampleRates = runtimeConfig.LocalTtsSampleRate;
            }

            if (runtimeConfig.LocalTtsNormalizeOutput >= 0)
            {
                synthesizer.NormalizeOutput = runtimeConfig.LocalTtsNormalizeOutput > 0;
            }

            if (runtimeConfig.LocalTtsPlaybackVolume >= 0f)
            {
                synthesizer.PlaybackVolume = runtimeConfig.LocalTtsPlaybackVolume;
            }

            preset.Id = SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId;
            var ttsConfig = SpeechSynthesizerConfiguration.CreateDefault();
            ttsConfig.AddPreset(preset, true);
            ttsConfig.SetActivePreset(SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId);
            synthesizer.ApplyConfiguration(ttsConfig);
        }

        private static bool TryMapRecognitionMode(int index, out RecognitionMode mode)
            => RecognitionModeMapping.TryMapRecognitionMode(index, out mode);

        private static int MapRecognitionModeToIndex(RecognitionMode mode)
            => RecognitionModeMapping.MapRecognitionModeToIndex(mode);
    }
}
#endif
