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
        private async void BeginAssistantResponse(string userInput)
        {
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
                OnChatStateChanged?.Invoke(ChatState.Failed, "API client not configured");
                return;
            }

            await CancelActiveResponseAsync().ConfigureAwait(false);

            lock (_stateLock)
            {
                _responseBuffer.Clear();
            }
            _sentenceAssembler.Reset();

            _llmInFlight = true;
            _totalRequestCount++;
            UpdateIdleState();

            _responseCts = new CancellationTokenSource();
            var token = _responseCts.Token;

            _ = RunChatCompletionAsync(userInput, token);
        }

        private async Task CancelActiveResponseAsync()
        {
            CancellationTokenSource oldCts;

            lock (_stateLock)
            {
                oldCts = _responseCts;
                _responseCts = null;
            }

            if (oldCts != null)
            {
                try
                {
                    if (!oldCts.IsCancellationRequested)
                    {
                        oldCts.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
            }

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

            if (oldCts != null)
            {
                oldCts.Dispose();
            }

            _sentenceAssembler.Reset();
            _llmInFlight = false;
            SetAssistantSpeakingState(false);
            UpdateIdleState();
        }

        private async Task RunChatCompletionAsync(string userInput, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            bool firstChunkReceived = false;

            try
            {
                var chatRequest = new OpenAIChatRequest
                {
                    Model = string.IsNullOrWhiteSpace(Config.LlmModel)
                        ? "gpt-4o-mini"
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

                        lock (_stateLock)
                        {
                            if (_responseBuffer.Length + chunk.Length <= MaxResponseBufferSize)
                            {
                                _responseBuffer.Append(chunk);
                            }
                        }

                        ProcessStreamingChunk(chunk);
                        OnChatStateChanged?.Invoke(ChatState.AssistantResponseStreaming, chunk);
                    }
                }

                FlushPendingSentences();

                string finalResponse = GetCleanedResponse();
                OnChatStateChanged?.Invoke(ChatState.AssistantResponseFinish, finalResponse);
                ExtractAndNotifyWebLinks(GetRawResponse());
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
                OnChatStateChanged?.Invoke(ChatState.Failed, "Request timeout");
            }
            catch (Exception ex)
            {
                _failedRequestCount++;
                Debug.LogError($"[AIChat] Chat completion failed: {ex.Message}");
                OnChatStateChanged?.Invoke(ChatState.Failed, ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                _llmInFlight = false;
                UpdateIdleState();

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
            var messages = new List<OpenAIChatMessage>();

            if (!string.IsNullOrWhiteSpace(Config.SystemPrompt))
            {
                messages.Add(new OpenAIChatMessage("system", Config.SystemPrompt.Trim()));
            }

            messages.Add(new OpenAIChatMessage("user", transcript));
            return messages;
        }

        private void ProcessStreamingChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            var readySentences = _sentenceAssembler.Append(chunk, forceFlush: false);
            foreach (string sentence in readySentences)
            {
                DispatchAssistantSentence(sentence);
            }
        }

        private void FlushPendingSentences()
        {
            var trailing = _sentenceAssembler.Append(string.Empty, forceFlush: true);
            foreach (string sentence in trailing)
            {
                DispatchAssistantSentence(sentence);
            }
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
