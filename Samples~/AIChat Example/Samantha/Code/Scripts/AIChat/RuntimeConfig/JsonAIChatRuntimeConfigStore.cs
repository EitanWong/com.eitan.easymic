#if EASYMIC_SHERPA_ONNX_INTEGRATION

using System;
using System.IO;
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class JsonAIChatRuntimeConfigStore : IAIChatRuntimeConfigStore
    {
        public AIChatRuntimeConfig CreateDefault(AIChatControllerConfig controllerConfig)
        {
            var config = controllerConfig ?? new AIChatControllerConfig();
            return new AIChatRuntimeConfig
            {
                ApiBaseUrl = config.ApiBaseUrl,
                LlmModel = config.LlmModel,
                LlmTemperature = config.LlmTemperature,
                TtsModel = config.TtsModel,
                TtsVoice = config.TtsVoice,
                UseLocalTts = config.UseLocalTts ? 1 : 0,
                AsrRecognitionModeIndex = MapRecognitionModeToIndex(RecognitionMode.OfflineWithVad),
                AsrEnablePunctuation = 0,
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
        }

        public AIChatRuntimeConfig Capture(AIChatControllerConfig controllerConfig)
        {
            var snapshot = CreateDefault(controllerConfig);
            if (controllerConfig == null)
            {
                return snapshot;
            }

            snapshot.ApiKey = controllerConfig.ResolveApiKey();
            snapshot.ApiBaseUrl = controllerConfig.ApiBaseUrl;
            snapshot.LlmModel = controllerConfig.LlmModel;
            snapshot.LlmTemperature = controllerConfig.LlmTemperature;
            snapshot.TtsModel = controllerConfig.TtsModel;
            snapshot.TtsVoice = controllerConfig.TtsVoice;
            snapshot.UseLocalTts = controllerConfig.UseLocalTts ? 1 : 0;
            snapshot.AsrTurnDetectionDelaySeconds = Mathf.Max(0.1f, controllerConfig.AsrTurnDetectionDelaySeconds);

            var mic = controllerConfig.Microphone;
            if (mic != null && mic.AsrConfig != null)
            {
                var preset = mic.AsrConfig.ActivePresetConfiguration;
                snapshot.AsrRecognitionModeIndex = MapRecognitionModeToIndex(preset.RecognitionMode);
                snapshot.AsrStreamingModelId = preset.StreamingModelId;
                snapshot.AsrOfflineModelId = preset.OfflineModelId;
                snapshot.AsrVadModelId = NormalizeVadModelId(preset.VadModelId);
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

                runtimeConfig = JsonUtility.FromJson<AIChatRuntimeConfig>(json);
                return runtimeConfig != null;
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

            try
            {
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                string json = JsonUtility.ToJson(runtimeConfig ?? new AIChatRuntimeConfig(), true);
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

            if (!string.IsNullOrWhiteSpace(runtimeConfig.ApiKey))
            {
                controllerConfig.SetApiKeyOverride(runtimeConfig.ApiKey);
            }

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

            if (runtimeConfig.AsrTurnDetectionDelaySeconds > 0f)
            {
                controllerConfig.AsrTurnDetectionDelaySeconds = runtimeConfig.AsrTurnDetectionDelaySeconds;
            }

            ApplyAsrConfig(runtimeConfig, controllerConfig.Microphone);
            ApplyLocalTtsConfig(runtimeConfig, controllerConfig.SpeechSynthesizer);
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
                preset.VadModelId = NormalizeVadModelId(runtimeConfig.AsrVadModelId);
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
                preset.TurnDetectionOptions = new TurnDetectionOptions(delay, delay);
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

            preset.Id = SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId;
            var ttsConfig = SpeechSynthesizerConfiguration.CreateDefault();
            ttsConfig.AddPreset(preset, true);
            ttsConfig.SetActivePreset(SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId);
            synthesizer.ApplyConfiguration(ttsConfig);
        }

        private static string NormalizeVadModelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            if (value.Equals("silero_vad_v5", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("silero-vad-v5", StringComparison.OrdinalIgnoreCase))
            {
                return AIChatRuntimeDefaults.DefaultAsrVadModelId;
            }

            return value;
        }

        private static bool TryMapRecognitionMode(int index, out RecognitionMode mode)
        {
            switch (index)
            {
                case 0:
                    mode = RecognitionMode.Streaming;
                    return true;
                case 1:
                    mode = RecognitionMode.OfflineWithVad;
                    return true;
                case 2:
                    mode = RecognitionMode.Hybrid;
                    return true;
                default:
                    mode = RecognitionMode.Streaming;
                    return false;
            }
        }

        private static int MapRecognitionModeToIndex(RecognitionMode mode)
        {
            switch (mode)
            {
                case RecognitionMode.Streaming:
                    return 0;
                case RecognitionMode.OfflineWithVad:
                    return 1;
                case RecognitionMode.Hybrid:
                    return 2;
                default:
                    return -1;
            }
        }
    }
}
#endif
