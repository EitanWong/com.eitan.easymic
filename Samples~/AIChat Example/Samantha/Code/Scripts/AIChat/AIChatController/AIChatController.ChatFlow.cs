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
            FullDuplexTurn responseTurn = _turnCoordinator.BeginTurn();
            long generation = responseTurn.TurnId;
            CancellationTokenSource responseCts = responseTurn.CancellationSource;
            CancellationToken token = responseTurn.Token;
            bool requestTurnPublished = _requestOrchestrator == null || _requestOrchestrator.BeginResponse(generation, Config.UseLocalTts);
            bool ttsTurnPublished = _ttsPipeline == null || _ttsPipeline.BeginTurn(generation);
            if (!requestTurnPublished || !ttsTurnPublished)
            {
                _turnCoordinator.TryCompleteLlm(generation, responseCts);
                try
                {
                    responseCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                responseCts.Dispose();
                return false;
            }
            lock (_stateLock)
            {
                _lastAssistantAudioStartRealtime = 0f;
            }

            ResetResponseLatencyTracking();
            _latencyTracker?.RecordLlmRequestSent();

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
                if (_turnCoordinator.TryCompleteLlm(generation, responseCts))
                {
                    UpdateIdleState(dispatchBufferedInput: false);
                }

                responseCts.Dispose();
                return false;
            }

            SafeFireAndForget(RunChatCompletionAsync(generation, userInput, responseCts, recordUserMessage, isProactive), nameof(RunChatCompletionAsync));
            return true;
        }

        private void SignalCancelActiveResponse(bool advanceGeneration = true, bool dispatchBufferedInputOnIdle = true)
        {
            try
            {
                _drainCompleteGate.Reset();
            }
            catch (ObjectDisposedException)
            {
            }

            // Calling an async stop method executes its synchronous stop prefix immediately.
            // Capture that task before cancelling the network turn so audible output is cut first.
            Task drainTask = BeginPipelineDrain();
            SafeFireAndForget(
                CompletePipelineDrainAsync(drainTask),
                nameof(CompletePipelineDrainAsync));

            FullDuplexInterruption interruption = _turnCoordinator.Interrupt(advanceGeneration);
            _requestOrchestrator?.BeginResponse(interruption.CurrentTurnId);
            UpdateIdleState(dispatchBufferedInputOnIdle);

            NotifyChatStateChanged(ChatState.Idle, null);
            if (interruption.HadActiveTurn)
            {
                _latencyTracker?.CancelCurrentRound();
            }
        }

        private Task BeginPipelineDrain()
        {
            try
            {
                if (_ttsPipeline != null)
                {
                    return _ttsPipeline.StopAndWaitAsync();
                }

                if (Config.UseLocalTts && SpeechSynthesizer != null)
                {
                    return SpeechSynthesizer.StopAndWaitAsync();
                }
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }

            return Task.CompletedTask;
        }

        private async Task CompletePipelineDrainAsync(Task drainTask)
        {
            try
            {
                if (drainTask != null)
                {
                    await drainTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChat] Error stopping TTS pipeline: {ex.Message}");
            }
            finally
            {
                try
                {
                    _drainCompleteGate.Set();
                }
                catch (ObjectDisposedException)
                {
                }
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

        private async Task RunChatCompletionAsync(long generation, string userInput, CancellationTokenSource responseCts, bool recordUserMessage, bool isProactive)
        {
            CancellationToken token = responseCts.Token;
            var stopwatch = Stopwatch.StartNew();
            bool firstChunkReceived = false;
            bool responseSucceeded = false;
            string errorMessage = null;
            string finalResponse = null;

            try
            {
                if (_initializationFailed)
                {
                    return;
                }

                var client = _openAiClient;
                if (client == null)
                {
                    throw new InvalidOperationException("OpenAI client not available");
                }

                string model = ResolveLlmModel();
                var chatRequest = new OpenAIChatRequest
                {
                    Model = model,
                    Stream = true,
                    Temperature = Config.LlmTemperature,
                    EnableThinkingOverride = false,
                    Messages = BuildMessages(userInput)
                };

                await foreach (string chunk in client.StreamChatCompletionAsync(chatRequest, token))
                {
                    token.ThrowIfCancellationRequested();
                    if (!IsCurrentResponseGeneration(generation))
                    {
                        break;
                    }

                    if (!firstChunkReceived)
                    {
                        firstChunkReceived = true;
                        float latencyMs = (float)stopwatch.Elapsed.TotalMilliseconds;
                        TryCaptureLatencyMilestone(ref _lastFirstTokenLatencyMs, generation);
                        _latencyTracker?.RecordLlmFirstToken();
                        _networkHandler.RecordLatency(latencyMs);
                        UpdateAverageLatency(latencyMs);

                        if (Config.DebugMode && Config.LogStreamingChunks)
                        {
                            Debug.Log($"[AIChat] First chunk latency: {latencyMs:F0}ms");
                        }
                    }

                    if (!string.IsNullOrEmpty(chunk))
                    {
                        if (Config.DebugMode && Config.LogStreamingChunks)
                        {
                            Debug.Log($"[AIChat][LLM] {chunk}");
                        }

                        string normalizedChunk = _requestOrchestrator?.AppendStreamingChunk(generation, chunk) ?? string.Empty;

                        if (!string.IsNullOrEmpty(normalizedChunk))
                        {
                            ProcessStreamingChunk(generation, normalizedChunk);
                            NotifyChatStateChanged(ChatState.AssistantResponseStreaming, normalizedChunk);
                        }
                    }
                }

                if (!IsCurrentResponseGeneration(generation))
                {
                    return;
                }

                FlushPendingSentences(generation);
                _latencyTracker?.RecordLlmLastToken();

                finalResponse = GetCleanedResponse(generation);
                string rawResponse = GetRawResponse(generation);
                AppendConversationHistory(recordUserMessage ? userInput : null, rawResponse);

                MarkAssistantResponse();
                NotifyChatStateChanged(ChatState.AssistantResponseFinish, finalResponse);
                ExtractAndNotifyWebLinks(rawResponse);
                responseSucceeded = true;
            }
            catch (OperationCanceledException)
            {
                if (Config.DebugMode)
                {
                    Debug.Log("[AIChat] Response cancelled.");
                }
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
                bool isCurrentGeneration = _turnCoordinator.TryCompleteLlm(generation, responseCts);
                if (isCurrentGeneration)
                {
                    if (!responseSucceeded && !string.IsNullOrEmpty(errorMessage) && recordUserMessage)
                    {
                        AppendConversationHistory(userInput, null);
                    }

                    _ttsPipeline?.CompleteTurn(generation);
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
                responseCts.Dispose();
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

        private void ProcessStreamingChunk(long generation, string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _requestOrchestrator?.ProcessStreamingChunk(
                generation,
                chunk,
                sentence => DispatchAssistantSentence(generation, sentence));
        }

        private void FlushPendingSentences(long generation)
        {
            _requestOrchestrator?.FlushPendingSentences(
                generation,
                sentence => DispatchAssistantSentence(generation, sentence));
        }

        private void DispatchAssistantSentence(long generation, string sentence)
        {
            if (!IsCurrentResponseGeneration(generation) || string.IsNullOrWhiteSpace(sentence))
            {
                return;
            }

            string cleaned = CleanText(sentence);
            if (string.IsNullOrEmpty(cleaned))
            {
                return;
            }

            if (Config.DebugMode && Config.LogStreamingChunks)
            {
                Debug.Log($"[AIChat][Sentence] {cleaned}");
            }

            if (_ttsPipeline != null)
            {
                if (_ttsPipeline.Enqueue(generation, cleaned))
                {
                    TryCaptureLatencyMilestone(ref _lastFirstSentenceLatencyMs, generation);
                    _latencyTracker?.RecordTtsSentenceDispatched();
                    _turnCoordinator.TrySetAssistantSpeaking(generation, true);
                    UpdateIdleState();
                }
            }
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
