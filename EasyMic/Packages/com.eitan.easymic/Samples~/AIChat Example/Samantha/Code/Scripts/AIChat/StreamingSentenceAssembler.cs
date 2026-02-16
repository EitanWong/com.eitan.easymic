using System;
using System.Collections.Generic;
using System.Text;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// High-performance sentence assembler with intelligent boundary detection.
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
        private const int SoftBreakThreshold = 100;

        private readonly StringBuilder _buffer = new StringBuilder(256);
        private readonly List<string> _pendingSentences = new List<string>();
        private int _openParentheses;
        private int _openBrackets;
        private int _openBraces;
        private bool _inQuote;
        private bool _inCodeBlock;
        private int _codeBlockTicks;

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

            UpdateBalanceTracking(c);
            _buffer.Append(c);

            if (IsSentenceEnd(c) && IsBalanced() && !IsAbbreviation())
            {
                EmitSentence();
            }
            else if (_buffer.Length > SoftBreakThreshold && IsSoftBreak(c) && IsBalanced())
            {
                if (_buffer.Length > MaxSentenceLength / 2)
                {
                    EmitSentence();
                }
            }
            else if (c == '\n' && _buffer.Length > MinSentenceLength)
            {
                string trimmed = _buffer.ToString().Trim();
                if (trimmed.Length > MinSentenceLength)
                {
                    EmitSentence();
                }
            }
        }

        private void UpdateBalanceTracking(char c)
        {
            switch (c)
            {
                case '(':
                    _openParentheses++;
                    break;
                case ')':
                    _openParentheses = Math.Max(0, _openParentheses - 1);
                    break;
                case '[':
                    _openBrackets++;
                    break;
                case ']':
                    _openBrackets = Math.Max(0, _openBrackets - 1);
                    break;
                case '{':
                    _openBraces++;
                    break;
                case '}':
                    _openBraces = Math.Max(0, _openBraces - 1);
                    break;
                case '"':
                    _inQuote = !_inQuote;
                    break;
            }
        }

        private bool IsSentenceEnd(char c)
        {
            foreach (var marker in SentenceEndMarkers)
            {
                if (c == marker)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsSoftBreak(char c)
        {
            foreach (var marker in SoftBreakMarkers)
            {
                if (c == marker)
                {
                    return true;
                }
            }
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
            if (_buffer.Length < 2)
            {
                return false;
            }

            string content = _buffer.ToString();
            int spaceIndex = content.LastIndexOf(' ');
            if (spaceIndex < 0)
            {
                spaceIndex = 0;
            }

            string lastWord = content.Substring(spaceIndex).Trim();
            return Abbreviations.Contains(lastWord);
        }

        private void EmitSentence()
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            string sentence = _buffer.ToString().Trim();
            _buffer.Clear();

            _openParentheses = 0;
            _openBrackets = 0;
            _openBraces = 0;
            _inQuote = false;

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
        }
    }
}
