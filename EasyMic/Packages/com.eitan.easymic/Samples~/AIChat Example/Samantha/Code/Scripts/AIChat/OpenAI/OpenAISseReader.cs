using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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

        public async IAsyncEnumerable<string> ReadDataPayloadLinesAsync(
            StreamReader reader,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (reader == null)
            {
                yield break;
            }

            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string line = await ReadLineWithTimeoutAsync(reader, cancellationToken).ConfigureAwait(false);
                if (line == null)
                {
                    yield break;
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

                yield return payloadLine;
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
