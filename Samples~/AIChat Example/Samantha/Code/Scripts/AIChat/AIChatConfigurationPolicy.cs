using System;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [AddComponentMenu("Examples/EasyMic/AI Chat/Configuration Policy")]
    [DisallowMultipleComponent]
    public sealed class AIChatConfigurationPolicy : MonoBehaviour
    {
        private const string OpenAiApiBaseUrl = "https://api.openai.com/v1/";
        private const string SiliconFlowApiBaseUrl = "https://api.siliconflow.cn/v1/";
        private const string OpenAiLlmModel = "gpt-5.4";
        private const string SiliconFlowLlmModel = "Qwen/Qwen3.5-9B";
        private const string OpenAiTtsModel = "tts-1";
        private const string OpenAiTtsVoice = "alloy";
        private const string SiliconFlowTtsModel = "FunAudioLLM/CosyVoice2-0.5B";
        private const string SiliconFlowTtsVoice = "FunAudioLLM/CosyVoice2-0.5B:alex";

        public enum PolicyPreset
        {
            Custom = 0,
            OpenAI = 1,
            SiliconFlow = 2,
            LocalOnly = 3
        }

        [Serializable]
        private struct StringOverride
        {
            public bool Enabled;
            public string Value;
        }

        [Serializable]
        private struct BoolOverride
        {
            public bool Enabled;
            public bool Value;
        }

        [Serializable]
        private struct IntOverride
        {
            public bool Enabled;
            public int Value;
        }

        [Serializable]
        private struct FloatOverride
        {
            public bool Enabled;
            public float Value;
        }

        [Header("Master")]
        [SerializeField] private bool _enabledOverride = true;
        [SerializeField] private PolicyPreset _preset = PolicyPreset.OpenAI;

        [Header("Optional Overrides")]
        [SerializeField] private StringOverride _apiKey;
        [SerializeField] private StringOverride _apiBaseUrl;
        [SerializeField] private StringOverride _llmModel;
        [SerializeField] private FloatOverride _llmTemperature;
        [SerializeField] private IntOverride _maxHistoryTurns;
        [SerializeField] private BoolOverride _useLocalTts;
        [SerializeField] private StringOverride _ttsModel;
        [SerializeField] private StringOverride _ttsVoice;
        [SerializeField] private BoolOverride _useStreamingTts;
        [SerializeField] private BoolOverride _enableTtsDiagnostics;
        [SerializeField] private BoolOverride _interruptAssistantOnUserSpeech;
        [SerializeField] private FloatOverride _micStartupDelay;
        [SerializeField] private FloatOverride _asrTurnDetectionDelaySeconds;
        [SerializeField] private IntOverride _asrRecognitionModeIndex;
        [SerializeField] private StringOverride _asrStreamingModelId;
        [SerializeField] private StringOverride _asrOfflineModelId;
        [SerializeField] private StringOverride _asrVadModelId;
        [SerializeField] private BoolOverride _asrEnablePunctuation;
        [SerializeField] private StringOverride _asrPunctuationModelId;
        [SerializeField] private StringOverride _localTtsModelId;
        [SerializeField] private IntOverride _localTtsVoiceId;
        [SerializeField] private FloatOverride _localTtsSpeed;
        [SerializeField] private IntOverride _localTtsSampleRate;

        public bool EnabledOverride => _enabledOverride;
        public PolicyPreset Preset => _preset;

        public void ApplyTo(AIChatControllerConfig config)
        {
            if (!_enabledOverride || config == null)
            {
                return;
            }

            ApplyPreset(config);
            ApplyConfigOverrides(config);
            ApplyAsrOverrides(config.Microphone);
            ApplyLocalTtsOverrides(config.SpeechSynthesizer);
        }

        public AIChatResolvedConfiguration CreateResolvedConfiguration(AIChatControllerConfig config)
        {
            return CaptureResolvedConfiguration(config);
        }

        public AIChatResolvedConfiguration PreviewResolvedConfiguration(AIChatControllerConfig config)
        {
            var resolved = CaptureResolvedConfiguration(config);
            if (!_enabledOverride)
            {
                return resolved;
            }

            ApplyPreset(ref resolved);
            ApplyResolvedOverrides(ref resolved);
            return resolved;
        }

        private void ApplyPreset(AIChatControllerConfig config)
        {
            switch (_preset)
            {
                case PolicyPreset.OpenAI:
                    config.ApiBaseUrl = OpenAiApiBaseUrl;
                    config.LlmModel = OpenAiLlmModel;
                    config.UseLocalTts = false;
                    config.TtsModel = OpenAiTtsModel;
                    config.TtsVoice = OpenAiTtsVoice;
                    config.UseStreamingTts = true;
                    config.EnableTtsDiagnostics = false;
                    break;
                case PolicyPreset.SiliconFlow:
                    config.ApiBaseUrl = SiliconFlowApiBaseUrl;
                    config.LlmModel = SiliconFlowLlmModel;
                    config.UseLocalTts = false;
                    config.TtsModel = SiliconFlowTtsModel;
                    config.TtsVoice = SiliconFlowTtsVoice;
                    config.UseStreamingTts = true;
                    config.EnableTtsDiagnostics = false;
                    break;
                case PolicyPreset.LocalOnly:
                    config.UseLocalTts = true;
                    config.UseStreamingTts = false;
                    config.EnableTtsDiagnostics = false;
                    break;
            }
        }

        private void ApplyPreset(ref AIChatResolvedConfiguration resolved)
        {
            switch (_preset)
            {
                case PolicyPreset.OpenAI:
                    resolved.ApiBaseUrl = OpenAiApiBaseUrl;
                    resolved.LlmModel = OpenAiLlmModel;
                    resolved.UseLocalTts = false;
                    resolved.TtsModel = OpenAiTtsModel;
                    resolved.TtsVoice = OpenAiTtsVoice;
                    resolved.UseStreamingTts = true;
                    resolved.EnableTtsDiagnostics = false;
                    break;
                case PolicyPreset.SiliconFlow:
                    resolved.ApiBaseUrl = SiliconFlowApiBaseUrl;
                    resolved.LlmModel = SiliconFlowLlmModel;
                    resolved.UseLocalTts = false;
                    resolved.TtsModel = SiliconFlowTtsModel;
                    resolved.TtsVoice = SiliconFlowTtsVoice;
                    resolved.UseStreamingTts = true;
                    resolved.EnableTtsDiagnostics = false;
                    break;
                case PolicyPreset.LocalOnly:
                    resolved.UseLocalTts = true;
                    resolved.UseStreamingTts = false;
                    resolved.EnableTtsDiagnostics = false;
                    break;
            }
        }

        private void ApplyConfigOverrides(AIChatControllerConfig config)
        {
            ApplyString(_apiKey, value => config.SetApiKeyOverride(value));
            ApplyString(_apiBaseUrl, value => config.ApiBaseUrl = value);
            ApplyString(_llmModel, value => config.LlmModel = value);
            ApplyFloat(_llmTemperature, value => config.LlmTemperature = Mathf.Clamp(value, 0f, 1.5f));
            ApplyInt(_maxHistoryTurns, value => config.MaxHistoryTurns = Mathf.Max(0, value));
            ApplyBool(_useLocalTts, value => config.UseLocalTts = value);
            ApplyString(_ttsModel, value => config.TtsModel = value);
            ApplyString(_ttsVoice, value => config.TtsVoice = value);
            ApplyBool(_useStreamingTts, value => config.UseStreamingTts = value);
            ApplyBool(_enableTtsDiagnostics, value => config.EnableTtsDiagnostics = value);
            ApplyBool(_interruptAssistantOnUserSpeech, value => config.InterruptAssistantOnUserSpeech = value);
            ApplyFloat(_micStartupDelay, value => config.MicStartupDelay = Mathf.Max(0f, value));
            ApplyFloat(_asrTurnDetectionDelaySeconds, value => config.AsrTurnDetectionDelaySeconds = Mathf.Max(0.1f, value));
        }

        private void ApplyResolvedOverrides(ref AIChatResolvedConfiguration resolved)
        {
            if (_apiKey.Enabled && !string.IsNullOrWhiteSpace(_apiKey.Value))
            {
                resolved.ApiKey = _apiKey.Value.Trim();
            }

            if (_apiBaseUrl.Enabled && !string.IsNullOrWhiteSpace(_apiBaseUrl.Value))
            {
                resolved.ApiBaseUrl = _apiBaseUrl.Value.Trim();
            }

            if (_llmModel.Enabled && !string.IsNullOrWhiteSpace(_llmModel.Value))
            {
                resolved.LlmModel = _llmModel.Value.Trim();
            }

            if (_llmTemperature.Enabled)
            {
                resolved.LlmTemperature = Mathf.Clamp(_llmTemperature.Value, 0f, 1.5f);
            }

            if (_maxHistoryTurns.Enabled)
            {
                resolved.MaxHistoryTurns = Mathf.Max(0, _maxHistoryTurns.Value);
            }

            if (_useLocalTts.Enabled)
            {
                resolved.UseLocalTts = _useLocalTts.Value;
            }

            if (_ttsModel.Enabled && !string.IsNullOrWhiteSpace(_ttsModel.Value))
            {
                resolved.TtsModel = _ttsModel.Value.Trim();
            }

            if (_ttsVoice.Enabled && !string.IsNullOrWhiteSpace(_ttsVoice.Value))
            {
                resolved.TtsVoice = _ttsVoice.Value.Trim();
            }

            if (_useStreamingTts.Enabled)
            {
                resolved.UseStreamingTts = _useStreamingTts.Value;
            }

            if (_enableTtsDiagnostics.Enabled)
            {
                resolved.EnableTtsDiagnostics = _enableTtsDiagnostics.Value;
            }

            if (_interruptAssistantOnUserSpeech.Enabled)
            {
                resolved.InterruptAssistantOnUserSpeech = _interruptAssistantOnUserSpeech.Value;
            }

            if (_micStartupDelay.Enabled)
            {
                resolved.MicStartupDelay = Mathf.Max(0f, _micStartupDelay.Value);
            }

            if (_asrTurnDetectionDelaySeconds.Enabled)
            {
                resolved.AsrTurnDetectionDelaySeconds = Mathf.Max(0.1f, _asrTurnDetectionDelaySeconds.Value);
            }

            if (_asrRecognitionModeIndex.Enabled && TryMapRecognitionMode(_asrRecognitionModeIndex.Value, out var recognitionMode))
            {
                resolved.AsrRecognitionModeIndex = MapRecognitionModeToIndex(recognitionMode);
            }

            if (_asrStreamingModelId.Enabled && !string.IsNullOrWhiteSpace(_asrStreamingModelId.Value))
            {
                resolved.AsrStreamingModelId = _asrStreamingModelId.Value.Trim();
            }

            if (_asrOfflineModelId.Enabled && !string.IsNullOrWhiteSpace(_asrOfflineModelId.Value))
            {
                resolved.AsrOfflineModelId = _asrOfflineModelId.Value.Trim();
            }

            if (_asrVadModelId.Enabled && !string.IsNullOrWhiteSpace(_asrVadModelId.Value))
            {
                resolved.AsrVadModelId = _asrVadModelId.Value.Trim();
            }

            if (_asrEnablePunctuation.Enabled)
            {
                resolved.AsrEnablePunctuation = _asrEnablePunctuation.Value;
            }

            if (_asrPunctuationModelId.Enabled && !string.IsNullOrWhiteSpace(_asrPunctuationModelId.Value))
            {
                resolved.AsrPunctuationModelId = _asrPunctuationModelId.Value.Trim();
            }

            if (_localTtsModelId.Enabled && !string.IsNullOrWhiteSpace(_localTtsModelId.Value))
            {
                resolved.LocalTtsModelId = _localTtsModelId.Value.Trim();
            }

            if (_localTtsVoiceId.Enabled)
            {
                resolved.LocalTtsVoiceId = _localTtsVoiceId.Value;
            }

            if (_localTtsSpeed.Enabled)
            {
                resolved.LocalTtsSpeed = _localTtsSpeed.Value;
            }

            if (_localTtsSampleRate.Enabled)
            {
                resolved.LocalTtsSampleRate = Mathf.Max(1, _localTtsSampleRate.Value);
            }
        }

        private void ApplyAsrOverrides(VoiceMicrophone microphone)
        {
            if (microphone == null || microphone.AsrConfig == null)
            {
                return;
            }

            var preset = microphone.AsrConfig.ActivePresetConfiguration;

            if (_asrRecognitionModeIndex.Enabled && TryMapRecognitionMode(_asrRecognitionModeIndex.Value, out var recognitionMode))
            {
                preset.RecognitionMode = recognitionMode;
            }

            ApplyString(_asrStreamingModelId, value => preset.StreamingModelId = value);
            ApplyString(_asrOfflineModelId, value => preset.OfflineModelId = value);
            ApplyString(_asrVadModelId, value => preset.VadModelId = value);
            ApplyBool(_asrEnablePunctuation, value => preset.EnablePunctuation = value);
            ApplyString(_asrPunctuationModelId, value => preset.PunctuationModelId = value);

            if (_asrTurnDetectionDelaySeconds.Enabled)
            {
                float delay = Mathf.Max(0.1f, _asrTurnDetectionDelaySeconds.Value);
                preset.TurnDetectionOptions = new TurnDetectionOptions(delay, delay);
            }

            preset.Id = AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId;
            var asrConfig = AutomaticSpeechRecognitionConfiguration.CreateDefault();
            asrConfig.AddPreset(preset, true);
            asrConfig.SetActivePreset(AutomaticSpeechRecognitionConfiguration.ASRPreset.DefaultPresetId);
            microphone.ApplyConfiguration(asrConfig);
        }

        private void ApplyLocalTtsOverrides(SpeechSynthesizer synthesizer)
        {
            if (synthesizer == null || synthesizer.TtsConfig == null)
            {
                return;
            }

            var preset = synthesizer.TtsConfig.GetActivePreset();
            ApplyString(_localTtsModelId, value => preset.modelId = value);
            ApplyInt(_localTtsVoiceId, value => preset.voiceId = value);
            ApplyFloat(_localTtsSpeed, value => preset.speed = value);
            ApplyInt(_localTtsSampleRate, value => preset.sampleRates = Mathf.Max(1, value));
            preset.Id = SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId;

            var ttsConfig = SpeechSynthesizerConfiguration.CreateDefault();
            ttsConfig.AddPreset(preset, true);
            ttsConfig.SetActivePreset(SpeechSynthesizerConfiguration.TTSPreset.DefaultPresetId);
            synthesizer.ApplyConfiguration(ttsConfig);
        }

        internal static AIChatResolvedConfiguration CaptureResolvedConfiguration(AIChatControllerConfig config)
        {
            var settings = new AIChatResolvedConfiguration();
            if (config == null)
            {
                return settings;
            }

            settings.ApiKey = config.ResolveApiKey();
            settings.ApiBaseUrl = config.ApiBaseUrl;
            settings.LlmModel = config.LlmModel;
            settings.LlmTemperature = config.LlmTemperature;
            settings.MaxHistoryTurns = config.MaxHistoryTurns;
            settings.UseLocalTts = config.UseLocalTts;
            settings.TtsModel = config.TtsModel;
            settings.TtsVoice = config.TtsVoice;
            settings.UseStreamingTts = config.UseStreamingTts;
            settings.EnableTtsDiagnostics = config.EnableTtsDiagnostics;
            settings.AsrTurnDetectionDelaySeconds = config.AsrTurnDetectionDelaySeconds;
            settings.InterruptAssistantOnUserSpeech = config.InterruptAssistantOnUserSpeech;
            settings.MicStartupDelay = config.MicStartupDelay;

            var mic = config.Microphone;
            if (mic != null && mic.AsrConfig != null)
            {
                var preset = mic.AsrConfig.ActivePresetConfiguration;
                settings.AsrRecognitionModeIndex = MapRecognitionModeToIndex(preset.RecognitionMode);
                settings.AsrStreamingModelId = preset.StreamingModelId;
                settings.AsrOfflineModelId = preset.OfflineModelId;
                settings.AsrVadModelId = preset.VadModelId;
                settings.AsrEnablePunctuation = preset.EnablePunctuation;
                settings.AsrPunctuationModelId = preset.PunctuationModelId;
            }

            var synthesizer = config.SpeechSynthesizer;
            if (synthesizer != null && synthesizer.TtsConfig != null)
            {
                var preset = synthesizer.TtsConfig.GetActivePreset();
                settings.LocalTtsModelId = preset.modelId;
                settings.LocalTtsVoiceId = preset.voiceId;
                settings.LocalTtsSpeed = preset.speed;
                settings.LocalTtsSampleRate = preset.sampleRates;
            }

            return settings;
        }

        private static void ApplyString(StringOverride value, Action<string> apply)
        {
            if (!value.Enabled || string.IsNullOrWhiteSpace(value.Value) || apply == null)
            {
                return;
            }

            apply(value.Value.Trim());
        }

        private static void ApplyBool(BoolOverride value, Action<bool> apply)
        {
            if (!value.Enabled || apply == null)
            {
                return;
            }

            apply(value.Value);
        }

        private static void ApplyInt(IntOverride value, Action<int> apply)
        {
            if (!value.Enabled || apply == null)
            {
                return;
            }

            apply(value.Value);
        }

        private static void ApplyFloat(FloatOverride value, Action<float> apply)
        {
            if (!value.Enabled || apply == null)
            {
                return;
            }

            apply(value.Value);
        }

        private static bool TryMapRecognitionMode(int index, out RecognitionMode mode)
            => RecognitionModeMapping.TryMapRecognitionMode(index, out mode);

        private static int MapRecognitionModeToIndex(RecognitionMode mode)
            => RecognitionModeMapping.MapRecognitionModeToIndex(mode);
    }
}
