using System;
using System.Collections.Generic;
using System.Text;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Intelligent sentence boundary detector.
    /// 
    /// Splitting rules (tiered by priority):
    ///   1. Strong end (. ! ? 。！？) — immediate split
    ///   2. Soft break (, ，: ：; ；— -) — split when buffer ≥ 100 chars
    ///   3. Safety backtrack — at 250 chars, scan back for last soft break or space
    ///   4. Absolute max — 500 chars, force split
    /// </summary>
    internal sealed class StreamingSentenceAssembler
    {
        private static readonly char[] SentenceEndMarkers = { '.', '!', '?', '。', '！', '？', '；', ';' };
        private static readonly char[] SoftBreakMarkers = { ',', '，', ':', '：', '—', '-' };
        private static readonly HashSet<string> Abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mr.", "Mrs.", "Ms.", "Dr.", "Prof.", "Jr.", "Sr.", "vs.", "etc.", "i.e.", "e.g.", "Inc.", "Ltd.", "Co."
        };

        private const int MinSentenceLength = 2;
        private const int MaxSentenceLength = 500;
        private const int SoftBreakMinimumLength = 100;
        private const int SafetySplitLength = 250;

        private readonly StringBuilder _buffer = new StringBuilder(256);
        private readonly List<string> _pendingSentences = new List<string>();
        private int _openParentheses;
        private int _openBrackets;
        private int _openBraces;
        private bool _inQuote;
        private bool _inCodeBlock;
        private int _codeBlockTicks;
        private int _lastSoftBreakIndex = -1;

        public int BufferLength => _buffer.Length;

        public IEnumerable<string> Append(string chunk, bool forceFlush = false)
        {
            _pendingSentences.Clear();

            if (!string.IsNullOrEmpty(chunk))
            {
                foreach (char c in chunk)
                {
                    ProcessCharacter(c);
                }
            }

            if (forceFlush)
            {
                FlushRemaining();
            }

            return _pendingSentences;
        }

        public void Reset()
        {
            _buffer.Clear();
            _pendingSentences.Clear();
            _openParentheses = 0;
            _openBrackets = 0;
            _openBraces = 0;
            _inQuote = false;
            _inCodeBlock = false;
            _codeBlockTicks = 0;
            _lastSoftBreakIndex = -1;
        }

        private void ProcessCharacter(char c)
        {
            if (c == '`')
            {
                _codeBlockTicks++;
                if (_codeBlockTicks == 3)
                {
                    _inCodeBlock = !_inCodeBlock;
                    _codeBlockTicks = 0;
                }
            }
            else
            {
                _codeBlockTicks = 0;
            }

            if (_inCodeBlock)
            {
                _buffer.Append(c);
                return;
            }

            // Track soft break positions BEFORE appending (c is the current char, buffer doesn't have it yet)
            if (IsSoftBreak(c))
            {
                _lastSoftBreakIndex = _buffer.Length; // index where soft break will be after Append
            }

            UpdateBalanceTracking(c);
            _buffer.Append(c);

            // Tier 1: Sentence-ending punctuation — immediate split
            if (IsSentenceEnd(c) && (IsBalanced() || _buffer.Length >= MaxSentenceLength) && !IsAbbreviation())
            {
                EmitSentence();
                return;
            }

            // Tier 2: Soft break with sufficient context
            if (IsSoftBreak(c) && IsBalanced() && _buffer.Length >= SoftBreakMinimumLength)
            {
                EmitSentence();
                return;
            }

            // Tier 3: Newline
            if (c == '\n' && _buffer.Length > MinSentenceLength)
            {
                string trimmed = _buffer.ToString().Trim();
                if (trimmed.Length > MinSentenceLength)
                {
                    EmitSentence();
                    return;
                }
            }

            // Tier 4: Safety backtrack at SafetySplitLength
            if (_buffer.Length >= SafetySplitLength && IsBalanced())
            {
                TrySafetySplit();
            }
        }

        /// <summary>
        /// When buffer exceeds SafetySplitLength without a sentence-ending punctuation,
        /// scan backwards for the last safe break point (soft break, then space) and split there.
        /// </summary>
        private void TrySafetySplit()
        {
            // Prefer soft break (comma, colon, dash, etc.)
            if (_lastSoftBreakIndex > MinSentenceLength)
            {
                EmitSentenceUpTo(_lastSoftBreakIndex + 1);
                return;
            }

            // Fallback 1: last space (word boundary)
            for (int i = _buffer.Length - 1; i >= 0; i--)
            {
                if (_buffer[i] == ' ')
                {
                    int wordLen = _buffer.Length - i - 1;
                    if (wordLen <= 0) continue;
                    // Emit up to and including the space, keep the rest
                    EmitSentenceUpTo(i + 1);
                    return;
                }
            }

            // Fallback 2: no safe point found, let MaxSentenceLength be the ultimate fallback
            // Do nothing — the buffer will keep growing and hit EmitSentence at MaxSentenceLength
        }

        /// <summary>
        /// Emit buffer[0..endIndex) as a complete sentence, keep buffer[endIndex..) for next cycle.
        /// Then recalculate balance tracking state from the remaining buffer content.
        /// </summary>
        private void EmitSentenceUpTo(int endIndex)
        {
            if (endIndex <= 0 || endIndex > _buffer.Length) return;

            string sentence = _buffer.ToString(0, endIndex).Trim();
            int remaining = _buffer.Length - endIndex;

            if (remaining > 0)
            {
                string tail = _buffer.ToString(endIndex, remaining);
                _buffer.Clear();
                _buffer.Append(tail);
            }
            else
            {
                _buffer.Clear();
            }

            if (sentence.Length >= MinSentenceLength)
            {
                _pendingSentences.Add(sentence);
            }

            RecalculateRemainingState();
        }

        /// <summary>
        /// Recalculate balance tracking and soft break index from the remaining buffer.
        /// Called after EmitSentenceUpTo removes the front portion of the buffer.
        /// </summary>
        private void RecalculateRemainingState()
        {
            _openParentheses = 0;
            _openBrackets = 0;
            _openBraces = 0;
            _inQuote = false;
            _lastSoftBreakIndex = -1;

            for (int i = 0; i < _buffer.Length; i++)
            {
                char c = _buffer[i];
                switch (c)
                {
                    case '(': _openParentheses++; break;
                    case ')': _openParentheses = Math.Max(0, _openParentheses - 1); break;
                    case '[': _openBrackets++; break;
                    case ']': _openBrackets = Math.Max(0, _openBrackets - 1); break;
                    case '{': _openBraces++; break;
                    case '}': _openBraces = Math.Max(0, _openBraces - 1); break;
                    case '"': _inQuote = !_inQuote; break;
                }

                if (IsSoftBreak(c))
                {
                    _lastSoftBreakIndex = i;
                }
            }
        }

        private void UpdateBalanceTracking(char c)
        {
            switch (c)
            {
                case '(': _openParentheses++; break;
                case ')': _openParentheses = Math.Max(0, _openParentheses - 1); break;
                case '[': _openBrackets++; break;
                case ']': _openBrackets = Math.Max(0, _openBrackets - 1); break;
                case '{': _openBraces++; break;
                case '}': _openBraces = Math.Max(0, _openBraces - 1); break;
                case '"': _inQuote = !_inQuote; break;
            }
        }

        private bool IsSentenceEnd(char c)
        {
            foreach (var marker in SentenceEndMarkers)
                if (c == marker) return true;
            return false;
        }

        private bool IsSoftBreak(char c)
        {
            foreach (var marker in SoftBreakMarkers)
                if (c == marker) return true;
            return false;
        }

        private bool IsBalanced()
        {
            return _openParentheses == 0 &&
                   _openBrackets == 0 &&
                   _openBraces == 0 &&
                   !_inQuote &&
                   !_inCodeBlock;
        }

        private bool IsAbbreviation()
        {
            if (_buffer.Length < 2) return false;

            for (int i = _buffer.Length - 1; i >= 0; i--)
            {
                if (_buffer[i] == ' ')
                {
                    int wordLen = _buffer.Length - i - 1;
                    if (wordLen <= 0) return false;
                    string lastWord = _buffer.ToString(i + 1, wordLen).Trim();
                    return Abbreviations.Contains(lastWord);
                }
            }

            string whole = _buffer.ToString().Trim();
            return !string.IsNullOrEmpty(whole) && Abbreviations.Contains(whole);
        }

        private void EmitSentence()
        {
            if (_buffer.Length == 0) return;

            string sentence = _buffer.ToString().Trim();
            _buffer.Clear();

            _openParentheses = 0;
            _openBrackets = 0;
            _openBraces = 0;
            _inQuote = false;
            _lastSoftBreakIndex = -1;

            if (sentence.Length >= MinSentenceLength)
            {
                _pendingSentences.Add(sentence);
            }
        }

        private void FlushRemaining()
        {
            if (_buffer.Length > 0)
            {
                string remaining = _buffer.ToString().Trim();
                if (remaining.Length >= MinSentenceLength)
                {
                    _pendingSentences.Add(remaining);
                }
                _buffer.Clear();
            }

            _openParentheses = 0;
            _openBrackets = 0;
            _openBraces = 0;
            _inQuote = false;
            _lastSoftBreakIndex = -1;
        }
    }
}
