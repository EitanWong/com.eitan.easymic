#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
{
    /// <summary>
    /// Buffers recognition output, coordinates keyword gating, and exposes aggregated transcripts.
    /// </summary>
    public sealed class RecognitionBuffer
    {
        #region Fields

        private static readonly char[] SentenceTerminators = { '.', '!', '?', '。', '！', '？', '\r', '\n' };

        private readonly KeywordGate _keywordGate;
        private readonly Action<string> _onStreaming;
        private readonly SynchronizationContext _synchronizationContext;
        private readonly StringBuilder _builder;

        // Cached committed transcript to avoid repeated StringBuilder.ToString() allocations in hot paths.
        private string _committedSnapshot = string.Empty;

        private string _lastStreamingPartial = string.Empty;
        private string _lastEmittedStreaming = string.Empty;
        // Tracks the suffix that is currently being streamed on top of committed content.
        private string _lastStreamingSuffix = string.Empty;
        private string _lastSnapshot = string.Empty;

        #endregion

        #region Events

        public event Action<string> BufferAmended;
        public event Action<string> Finalized;

        #endregion

        #region Properties

        public string LastStreamingPartial => _lastStreamingPartial;
        public string CurrentTranscript => _committedSnapshot;

        #endregion

        #region Public API

        public RecognitionBuffer(
            KeywordGate keywordGate,
            Action<string> streamingCallback,
            int bufferCapacity = 512,
            SynchronizationContext synchronizationContext = null)
        {
            _keywordGate = keywordGate;
            _onStreaming = streamingCallback;
            _synchronizationContext = synchronizationContext ?? SynchronizationContext.Current;
            _builder = new StringBuilder(Math.Max(1, bufferCapacity));
        }

        public void EmitPartial(string partial)
        {
            string payload = partial ?? string.Empty;
            _lastStreamingPartial = NormalizePayload(payload);

            if (_keywordGate != null)
            {
                if (!_keywordGate.TryGetStreamingPayload(payload, out var gatedPayload))
                {
                    return;
                }

                PushStreaming(NormalizePayload(gatedPayload), treatAsCompositePayload: false);
                return;
            }

            PushStreaming(_lastStreamingPartial, treatAsCompositePayload: false);
        }

        public string Commit(string text)
        {
            if (_keywordGate != null && _keywordGate.RequiresKeyword && !_keywordGate.AllowsRecognition)
            {
                return CurrentTranscript;
            }

            string normalized = NormalizePayload(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return CurrentTranscript;
            }

            bool appendOnly = TryGetAppendDelta(normalized, out string delta);
            if (delta.Length == 0)
            {
                return CurrentTranscript;
            }

            if (!appendOnly)
            {
                // Replace the buffer when upstream edits earlier content (e.g., punctuation).
                _builder.Clear();
                _lastStreamingSuffix = string.Empty;
                _lastEmittedStreaming = string.Empty;
            }

            _builder.Append(delta);
            if (appendOnly)
            {
                ConsumeStreamingSuffix(delta);
            }
            _committedSnapshot = _builder.ToString();
            string current = _committedSnapshot;
            RaiseBufferAmended(current);

            if (_keywordGate != null)
            {
                if (_keywordGate.TryGetStreamingPayload(current, out var gatedPayload))
                {
                    PushStreaming(NormalizePayload(gatedPayload), treatAsCompositePayload: true);
                }
            }
            else
            {
                PushStreaming(current, treatAsCompositePayload: true);
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

            RaiseFinalized(final);
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
            _lastEmittedStreaming = string.Empty;
            _lastStreamingSuffix = string.Empty;
        }

        public void ResetConversation()
        {
            _builder.Clear();
            _committedSnapshot = string.Empty;
            _lastSnapshot = string.Empty;
        }

        #endregion

        #region Helpers

        private void PushStreaming(string content, bool treatAsCompositePayload)
        {
            string preview;
            string committedSnapshot = null;

            if (treatAsCompositePayload)
            {
                committedSnapshot = content ?? string.Empty;
                preview = ComposeCompositePreview(committedSnapshot);
            }
            else
            {
                preview = ComposeStreamingPreview(content, out committedSnapshot);
            }

            CaptureStreamingSuffix(preview, committedSnapshot);

            if (string.IsNullOrEmpty(preview))
            {
                return;
            }

            if (string.Equals(preview, _lastEmittedStreaming, StringComparison.Ordinal))
            {
                return;
            }

            _lastEmittedStreaming = preview;
            RaiseStreaming(preview);
        }

        private string ComposeStreamingPreview(string streamingPayload, out string committedSnapshot)
        {
            streamingPayload ??= string.Empty;
            committedSnapshot = _committedSnapshot ?? string.Empty;

            if (string.IsNullOrEmpty(committedSnapshot))
            {
                return streamingPayload;
            }

            if (string.IsNullOrEmpty(streamingPayload))
            {
                return committedSnapshot;
            }

            if (streamingPayload.Length <= committedSnapshot.Length &&
                committedSnapshot.IndexOf(streamingPayload, StringComparison.Ordinal) >= 0)
            {
                return committedSnapshot;
            }

            if (streamingPayload.Length >= committedSnapshot.Length &&
                streamingPayload.IndexOf(committedSnapshot, StringComparison.Ordinal) == 0)
            {
                return streamingPayload;
            }

            return MergeWithOverlap(committedSnapshot, streamingPayload);
        }

        private string MergeWithOverlap(string committedSnapshot, string streamingPayload)
        {
            int maxOverlap = Math.Min(committedSnapshot.Length, streamingPayload.Length);
            for (int overlap = maxOverlap; overlap > 0; overlap--)
            {
                if (HasOverlap(committedSnapshot, streamingPayload, overlap))
                {
                    return committedSnapshot + streamingPayload.Substring(overlap);
                }
            }

            char lastCommitted = committedSnapshot[committedSnapshot.Length - 1];
            char firstStreaming = streamingPayload[0];
            bool needsSeparator = IsWordChar(lastCommitted) && IsWordChar(firstStreaming);

            return needsSeparator ? string.Concat(committedSnapshot, ' ', streamingPayload) : committedSnapshot + streamingPayload;
        }

        private static bool HasOverlap(string committedSnapshot, string streamingPayload, int length)
        {
            return committedSnapshot.EndsWith(streamingPayload.Substring(0, length), StringComparison.Ordinal);
        }

        private string ComposeCompositePreview(string committedPayload)
        {
            string committed = committedPayload ?? string.Empty;

            if (string.IsNullOrEmpty(committed))
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(_lastStreamingSuffix))
            {
                return committed;
            }

            return committed + _lastStreamingSuffix;
        }

        private void CaptureStreamingSuffix(string preview, string committedSnapshot)
        {
            if (string.IsNullOrEmpty(preview))
            {
                _lastStreamingSuffix = string.Empty;
                return;
            }

            string committed = committedSnapshot ?? string.Empty;
            if (preview.Length <= committed.Length)
            {
                _lastStreamingSuffix = string.Empty;
                return;
            }

            _lastStreamingSuffix = preview.Substring(committed.Length);
        }

        private void ConsumeStreamingSuffix(string committedDelta)
        {
            if (string.IsNullOrEmpty(committedDelta) || string.IsNullOrEmpty(_lastStreamingSuffix))
            {
                return;
            }

            int prefixLength = GetCommonPrefixLength(_lastStreamingSuffix, committedDelta);
            if (prefixLength <= 0)
            {
                _lastStreamingSuffix = string.Empty;
                return;
            }

            if (prefixLength >= _lastStreamingSuffix.Length)
            {
                _lastStreamingSuffix = string.Empty;
                return;
            }

            _lastStreamingSuffix = _lastStreamingSuffix.Substring(prefixLength);
        }

        private bool TryGetAppendDelta(string text, out string delta)
        {
            if (string.IsNullOrEmpty(_lastSnapshot))
            {
                _lastSnapshot = text;
                delta = text;
                return true;
            }

            if (string.Equals(text, _lastSnapshot, StringComparison.Ordinal))
            {
                delta = string.Empty;
                return true;
            }

            if (text.StartsWith(_lastSnapshot, StringComparison.Ordinal))
            {
                delta = text.Substring(_lastSnapshot.Length);
                _lastSnapshot = text;
                return true;
            }

            _lastSnapshot = text;
            delta = text;
            return false;
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
            int index = 0;
            while (index < transcript.Length)
            {
                int hit = transcript.IndexOfAny(SentenceTerminators, index);
                if (hit < 0)
                {
                    break;
                }

                segments++;
                index = hit + 1;
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
            return Array.IndexOf(SentenceTerminators, ch) >= 0;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);

        private static string NormalizePayload(string value) => (value ?? string.Empty).Normalize(NormalizationForm.FormC);

        private void RaiseStreaming(string preview)
        {
            if (string.IsNullOrEmpty(preview))
            {
                return;
            }

            var callback = _onStreaming;
            if (callback == null)
            {
                return;
            }

            DispatchOnContext(() => callback(preview));
        }

        private void RaiseBufferAmended(string current)
        {
            var handler = BufferAmended;
            if (handler == null)
            {
                return;
            }

            DispatchOnContext(() => handler(current));
        }

        private void RaiseFinalized(string transcript)
        {
            var handler = Finalized;
            if (handler == null)
            {
                return;
            }

            DispatchOnContext(() => handler(transcript));
        }

        private void DispatchOnContext(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_synchronizationContext == null)
            {
                action();
                return;
            }

            _synchronizationContext.Post(static state => ((Action)state)(), action);
        }

        #endregion

        #region Testing Hooks

#if UNITY_INCLUDE_TESTS
        internal string DebugExtractDelta(string text)
        {
            TryGetAppendDelta(text, out string delta);
            return delta;
        }
        internal int DebugCountSegments(string transcript) => CountSegmentsStatic(transcript);
        internal bool DebugEndsWithTerminator(string transcript) => EndsWithTerminatorStatic(transcript);
#endif

        #endregion
    }
}
#endif
