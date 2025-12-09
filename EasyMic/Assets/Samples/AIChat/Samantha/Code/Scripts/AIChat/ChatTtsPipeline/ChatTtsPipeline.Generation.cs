using System;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
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
                        _completedJobs[job.SequenceNumber] = job;
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
                SafeInvoke(() => OnSentenceStarted?.Invoke(job.Sentence));

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

                var request = new OpenAITtsRequest
                {
                    Model = _config.RemoteModel,
                    Voice = _config.RemoteVoice,
                    Input = job.Sentence,
                    ResponseFormat = RemoteFormat
                };

                if (_config.LogSentences)
                {
                    Debug.Log($"[ParallelTtsPipeline][TTS] Generating: {job.Sentence}");
                }

                if (_config.EnableStreamingTts)
                {
                    bool streamed = await TryStreamTtsForJobAsync(job, client, request, token).ConfigureAwait(false);
                    if (streamed)
                    {
                        return false;
                    }
                }

                request.stream = false;
                byte[] audioBytes = await client.CreateSpeechAsync(request, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                if (!TryDecodeAudioPayload(audioBytes, out var samples, out int channels, out int sampleRate))
                {
                    throw new InvalidOperationException("Failed to decode audio response");
                }

                job.MarkComplete(samples, channels, sampleRate);

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

        private async Task<bool> TryStreamTtsForJobAsync(
            TtsJob job,
            OpenAICompatibleClient client,
            OpenAITtsRequest request,
            CancellationToken token)
        {
            bool started = false;

            try
            {
                await foreach (byte[] chunk in client.StreamSpeechAsync(request, token))
                {
                    token.ThrowIfCancellationRequested();

                    if (chunk == null || chunk.Length == 0)
                    {
                        continue;
                    }

                    if (!TryDecodeAudioPayload(chunk, out var samples, out int channels, out int sampleRate))
                    {
                        continue;
                    }

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
                    return false;
                }

                job.MarkStreamingCompleted();
                return true;
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
                    return false;
                }

                job.MarkFailed(ex);
                job.MarkStreamingCompleted();
                RegisterStreamingJob(job);
                return true;
            }
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
