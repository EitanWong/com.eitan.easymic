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

            var readTask = reader.ReadLineAsync();
            var timeoutTask = Task.Delay(_idleTimeout, CancellationToken.None);
            var completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

            // Check cancellation first — if cancelled, the read result may be stale/incorrect
            cancellationToken.ThrowIfCancellationRequested();

            if (completed == timeoutTask)
            {
                // Ensure the read task doesn't leak — it will be abandoned when the reader is disposed
                throw new TimeoutException("Stream idle timeout.");
            }

            return await readTask.ConfigureAwait(false);
        }
    }
}
