#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private enum StreamTtsResult
        {
            None,
            Streamed,
            BufferedComplete
        }

        private async Task RunGenerationWorkerAsync(long sessionId, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_pendingJobs.TryDequeue(out var job))
                {
                    if (_pendingJobs.IsEmpty)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(10, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    await _generationSemaphore.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _pendingJobs.Enqueue(job);
                    break;
                }

                try
                {
                    bool enqueue = await GenerateTtsForJobAsync(job, token).ConfigureAwait(false);
                    _resourceMonitor.RecordGeneration(job.Stopwatch.ElapsedMilliseconds);

                    if (enqueue)
                    {
                        TryAddCompletedJob(job);
                    }
                }
                catch (OperationCanceledException)
                {
                    job.MarkFailed(new OperationCanceledException());
                    TryAddCompletedJob(job);
                    break;
                }
                catch (Exception ex)
                {
                    job.MarkFailed(ex);
                    TryAddCompletedJob(job);
                    Debug.LogError($"[ParallelTtsPipeline] Generation failed: {ex.Message}");
                }
                finally
                {
                    _generationSemaphore.Release();
                }
            }
        }

        private async Task<bool> GenerateTtsForJobAsync(TtsJob job, CancellationToken token)
        {
            try
            {
                var client = _clientAccessor?.Invoke();
                if (client == null)
                {
                    throw new InvalidOperationException("OpenAI client not available");
                }

                if (string.IsNullOrWhiteSpace(_config.RemoteModel) ||
                    string.IsNullOrWhiteSpace(_config.RemoteVoice))
                {
                    throw new InvalidOperationException("Remote TTS model or voice not configured");
                }

                string remoteInput = ResolveRemoteInput(job.Sentence);

                var request = new OpenAITtsRequest
                {
                    Model = _config.RemoteModel,
                    Voice = _config.RemoteVoice,
                    Input = remoteInput,
                    ResponseFormat = RemoteFormat
                };

                if (_config.LogSentences)
                {
                    Debug.Log($"[ParallelTtsPipeline][TTS] Generating: {job.Sentence}");
                }

                if (_config.EnableStreamingTts)
                {
                    StreamTtsResult streamed = await TryStreamTtsForJobAsync(job, client, request, token).ConfigureAwait(false);
                    if (streamed == StreamTtsResult.Streamed)
                    {
                        return false;
                    }

                    if (streamed == StreamTtsResult.BufferedComplete)
                    {
                        return true;
                    }
                }

                request.stream = false;
                byte[] audioBytes = await client.CreateSpeechAsync(request, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                int expectedChannels = RemoteDefaultChannels;
                int expectedSampleRate = request.SampleRate > 0 ? request.SampleRate : RemoteDefaultSampleRate;

                if (!TryDecodeAudioPayload(audioBytes, expectedChannels, expectedSampleRate, out var samples, out int channels, out int sampleRate))
                {
                    throw new InvalidOperationException("Failed to decode audio response");
                }

                job.MarkComplete(samples, channels, sampleRate);
                TrySaveTtsWav(job, samples, channels, sampleRate);

                if (_config.LogSentences)
                {
                    Debug.Log($"[ParallelTtsPipeline][TTS] Generated {samples.Length} samples in {job.Stopwatch.ElapsedMilliseconds}ms");
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                job.MarkFailed(ex);
                throw;
            }
        }

        private string ResolveRemoteInput(string sourceInput)
        {
            string original = sourceInput ?? string.Empty;
            var formatter = _config.RemoteInputFormatter;

            if (formatter == null)
            {
                return original;
            }

            try
            {
                string formatted = formatter(original, _config.RemoteModel, _config.RemoteVoice);
                if (string.IsNullOrWhiteSpace(formatted))
                {
                    return original;
                }

                return formatted.Trim();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ParallelTtsPipeline] Remote input formatter failed: {ex.Message}");
                return original;
            }
        }

        private async Task<StreamTtsResult> TryStreamTtsForJobAsync(
            TtsJob job,
            OpenAICompatibleClient client,
            OpenAITtsRequest request,
            CancellationToken token)
        {
            bool started = false;
            var buffered = new System.IO.MemoryStream();
            byte[] lastChunk = null;

            int expectedChannels = RemoteDefaultChannels;
            int expectedSampleRate = request.SampleRate > 0 ? request.SampleRate : RemoteDefaultSampleRate;
            byte[] remainder = Array.Empty<byte>();

            try
            {
                await foreach (byte[] chunk in client.StreamSpeechAsync(request, token))
                {
                    token.ThrowIfCancellationRequested();

                    if (chunk == null || chunk.Length == 0)
                    {
                        continue;
                    }

                    var currentChunk = chunk;

                    if (lastChunk != null)
                    {
                        int overlap = FindOverlapLength(lastChunk, currentChunk);
                        int frameBytes = 2 * Math.Max(1, expectedChannels);
                        if (frameBytes > 1)
                        {
                            overlap = (overlap / frameBytes) * frameBytes;
                        }
                        if (overlap == currentChunk.Length)
                        {
                            lastChunk = chunk;
                            continue;
                        }

                        if (overlap > 0)
                        {
                            int deltaLength = currentChunk.Length - overlap;
                            var delta = new byte[deltaLength];
                            Buffer.BlockCopy(currentChunk, overlap, delta, 0, deltaLength);
                            currentChunk = delta;
                        }
                    }

                    lastChunk = chunk;

                    if (!started)
                    {
                        buffered.Write(currentChunk, 0, currentChunk.Length);
                    }

                    if (!TryDecodeAudioPayloadStreaming(
                        currentChunk,
                        expectedChannels,
                        expectedSampleRate,
                        ref remainder,
                        out var samples,
                        out int channels,
                        out int sampleRate))
                    {
                        continue;
                    }

                    expectedChannels = channels;
                    expectedSampleRate = sampleRate;

                    if (!started)
                    {
                        job.BeginStreaming(channels, sampleRate);
                        RegisterStreamingJob(job);
                        started = true;
                    }

                    job.EnqueueStreamChunk(samples);
                }

                if (!started)
                {
                    if (buffered.Length > 0)
                    {
                        byte[] payload = buffered.ToArray();
                        if (TryDecodeAudioPayload(payload, expectedChannels, expectedSampleRate, out var samples, out int channels, out int sampleRate))
                        {
                            job.MarkComplete(samples, channels, sampleRate);
                            TrySaveTtsWav(job, samples, channels, sampleRate);
                            return StreamTtsResult.BufferedComplete;
                        }
                    }

                    return StreamTtsResult.None;
                }

                job.MarkStreamingCompleted();
                return StreamTtsResult.Streamed;
            }
            catch (OperationCanceledException)
            {
                job.MarkFailed(new OperationCanceledException());
                RegisterStreamingJob(job);
                job.MarkStreamingCompleted();
                throw;
            }
            catch (Exception ex)
            {
                if (!started)
                {
                    return StreamTtsResult.None;
                }

                job.MarkFailed(ex);
                job.MarkStreamingCompleted();
                RegisterStreamingJob(job);
                return StreamTtsResult.Streamed;
            }
            finally
            {
                buffered.Dispose();
            }
        }

        private static int FindOverlapLength(byte[] previous, byte[] current)
        {
            if (previous == null || current == null)
            {
                return 0;
            }

            int max = Math.Min(previous.Length, current.Length);
            for (int len = max; len > 0; len--)
            {
                if (SuffixEquals(previous, current, len))
                {
                    return len;
                }
            }

            return 0;
        }

        private static bool SuffixEquals(byte[] previous, byte[] current, int length)
        {
            int start = previous.Length - length;
            for (int i = 0; i < length; i++)
            {
                if (previous[start + i] != current[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void RegisterStreamingJob(TtsJob job)
        {
            if (job.HasStreamingRegistration)
            {
                return;
            }

            if (_completedJobs.TryAdd(job.SequenceNumber, job))
            {
                job.HasStreamingRegistration = true;
            }
        }

        private void TryAddCompletedJob(TtsJob job)
        {
            _completedJobs.TryAdd(job.SequenceNumber, job);
        }
    }
}
#endif
