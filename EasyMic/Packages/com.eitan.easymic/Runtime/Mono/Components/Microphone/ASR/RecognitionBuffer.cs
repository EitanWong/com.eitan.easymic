#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Text;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Buffers recognition output, coordinates keyword gating, and exposes aggregated transcripts.
    /// </summary>
    public sealed class RecognitionBuffer
    {
        #region Fields

        private static readonly char[] SentenceTerminators = { '.', '!', '?', '。', '！', '？', '\n' };

        private readonly KeywordGate _keywordGate;
        private readonly Action<string> _onStreaming;
        private readonly StringBuilder _builder;

        private string _lastStreamingPartial = string.Empty;
        private string _lastEmittedStreaming = string.Empty;
        private string _lastSnapshot = string.Empty;

        #endregion

        #region Events

        public event Action<string> BufferAmended;
        public event Action<string> Finalized;

        #endregion

        #region Properties

        public string LastStreamingPartial => _lastStreamingPartial;
        public string CurrentTranscript => _builder.ToString().Trim();

        #endregion

        #region Public API

        public RecognitionBuffer(
            KeywordGate keywordGate,
            Action<string> streamingCallback,
            int bufferCapacity = 512)
        {
            _keywordGate = keywordGate;
            _onStreaming = streamingCallback;
            _builder = new StringBuilder(Math.Max(1, bufferCapacity));
        }

        public void EmitPartial(string partial)
        {
            string payload = partial ?? string.Empty;
            _lastStreamingPartial = payload;

            if (_keywordGate != null)
            {
                if (!_keywordGate.TryGetStreamingPayload(payload, out var gatedPayload))
                {
                    return;
                }

                PushStreaming(gatedPayload);
                return;
            }

            PushStreaming(payload);
        }

        public string Commit(string text)
        {
            if (_keywordGate != null && _keywordGate.RequiresKeyword && !_keywordGate.AllowsRecognition)
            {
                return CurrentTranscript;
            }

            string normalized = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return CurrentTranscript;
            }

            string delta = ExtractDelta(normalized);
            if (delta.Length == 0)
            {
                return CurrentTranscript;
            }

            _builder.Append(delta);
            string current = _builder.ToString();
            BufferAmended?.Invoke(current);

            if (_keywordGate != null)
            {
                if (_keywordGate.TryGetStreamingPayload(current, out var gatedPayload))
                {
                    PushStreaming(gatedPayload);
                }
            }
            else
            {
                PushStreaming(current);
            }

            return current;
        }

        public void FinalPush()
        {
            string final = CurrentTranscript;
            if (string.IsNullOrEmpty(final))
            {
                return;
            }

            Finalized?.Invoke(final);
            ResetConversation();
            ResetStreaming();
        }

        public void Reset()
        {
            ResetStreaming();
            ResetConversation();
        }

        public void ResetStreaming()
        {
            _lastStreamingPartial = string.Empty;
            PushStreaming(string.Empty);
        }

        public void ResetConversation()
        {
            _builder.Clear();
            _lastSnapshot = string.Empty;
        }

        #endregion

        #region Helpers

        private void PushStreaming(string content)
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
            if (string.IsNullOrEmpty(_lastSnapshot))
            {
                _lastSnapshot = text;
                return text;
            }

            if (string.Equals(text, _lastSnapshot, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            int prefixLength = GetCommonPrefixLength(_lastSnapshot, text);
            _lastSnapshot = text;

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

        private static int GetCommonPrefixLength(string previous, string current)
        {
            int length = Math.Min(previous.Length, current.Length);
            int index = 0;
            while (index < length && previous[index] == current[index])
            {
                index++;
            }

            return index;
        }

        internal static int CountSegmentsStatic(string transcript)
        {
            if (string.IsNullOrEmpty(transcript))
            {
                return 0;
            }

            int segments = 0;
            foreach (char ch in transcript)
            {
                if (IsSentenceTerminator(ch))
                {
                    segments++;
                }
            }

            return Mathf.Max(segments, 1);
        }

        internal static bool EndsWithTerminatorStatic(string transcript)
        {
            if (string.IsNullOrEmpty(transcript))
            {
                return false;
            }

            return IsSentenceTerminator(transcript[transcript.Length - 1]);
        }

        private static bool IsSentenceTerminator(char ch)
        {
            for (int i = 0; i < SentenceTerminators.Length; i++)
            {
                if (SentenceTerminators[i] == ch)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Testing Hooks

#if UNITY_INCLUDE_TESTS
        internal string DebugExtractDelta(string text) => ExtractDelta(text);
        internal int DebugCountSegments(string transcript) => CountSegmentsStatic(transcript);
        internal bool DebugEndsWithTerminator(string transcript) => EndsWithTerminatorStatic(transcript);
#endif

        #endregion
    }
}
#endif
