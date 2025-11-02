#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Text;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Aggregates recognition output, producing streaming deltas and final transcripts.
    /// </summary>
    public sealed class TextAccumulator
    {
        private readonly KeywordGate _keywordGate;
        private readonly Action<string> _onStreaming;
        private readonly StringBuilder _builder;
        private string _lastStreamingPartial = string.Empty;
        private string _lastEmittedStreaming = string.Empty;
        private string _lastCommittedSnapshot = string.Empty;
        private string _lastSubmitted = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextAccumulator"/> class.
        /// </summary>
        public TextAccumulator(KeywordGate keywordGate, Action<string> onStreaming, int bufferCapacity = 512)
        {
            _keywordGate = keywordGate;
            _onStreaming = onStreaming;
            _builder = new StringBuilder(Math.Max(1, bufferCapacity));
        }

        /// <summary>
        /// Raised when the internal buffer changes while accumulating a final transcript.
        /// </summary>
        public event Action<string> BufferAmended;

        /// <summary>
        /// Raised when a final transcript is ready for submission.
        /// </summary>
        public event Action<string> Finalized;

        /// <summary>
        /// Emits streaming content after passing through the keyword gate.
        /// </summary>
        public void EmitPartial(string newPartial)
        {
            string partial = newPartial ?? string.Empty;
            _lastStreamingPartial = partial;

            if (_keywordGate != null)
            {
                if (!_keywordGate.TryGetStreamingPayload(partial, out var payload))
                {
                    return;
                }

                EmitStreaming(payload);
                return;
            }

            EmitStreaming(partial);
        }

        /// <summary>
        /// Commits the supplied text to the accumulator and raises submission events.
        /// </summary>
        public void CommitFinal(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string delta = ExtractDelta(text);
            if (delta.Length == 0)
            {
                return;
            }

            _builder.Append(delta);
            var current = _builder.ToString();
            BufferAmended?.Invoke(current);

            string trimmed = current.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            if (string.Equals(trimmed, _lastSubmitted, StringComparison.Ordinal))
            {
                return;
            }

            _lastSubmitted = trimmed;
            Finalized?.Invoke(trimmed);
        }

        /// <summary>
        /// Resets streaming state and clears all buffers.
        /// </summary>
        public void Reset()
        {
            CompleteConversation();
            _lastStreamingPartial = string.Empty;
            _lastEmittedStreaming = string.Empty;
        }

        /// <summary>
        /// Clears streaming history without touching the accumulated buffer.
        /// </summary>
        public void ResetStreaming()
        {
            _lastStreamingPartial = string.Empty;
            _lastEmittedStreaming = string.Empty;
            _lastCommittedSnapshot = string.Empty;
        }

        /// <summary>
        /// Clears the accumulated buffer and resets submission tracking.
        /// </summary>
        public void CompleteConversation()
        {
            _builder.Clear();
            _lastCommittedSnapshot = string.Empty;
            _lastSubmitted = string.Empty;
        }

        private void EmitStreaming(string content)
        {
            content ??= string.Empty;
            if (string.Equals(content, _lastEmittedStreaming, StringComparison.Ordinal))
            {
                return;
            }

            _lastEmittedStreaming = content;
            _onStreaming?.Invoke(content);
        }

        private string ExtractDelta(string text)
        {
            if (string.IsNullOrEmpty(_lastCommittedSnapshot))
            {
                _lastCommittedSnapshot = text;
                return text;
            }

            if (string.Equals(text, _lastCommittedSnapshot, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            int prefixLength = GetCommonPrefixLength(_lastCommittedSnapshot, text);
            _lastCommittedSnapshot = text;

            if (prefixLength <= 0)
            {
                return text;
            }

            if (prefixLength >= text.Length)
            {
                return string.Empty;
            }

            return text.Substring(prefixLength);
        }

        private static int GetCommonPrefixLength(string a, string b)
        {
            int length = Math.Min(a.Length, b.Length);
            int index = 0;
            while (index < length && a[index] == b[index])
            {
                index++;
            }

            return index;
        }

#if UNITY_INCLUDE_TESTS
        internal string DebugExtractDelta(string text)
        {
            return ExtractDelta(text);
        }
#endif
    }
}
#endif
