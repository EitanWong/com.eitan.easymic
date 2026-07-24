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

        private Task StartGenerationWorkers(long sessionId, long turnId, CancellationToken token)
        {
            int configured = GetConfigSnapshot().MaxParallelGenerations;
            int workerCount = Math.Max(1, Math.Min(MaxParallelTtsRequests, configured > 0 ? configured : MaxParallelTtsRequests));
            if (workerCount == 1)
            {
                return RunGenerationWorkerAsync(sessionId, turnId, token);
            }

            var workers = new Task[workerCount];
            for (int i = 0; i < workerCount; i++)
            {
                workers[i] = RunGenerationWorkerAsync(sessionId, turnId, token);
            }

            return Task.WhenAll(workers);
        }

        private async Task RunGenerationWorkerAsync(long sessionId, long turnId, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TtsJob job;
                lock (_queueStateLock)
                {
                    if (!IsGenerationActive(sessionId, turnId, token))
                    {
                        break;
                    }

                    _pendingJobs.TryDequeue(out job);
                }

                if (job == null)
                {
                    if (IsTurnInputComplete(turnId))
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(5, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                if (job.TurnId != turnId || !IsGenerationActive(sessionId, turnId, token))
                {
                    TryRequeuePendingJob(sessionId, turnId, job, token);
                    break;
                }

                try
                {
                    await _generationSemaphore.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryRequeuePendingJob(sessionId, turnId, job, token);
                    break;
                }

                try
                {
                    bool enqueue = await GenerateTtsForJobAsync(sessionId, turnId, job, token).ConfigureAwait(false);
                    _resourceMonitor.RecordGeneration(job.Stopwatch.ElapsedMilliseconds);

                    if (enqueue)
                    {
                        TryAddCompletedJob(sessionId, turnId, job, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    job.MarkFailed(new OperationCanceledException());
                    break;
                }
                catch (Exception ex)
                {
                    job.MarkFailed(ex);
                    TryAddCompletedJob(sessionId, turnId, job, token);
                    Debug.LogError($"[ParallelTtsPipeline] Generation failed: {ex.Message}");
                }
                finally
                {
                    _generationSemaphore.Release();
                }
            }
        }

        private async Task<bool> GenerateTtsForJobAsync(
            long sessionId,
            long turnId,
            TtsJob job,
            CancellationToken token)
        {
            try
            {
                TtsPipelineConfig config = GetConfigSnapshot();
                var client = _clientAccessor?.Invoke();
                if (client == null)
                {
                    throw new InvalidOperationException("OpenAI client not available");
                }

                if (string.IsNullOrWhiteSpace(config.RemoteModel) ||
                    string.IsNullOrWhiteSpace(config.RemoteVoice))
                {
                    throw new InvalidOperationException("Remote TTS model or voice not configured");
                }

                string remoteInput = ResolveRemoteInput(job.Sentence, config);

                var request = new OpenAITtsRequest
                {
                    Model = config.RemoteModel,
                    Voice = config.RemoteVoice,
                    Input = remoteInput,
                    ResponseFormat = RemoteFormat,
                    SampleRate = RemoteDefaultSampleRate
                };

                if (config.LogSentences)
                {
                    Debug.Log($"[ParallelTtsPipeline][TTS] Generating: {job.Sentence}");
                }

                if (config.EnableStreamingTts)
                {
                    StreamTtsResult streamed = await TryStreamTtsForJobAsync(
                        sessionId,
                        turnId,
                        job,
                        client,
                        request,
                        token).ConfigureAwait(false);
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
                if (!IsGenerationActive(sessionId, turnId, token))
                {
                    return false;
                }

                int expectedChannels = RemoteDefaultChannels;
                int expectedSampleRate = request.SampleRate > 0 ? request.SampleRate : RemoteDefaultSampleRate;

                if (!TryDecodeAudioPayload(audioBytes, expectedChannels, expectedSampleRate, out var samples, out int channels, out int sampleRate))
                {
                    throw new InvalidOperationException("Failed to decode audio response");
                }

                job.MarkComplete(samples, channels, sampleRate);
                TrySaveTtsWav(job, samples, channels, sampleRate, config);

                if (config.LogSentences)
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

        private string ResolveRemoteInput(string sourceInput, TtsPipelineConfig config)
        {
            string original = sourceInput ?? string.Empty;
            var formatter = config.RemoteInputFormatter;

            if (formatter == null)
            {
                return original;
            }

            try
            {
                string formatted = formatter(original, config.RemoteModel, config.RemoteVoice);
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
            long sessionId,
            long turnId,
            TtsJob job,
            OpenAICompatibleClient client,
            OpenAITtsRequest request,
            CancellationToken token)
        {
            bool started = false;
            var buffered = new System.IO.MemoryStream();

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

                    if (!IsGenerationActive(sessionId, turnId, token))
                    {
                        throw new OperationCanceledException(token);
                    }

                    byte[] currentChunk = chunk;

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
                        RegisterStreamingJob(sessionId, turnId, job, token);
                        if (!job.HasStreamingRegistration)
                        {
                            throw new OperationCanceledException(token);
                        }
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
                            TrySaveTtsWav(job, samples, channels, sampleRate, GetConfigSnapshot());
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
                RegisterStreamingJob(sessionId, turnId, job, token);
                return StreamTtsResult.Streamed;
            }
            finally
            {
                buffered.Dispose();
            }
        }

        private bool IsGenerationActive(long sessionId, long turnId, CancellationToken token)
        {
            return !_disposed &&
                   !token.IsCancellationRequested &&
                   _session.IsCurrent(sessionId) &&
                   turnId == Volatile.Read(ref _activeTurnId);
        }

        private bool TryRequeuePendingJob(
            long sessionId,
            long turnId,
            TtsJob job,
            CancellationToken token)
        {
            if (job == null)
            {
                return false;
            }

            lock (_queueStateLock)
            {
                if (job.TurnId != turnId || !IsGenerationActive(sessionId, turnId, token))
                {
                    return false;
                }

                _pendingJobs.Enqueue(job);
                return true;
            }
        }

        private void RegisterStreamingJob(
            long sessionId,
            long turnId,
            TtsJob job,
            CancellationToken token)
        {
            if (job == null)
            {
                return;
            }

            lock (_queueStateLock)
            {
                if (job.HasStreamingRegistration ||
                    job.TurnId != turnId ||
                    !IsGenerationActive(sessionId, turnId, token))
                {
                    return;
                }

                if (_completedJobs.TryAdd(job.SequenceNumber, job))
                {
                    job.HasStreamingRegistration = true;
                }
            }
        }

        private void TryAddCompletedJob(
            long sessionId,
            long turnId,
            TtsJob job,
            CancellationToken token)
        {
            if (job == null)
            {
                return;
            }

            lock (_queueStateLock)
            {
                if (job.TurnId != turnId || !IsGenerationActive(sessionId, turnId, token))
                {
                    return;
                }

                _completedJobs.TryAdd(job.SequenceNumber, job);
            }
        }
    }
}
#endif
