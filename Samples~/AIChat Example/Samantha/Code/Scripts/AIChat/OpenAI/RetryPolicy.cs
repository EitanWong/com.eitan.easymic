using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Retry policy with exponential backoff and full jitter.
    /// Retries on: 429 (rate limit), 5xx (server errors), network failures.
    /// Does NOT retry on: 4xx (except 429), auth errors, cancellation.
    /// 
    /// Full jitter formula: delay = random(0, min(maxDelay, baseDelay * 2^attempt))
    /// Reference: AWS Builder's Library - Timeouts, retries and backoff with jitter
    /// </summary>
    internal sealed class ApiRetryPolicy
    {
        private const int DefaultMaxRetries = 2;
        private const int DefaultBaseDelayMs = 500;
        private const int DefaultMaxDelayMs = 4000;
        private static readonly System.Random _rng = new System.Random();

        private readonly int _maxRetries;
        private readonly double _baseDelayMs;
        private readonly double _maxDelayMs;

        public int MaxRetries => _maxRetries;

        public ApiRetryPolicy(int maxRetries = DefaultMaxRetries,
                              int baseDelayMs = DefaultBaseDelayMs,
                              int maxDelayMs = DefaultMaxDelayMs)
        {
            _maxRetries = Math.Max(0, maxRetries);
            _baseDelayMs = Math.Max(100, baseDelayMs);
            _maxDelayMs = Math.Max(_baseDelayMs, maxDelayMs);
        }

        /// <summary>
        /// Returns true if the HTTP status code is retryable.
        /// Retryable: 429 (rate limit), 5xx (server error).
        /// </summary>
        public bool IsRetryable(HttpStatusCode statusCode)
        {
            int code = (int)statusCode;
            return code == 429 || (code >= 500 && code < 600);
        }

        /// <summary>
        /// Returns true if the exception is retryable (network failure, timeout, or OpenAIApiException with retryable code).
        /// </summary>
        public bool IsRetryable(Exception ex)
        {
            // Cancellation — never retry
            if (ex is OperationCanceledException)
                return false;

            // Network/HTTP failure — retry
            if (ex is HttpRequestException || ex is System.IO.IOException)
                return true;

            // Timeout — retry
            if (ex is TaskCanceledException)
                return true;

            // OpenAIApiException with HTTP status — check status code
            if (ex is OpenAIApiException apiEx && TryExtractStatusCode(apiEx.Message, out var code))
                return IsRetryable(code);

            // Unknown — do not retry
            return false;
        }

        /// <summary>
        /// Compute delay with full jitter for the given attempt (0-based).
        /// delay = random(0, min(maxDelayMs, baseDelayMs * 2^attempt))
        /// </summary>
        public TimeSpan GetDelay(int attempt)
        {
            double exponential = Math.Min(_maxDelayMs, _baseDelayMs * Math.Pow(2, attempt));
            double jitter = _rng.NextDouble() * exponential;
            return TimeSpan.FromMilliseconds(jitter);
        }

        /// <summary>
        /// Extract HTTP status code from an OpenAIApiException message like "HTTP 500: ...".
        /// Returns false if no code found.
        /// </summary>
        private static bool TryExtractStatusCode(string message, out HttpStatusCode code)
        {
            code = default;
            if (string.IsNullOrEmpty(message)) return false;

            // Expected format: "HTTP 500: ..."
            if (message.Length > 8 && message.StartsWith("HTTP ", StringComparison.Ordinal))
            {
                int end = message.IndexOf(':', 5);
                if (end > 5 && int.TryParse(message.Substring(5, end - 5), out int statusCode))
                {
                    code = (HttpStatusCode)statusCode;
                    return true;
                }
            }
            return false;
        }
    }
}
