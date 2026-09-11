#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Collections.Generic;
using System.Text;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class AIChatRequestOrchestrator
    {
        private readonly object _sync = new object();
        private readonly StringBuilder _responseBuffer = new StringBuilder(1024);
        private readonly List<OpenAIChatMessage> _conversationHistory = new List<OpenAIChatMessage>();
        private readonly StreamingSentenceAssembler _sentenceAssembler = new StreamingSentenceAssembler();
        private readonly Func<int> _historyTurnProvider;
        private readonly Func<string> _systemPromptProvider;
        private readonly Func<string, string> _cleanText;
        private readonly int _maxResponseBufferSize;

        private StreamChunkMode _streamChunkMode;
        private string _lastStreamChunk = string.Empty;
        private long _activeResponseId;

        private enum StreamChunkMode
        {
            Unknown,
            Delta,
            Cumulative
        }

        public AIChatRequestOrchestrator(
            Func<int> historyTurnProvider,
            Func<string> systemPromptProvider,
            Func<string, string> cleanText,
            int maxResponseBufferSize)
        {
            _historyTurnProvider = historyTurnProvider ?? throw new ArgumentNullException(nameof(historyTurnProvider));
            _systemPromptProvider = systemPromptProvider ?? throw new ArgumentNullException(nameof(systemPromptProvider));
            _cleanText = cleanText ?? throw new ArgumentNullException(nameof(cleanText));
            _maxResponseBufferSize = Math.Max(1024, maxResponseBufferSize);
        }

        public bool HasConversationHistory
        {
            get
            {
                lock (_sync)
                {
                    return _conversationHistory.Count > 0;
                }
            }
        }

        public bool BeginResponse(long responseId, bool preferShortPhrases = false)
        {
            lock (_sync)
            {
                if (responseId < _activeResponseId)
                {
                    return false;
                }

                _activeResponseId = responseId;
                ResetCurrentResponseLocked();
                _sentenceAssembler.PreferShortPhrases = preferShortPhrases;
                return true;
            }
        }

        public string AppendStreamingChunk(long responseId, string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return string.Empty;
            }

            lock (_sync)
            {
                if (responseId != _activeResponseId)
                {
                    return string.Empty;
                }

                string normalizedChunk = NormalizeStreamingChunkLocked(chunk);
                if (!string.IsNullOrEmpty(normalizedChunk) &&
                    _responseBuffer.Length + normalizedChunk.Length <= _maxResponseBufferSize)
                {
                    _responseBuffer.Append(normalizedChunk);
                }

                return normalizedChunk;
            }
        }

        public void ProcessStreamingChunk(long responseId, string chunk, Action<string> onSentenceReady)
        {
            lock (_sync)
            {
                if (responseId != _activeResponseId)
                {
                    return;
                }

                DispatchSentencesLocked(chunk, forceFlush: false, onSentenceReady);
            }
        }

        public void FlushPendingSentences(long responseId, Action<string> onSentenceReady)
        {
            lock (_sync)
            {
                if (responseId != _activeResponseId)
                {
                    return;
                }

                DispatchSentencesLocked(string.Empty, forceFlush: true, onSentenceReady);
            }
        }

        public string GetRawResponse(long responseId)
        {
            lock (_sync)
            {
                if (responseId != _activeResponseId)
                {
                    return string.Empty;
                }

                return _responseBuffer.ToString();
            }
        }

        public string GetCleanedResponse(long responseId)
        {
            return _cleanText(GetRawResponse(responseId));
        }

        public void AppendConversationHistory(string userMessage, string assistantMessage)
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

            lock (_sync)
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

        public List<OpenAIChatMessage> BuildMessages(string transcript)
        {
            var messages = new List<OpenAIChatMessage>();
            string systemPrompt = _systemPromptProvider();
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
            int turns = Math.Max(0, _historyTurnProvider());
            return turns * 2;
        }

        private List<OpenAIChatMessage> GetConversationHistorySnapshot(int maxMessages)
        {
            if (maxMessages <= 0)
            {
                return null;
            }

            lock (_sync)
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

        private void DispatchSentencesLocked(string chunk, bool forceFlush, Action<string> onSentenceReady)
        {
            if (onSentenceReady == null)
            {
                return;
            }

            List<string> ready = null;

            foreach (string sentence in _sentenceAssembler.Append(chunk, forceFlush))
            {
                if (ready == null)
                {
                    ready = new List<string>();
                }

                ready.Add(sentence);
            }

            if (ready == null)
            {
                return;
            }

            for (int i = 0; i < ready.Count; i++)
            {
                onSentenceReady(ready[i]);
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

        private string NormalizeStreamingChunkLocked(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(_lastStreamChunk))
            {
                _lastStreamChunk = chunk;
                return chunk;
            }

            if (_streamChunkMode == StreamChunkMode.Unknown)
            {
                _streamChunkMode = chunk.Length > _lastStreamChunk.Length &&
                                   chunk.StartsWith(_lastStreamChunk, StringComparison.Ordinal)
                    ? StreamChunkMode.Cumulative
                    : StreamChunkMode.Delta;
            }

            if (_streamChunkMode == StreamChunkMode.Cumulative)
            {
                if (chunk.StartsWith(_lastStreamChunk, StringComparison.Ordinal))
                {
                    string delta = chunk.Substring(_lastStreamChunk.Length);
                    _lastStreamChunk = chunk;
                    return delta;
                }

                _streamChunkMode = StreamChunkMode.Delta;
            }

            if (string.Equals(_lastStreamChunk, chunk, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            _lastStreamChunk = chunk;
            return chunk;
        }

        private void ResetCurrentResponseLocked()
        {
            _responseBuffer.Clear();
            _streamChunkMode = StreamChunkMode.Unknown;
            _lastStreamChunk = string.Empty;
            _sentenceAssembler.Reset();
        }

        private static string NormalizeHistoryContent(string content)
        {
            return string.IsNullOrWhiteSpace(content) ? string.Empty : content.Trim();
        }
    }
}
#endif
