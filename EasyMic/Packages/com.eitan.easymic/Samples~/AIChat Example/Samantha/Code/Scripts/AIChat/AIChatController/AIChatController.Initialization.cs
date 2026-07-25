#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        private void ApplyConfigurationLayers()
        {
            if (_initializationFailed)
            {
                return;
            }

            try
            {
                AIChatRuntimeConfigurationFlow.ApplyStartupLayers(
                    _fixedSettingsOverride,
                    _runtimeConfigStore,
                    RuntimeConfigPath,
                    Config,
                    Config.LoadRuntimeConfigOnAwake);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Failed to apply startup configuration: {ex.Message}");
            }
        }

        internal async Task RefreshRuntimeConfigurationAsync()
        {
            var refreshCts = new CancellationTokenSource();
            CancellationTokenSource previousRefresh =
                Interlocked.Exchange(ref _runtimeConfigurationRefreshCts, refreshCts);
            TryCancelRuntimeConfigurationRefresh(previousRefresh);

            try
            {
                CancellationToken cancellationToken = refreshCts.Token;
                cancellationToken.ThrowIfCancellationRequested();
                if (_isShuttingDown || this == null)
                {
                    throw new ObjectDisposedException(nameof(AIChatController));
                }

                if (_initializationFailed)
                {
                    throw new InvalidOperationException("AIChatController has a fatal initialization error.");
                }

                InitializeOpenAiClient();
                if (_openAiClient == null)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(_lastErrorMessage)
                            ? "OpenAI-compatible client configuration is invalid."
                            : _lastErrorMessage);
                }

                await RefreshSpeechSynthesizerConfigurationAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!isActiveAndEnabled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (_openAiClient == null)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(_lastErrorMessage)
                            ? "OpenAI-compatible client configuration is invalid."
                            : _lastErrorMessage);
                }

                EnsureTtsPipelineConfigured();
                RefreshSystemPromptCache();
                if (IsIdle)
                {
                    NotifyChatStateChanged(ChatState.Idle, string.Empty);
                }
            }
            finally
            {
                Interlocked.CompareExchange(
                    ref _runtimeConfigurationRefreshCts,
                    null,
                    refreshCts);
                refreshCts.Dispose();
            }
        }

        private async Task RefreshSpeechSynthesizerConfigurationAsync(
            CancellationToken cancellationToken)
        {
            SpeechSynthesizer synthesizer = SpeechSynthesizer;
            if (!Config.UseLocalTts)
            {
                TeardownSpeechSynthesizer();
                if (synthesizer != null)
                {
                    await synthesizer.StopAndWaitAsync();
                    cancellationToken.ThrowIfCancellationRequested();
                    synthesizer.enabled = false;
                }

                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 1f);
                return;
            }

            if (synthesizer == null)
            {
                throw new InvalidOperationException(
                    "SpeechSynthesizer is required when local TTS is enabled.");
            }

            synthesizer.enabled = true;
            if (!_localTtsCallbacksRegistered)
            {
                synthesizer.OnLoadingProgressFeedback += OnSpeechSynthesizerProgressFeedbackHandler;
                _localTtsCallbacksRegistered = true;
            }

            UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 0f);
            await synthesizer.ReloadConfigurationAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!synthesizer.Initialized)
            {
                throw new InvalidOperationException(
                    "Local TTS model initialization did not complete successfully.");
            }

            UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 1f);
        }

        private void CancelRuntimeConfigurationRefresh()
        {
            CancellationTokenSource refreshCts =
                Interlocked.Exchange(ref _runtimeConfigurationRefreshCts, null);
            TryCancelRuntimeConfigurationRefresh(refreshCts);
        }

        private static void TryCancelRuntimeConfigurationRefresh(
            CancellationTokenSource refreshCts)
        {
            if (refreshCts == null)
            {
                return;
            }

            try
            {
                refreshCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
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
                const string message = "API base URL is empty.";
                Debug.LogWarning($"[AIChat] {message}");
                ReportRecoverableClientConfigurationError(message);
                return;
            }

            var apiKey = Config.ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                const string message = "API key is missing. Set it via runtime config or SetApiKey.";
                Debug.LogWarning($"[AIChat] {message}");
                ReportRecoverableClientConfigurationError(message);
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
                string message = $"Failed to initialize OpenAI client: {ex.Message}";
                Debug.LogWarning($"[AIChat] {message}");
                ReportRecoverableClientConfigurationError(message);
            }
        }

        private void ReportRecoverableClientConfigurationError(string message)
        {
            OpenAICompatibleClient previousClient = _openAiClient;
            _openAiClient = null;
            DisposeOpenAiClientWhenIdle(previousClient);
            _lastErrorMessage = message ?? string.Empty;
            NotifyChatStateChanged(ChatState.Failed, _lastErrorMessage);
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
            float maxDelay = Mathf.Clamp(delay * 2.5f, 0.6f, 1.2f);
            microphone.ConfigureTurnDetection(new TurnDetectionOptions(delay, Mathf.Max(delay, maxDelay)));
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
            _ttsPipeline.OnPlaybackAudioQueued -= OnTtsPlaybackAudioQueued;
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
                _ttsPipeline.OnPlaybackAudioQueued += OnTtsPlaybackAudioQueued;
            }

            if (_openAiClient != null)
            {
                _openAiClient.EnableTtsDiagnostics = Config.EnableTtsDiagnostics;
            }

            var pipelineConfig = new TtsPipelineConfig
            {
                UseLocalTts = Config.UseLocalTts && SpeechSynthesizer != null,
                LocalSynthesizer = SpeechSynthesizer,
                PlaybackSource = Config.UseLocalTts && SpeechSynthesizer != null
                    ? SpeechSynthesizer.PlaybackSource
                    : null,
                ClientProvider = GetOrCreateOpenAiClient,
                RemoteModel = Config.TtsModel,
                RemoteVoice = Config.TtsVoice,
                RemoteInputFormatter = BuildRemoteTtsInputFormatter(),
                EnableStreamingTts = Config.UseStreamingTts,
                MaxParallelGenerations = 2,
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

        private void OnPipelineSpeakingStateChanged(long turnId, bool isSpeaking)
        {
            if (!IsCurrentResponseGeneration(turnId))
            {
                return;
            }

            SetAssistantSpeakingState(turnId, isSpeaking);
            if (!isSpeaking)
            {
                _latencyTracker?.RecordPlaybackDrained();
            }
        }

        private void OnTtsSentenceStarted(long turnId, string sentence)
        {
            if (!IsCurrentResponseGeneration(turnId))
            {
                return;
            }

            lock (_stateLock)
            {
                _lastAssistantAudioStartRealtime = Time.realtimeSinceStartup;
            }
            TryCaptureLatencyMilestone(ref _lastFirstAudioLatencyMs, turnId);
            _latencyTracker?.RecordTtsFirstAudio();

            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][TTS] Speaking: {sentence}");
            }
        }

        private void OnTtsSentenceCompleted(long turnId, string sentence)
        {
            if (!IsCurrentResponseGeneration(turnId))
            {
                return;
            }

            _latencyTracker?.RecordTtsSentenceCompleted();
            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][TTS] Completed: {sentence}");
            }
        }

        private void OnTtsBufferProgress(long turnId, float bufferedSeconds)
        {
            if (!IsCurrentResponseGeneration(turnId))
            {
                return;
            }

            _lastPlaybackBufferedSeconds = bufferedSeconds;
        }

        private void OnTtsPlaybackAudioQueued(float[] samples, int count, int channels, int sampleRate)
        {
            var handler = OnAssistantAudioQueued;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(samples, count, channels, sampleRate);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Assistant audio observer failed: {ex.Message}");
            }
        }
    }
}
#endif
