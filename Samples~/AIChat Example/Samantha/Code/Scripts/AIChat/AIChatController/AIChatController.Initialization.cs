#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private void InitializeComponents()
        {
            _serviceLoadingRecord = new Dictionary<string, float>
            {
                [SERVICE_ASR_INIT_KEY] = 0f,
                [SERVICE_TTS_INIT_KEY] = 0f
            };

            _fixedSettingsOverride = GetComponent<AIChatConfigurationPolicy>();
            _runtimeConfigStore = new JsonAIChatRuntimeConfigStore();
            _requestOrchestrator = new AIChatRequestOrchestrator(
                historyTurnProvider: () => Config.MaxHistoryTurns,
                systemPromptProvider: GetSystemPrompt,
                cleanText: CleanText,
                maxResponseBufferSize: MaxResponseBufferSize);

            _initialized = false;
            _lastLoadingProgress = 0f;
            _networkHandler = new NetworkAdaptiveHandler();
        }

        private void LoadRuntimeConfigIfNeeded()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (!Config.LoadRuntimeConfigOnAwake)
            {
                return;
            }

            try
            {
                string path = RuntimeConfigPath;
                var runtimeConfig = _runtimeConfigStore.LoadOrCreate(path, Config, out _);
                if (runtimeConfig == null)
                {
                    return;
                }

                _runtimeConfigStore.Apply(runtimeConfig, Config);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to load runtime config: {ex.Message}");
            }
        }

        private void ApplyFixedSettingsOverrideIfPresent()
        {
            if (_initializationFailed || _fixedSettingsOverride == null || !_fixedSettingsOverride.EnabledOverride)
            {
                return;
            }

            _fixedSettingsOverride.ApplyTo(Config);
            PersistRuntimeConfigSnapshot();
        }

        private void PersistRuntimeConfigSnapshot()
        {
            if (_runtimeConfigStore == null)
            {
                return;
            }

            try
            {
                AIChatRuntimeConfig snapshot = _runtimeConfigStore.Capture(Config);
                _runtimeConfigStore.TrySave(RuntimeConfigPath, snapshot, out _);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to persist runtime config snapshot: {ex.Message}");
            }
        }

        private void InitializeOpenAiClient()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(Config.ApiBaseUrl))
            {
                Debug.LogWarning("[AIChat] API base URL is empty.");
                ReportError("API base URL is empty.");
                return;
            }

            var apiKey = Config.ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Debug.LogWarning("[AIChat] API key is missing. Set it via runtime config or SetApiKey.");
                ReportError("API key is missing. Set it via runtime config or SetApiKey.");
                return;
            }

            string normalized = Config.ApiBaseUrl.Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            try
            {
                OpenAICompatibleClient previousClient = _openAiClient;
                _openAiClient = new OpenAICompatibleClient(normalized, apiKey);
                _openAiClient.EnableTtsDiagnostics = Config.EnableTtsDiagnostics;
                DisposeOpenAiClientWhenIdle(previousClient);
                _lastErrorMessage = string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIChat] Failed to initialize OpenAI client: {ex.Message}");
                ReportError($"Failed to initialize OpenAI client: {ex.Message}");
                _openAiClient = null;
            }
        }

        private void InitializeMicrophone()
        {
            if (_initializationFailed)
            {
                return;
            }

            var mic = Microphone;
            if (mic == null)
            {
                Debug.LogError("[AIChat] VoiceMicrophone reference is missing.");
                ReportError("VoiceMicrophone reference is missing.");
                return;
            }

            ApplyTurnDetectionDelayOverride(mic);

            mic.OnMicrophoneInitialized += OnMicrophoneInitializedHandler;
            mic.OnASRTranscriptionStreaming += OnAsrStreamingHandler;
            mic.OnASRTranscriptionSubmit += OnAsrSubmitHandler;
            mic.OnSpeakingChanged += OnSpeakingChangedHandler;
            mic.OnLoadingProgressFeedback += OnMicrophoneLoadingProgressFeedbackHandler;

            if (!mic.MicrophoneOpts.recordOnAwake)
            {
                mic.Init();
            }
        }

        private void ApplyTurnDetectionDelayOverride(VoiceMicrophone microphone)
        {
            if (microphone == null)
            {
                return;
            }

            float delay = Mathf.Max(0.1f, Config.AsrTurnDetectionDelaySeconds);
            microphone.ConfigureTurnDetection(new TurnDetectionOptions(delay, delay));
        }

        private void InitializeSpeechSynthesizer()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (!Config.UseLocalTts)
            {
                if (SpeechSynthesizer != null)
                {
                    SpeechSynthesizer.enabled = false;
                }
                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 1f);
                EnsureTtsPipelineConfigured();
                return;
            }

            if (SpeechSynthesizer == null)
            {
                Debug.LogWarning("[AIChat] SpeechSynthesizer reference is required when local TTS is enabled.");
                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 0f);
                ReportError("SpeechSynthesizer reference is required when local TTS is enabled.");
                return;
            }

            SpeechSynthesizer.OnLoadingProgressFeedback += OnSpeechSynthesizerProgressFeedbackHandler;
            SpeechSynthesizer.OnTTSStateChanged += OnLocalTtsStateChanged;
            _localTtsCallbacksRegistered = true;
            EnsureTtsPipelineConfigured();
        }

        private void TeardownMicrophone()
        {
            StopPendingMicStartup();

            var mic = Microphone;
            if (mic == null)
            {
                return;
            }

            mic.OnMicrophoneInitialized -= OnMicrophoneInitializedHandler;
            mic.OnASRTranscriptionStreaming -= OnAsrStreamingHandler;
            mic.OnASRTranscriptionSubmit -= OnAsrSubmitHandler;
            mic.OnSpeakingChanged -= OnSpeakingChangedHandler;
            mic.OnLoadingProgressFeedback -= OnMicrophoneLoadingProgressFeedbackHandler;

            if (mic.IsRecording)
            {
                mic.StopRecording();
            }
        }

        private void TeardownSpeechSynthesizer()
        {
            if (!_localTtsCallbacksRegistered || SpeechSynthesizer == null)
            {
                return;
            }

            SpeechSynthesizer.OnLoadingProgressFeedback -= OnSpeechSynthesizerProgressFeedbackHandler;
            SpeechSynthesizer.OnTTSStateChanged -= OnLocalTtsStateChanged;
            _localTtsCallbacksRegistered = false;
        }

        private void TeardownTtsPipeline()
        {
            if (_ttsPipeline == null)
            {
                return;
            }

            _ttsPipeline.OnSpeakingStateChanged -= OnPipelineSpeakingStateChanged;
            _ttsPipeline.OnSentenceStarted -= OnTtsSentenceStarted;
            _ttsPipeline.OnSentenceCompleted -= OnTtsSentenceCompleted;
            _ttsPipeline.OnBufferProgress -= OnTtsBufferProgress;
            _ttsPipeline.Dispose();
            _ttsPipeline = null;
        }

        private OpenAICompatibleClient GetOrCreateOpenAiClient()
        {
            if (_openAiClient == null)
            {
                InitializeOpenAiClient();
            }

            return _openAiClient;
        }

        private void EnsureTtsPipelineConfigured()
        {
            if (_initializationFailed)
            {
                return;
            }

            if (_ttsPipeline == null)
            {
                _ttsPipeline = new ChatTtsPipeline(GetOrCreateOpenAiClient);
                _ttsPipeline.OnSpeakingStateChanged += OnPipelineSpeakingStateChanged;
                _ttsPipeline.OnSentenceStarted += OnTtsSentenceStarted;
                _ttsPipeline.OnSentenceCompleted += OnTtsSentenceCompleted;
                _ttsPipeline.OnBufferProgress += OnTtsBufferProgress;
            }

            if (_openAiClient != null)
            {
                _openAiClient.EnableTtsDiagnostics = Config.EnableTtsDiagnostics;
            }

            var pipelineConfig = new TtsPipelineConfig
            {
                UseLocalTts = Config.UseLocalTts && SpeechSynthesizer != null,
                LocalSynthesizer = SpeechSynthesizer,
                PlaybackSource = SpeechSynthesizer != null ? SpeechSynthesizer.PlaybackSource : null,
                ClientProvider = GetOrCreateOpenAiClient,
                RemoteModel = Config.TtsModel,
                RemoteVoice = Config.TtsVoice,
                RemoteInputFormatter = BuildRemoteTtsInputFormatter(),
                EnableStreamingTts = Config.UseStreamingTts,
                LogSentences = Config.LogStreamingChunks,
                EnableDiagnostics = Config.EnableTtsDiagnostics,
                MainThreadDispatcher = PostToUnityThread
            };

            _ttsPipeline.Configure(pipelineConfig);
        }

        private Func<string, string, string, string> BuildRemoteTtsInputFormatter()
        {
            _activeSiliconFlowTtsInputPlugin = null;
            _activeSiliconFlowTtsInputBinding = null;

            var plugins = new List<SiliconFlowExpressiveTtsInputPlugin>();
            GetComponents(plugins);

            if (_pluginBehaviours != null && _pluginBehaviours.Count > 0)
            {
                for (int i = 0; i < _pluginBehaviours.Count; i++)
                {
                    if (!(_pluginBehaviours[i] is SiliconFlowExpressiveTtsInputPlugin plugin))
                    {
                        continue;
                    }

                    if (!plugins.Contains(plugin))
                    {
                        plugins.Add(plugin);
                    }
                }
            }

            if (plugins.Count == 0)
            {
                return null;
            }

            string apiBaseUrl = Config.ApiBaseUrl;
            for (int i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                if (plugin == null)
                {
                    continue;
                }

                var profile = plugin.CreateRuntimeProfile();
                if (!profile.Enabled)
                {
                    continue;
                }

                var binding = plugin.CreateRuntimeBinding();
                if (binding == null)
                {
                    continue;
                }

                _activeSiliconFlowTtsInputPlugin = plugin;
                _activeSiliconFlowTtsInputBinding = binding;

                return (input, model, _) =>
                {
                    SiliconFlowExpressiveTtsInputPlugin.RuntimeProfile currentProfile = binding.CreateCurrentProfile();
                    if (!SiliconFlowExpressiveTtsInputPlugin.ShouldApply(apiBaseUrl, model, currentProfile))
                    {
                        return input;
                    }

                    return SiliconFlowExpressiveTtsInputPlugin.FormatInput(input, currentProfile);
                };
            }

            return null;
        }

        private void OnPipelineSpeakingStateChanged(bool isSpeaking)
        {
            SetAssistantSpeakingState(isSpeaking);
            if (!isSpeaking)
            {
                // Guard: Only record playback drain if this is still the current response.
                // During interruption, SignalCancelActiveResponse cancels the old round and
                // starts a new one. The TTS pipeline's stale `NotifySpeakingState(false)` can
                // arrive AFTER the new round is already created, causing RecordPlaybackDrained
                // to finalize the new round prematurely (= corrupted timing data).
                // The generation check rejects stale drain events from previous responses.
                _latencyTracker?.RecordPlaybackDrained();
            }
        }

        private void OnTtsSentenceStarted(string sentence)
        {
            TryCaptureLatencyMilestone(ref _lastFirstAudioLatencyMs, Interlocked.Read(ref _responseGeneration));
            _latencyTracker?.RecordTtsFirstAudio();

            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][TTS] Speaking: {sentence}");
            }
        }

        private void OnTtsSentenceCompleted(string sentence)
        {
            _latencyTracker?.RecordTtsSentenceCompleted();
            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][TTS] Completed: {sentence}");
            }
        }

        private void OnTtsBufferProgress(float bufferedSeconds)
        {
            _lastPlaybackBufferedSeconds = bufferedSeconds;
        }
    }
}
#endif
