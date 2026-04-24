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
        private async void BeginAssistantResponse(string userInput, bool recordUserMessage = true, bool isProactive = false)
        {
            if (_initializationFailed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                return;
            }

            long generation = Interlocked.Increment(ref _responseGeneration);

            if (_openAiClient == null)
            {
                InitializeOpenAiClient();
            }

            if (_openAiClient == null)
            {
                Debug.LogWarning("[AIChat] OpenAI client not available.");
                NotifyChatStateChanged(ChatState.Failed, "API client not configured");
                return;
            }

            await CancelActiveResponseAsync(advanceGeneration: false).ConfigureAwait(false);
            if (!IsCurrentResponseGeneration(generation))
            {
                return;
            }

            _requestOrchestrator?.ResetCurrentResponse();
            ResetResponseLatencyTracking();

            _llmInFlight = true;
            _totalRequestCount++;
            UpdateIdleState();

            var responseCts = new CancellationTokenSource();
            ReplaceResponseCancellationTokenSource(responseCts);
            var token = responseCts.Token;
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

            _ = RunChatCompletionAsync(generation, userInput, token, recordUserMessage, isProactive);
        }

        private async Task CancelActiveResponseAsync(bool advanceGeneration = true)
        {
            if (advanceGeneration)
            {
                Interlocked.Increment(ref _responseGeneration);
            }

            CancelAndDisposeCts(TakeResponseCancellationTokenSource());

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

            _requestOrchestrator?.ResetCurrentResponse();
            _llmInFlight = false;
            SetAssistantSpeakingState(false);
            UpdateIdleState();
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
                _failedRequestCount++;
                _networkHandler.RecordTimeout();
                Debug.LogError($"[AIChat] Request timeout: {ex.Message}");
                NotifyChatStateChanged(ChatState.Failed, "Request timeout");
                errorMessage = "Request timeout";
            }
            catch (Exception ex)
            {
                _failedRequestCount++;
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
                    _llmInFlight = false;
                    UpdateIdleState();
                    NotifyPluginHost(host => host.NotifyAssistantResponseFinished(
                        responseSucceeded ? finalResponse : null,
                        responseSucceeded,
                        errorMessage));

                    if (_ttsPipeline != null)
                    {
                        try
                        {
                            await _ttsPipeline.WaitForIdleAsync().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[AIChat] Error waiting for TTS idle: {ex.Message}");
                        }
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

            _ = plugin.RefreshInstructionFromContextAsync(client, ResolveLlmModel(), userInput, binding, token);
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
