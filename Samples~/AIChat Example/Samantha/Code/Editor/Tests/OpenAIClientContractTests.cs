using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class OpenAIClientContractTests
    {
        [Test]
        public void OfficialOpenAI_ShouldUseResponsesWhileCompatibleProvidersUseChatCompletions()
        {
            IOpenAIProviderAdapter openAi = OpenAIProviderAdapterResolver.Resolve("https://api.openai.com/v1/");
            IOpenAIProviderAdapter siliconFlow = OpenAIProviderAdapterResolver.Resolve("https://api.siliconflow.cn/v1/");
            IOpenAIProviderAdapter custom = OpenAIProviderAdapterResolver.Resolve("https://provider.example/v1/");

            Assert.IsTrue(openAi.SupportsResponsesApi);
            Assert.IsFalse(siliconFlow.SupportsResponsesApi);
            Assert.IsFalse(custom.SupportsResponsesApi);
        }

        [TestCase("https://api.siliconflow.cn/v1/", "SiliconFlow", false)]
        [TestCase("https://api.openai.com/v1/", "OpenAI", true)]
        [TestCase("https://provider.example/v1/", "OpenAI-Compatible", false)]
        public void ProviderResolver_ShouldSelectCompatibleCapabilities(
            string baseUrl,
            string expectedName,
            bool expectedResponsesApiSupport)
        {
            IOpenAIProviderAdapter adapter = OpenAIProviderAdapterResolver.Resolve(baseUrl);

            Assert.AreEqual(expectedName, adapter.Name);
            Assert.AreEqual(expectedResponsesApiSupport, adapter.SupportsResponsesApi);
        }

        [Test]
        public void GenericTtsPayload_ShouldOmitProviderSpecificFields()
        {
            IOpenAIProviderAdapter adapter = OpenAIProviderAdapterResolver.Resolve("https://provider.example/v1/");
            var request = new OpenAITtsRequest
            {
                Model = "speech-model",
                Input = "hello",
                Voice = "voice-id",
                ResponseFormat = "pcm",
                SampleRate = 24000,
                Speed = 1.2f,
                Gain = 2f,
                stream = true
            };

            string payload = adapter.BuildTtsPayload(request);

            StringAssert.Contains("\"model\":\"speech-model\"", payload);
            StringAssert.Contains("\"response_format\":\"pcm\"", payload);
            StringAssert.Contains("\"speed\":1.2", payload);
            StringAssert.DoesNotContain("sample_rate", payload);
            StringAssert.DoesNotContain("gain", payload);
            StringAssert.DoesNotContain("stream", payload);
        }

        [Test]
        public void SiliconFlowTtsPayload_ShouldKeepProviderSpecificFields()
        {
            IOpenAIProviderAdapter adapter = OpenAIProviderAdapterResolver.Resolve("https://api.siliconflow.cn/v1/");
            var request = new OpenAITtsRequest
            {
                Model = "speech-model",
                Input = "hello",
                Voice = "voice-id",
                ResponseFormat = "pcm",
                SampleRate = 24000,
                Gain = 2f,
                stream = true
            };

            string payload = adapter.BuildTtsPayload(request);

            StringAssert.Contains("sample_rate", payload);
            StringAssert.Contains("gain", payload);
            StringAssert.Contains("\"stream\":true", payload);
        }

        [TestCase("https://provider.example/v1", true, "https://provider.example/v1/")]
        [TestCase("https://provider.example/openai/", true, "https://provider.example/openai/")]
        [TestCase("http://localhost:8080/v1", true, "http://localhost:8080/v1/")]
        [TestCase("http://provider.example/v1", false, "")]
        [TestCase("https://user:password@provider.example/v1", false, "")]
        [TestCase("https://provider.example/v1?tenant=one", false, "")]
        [TestCase("https://provider.example/v1#fragment", false, "")]
        public void BaseUrlValidation_ShouldProtectCredentialsAndNormalizeSupportedUrls(
            string value,
            bool expectedValid,
            string expectedNormalized)
        {
            bool valid = OpenAICompatibleClient.TryNormalizeBaseUrl(value, out string normalized, out _);

            Assert.AreEqual(expectedValid, valid);
            Assert.AreEqual(expectedNormalized, normalized);
        }

        [TestCase("https://provider.example/", "chat/completions", "v1/chat/completions")]
        [TestCase("https://provider.example/v1/", "chat/completions", "chat/completions")]
        [TestCase("https://provider.example/openai/", "chat/completions", "chat/completions")]
        public void EndpointResolution_ShouldTreatExplicitBasePathAsAuthoritative(
            string normalizedBaseUrl,
            string endpoint,
            string expected)
        {
            Assert.AreEqual(expected, OpenAICompatibleClient.ResolveEndpointPath(normalizedBaseUrl, endpoint));
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
