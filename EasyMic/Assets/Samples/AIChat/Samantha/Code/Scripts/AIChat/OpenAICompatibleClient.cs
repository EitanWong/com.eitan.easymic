using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed class OpenAICompatibleClient : IDisposable
    {
        private static readonly MediaTypeWithQualityHeaderValue EventStreamHeader = new MediaTypeWithQualityHeaderValue("text/event-stream");
        private static readonly MediaTypeWithQualityHeaderValue AudioHeader = new MediaTypeWithQualityHeaderValue("audio/wav");

        private readonly HttpClient _httpClient;
        private readonly bool _baseIncludesVersion;
        private readonly string _chatEndpoint;
        private readonly string _ttsEndpoint;

        public OpenAICompatibleClient(string baseUrl, string apiKey, TimeSpan? timeout = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("API base URL is required.", nameof(baseUrl));
            }

            string normalized = baseUrl.Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            string trimmed = normalized.TrimEnd('/');
            _baseIncludesVersion = trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
            _chatEndpoint = _baseIncludesVersion ? "chat/completions" : "v1/chat/completions";
            _ttsEndpoint = _baseIncludesVersion ? "audio/speech" : "v1/audio/speech";

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(normalized, UriKind.Absolute),
                Timeout = timeout ?? TimeSpan.FromSeconds(120)
            };

            UpdateCredentials(apiKey);
        }

        public void UpdateCredentials(string apiKey)
        {
            if (_httpClient == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        public async IAsyncEnumerable<string> StreamChatCompletionAsync(
            OpenAIChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                yield break;
            }

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint);
            using (httpRequest)
            {
                httpRequest.Headers.Accept.Clear();
                httpRequest.Headers.Accept.Add(EventStreamHeader);

                string payload = JsonUtility.ToJson(request);
                httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                using (response)
                {
                    response.EnsureSuccessStatusCode();

                    var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using (body)
                    using (var reader = new StreamReader(body, Encoding.UTF8))
                    {
                        while (!reader.EndOfStream)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null)
                            {
                                break;
                            }

                            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string payloadLine = line.Substring(5).Trim();
                            if (payloadLine.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                            {
                                yield break;
                            }

                            if (string.IsNullOrWhiteSpace(payloadLine))
                            {
                                continue;
                            }

                            ChatCompletionChunk chunk;
                            try
                            {
                                chunk = JsonUtility.FromJson<ChatCompletionChunk>(payloadLine);
                            }
                            catch (Exception)
                            {
                                continue;
                            }

                            if (chunk?.Choices == null)
                            {
                                continue;
                            }

                            foreach (var choice in chunk.Choices)
                            {
                                string delta = choice?.Delta?.Content;
                                if (!string.IsNullOrEmpty(delta))
                                {
                                    yield return delta;
                                }
                            }
                        }
                    }
                }
            }
        }

        public async Task<byte[]> CreateSpeechAsync(OpenAITtsRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Array.Empty<byte>();
            }

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, _ttsEndpoint);
            using (httpRequest)
            {
                httpRequest.Headers.Accept.Clear();
                httpRequest.Headers.Accept.Add(AudioHeader);

                string payload = JsonUtility.ToJson(request);
                httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                using (response)
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    [Serializable]
    internal sealed class OpenAIChatRequest
    {
        [SerializeField] private string model;
        [SerializeField] private bool stream = true;
        [SerializeField] private float temperature = 0.7f;
        [SerializeField] private List<OpenAIChatMessage> messages = new List<OpenAIChatMessage>();

        public string Model
        {
            get => model;
            set => model = value;
        }

        public bool Stream
        {
            get => stream;
            set => stream = value;
        }

        public float Temperature
        {
            get => temperature;
            set => temperature = value;
        }

        public List<OpenAIChatMessage> Messages
        {
            get => messages;
            set => messages = value ?? new List<OpenAIChatMessage>();
        }
    }

    [Serializable]
    internal sealed class OpenAIChatMessage
    {
        [SerializeField] private string role;
        [SerializeField] private string content;

        public OpenAIChatMessage()
        {
        }

        public OpenAIChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }

        public string Role
        {
            get => role;
            set => role = value;
        }

        public string Content
        {
            get => content;
            set => content = value;
        }
    }

    [Serializable]
    internal sealed class OpenAITtsRequest
    {
        [SerializeField] private string model;
        [SerializeField] private string voice;
        [SerializeField] private string input;
        [SerializeField] private string format = "wav";
        [SerializeField] private float speed = 1f;

        public string Model
        {
            get => model;
            set => model = value;
        }

        public string Voice
        {
            get => voice;
            set => voice = value;
        }

        public string Input
        {
            get => input;
            set => input = value;
        }

        public string Format
        {
            get => format;
            set => format = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = value;
        }
    }

    [Serializable]
    internal sealed class ChatCompletionChunk
    {
        public ChatCompletionChoice[] choices;

        public ChatCompletionChoice[] Choices => choices;
    }

    [Serializable]
    internal sealed class ChatCompletionChoice
    {
        public ChatCompletionDelta delta;
        public string finish_reason;

        public ChatCompletionDelta Delta => delta;
        public string FinishReason => finish_reason;
    }

    [Serializable]
    internal sealed class ChatCompletionDelta
    {
        public string content;
        public string role;

        public string Content => content;
        public string Role => role;
    }
}
