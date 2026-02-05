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

            await CancelActiveResponseAsync().ConfigureAwait(false);

            lock (_stateLock)
            {
                _responseBuffer.Clear();
                _streamedResponseSnapshot = string.Empty;
            }
            _sentenceAssembler.Reset();

            _llmInFlight = true;
            _totalRequestCount++;
            UpdateIdleState();

            _responseCts = new CancellationTokenSource();
            var token = _responseCts.Token;

            if (!_conversationStarted)
            {
                _conversationStarted = true;
                _pluginHost?.NotifyConversationStarted(isProactive);
            }

            if (recordUserMessage)
            {
                _pluginHost?.NotifyUserMessageSubmitted(userInput, isProactive);
            }

            _pluginHost?.NotifyAssistantRequestStarted(userInput, isProactive);

            _ = RunChatCompletionAsync(userInput, token, recordUserMessage, isProactive);
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

                        string normalizedChunk;
                        lock (_stateLock)
                        {
                            normalizedChunk = NormalizeStreamingChunkLocked(chunk);
                            if (!string.IsNullOrEmpty(normalizedChunk) &&
                                _responseBuffer.Length + normalizedChunk.Length <= MaxResponseBufferSize)
                            {
                                _responseBuffer.Append(normalizedChunk);
                            }
                        }

                        if (!string.IsNullOrEmpty(normalizedChunk))
                        {
                            ProcessStreamingChunk(normalizedChunk);
                            NotifyChatStateChanged(ChatState.AssistantResponseStreaming, normalizedChunk);
                        }
                    }
                }

                FlushPendingSentences();

                finalResponse = GetCleanedResponse();
                if (recordUserMessage)
                {
                    AppendConversationHistory(userInput, GetRawResponse());
                }
                else
                {
                    AppendConversationHistory(null, GetRawResponse());
                }

                MarkAssistantResponse();
                NotifyChatStateChanged(ChatState.AssistantResponseFinish, finalResponse);
                ExtractAndNotifyWebLinks(GetRawResponse());
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
            var messages = new List<OpenAIChatMessage>();

            string systemPrompt = GetSystemPrompt();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new OpenAIChatMessage("system", systemPrompt.Trim()));
            }

            int maxHistoryMessages = GetHistoryMessageLimit();
            if (maxHistoryMessages > 0)
            {
                var history = GetConversationHistorySnapshot(maxHistoryMessages);
                if (history != null && history.Count > 0)
                {
                    messages.AddRange(history);
                }
            }

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                messages.Add(new OpenAIChatMessage("user", transcript));
            }

            return messages;
        }

        private int GetHistoryMessageLimit()
        {
            int turns = Math.Max(0, Config.MaxHistoryTurns);
            return turns * 2;
        }

        private List<OpenAIChatMessage> GetConversationHistorySnapshot(int maxMessages)
        {
            if (maxMessages <= 0)
            {
                return null;
            }

            lock (_stateLock)
            {
                if (_conversationHistory.Count == 0)
                {
                    return null;
                }

                int startIndex = Math.Max(0, _conversationHistory.Count - maxMessages);
                var snapshot = new List<OpenAIChatMessage>(_conversationHistory.Count - startIndex);

                for (int i = startIndex; i < _conversationHistory.Count; i++)
                {
                    var message = _conversationHistory[i];
                    if (message == null || string.IsNullOrWhiteSpace(message.Content))
                    {
                        continue;
                    }

                    snapshot.Add(new OpenAIChatMessage(message.Role, message.Content));
                }

                return snapshot;
            }
        }

        private void AppendConversationHistory(string userMessage, string assistantMessage)
        {
            int maxMessages = GetHistoryMessageLimit();
            if (maxMessages <= 0)
            {
                return;
            }

            string userContent = NormalizeHistoryContent(userMessage);
            string assistantContent = NormalizeHistoryContent(assistantMessage);

            if (string.IsNullOrEmpty(userContent) && string.IsNullOrEmpty(assistantContent))
            {
                return;
            }

            lock (_stateLock)
            {
                if (!string.IsNullOrEmpty(userContent))
                {
                    _conversationHistory.Add(OpenAIChatMessage.User(userContent));
                }

                if (!string.IsNullOrEmpty(assistantContent))
                {
                    _conversationHistory.Add(OpenAIChatMessage.Assistant(assistantContent));
                }

                TrimConversationHistoryLocked(maxMessages);
            }
        }

        private void TrimConversationHistoryLocked(int maxMessages)
        {
            if (maxMessages <= 0)
            {
                _conversationHistory.Clear();
                return;
            }

            int excess = _conversationHistory.Count - maxMessages;
            if (excess > 0)
            {
                _conversationHistory.RemoveRange(0, excess);
            }
        }

        private static string NormalizeHistoryContent(string content)
        {
            return string.IsNullOrWhiteSpace(content) ? string.Empty : content.Trim();
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
