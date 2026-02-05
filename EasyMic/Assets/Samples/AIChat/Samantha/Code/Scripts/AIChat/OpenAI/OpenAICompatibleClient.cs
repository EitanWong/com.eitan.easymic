using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// OpenAI 兼容客户端，支持 Responses API 和 Chat Completions API
    /// 优先使用 Responses API，不支持时自动回退到 Chat Completions API
    /// </summary>
    internal sealed class OpenAICompatibleClient : IDisposable
    {
        private static readonly MediaTypeWithQualityHeaderValue EventStreamHeader =
            new MediaTypeWithQualityHeaderValue("text/event-stream");
        private static readonly MediaTypeWithQualityHeaderValue AudioHeader =
            new MediaTypeWithQualityHeaderValue("application/octet-stream");
        private static readonly MediaTypeWithQualityHeaderValue JsonHeader =
            new MediaTypeWithQualityHeaderValue("application/json");
        private static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _httpClient;
        private readonly bool _baseIncludesVersion;
        private readonly string _chatEndpoint;
        private readonly string _responsesEndpoint;
        private readonly string _ttsEndpoint;
        private readonly IOpenAIProviderAdapter _providerAdapter;

        /// <summary>
        /// 是否强制使用 Chat Completions API（跳过 Responses API）
        /// </summary>
        public bool ForceChatCompletions { get; set; } = false;

        /// <summary>
        /// 是否保存 TTS 请求 payload 调试文件
        /// </summary>
        public bool EnableTtsDiagnostics { get; set; } = false;

        /// <summary>
        /// 是否已知服务器不支持 Responses API（运行时自动检测）
        /// </summary>
        private bool _responsesApiNotSupported = false;

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
            _responsesEndpoint = _baseIncludesVersion ? "responses" : "v1/responses";
            _ttsEndpoint = _baseIncludesVersion ? "audio/speech" : "v1/audio/speech";

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(normalized, UriKind.Absolute),
                Timeout = timeout ?? TimeSpan.FromSeconds(120)
            };

            _providerAdapter = OpenAIProviderAdapterResolver.Resolve(normalized);
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
                string normalizedKey = apiKey.Trim();
                const string bearerPrefix = "Bearer ";
                if (normalizedKey.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalizedKey = normalizedKey.Substring(bearerPrefix.Length).Trim();
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", normalizedKey);
            }
        }

        /// <summary>
        /// 重置 API 支持检测状态
        /// </summary>
        public void ResetApiDetection()
        {
            _responsesApiNotSupported = false;
        }

        #region Chat Completion - 统一入口

        /// <summary>
        /// 流式聊天补全（自动选择 Responses API 或 Chat Completions API）
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatCompletionAsync(
            OpenAIChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                yield break;
            }

            // 如果强制使用 Chat Completions 或已知不支持 Responses API，直接使用 Chat Completions

            if (ForceChatCompletions || _responsesApiNotSupported || !_providerAdapter.SupportsResponsesApi)
            {
                await foreach (string chunk in StreamChatCompletionsApiAsync(request, cancellationToken))
                {
                    yield return chunk;
                }
                yield break;
            }

            // 尝试 Responses API
            bool fallbackRequired = false;

            await foreach (string chunk in StreamResponsesApiAsync(request, cancellationToken, () => fallbackRequired = true))
            {
                yield return chunk;
            }

            if (!fallbackRequired)
            {
                yield break;
            }

            // 记住不支持 Responses API

            _responsesApiNotSupported = true;

            // 回退到 Chat Completions API
            await foreach (string chunk in StreamChatCompletionsApiAsync(request, cancellationToken))
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// 非流式聊天补全
        /// </summary>
        public async Task<OpenAIChatResult> ChatCompletionAsync(
            OpenAIChatRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {

                return new OpenAIChatResult { Success = false, ErrorMessage = "Request is null" };
            }

            // 如果强制使用 Chat Completions 或已知不支持 Responses API

            if (ForceChatCompletions || _responsesApiNotSupported || !_providerAdapter.SupportsResponsesApi)
            {
                return await ChatCompletionsApiAsync(request, cancellationToken);
            }

            // 尝试 Responses API
            var result = await ResponsesApiAsync(request, cancellationToken);

            if (result.FallbackRequired)
            {
                _responsesApiNotSupported = true;
                return await ChatCompletionsApiAsync(request, cancellationToken);
            }

            return result;
        }

        #endregion

        #region Responses API

        private async IAsyncEnumerable<string> StreamResponsesApiAsync(
            OpenAIChatRequest chatRequest,
            [EnumeratorCancellation] CancellationToken cancellationToken,
            Action onNotSupported = null)
        {
            var responseRequest = OpenAIResponseRequest.FromChatRequest(chatRequest);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _responsesEndpoint);
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(EventStreamHeader);

            string payload = _providerAdapter.BuildResponsesPayload(responseRequest);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                onNotSupported?.Invoke();
                yield break;
            }

            using (response)
            {
                // 检查是否不支持 Responses API
                if (response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == HttpStatusCode.NotImplemented ||
                    response.StatusCode == HttpStatusCode.Forbidden ||
                    (int)response.StatusCode == 421) // Misdirected Request
                {
                    onNotSupported?.Invoke();
                    yield break;
                }

                response.EnsureSuccessStatusCode();

                string mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                bool isEventStream = mediaType.IndexOf("text/event-stream", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isEventStream)
                {
                    await foreach (string chunk in ReadResponsesEventStreamAsync(response, cancellationToken))
                    {
                        yield return chunk;
                    }
                }
                else
                {
                    // 非流式响应
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    foreach (string chunk in ExtractTextFromResponseJson(json))
                    {
                        if (!string.IsNullOrEmpty(chunk))
                        {
                            yield return chunk;
                        }
                    }
                }
            }
        }

        private async Task<OpenAIChatResult> ResponsesApiAsync(
            OpenAIChatRequest chatRequest,
            CancellationToken cancellationToken)
        {
            var responseRequest = OpenAIResponseRequest.FromChatRequest(chatRequest);
            responseRequest.Stream = false;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _responsesEndpoint);
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(JsonHeader);

            string payload = _providerAdapter.BuildResponsesPayload(responseRequest);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                return new OpenAIChatResult
                {
                    Success = false,
                    FallbackRequired = true,
                    ErrorMessage = ex.Message
                };
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound ||
                    response.StatusCode == HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == HttpStatusCode.NotImplemented ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new OpenAIChatResult { FallbackRequired = true };
                }

                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var texts = new List<string>();

                foreach (string chunk in ExtractTextFromResponseJson(json))
                {
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        texts.Add(chunk);
                    }
                }

                return new OpenAIChatResult
                {
                    Success = true,
                    Content = string.Join("", texts)
                };
            }
        }

        private async IAsyncEnumerable<string> ReadResponsesEventStreamAsync(
            HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using (body)
            using (var reader = new StreamReader(body, Encoding.UTF8))
            {
                bool streamedDelta = false;

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string line = await ReadLineWithTimeoutAsync(reader, cancellationToken).ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }


                    if (string.IsNullOrWhiteSpace(line) ||
                        !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
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

                    payloadLine = _providerAdapter.NormalizeResponsesStreamEventJson(payloadLine);
                    if (string.IsNullOrWhiteSpace(payloadLine))
                    {
                        continue;
                    }

                    OpenAIResponseStreamEvent envelope;
                    try
                    {
                        envelope = JsonUtility.FromJson<OpenAIResponseStreamEvent>(payloadLine);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (envelope == null)
                    {
                        continue;
                    }

                    // 检查错误

                    if (envelope.error != null && !string.IsNullOrEmpty(envelope.error.message))
                    {
                        throw new OpenAIApiException($"Responses API error: {envelope.error.message}", envelope.error.code);
                    }

                    // 处理 delta 文本
                    if (!string.IsNullOrEmpty(envelope.delta))
                    {
                        streamedDelta = true;
                        yield return envelope.delta;
                        continue;
                    }

                    // 处理完整的 output（非流式场景或完成事件）
                    if (!streamedDelta && envelope.response?.output != null)
                    {
                        foreach (string chunk in ExtractTextFromOutputs(envelope.response.output))
                        {
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                yield return chunk;
                            }
                        }
                    }
                }
            }
        }

        private IEnumerable<string> ExtractTextFromResponseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                yield break;
            }

            json = _providerAdapter.NormalizeResponsesResponseJson(json);
            if (string.IsNullOrWhiteSpace(json))
            {
                yield break;
            }

            OpenAIResponseObject response;
            try
            {
                response = JsonUtility.FromJson<OpenAIResponseObject>(json);
            }
            catch (Exception)
            {
                yield break;
            }

            if (response == null)
            {
                yield break;
            }


            if (response.error != null && !string.IsNullOrEmpty(response.error.message))
            {
                throw new OpenAIApiException($"Responses API error: {response.error.message}", response.error.code);
            }

            foreach (string chunk in ExtractTextFromOutputs(response.output))
            {
                yield return chunk;
            }
        }

        private IEnumerable<string> ExtractTextFromOutputs(OpenAIResponseOutputItem[] outputs)
        {
            if (outputs == null)
            {
                yield break;
            }


            foreach (var output in outputs)
            {
                if (output?.content == null)
                {
                    continue;
                }


                foreach (var content in output.content)
                {
                    if (!string.IsNullOrEmpty(content?.text))
                    {
                        yield return content.text;
                    }
                }
            }
        }

        #endregion

        #region Chat Completions API

        private async IAsyncEnumerable<string> StreamChatCompletionsApiAsync(
            OpenAIChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            request.Stream = true;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint);
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(EventStreamHeader);

            string payload = _providerAdapter.BuildChatCompletionsPayload(request);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string errorMessage = TryExtractErrorMessage(errorBody);
                    string fallbackMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    throw new OpenAIApiException(string.IsNullOrWhiteSpace(errorMessage) ? fallbackMessage : errorMessage);
                }

                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (body)
                using (var reader = new StreamReader(body, Encoding.UTF8))
                {
                    while (!reader.EndOfStream)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string line = await ReadLineWithTimeoutAsync(reader, cancellationToken).ConfigureAwait(false);
                        if (line == null)
                        {
                            break;
                        }


                        if (string.IsNullOrWhiteSpace(line) ||
                            !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
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

                        payloadLine = _providerAdapter.NormalizeChatCompletionChunkJson(payloadLine);
                        if (string.IsNullOrWhiteSpace(payloadLine))
                        {
                            continue;
                        }

                        OpenAIChatCompletionChunk chunk;
                        try
                        {
                            chunk = JsonUtility.FromJson<OpenAIChatCompletionChunk>(payloadLine);
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        if (chunk?.choices == null)
                        {
                            continue;
                        }


                        foreach (var choice in chunk.choices)
                        {
                            string delta = _providerAdapter.SelectChatCompletionDeltaText(choice);
                            if (!string.IsNullOrEmpty(delta))
                            {
                                yield return delta;
                            }
                        }
                    }
                }
            }
        }

        private static async Task<string> ReadLineWithTimeoutAsync(
            StreamReader reader,
            CancellationToken cancellationToken)
        {
            var readTask = reader.ReadLineAsync();
            var timeoutTask = Task.Delay(StreamIdleTimeout, cancellationToken);
            var completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
            if (completed == timeoutTask)
            {
                throw new TimeoutException("Stream idle timeout.");
            }

            return await readTask.ConfigureAwait(false);
        }

        private async Task<OpenAIChatResult> ChatCompletionsApiAsync(
            OpenAIChatRequest request,
            CancellationToken cancellationToken)
        {
            request.Stream = false;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatEndpoint);
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(JsonHeader);

            string payload = _providerAdapter.BuildChatCompletionsPayload(request);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string errorMessage = TryExtractErrorMessage(errorBody);
                    string fallbackMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    return new OpenAIChatResult
                    {
                        Success = false,
                        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? fallbackMessage : errorMessage
                    };
                }

                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                json = _providerAdapter.NormalizeChatCompletionResponseJson(json);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new OpenAIChatResult
                    {
                        Success = false,
                        ErrorMessage = "Empty response payload"
                    };
                }

                OpenAIChatCompletionResponse result;
                try
                {
                    result = JsonUtility.FromJson<OpenAIChatCompletionResponse>(json);
                }
                catch (Exception ex)
                {
                    return new OpenAIChatResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to parse response: {ex.Message}"
                    };
                }

                if (result?.choices != null && result.choices.Count > 0)
                {
                    var message = result.choices[0]?.message;
                    string content = _providerAdapter.SelectChatCompletionMessageText(message);
                    return new OpenAIChatResult
                    {
                        Success = true,
                        Content = content ?? string.Empty,
                        ReasoningContent = message?.reasoning_content
                    };
                }

                return new OpenAIChatResult
                {
                    Success = false,
                    ErrorMessage = "No choices in response"
                };
            }
        }

        #endregion

        #region TTS API

        /// <summary>
        /// 文本转语音
        /// </summary>
        public async Task<byte[]> CreateSpeechAsync(
            OpenAITtsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Array.Empty<byte>();
            }

            TryDumpTtsPayload(request);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _ttsEndpoint);
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(AudioHeader);

            string payload = _providerAdapter.BuildTtsPayload(request);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            using (response)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 流式文本转语音
        /// </summary>
        public async IAsyncEnumerable<byte[]> StreamSpeechAsync(
            OpenAITtsRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request == null)
            {
                yield break;
            }

            request.stream = true;
            TryDumpTtsPayload(request);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _ttsEndpoint);
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(AudioHeader);

            string payload = _providerAdapter.BuildTtsPayload(request);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            using (response)
            {
                response.EnsureSuccessStatusCode();

                var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using (stream)
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        byte[] chunk = new byte[bytesRead];
                        Array.Copy(buffer, chunk, bytesRead);
                        yield return chunk;
                    }
                }
            }
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private void TryDumpTtsPayload(OpenAITtsRequest request)
        {
            if (!EnableTtsDiagnostics)
            {
                return;
            }

            try
            {
                string projectRoot = ResolveProjectRoot();
                string root = Path.Combine(projectRoot, "TtsDiagnostics");
                Directory.CreateDirectory(root);

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string filePath = Path.Combine(root, $"tts_payload_{timestamp}.json");
                File.WriteAllText(filePath, _providerAdapter.BuildTtsPayload(request), Encoding.UTF8);

                string metaPath = Path.Combine(root, $"tts_payload_{timestamp}.meta.txt");
                string endpoint = new Uri(_httpClient.BaseAddress, _ttsEndpoint).ToString();
                File.WriteAllText(metaPath, $"endpoint: {endpoint}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TTS][Diag] Failed to save payload: {ex.Message}");
            }
        }

        private static string ResolveProjectRoot()
        {
            string current = Environment.CurrentDirectory;
            string dir = current;
            for (int i = 0; i < 5; i++)
            {
                if (string.IsNullOrEmpty(dir))
                {
                    break;
                }

                if (Directory.Exists(Path.Combine(dir, "Assets")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            return current;
        }

        private static string TryExtractErrorMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var envelope = JsonUtility.FromJson<OpenAIErrorEnvelope>(json);
                if (!string.IsNullOrWhiteSpace(envelope?.error?.message))
                {
                    return envelope.error.message;
                }

                if (!string.IsNullOrWhiteSpace(envelope?.message))
                {
                    return envelope.message;
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        [Serializable]
        private sealed class OpenAIErrorEnvelope
        {
            public OpenAIErrorDetail error;
            public string message;
        }

        [Serializable]
        private sealed class OpenAIErrorDetail
        {
            public string message;
            public string code;
            public string type;
        }
    }

    /// <summary>
    /// OpenAI API 异常
    /// </summary>
    public class OpenAIApiException : Exception
    {
        public string ErrorCode { get; }

        public OpenAIApiException(string message, string errorCode = null) : base(message)
        {
            ErrorCode = errorCode;
        }
    }
}
