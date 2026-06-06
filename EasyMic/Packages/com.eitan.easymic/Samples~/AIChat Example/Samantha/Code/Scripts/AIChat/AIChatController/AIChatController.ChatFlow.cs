#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public partial class AIChatController
    {
        private bool BeginAssistantResponse(string userInput, bool recordUserMessage = true, bool isProactive = false)
        {
            if (_initializationFailed)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                return true;
            }

            long generation = Interlocked.Increment(ref _responseGeneration);
            lock (_stateLock) _lastResponseStartRealtime = Time.realtimeSinceStartup;

            if (_openAiClient == null)
            {
                InitializeOpenAiClient();
            }

            if (_openAiClient == null)
            {
                Debug.LogWarning("[AIChat] OpenAI client not available.");
                NotifyChatStateChanged(ChatState.Failed, "API client not configured");
                return true;
            }

            SignalCancelActiveResponse(advanceGeneration: false, dispatchBufferedInputOnIdle: false);

            // Wait for TTS pipeline drain from SignalCancelActiveResponse to complete
            // before creating the new tracker round. Without this, a stale TTS drain
            // event from the previous response can fire OnPipelineSpeakingStateChanged(false)
            // after RecordLlmRequestSent creates the new round, causing RecordPlaybackDrained
            // to finalize the new round prematurely (= corrupted timing data).
            // NOTE: BeginAssistantResponse runs on the Unity main thread (ASR callback via
            // UnitySynchronizationContext). A blocking Wait() would freeze the UI for its
            // duration, so we use Wait(0) — just check without blocking. The stale drain
            // guard in PipelineDebugTracker (RecordPlaybackDrained's IsComplete check +
            // _drainGenerationAtCancel) provides defense-in-depth protection.
            try
            {
                if (!_drainCompleteGate.Wait(0))
                {
                    // Drain not yet complete; the stale-event guard in the tracker
                    // will protect the new round from any late-arriving drain events.
                }
            }
            catch (ObjectDisposedException)
            {
            }

            if (!IsCurrentResponseGeneration(generation))
            {
                return false;
            }

            ResetResponseLatencyTracking();

            var responseCts = new CancellationTokenSource();
            ReplaceResponseCancellationTokenSource(responseCts);

            if (!IsCurrentResponseGeneration(generation))
            {
                if (TryTakeResponseCancellationTokenSource(responseCts, out var staleCts))
                {
                    CancelAndDisposeCts(staleCts);
                }

                return false;
            }

            var token = responseCts.Token;
            _latencyTracker?.RecordLlmRequestSent();

            _llmInFlight = true;
            Interlocked.Increment(ref _totalRequestCount);
            UpdateIdleState();

            RefreshExpressiveTtsInstruction(userInput, token);

            if (!_conversationStarted)
            {
                _conversationStarted = true;
                NotifyPluginHost(host => host.NotifyConversationStarted(isProactive));
            }

            if (recordUserMessage)
            {
                NotifyPluginHost(host => host.NotifyUserMessageSubmitted(userInput, isProactive));
            }

            NotifyPluginHost(host => host.NotifyAssistantRequestStarted(userInput, isProactive));

            if (!IsCurrentResponseGeneration(generation) || token.IsCancellationRequested)
            {
                if (TryTakeResponseCancellationTokenSource(responseCts, out var staleCts))
                {
                    CancelAndDisposeCts(staleCts);
                    _llmInFlight = false;
                    UpdateIdleState(dispatchBufferedInput: false);
                }

                return false;
            }

            SafeFireAndForget(RunChatCompletionAsync(generation, userInput, token, recordUserMessage, isProactive), nameof(RunChatCompletionAsync));
            return true;
        }

        private void SignalCancelActiveResponse(bool advanceGeneration = true, bool dispatchBufferedInputOnIdle = true)
        {
            // Capture state before clearing flags — used to decide whether to cancel
            bool hadActiveResponse = _llmInFlight || _isAssistantSpeaking;

            if (advanceGeneration)
            {
                Interlocked.Increment(ref _responseGeneration);
            }

            // STOP TTS AUDIO FIRST: Begin draining the TTS pipeline before cancelling the LLM.
            // Industry best practice: stop audio output immediately, then cancel generation.
            // This ensures the user hears silence as fast as possible during barge-in.
            _drainCompleteGate.Reset();
            SafeFireAndForget(DrainPipelineAfterCancelAsyncInternal(), nameof(DrainPipelineAfterCancelAsyncInternal));

            CancelAndDisposeCts(TakeResponseCancellationTokenSource());

            _requestOrchestrator?.ResetCurrentResponse();
            _llmInFlight = false;
            _isAssistantSpeaking = false;
            UpdateIdleState(dispatchBufferedInputOnIdle);

            NotifyChatStateChanged(ChatState.Idle, null);
            // Only cancel tracker round if there was an active response (avoids destroying
            // the ASR round when BeginAssistantResponse calls cancel during normal flow)
            if (hadActiveResponse)
                _latencyTracker?.CancelCurrentRound();
        }

        private async Task DrainPipelineAfterCancelAsync()
        {
            if (_ttsPipeline != null)
            {
                try
                {
                    await _ttsPipeline.StopAndWaitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AIChat] Error stopping TTS pipeline: {ex.Message}");
                }
            }
            else if (Config.UseLocalTts && SpeechSynthesizer != null)
            {
                try
                {
                    await SpeechSynthesizer.StopAndWaitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AIChat] Error stopping synthesizer: {ex.Message}");
                }
            }
        }

        private async Task DrainPipelineAfterCancelAsyncInternal()
        {
            await DrainPipelineAfterCancelAsync().ConfigureAwait(false);
            try
            {
                _drainCompleteGate.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task CancelActiveResponseAsync(bool advanceGeneration = true)
        {
            SignalCancelActiveResponse(advanceGeneration);
            // Drain is already started as fire-and-forget inside SignalCancelActiveResponse.
            // Wait for the drain gate instead of calling DrainPipelineAfterCancelAsync again
            // (which would invoke StopAndWaitAsync a second time on the TTS pipeline).
            // Use async non-blocking wait to avoid thread pool starvation (nested Task.Run).
            // ManualResetEventSlim doesn't have WaitAsync, so poll with small async delays.
            try
            {
                int maxWaitMs = 1000;
                int pollIntervalMs = 10;
                int elapsedMs = 0;
                while (elapsedMs < maxWaitMs)
                {
                    if (_drainCompleteGate.IsSet)
                        break;
                    await Task.Delay(pollIntervalMs).ConfigureAwait(false);
                    elapsedMs += pollIntervalMs;
                }
                if (elapsedMs >= maxWaitMs)
                {
                    Debug.LogWarning("[AIChat] CancelActiveResponseAsync drain gate timed out.");
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task RunChatCompletionAsync(long generation, string userInput, CancellationToken token, bool recordUserMessage, bool isProactive)
        {
            if (_initializationFailed)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            bool firstChunkReceived = false;
            bool responseSucceeded = false;
            string errorMessage = null;
            string finalResponse = null;

            try
            {
                string model = ResolveLlmModel();
                var chatRequest = new OpenAIChatRequest
                {
                    Model = model,
                    Stream = true,
                    Temperature = Config.LlmTemperature,
                    EnableThinkingOverride = false,
                    Messages = BuildMessages(userInput)
                };

                await foreach (string chunk in _openAiClient.StreamChatCompletionAsync(chatRequest, token))
                {
                    token.ThrowIfCancellationRequested();

                    if (!firstChunkReceived)
                    {
                        firstChunkReceived = true;
                        float latencyMs = (float)stopwatch.Elapsed.TotalMilliseconds;
                        TryCaptureLatencyMilestone(ref _lastFirstTokenLatencyMs, generation);
                        _latencyTracker?.RecordLlmFirstToken();
                        _networkHandler.RecordLatency(latencyMs);
                        UpdateAverageLatency(latencyMs);

                        if (Config.LogStreamingChunks)
                        {
                            Debug.Log($"[AIChat] First chunk latency: {latencyMs:F0}ms");
                        }
                    }

                    if (!string.IsNullOrEmpty(chunk))
                    {
                        if (Config.LogStreamingChunks)
                        {
                            Debug.Log($"[AIChat][LLM] {chunk}");
                        }

                        string normalizedChunk = _requestOrchestrator?.AppendStreamingChunk(chunk) ?? string.Empty;

                        if (!string.IsNullOrEmpty(normalizedChunk))
                        {
                            if (!IsCurrentResponseGeneration(generation))
                            {
                                break;
                            }

                            ProcessStreamingChunk(normalizedChunk);
                            NotifyChatStateChanged(ChatState.AssistantResponseStreaming, normalizedChunk);
                        }
                    }
                }

                if (!IsCurrentResponseGeneration(generation))
                {
                    return;
                }

                FlushPendingSentences();
                _latencyTracker?.RecordLlmLastToken();

                finalResponse = GetCleanedResponse();
                string rawResponse = GetRawResponse();
                AppendConversationHistory(recordUserMessage ? userInput : null, rawResponse);

                MarkAssistantResponse();
                NotifyChatStateChanged(ChatState.AssistantResponseFinish, finalResponse);
                ExtractAndNotifyWebLinks(rawResponse);
                responseSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AIChat] Response cancelled.");
            }
            catch (TimeoutException ex)
            {
                Interlocked.Increment(ref _failedRequestCount);
                _networkHandler.RecordTimeout();
                Debug.LogError($"[AIChat] Request timeout: {ex.Message}");
                NotifyChatStateChanged(ChatState.Failed, "Request timeout");
                errorMessage = "Request timeout";
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedRequestCount);
                UnityEngine.Debug.LogError(ex);
                Debug.LogError($"[AIChat] Chat completion failed: {ex.Message}");
                NotifyChatStateChanged(ChatState.Failed, ex.Message);
                errorMessage = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                bool isCurrentGeneration = IsCurrentResponseGeneration(generation);
                if (isCurrentGeneration)
                {
                    if (!responseSucceeded && !string.IsNullOrEmpty(errorMessage) && recordUserMessage)
                    {
                        AppendConversationHistory(userInput, null);
                    }

                    _llmInFlight = false;
                    UpdateIdleState();
                    NotifyPluginHost(host => host.NotifyAssistantResponseFinished(
                        responseSucceeded ? finalResponse : null,
                        responseSucceeded,
                        errorMessage));

                    if (_ttsPipeline != null)
                    {
                        SafeFireAndForget(_ttsPipeline.WaitForIdleAsync(), nameof(_ttsPipeline.WaitForIdleAsync));
                    }
                }

                EndResponseLatencyTracking(generation);
            }
        }

        private List<OpenAIChatMessage> BuildMessages(string transcript)
        {
            return _requestOrchestrator?.BuildMessages(transcript) ?? new List<OpenAIChatMessage>();
        }

        private void RefreshExpressiveTtsInstruction(string userInput, CancellationToken token)
        {
            var plugin = _activeSiliconFlowTtsInputPlugin;
            var binding = _activeSiliconFlowTtsInputBinding;
            if (plugin == null || binding == null)
            {
                return;
            }

            var client = _openAiClient;
            if (client == null)
            {
                return;
            }

            SafeFireAndForget(plugin.RefreshInstructionFromContextAsync(client, ResolveLlmModel(), userInput, binding, token), nameof(RefreshExpressiveTtsInstruction));
        }

        private void AppendConversationHistory(string userMessage, string assistantMessage)
        {
            _requestOrchestrator?.AppendConversationHistory(userMessage, assistantMessage);
        }

        private void ProcessStreamingChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _requestOrchestrator?.ProcessStreamingChunk(chunk, DispatchAssistantSentence);
        }

        private void FlushPendingSentences()
        {
            _requestOrchestrator?.FlushPendingSentences(DispatchAssistantSentence);
        }

        private void DispatchAssistantSentence(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
            {
                return;
            }

            string cleaned = CleanText(sentence);
            if (string.IsNullOrEmpty(cleaned))
            {
                return;
            }

            if (Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][Sentence] {cleaned}");
            }

            TryCaptureLatencyMilestone(ref _lastFirstSentenceLatencyMs, Interlocked.Read(ref _responseGeneration));
            _latencyTracker?.RecordTtsSentenceDispatched();
            _ttsPipeline?.Enqueue(cleaned);
        }

        private void ExtractAndNotifyWebLinks(string content)
        {
            if (string.IsNullOrEmpty(content) || OnWebLinksExtracted == null)
            {
                return;
            }

            string sanitized = content.Replace("{{", string.Empty).Replace("}}", string.Empty);
            MatchCollection matches = WebLinkRegex.Matches(sanitized);

            if (matches.Count == 0)
            {
                return;
            }

            var links = new List<string>(matches.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in matches)
            {
                string url = match.Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '}', '"', '\'',
                    '，', '。', '！', '？', '；', '：', '）', '】', '》');
                url = url.Replace("{{", string.Empty).Replace("}}", string.Empty);

                if (seen.Add(url))
                {
                    links.Add(url);
                }
            }

            if (links.Count > 0)
            {
                OnWebLinksExtracted.Invoke(links.ToArray());
            }
        }
    }
}
#endif
