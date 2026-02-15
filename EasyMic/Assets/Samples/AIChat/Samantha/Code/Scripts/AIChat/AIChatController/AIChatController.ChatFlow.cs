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

            await CancelActiveResponseAsync();
            _requestOrchestrator?.ResetCurrentResponse();

            _llmInFlight = true;
            _totalRequestCount++;
            UpdateIdleState();

            var responseCts = new CancellationTokenSource();
            ReplaceResponseCancellationTokenSource(responseCts);
            var token = responseCts.Token;

            if (!_conversationStarted)
            {
                _conversationStarted = true;
                if (IsOnUnityThread)
                {
                    _pluginHost?.NotifyConversationStarted(isProactive);
                }
                else
                {
                    PostToUnityThread(() => _pluginHost?.NotifyConversationStarted(isProactive));
                }
            }

            if (recordUserMessage)
            {
                if (IsOnUnityThread)
                {
                    _pluginHost?.NotifyUserMessageSubmitted(userInput, isProactive);
                }
                else
                {
                    PostToUnityThread(() => _pluginHost?.NotifyUserMessageSubmitted(userInput, isProactive));
                }
            }

            if (IsOnUnityThread)
            {
                _pluginHost?.NotifyAssistantRequestStarted(userInput, isProactive);
            }
            else
            {
                PostToUnityThread(() => _pluginHost?.NotifyAssistantRequestStarted(userInput, isProactive));
            }

            _ = RunChatCompletionAsync(userInput, token, recordUserMessage, isProactive);
        }

        private async Task CancelActiveResponseAsync()
        {
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

        private async Task RunChatCompletionAsync(string userInput, CancellationToken token, bool recordUserMessage, bool isProactive)
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
                var chatRequest = new OpenAIChatRequest
                {
                    Model = string.IsNullOrWhiteSpace(Config.LlmModel)
                        ? "gpt-5.2"
                        : Config.LlmModel.Trim(),
                    Stream = true,
                    Temperature = Config.LlmTemperature,
                    Messages = BuildMessages(userInput)
                };

                await foreach (string chunk in _openAiClient.StreamChatCompletionAsync(chatRequest, token))
                {
                    token.ThrowIfCancellationRequested();

                    if (!firstChunkReceived)
                    {
                        firstChunkReceived = true;
                        float latencyMs = (float)stopwatch.Elapsed.TotalMilliseconds;
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
                            ProcessStreamingChunk(normalizedChunk);
                            NotifyChatStateChanged(ChatState.AssistantResponseStreaming, normalizedChunk);
                        }
                    }
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
                _llmInFlight = false;
                UpdateIdleState();
                PostToUnityThread(() =>
                    _pluginHost?.NotifyAssistantResponseFinished(
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
        }

        private List<OpenAIChatMessage> BuildMessages(string transcript)
        {
            return _requestOrchestrator?.BuildMessages(transcript) ?? new List<OpenAIChatMessage>();
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
