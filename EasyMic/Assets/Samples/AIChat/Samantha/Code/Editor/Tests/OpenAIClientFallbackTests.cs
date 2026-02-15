using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class OpenAIClientFallbackTests
    {
        [Test]
        public void FallbackPolicy_ShouldRespectForceAndProviderSupport()
        {
            var policy = new OpenAIFallbackPolicy();

            Assert.IsFalse(policy.ShouldUseChatCompletions(forceChatCompletions: false, providerSupportsResponsesApi: true));
            Assert.IsTrue(policy.ShouldUseChatCompletions(forceChatCompletions: true, providerSupportsResponsesApi: true));
            Assert.IsTrue(policy.ShouldUseChatCompletions(forceChatCompletions: false, providerSupportsResponsesApi: false));
        }

        [Test]
        public void FallbackPolicy_ShouldPersistAndResetUnsupportedState()
        {
            var policy = new OpenAIFallbackPolicy();

            policy.MarkResponsesApiUnsupported();
            Assert.IsTrue(policy.ShouldUseChatCompletions(forceChatCompletions: false, providerSupportsResponsesApi: true));

            policy.Reset();
            Assert.IsFalse(policy.ShouldUseChatCompletions(forceChatCompletions: false, providerSupportsResponsesApi: true));
        }

        [Test]
        public void SseReader_ShouldThrowTimeoutException_OnIdleTimeout()
        {
            var sseReader = new OpenAISseReader(TimeSpan.FromMilliseconds(80));
            using var stream = new BlockingReadStream();
            using var reader = new StreamReader(stream);
            var iterator = sseReader.ReadDataPayloadLinesAsync(reader, CancellationToken.None).GetAsyncEnumerator();

            try
            {
                Assert.Throws<TimeoutException>(() =>
                {
                    iterator.MoveNextAsync().GetAwaiter().GetResult();
                });
            }
            finally
            {
                iterator.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void SseReader_ShouldThrowOperationCanceledException_OnCancellation()
        {
            var sseReader = new OpenAISseReader(TimeSpan.FromSeconds(5));
            using var stream = new BlockingReadStream();
            using var reader = new StreamReader(stream);
            using var cts = new CancellationTokenSource(80);
            var iterator = sseReader.ReadDataPayloadLinesAsync(reader, cts.Token).GetAsyncEnumerator();

            try
            {
                Assert.Throws<OperationCanceledException>(() =>
                {
                    iterator.MoveNextAsync().GetAwaiter().GetResult();
                });
            }
            finally
            {
                iterator.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        private sealed class BlockingReadStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Thread.Sleep(500);
                return 0;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return WaitForDataAsync(cancellationToken);
            }

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                return new ValueTask<int>(WaitForDataAsync(cancellationToken));
            }
#endif

            private static async Task<int> WaitForDataAsync(CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }
        }
    }
}
