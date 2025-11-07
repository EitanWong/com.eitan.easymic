using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.ASR;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public class AIChatController : MonoBehaviour
    {
        #region SerializeFields
        [Header("Input")]
        [SerializeField] private VoiceMicrophone microphone;

        [Header("Speech Output")]
        [SerializeField] private bool useLocalTts = true;
        [SerializeField] private SpeechSynthesizer speechSynthesizer;

        [Header("OpenAI Compatible API")]
        [SerializeField] private string apiBaseUrl = "http://127.0.0.1:8000/v1/";
        [SerializeField] private string apiKey = "";
        [SerializeField] private string llmModel = "gpt-4o-mini";
        [SerializeField, Range(0f, 1.5f)] private float llmTemperature = 0.7f;
        [SerializeField] private string ttsModel = "gpt-4o-mini-tts";
        [SerializeField] private string ttsVoice = "alloy";
        [SerializeField] private string ttsAudioFormat = "wav";
        [SerializeField, TextArea(3, 8)] private string systemPrompt = "You are Samantha, an empathetic AI companion. Keep answers concise, warm, and natural.";
        [SerializeField] private bool logStreamingChunks;

        [Header("Runtime")]
        [SerializeField] private float micStartupDelay = 1f;
        #endregion


        #region Event
        public Action<float> OnLoadingCallback; //float: progress, bool: loading result (success or not)
        #endregion

        #region PrivateFields

        private Dictionary<string, float> _serviceLoadingRecord;
        private bool _initialized;
        private OpenAICompatibleClient _openAiClient;
        private CancellationTokenSource _responseCts;
        private readonly SentenceAggregator _sentenceAggregator = new SentenceAggregator();
        private Task _remoteTtsPipeline = Task.CompletedTask;
        private readonly object _remoteTtsLock = new object();
        private readonly object _remotePlaybackLock = new object();
        private PlaybackHandle _remotePlaybackHandle;
        private bool _remotePlaybackInitialized;
        private bool _remoteTtsConfigWarned;
        #endregion
        #region Constant
        private const string SERVICE_ASR_INIT_KEY = "SERVICE_ASR";
        private const string SERVICE_TTS_INIT_KEY = "SERVICE_TTS";
        #endregion

        #region MonoBehaviour

        private void Awake()
        {
            _serviceLoadingRecord = new Dictionary<string, float>();
            InitializeOpenAiClient();
            InitializeMicrophone();
            InitializeSpeechSynthesizer();
        }
        private void Update()
        {
            if (!useLocalTts || speechSynthesizer == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                speechSynthesizer.EnqueueSentence("测试一下语音转文字。");
            }
        }

        private void OnDestroy()
        {
            CancelActiveResponse();

            if (microphone != null)
            {
                // unregister voice microphone event callback. when component destroying;
                microphone.OnMicrophoneInitialized -= OnMicrophoneInitializedHandler;
                microphone.OnASRTranscriptionStreaming -= OnASRTranscriptionStreamingHandler;
                microphone.OnASRTranscriptionSubmit -= OnASRTranscriptionSubmitHandler;
                microphone.OnSpeakingChanged -= OnSpeakingChangedHandler;
                microphone.OnLoadingProgressFeedback -= OnMicrophoneLoadingProgressFeedbackHandler;
            }

            if (useLocalTts && speechSynthesizer != null)
            {
                speechSynthesizer.OnLoadingProgressFeedback -= OnSpeechSynthesizerProgressFeedbackHandler;
            }

            StopRemotePlayback();

            _openAiClient?.Dispose();
            _openAiClient = null;
        }
        #endregion

        #region EventHandler

        #region VocieMicrophoneEventCallback

        private void OnMicrophoneLoadingProgressFeedbackHandler(string message, float progress)
        {
            // UnityEngine.Debug.Log($"<color=green><b>message</b></color>: {message} <color=cyan><b>progress: <i>{progress}</i></b></color>");
            UpdateServiceLoading(SERVICE_ASR_INIT_KEY, progress);
        }
        private void OnASRTranscriptionStreamingHandler(string content)
        {
            // if (!string.IsNullOrEmpty(content))
            // {
            UnityEngine.Debug.Log($"<color=cyan><b>[Transcription]</b></color>: {content}");
            // }
        }

        private void OnASRTranscriptionSubmitHandler(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            UnityEngine.Debug.Log($"<color=cyan><b><i>[Submit]</i></b></color>: {content}");
            BeginAssistantResponse(content);
        }

        private void OnSpeakingChangedHandler(bool isSpeaking)
        {
        }

        private void OnMicrophoneInitializedHandler(bool state)
        {
        }

        #endregion
        #region SpeechSynthesizerEventCallback

        private void OnSpeechSynthesizerProgressFeedbackHandler(string message, float progress)
        {
            // UnityEngine.Debug.Log($"<color=green><b>message</b></color>: {message} <color=cyan><b>progress: <i>{progress}</i></b></color>");
            UpdateServiceLoading(SERVICE_TTS_INIT_KEY, progress);
        }
        #endregion
        #endregion

        #region PrivateMethods

        private void InitializeOpenAiClient()
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                Debug.LogWarning("[AIChat] OpenAI Compatible API base URL is empty.");
                return;
            }

            string normalized = apiBaseUrl.Trim();
            if (!normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized += "/";
            }

            try
            {
                _openAiClient?.Dispose();
                _openAiClient = new OpenAICompatibleClient(normalized, apiKey);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIChat] Failed to initialize OpenAI Compatible client: {ex.Message}");
                _openAiClient = null;
            }
        }

        private void InitializeMicrophone()
        {
            if (microphone == null)
            {
                Debug.LogWarning("[AIChat] VoiceMicrophone reference is missing.");
                return;
            }

            microphone.OnMicrophoneInitialized += OnMicrophoneInitializedHandler;
            microphone.OnASRTranscriptionStreaming += OnASRTranscriptionStreamingHandler;
            microphone.OnASRTranscriptionSubmit += OnASRTranscriptionSubmitHandler;
            microphone.OnSpeakingChanged += OnSpeakingChangedHandler;
            microphone.OnLoadingProgressFeedback += OnMicrophoneLoadingProgressFeedbackHandler;

            if (!microphone.MicrophoneOpts.recordOnAwake)
            {
                microphone.Init();
            }
        }

        private void InitializeSpeechSynthesizer()
        {
            if (!useLocalTts)
            {
                if (speechSynthesizer != null)
                {
                    speechSynthesizer.enabled = false;
                }
                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 1f);
                return;
            }

            if (speechSynthesizer == null)
            {
                Debug.LogWarning("[AIChat] SpeechSynthesizer is required when local TTS is enabled.");
                UpdateServiceLoading(SERVICE_TTS_INIT_KEY, 1f);
                return;
            }

            speechSynthesizer.OnLoadingProgressFeedback += OnSpeechSynthesizerProgressFeedbackHandler;
        }

        private void BeginAssistantResponse(string transcript)
        {
            if (_openAiClient == null)
            {
                InitializeOpenAiClient();
            }

            if (_openAiClient == null)
            {
                Debug.LogWarning("[AIChat] OpenAI Compatible client is not available.");
                return;
            }

            CancelActiveResponse();

            lock (_remoteTtsLock)
            {
                _remoteTtsPipeline = Task.CompletedTask;
            }

            _responseCts = new CancellationTokenSource();
            var token = _responseCts.Token;
            _ = HandleUserPromptAsync(transcript, token);
        }

        private async Task HandleUserPromptAsync(string transcript, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            _sentenceAggregator.Clear();

            var chatRequest = new OpenAIChatRequest
            {
                Model = string.IsNullOrWhiteSpace(llmModel) ? "gpt-4o-mini" : llmModel,
                Stream = true,
                Temperature = llmTemperature,
                Messages = BuildMessages(transcript)
            };

            try
            {
                await foreach (string chunk in _openAiClient.StreamChatCompletionAsync(chatRequest, token))
                {
                    token.ThrowIfCancellationRequested();

                    if (logStreamingChunks && !string.IsNullOrWhiteSpace(chunk))
                    {
                        Debug.Log($"[AIChat][LLM] {chunk}");
                    }

                    var readySentences = _sentenceAggregator.Append(chunk);
                    DispatchSentences(readySentences, token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AIChat] Response cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIChat] Chat completion failed: {ex.Message}");
            }
            finally
            {
                var trailingSentences = _sentenceAggregator.Append(string.Empty, flush: true);
                DispatchSentences(trailingSentences, token);

                if (!useLocalTts)
                {
                    Task remotePipelineSnapshot;
                    lock (_remoteTtsLock)
                    {
                        remotePipelineSnapshot = _remoteTtsPipeline;
                    }

                    try
                    {
                        await remotePipelineSnapshot;
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    CompleteRemotePlaybackStream();
                }
            }
        }

        private List<OpenAIChatMessage> BuildMessages(string transcript)
        {
            var messages = new List<OpenAIChatMessage>();

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new OpenAIChatMessage("system", systemPrompt.Trim()));
            }

            messages.Add(new OpenAIChatMessage("user", transcript));

            return messages;
        }

        private void DispatchSentences(List<string> sentences, CancellationToken token)
        {
            if (sentences == null || sentences.Count == 0)
            {
                return;
            }

            foreach (string sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                if (useLocalTts)
                {
                    if (speechSynthesizer != null)
                    {
                        speechSynthesizer.EnqueueSentence(sentence);
                    }
                    else
                    {
                        Debug.LogWarning("[AIChat] SpeechSynthesizer is missing; cannot enqueue sentence.");
                    }
                }
                else
                {
                    QueueRemoteSentence(sentence, token);
                }
            }
        }

        private void QueueRemoteSentence(string sentence, CancellationToken token)
        {
            if (token.IsCancellationRequested || string.IsNullOrWhiteSpace(sentence))
            {
                return;
            }

            if (_openAiClient == null)
            {
                Debug.LogWarning("[AIChat] Remote TTS requested but OpenAI client is missing.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ttsModel) || string.IsNullOrWhiteSpace(ttsVoice))
            {
                WarnRemoteTtsConfig();
                return;
            }

            _remoteTtsConfigWarned = false;

            lock (_remoteTtsLock)
            {
                _remoteTtsPipeline = _remoteTtsPipeline.ContinueWith(
                    _ => PlayRemoteSentenceAsync(sentence, token),
                    CancellationToken.None,
                    TaskContinuationOptions.RunContinuationsAsynchronously,
                    TaskScheduler.Default).Unwrap();
            }
        }

        private async Task PlayRemoteSentenceAsync(string sentence, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();

                var ttsRequest = new OpenAITtsRequest
                {
                    Model = ttsModel,
                    Voice = ttsVoice,
                    Input = sentence,
                    Format = string.IsNullOrWhiteSpace(ttsAudioFormat) ? "wav" : ttsAudioFormat
                };

                byte[] audioBytes = await _openAiClient.CreateSpeechAsync(ttsRequest, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                if (!WaveUtility.TryParsePcm16(audioBytes, out var samples, out int channels, out int sampleRate))
                {
                    Debug.LogWarning("[AIChat] Unsupported TTS audio payload (expected PCM16 WAV).");
                    return;
                }

                var playbackHandle = EnsureRemotePlaybackStream();
                if (!playbackHandle.IsValid)
                {
                    Debug.LogWarning("[AIChat] Remote playback handle is invalid.");
                    return;
                }

                playbackHandle.Enqueue(samples, samples.Length, channels, sampleRate, false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AIChat] Remote TTS failed: {ex.Message}");
            }
        }

        private PlaybackHandle EnsureRemotePlaybackStream()
        {
            lock (_remotePlaybackLock)
            {
                if (_remotePlaybackInitialized && _remotePlaybackHandle.IsValid)
                {
                    return _remotePlaybackHandle;
                }

                try
                {
                    _remotePlaybackHandle = AudioPlayback.CreateStream(volume: 1f);
                    _remotePlaybackInitialized = _remotePlaybackHandle.IsValid;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AIChat] Failed to create remote playback stream: {ex.Message}");
                    _remotePlaybackHandle = default;
                    _remotePlaybackInitialized = false;
                }

                return _remotePlaybackHandle;
            }
        }

        private void CompleteRemotePlaybackStream()
        {
            lock (_remotePlaybackLock)
            {
                if (_remotePlaybackInitialized && _remotePlaybackHandle.IsValid)
                {
                    try
                    {
                        _remotePlaybackHandle.CompleteStream();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AIChat] Failed to finalize remote playback stream: {ex.Message}");
                    }
                }
            }
        }

        private void StopRemotePlayback()
        {
            lock (_remotePlaybackLock)
            {
                if (!_remotePlaybackInitialized || !_remotePlaybackHandle.IsValid)
                {
                    _remotePlaybackInitialized = false;
                    _remotePlaybackHandle = default;
                    return;
                }

                try
                {
                    _remotePlaybackHandle.Stop();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AIChat] Failed to stop playback: {ex.Message}");
                }

                try
                {
                    _remotePlaybackHandle.CompleteStream();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AIChat] Failed to complete playback stream: {ex.Message}");
                }

                try
                {
                    _remotePlaybackHandle.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AIChat] Failed to dispose playback handle: {ex.Message}");
                }

                _remotePlaybackHandle = default;
                _remotePlaybackInitialized = false;
            }
        }

        private void CancelActiveResponse()
        {
            if (_responseCts != null)
            {
                try
                {
                    _responseCts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                _responseCts.Dispose();
                _responseCts = null;
            }

            _sentenceAggregator.Clear();

            if (useLocalTts && speechSynthesizer != null)
            {
                speechSynthesizer.Stop();
            }
            else
            {
                StopRemotePlayback();
            }
        }

        private void WarnRemoteTtsConfig()
        {
            if (_remoteTtsConfigWarned)
            {
                return;
            }

            _remoteTtsConfigWarned = true;
            Debug.LogWarning("[AIChat] Remote TTS configuration is incomplete (model or voice missing).");
        }

        private void UpdateServiceLoading(string key, float progress)
        {
            _serviceLoadingRecord[key] = progress;

            // 计算总体进度：所有服务平均进度
            if (_serviceLoadingRecord.Count == 0)
            {
                return;
            }

            float total = 0f;
            foreach (var kv in _serviceLoadingRecord)
            {
                total += kv.Value;
            }
            float overallProgress = total / _serviceLoadingRecord.Count;

            OnLoadingCallback?.Invoke(overallProgress);
            if (!_initialized && overallProgress >= 1f)
            {
                _initialized = true;

                if (microphone != null && !microphone.MicrophoneOpts.recordOnAwake)
                {
                    StartCoroutine(WaitToInvoke(() =>
                    {
                        if (microphone != null && !microphone.MicrophoneOpts.recordOnAwake)
                        {
                            microphone.StartRecording();
                        }
                    }, micStartupDelay));
                }

            }
        }

        private System.Collections.IEnumerator WaitToInvoke(Action callback, float wait)
        {
            yield return new WaitForSeconds(wait);
            callback?.Invoke();
        }
        #endregion

        #region Nested Helpers
        private sealed class SentenceAggregator
        {
            private static readonly Regex SentenceSplitRegex = new Regex(@"(?<=[.!?\n。！？])(?=\s|\S)", RegexOptions.Compiled);
            private readonly StringBuilder _buffer = new StringBuilder();

            public List<string> Append(string chunk, bool flush = false)
            {
                var ready = new List<string>();

                if (!string.IsNullOrEmpty(chunk))
                {
                    _buffer.Append(chunk);
                }

                if (_buffer.Length == 0)
                {
                    return ready;
                }

                string working = _buffer.ToString();
                string[] segments = SentenceSplitRegex.Split(working);
                int consumed = 0;

                for (int i = 0; i < segments.Length; i++)
                {
                    string segment = segments[i];
                    if (segment.Length == 0)
                    {
                        continue;
                    }

                    bool isLast = i == segments.Length - 1;
                    if (!flush && isLast && !EndsWithTerminator(segment))
                    {
                        break;
                    }

                    ready.Add(segment.Trim());
                    consumed += segment.Length;
                }

                if (consumed > 0)
                {
                    _buffer.Remove(0, consumed);
                }

                if (flush && _buffer.Length > 0)
                {
                    string trailing = _buffer.ToString().Trim();
                    if (!string.IsNullOrEmpty(trailing))
                    {
                        ready.Add(trailing);
                    }

                    _buffer.Clear();
                }

                return ready;
            }

            public void Clear()
            {
                _buffer.Clear();
            }

            private static bool EndsWithTerminator(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                char last = value[value.Length - 1];
                return last == '.' || last == '?' || last == '!' || last == '。' || last == '！' || last == '？' || last == '\n';
            }
        }

        private static class WaveUtility
        {
            public static bool TryParsePcm16(byte[] data, out float[] samples, out int channels, out int sampleRate)
            {
                samples = Array.Empty<float>();
                channels = 0;
                sampleRate = 0;

                if (data == null || data.Length < 44)
                {
                    return false;
                }

                using var memoryStream = new MemoryStream(data);
                using var reader = new BinaryReader(memoryStream);

                if (!ReadTag(reader, "RIFF"))
                {
                    return false;
                }

                reader.ReadInt32(); // file size

                if (!ReadTag(reader, "WAVE"))
                {
                    return false;
                }

                ushort bitsPerSample = 0;
                bool fmtParsed = false;

                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    int chunkSize = reader.ReadInt32();

                    switch (chunkId)
                    {
                        case "fmt ":
                            ushort audioFormat = reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = reader.ReadInt32();
                            reader.ReadInt32(); // byte rate
                            reader.ReadUInt16(); // block align
                            bitsPerSample = reader.ReadUInt16();

                            int remaining = chunkSize - 16;
                            if (remaining > 0)
                            {
                                reader.BaseStream.Seek(remaining, SeekOrigin.Current);
                            }

                            if (audioFormat != 1 && audioFormat != 3)
                            {
                                return false;
                            }

                            fmtParsed = true;
                            break;

                        case "data":
                            if (!fmtParsed)
                            {
                                return false;
                            }

                            byte[] raw = reader.ReadBytes(chunkSize);
                            if (bitsPerSample == 0)
                            {
                                bitsPerSample = 16;
                            }

                            if (bitsPerSample == 16)
                            {
                                int sampleCount = raw.Length / 2;
                                var result = new float[sampleCount];
                                for (int i = 0; i < sampleCount; i++)
                                {
                                    short sample = BitConverter.ToInt16(raw, i * 2);
                                    result[i] = sample / 32768f;
                                }

                                samples = result;
                            }
                            else if (bitsPerSample == 32)
                            {
                                int sampleCount = raw.Length / 4;
                                var result = new float[sampleCount];
                                for (int i = 0; i < sampleCount; i++)
                                {
                                    result[i] = BitConverter.ToSingle(raw, i * 4);
                                }

                                samples = result;
                            }
                            else
                            {
                                return false;
                            }

                            channels = Math.Max(1, channels);
                            sampleRate = Math.Max(8000, sampleRate);
                            return true;

                        default:
                            reader.BaseStream.Seek(chunkSize, SeekOrigin.Current);
                            break;
                    }

                    if ((chunkSize & 1) == 1)
                    {
                        reader.BaseStream.Seek(1, SeekOrigin.Current);
                    }
                }

                return false;
            }

            private static bool ReadTag(BinaryReader reader, string tag)
            {
                byte[] bytes = reader.ReadBytes(4);
                if (bytes.Length < 4)
                {
                    return false;
                }

                return string.Equals(Encoding.ASCII.GetString(bytes), tag, StringComparison.Ordinal);
            }
        }
        #endregion
    }
}
