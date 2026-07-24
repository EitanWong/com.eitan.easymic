using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class OpenAISseReader
    {
        private readonly TimeSpan _idleTimeout;

        public OpenAISseReader(TimeSpan idleTimeout)
        {
            _idleTimeout = idleTimeout;
        }

        public IAsyncEnumerable<string> ReadDataPayloadLinesAsync(
            StreamReader reader,
            CancellationToken cancellationToken)
        {
            return new DataPayloadEnumerable(this, reader, cancellationToken);
        }

        private sealed class DataPayloadEnumerable : IAsyncEnumerable<string>
        {
            private readonly OpenAISseReader _owner;
            private readonly StreamReader _reader;
            private readonly CancellationToken _requestCancellationToken;

            public DataPayloadEnumerable(
                OpenAISseReader owner,
                StreamReader reader,
                CancellationToken requestCancellationToken)
            {
                _owner = owner;
                _reader = reader;
                _requestCancellationToken = requestCancellationToken;
            }

            public IAsyncEnumerator<string> GetAsyncEnumerator(
                CancellationToken cancellationToken = default)
            {
                return new DataPayloadEnumerator(
                    _owner,
                    _reader,
                    _requestCancellationToken,
                    cancellationToken);
            }
        }

        private sealed class DataPayloadEnumerator : IAsyncEnumerator<string>
        {
            private readonly OpenAISseReader _owner;
            private readonly StreamReader _reader;
            private readonly CancellationToken _cancellationToken;
            private CancellationTokenSource _linkedCancellation;
            private bool _disposed;

            public DataPayloadEnumerator(
                OpenAISseReader owner,
                StreamReader reader,
                CancellationToken requestCancellationToken,
                CancellationToken enumerationCancellationToken)
            {
                _owner = owner;
                _reader = reader;

                if (requestCancellationToken.CanBeCanceled &&
                    enumerationCancellationToken.CanBeCanceled &&
                    requestCancellationToken != enumerationCancellationToken)
                {
                    _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        requestCancellationToken,
                        enumerationCancellationToken);
                    _cancellationToken = _linkedCancellation.Token;
                }
                else
                {
                    _cancellationToken = enumerationCancellationToken.CanBeCanceled
                        ? enumerationCancellationToken
                        : requestCancellationToken;
                }
            }

            public string Current { get; private set; } = string.Empty;

            public ValueTask<bool> MoveNextAsync()
            {
                return _disposed
                    ? new ValueTask<bool>(false)
                    : new ValueTask<bool>(MoveNextCoreAsync());
            }

            public ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    return default;
                }

                _disposed = true;
                _linkedCancellation?.Cancel();
                _linkedCancellation?.Dispose();
                _linkedCancellation = null;
                Current = string.Empty;
                return default;
            }

            private async Task<bool> MoveNextCoreAsync()
            {
                if (_reader == null)
                {
                    return false;
                }

                while (!_disposed)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

                    string line = await _owner
                        .ReadLineWithTimeoutAsync(_reader, _cancellationToken)
                        .ConfigureAwait(false);
                    if (line == null)
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(line) ||
                        !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string payloadLine = line.Substring(5).Trim();
                    if (string.IsNullOrWhiteSpace(payloadLine))
                    {
                        continue;
                    }

                    Current = payloadLine;
                    return true;
                }

                return false;
            }
        }

        private async Task<string> ReadLineWithTimeoutAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var readTask = reader.ReadLineAsync();
            var timeoutTask = Task.Delay(_idleTimeout, timeoutCts.Token);
            var completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

            // Cancel the timeout task immediately — prevents Timer leak on every SSE line
            timeoutCts.Cancel();

            if (completed == timeoutTask)
            {
                // Distinguish between caller cancellation and actual idle timeout
                cancellationToken.ThrowIfCancellationRequested();
                // readTask will be abandoned — the caller is responsible for disposing the reader
                throw new TimeoutException("Stream idle timeout.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return await readTask.ConfigureAwait(false);
        }
    }
}
