#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading;
using System.Threading.Tasks;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.Components;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private struct PlaybackSink
        {
            public PlaybackHandle Handle;
            public PlaybackAudioSourceBehaviour Behaviour;
            public bool UseBehaviour;

            public bool IsValid => UseBehaviour ? Behaviour != null : Handle.IsValid;

            public double BufferedSeconds
            {
                get
                {
                    if (!IsValid)
                    {
                        return 0d;
                    }

                    if (!UseBehaviour)
                    {
                        return Handle.BufferedSeconds;
                    }

                    try
                    {
                        return Behaviour.BufferedSeconds;
                    }
                    catch (Exception)
                    {
                        return 0d;
                    }
                }
            }

            public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
            {
                if (!IsValid)
                {
                    return;
                }

                if (UseBehaviour)
                {
                    try
                    {
                        Behaviour.Enqueue(samples, count, channels, sampleRate, markEndOfStream);
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    Handle.Enqueue(samples, count, channels, sampleRate, markEndOfStream);
                }
            }

            public void CompleteStream()
            {
                if (!IsValid)
                {
                    return;
                }

                if (UseBehaviour)
                {
                    try
                    {
                        Behaviour.CompleteStream();
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    Handle.CompleteStream();
                }
            }

            public void Stop()
            {
                if (!IsValid)
                {
                    return;
                }

                if (UseBehaviour)
                {
                    try
                    {
                        Behaviour.Stop();
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    Handle.Stop();
                }
            }

            public void Dispose()
            {
                if (!UseBehaviour && Handle.IsValid)
                {
                    Handle.Dispose();
                }
            }
        }

        private void ConfigurePlayback(TtsPipelineConfig config)
        {
            lock (_playbackLock)
            {
                _playbackSource = config.PlaybackSource;
                DisposePlaybackUnsafe();
            }

            if (_playbackSource == null)
            {
                return;
            }

            DispatchToMainThread(() => PreparePlaybackSource(_playbackSource), waitForCompletion: false);
        }

        private async Task RunPlaybackWorkerAsync(long sessionId, CancellationToken token)
        {
            int stagnantCycles = 0;
            const int maxStagnantCycles = 200;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    int expectedSeq = _nextPlaybackSequence + 1;

                    if (_completedJobs.TryRemove(expectedSeq, out var job))
                    {
                        stagnantCycles = 0;
                        _nextPlaybackSequence = expectedSeq;

                        if (job.IsStreaming)
                        {
                            await PlayStreamingJobAsync(sessionId, job, token).ConfigureAwait(false);
                        }
                        else if (job.IsComplete && job.AudioSamples != null && job.AudioSamples.Length > 0)
                        {
                            await PlayJobAudioAsync(sessionId, job, token).ConfigureAwait(false);
                        }
                        else if (job.IsFailed)
                        {
                            Debug.LogWarning($"[ParallelTtsPipeline] Skipping failed job: {job.Error?.Message}");
                        }

                        ReleaseInFlightSentence(job.Sentence);
                        SafeInvoke(() => OnSentenceCompleted?.Invoke(job.Sentence));
                    }
                    else
                    {
                        bool generationDone = _pendingJobs.IsEmpty;
                        bool noMoreCompleted = _completedJobs.IsEmpty;

                        if (generationDone && noMoreCompleted)
                        {
                            stagnantCycles++;
                            if (stagnantCycles > maxStagnantCycles)
                            {
                                break;
                            }
                        }
                        else
                        {
                            stagnantCycles = 0;
                        }

                        try
                        {
                            await Task.Delay(10, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task PlayJobAudioAsync(long sessionId, TtsJob job, CancellationToken token)
        {
            var sink = EnsurePlaybackHandle(sessionId);
            if (!sink.IsValid)
            {
                Debug.LogWarning("[ParallelTtsPipeline] Invalid playback handle, skipping audio");
                return;
            }

            SafeInvoke(() => OnSentenceStarted?.Invoke(job.Sentence));

            int channels = Math.Max(1, job.Channels);
            int sampleRate = Math.Max(1, job.SampleRate);
            int chunkSamples = Math.Max(channels, (int)Math.Ceiling(BufferedPlaybackChunkSeconds * channels * sampleRate));
            chunkSamples -= chunkSamples % channels;
            if (chunkSamples <= 0)
            {
                chunkSamples = job.AudioSamples.Length;
            }

            float[] chunkBuffer = null;
            for (int offset = 0; offset < job.AudioSamples.Length && !token.IsCancellationRequested; offset += chunkSamples)
            {
                int count = Math.Min(chunkSamples, job.AudioSamples.Length - offset);
                double bufferBudget = GetAdaptiveBufferBudgetSeconds();
                await WaitForBufferBudgetAsync(sink, bufferBudget, token).ConfigureAwait(false);
                chunkBuffer = CopyChunk(job.AudioSamples, offset, count, ref chunkBuffer);
                EnqueueSinkSamples(sink, chunkBuffer, count, channels, sampleRate, false);
                ReportPlaybackBuffer(GetSinkBufferedSeconds(sink), trackAdaptive: false);
            }
        }

        private async Task PlayStreamingJobAsync(long sessionId, TtsJob job, CancellationToken token)
        {
            var sink = EnsurePlaybackHandle(sessionId);
            if (!sink.IsValid)
            {
                Debug.LogWarning("[ParallelTtsPipeline] Invalid playback handle for streaming audio, skipping.");
                return;
            }

            int idleCycles = 0;
            const int maxIdleCycles = 300;
            int channels = job.Channels > 0 ? job.Channels : RemoteDefaultChannels;
            int sampleRate = job.SampleRate > 0 ? job.SampleRate : RemoteDefaultSampleRate;
            bool warnedAboutStall = false;
            bool sentenceStarted = false;
            float[] pendingBatch = null;
            int pendingBatchCount = 0;

            while (!token.IsCancellationRequested)
            {
                if (job.IsFailed && !job.HasPendingChunks)
                {
                    Debug.LogWarning("[ParallelTtsPipeline] Streaming job failed before completion.");
                    break;
                }

                if (job.TryDequeueStreamChunk(out var samples))
                {
                    AppendBatchSamples(ref pendingBatch, ref pendingBatchCount, samples);
                    double batchedSeconds = channels > 0 && sampleRate > 0
                        ? (double)pendingBatchCount / (channels * sampleRate)
                        : 0d;
                    bool shouldFlushNow = sentenceStarted
                        ? batchedSeconds >= MinimumStreamingChunkSeconds || job.StreamingCompleted
                        : batchedSeconds >= InitialStreamingPrebufferSeconds || job.StreamingCompleted;
                    if (shouldFlushNow)
                    {
                        pendingBatch = await FlushStreamingBatchAsync(
                            sink,
                            pendingBatch,
                            pendingBatchCount,
                            channels,
                            sampleRate,
                            sentenceStarted,
                            job.Sentence,
                            token).ConfigureAwait(false);

                        pendingBatchCount = 0;
                        sentenceStarted = true;
                    }

                    warnedAboutStall = false;
                    idleCycles = 0;
                    continue;
                }

                if (pendingBatchCount > 0 &&
                    (job.StreamingCompleted || job.GetIdleDuration().TotalMilliseconds >= StreamingFlushTimeoutMs))
                {
                    pendingBatch = await FlushStreamingBatchAsync(
                        sink,
                        pendingBatch,
                        pendingBatchCount,
                        channels,
                        sampleRate,
                        sentenceStarted,
                        job.Sentence,
                        token).ConfigureAwait(false);

                    pendingBatchCount = 0;
                    sentenceStarted = true;
                }

                if (job.StreamingCompleted && !job.HasPendingChunks && pendingBatchCount == 0)
                {
                    break;
                }

                double buffered = GetSinkBufferedSeconds(sink);
                ReportPlaybackBuffer(buffered, trackAdaptive: true);

                TimeSpan idleDuration = job.GetIdleDuration();
                double idleSeconds = idleDuration.TotalSeconds;
                if (!warnedAboutStall && idleSeconds >= StreamingStallWarningSeconds)
                {
                    string phase = sentenceStarted ? "after audio start" : "before first chunk";
                    Debug.LogWarning($"[ParallelTtsPipeline] Streaming playback waiting for TTS chunks ({phase}, idle {idleSeconds:0.00}s).");
                    warnedAboutStall = true;
                }

                if (idleSeconds >= StreamingStallAbortSeconds)
                {
                    Debug.LogWarning("[ParallelTtsPipeline] Streaming playback aborted after prolonged chunk starvation.");
                    break;
                }

                idleCycles++;
                if (idleCycles > maxIdleCycles)
                {
                    Debug.LogWarning("[ParallelTtsPipeline] Streaming playback stalled, forcing completion.");
                    break;
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (pendingBatchCount > 0)
            {
                pendingBatch = await FlushStreamingBatchAsync(
                    sink,
                    pendingBatch,
                    pendingBatchCount,
                    channels,
                    sampleRate,
                    sentenceStarted,
                    job.Sentence,
                    token).ConfigureAwait(false);

                pendingBatchCount = 0;
            }
        }

        private PlaybackSink EnsurePlaybackHandle(long sessionId)
        {
            lock (_playbackLock)
            {
                if (_playbackInitialized && _playbackSink.IsValid && _playbackSessionId == sessionId)
                {
                    return _playbackSink;
                }

                DisposePlaybackUnsafe();

                try
                {
                    if (_playbackSource != null)
                    {
                        DispatchToMainThread(() => PreparePlaybackSource(_playbackSource), waitForCompletion: true);

                        _playbackSink = new PlaybackSink
                        {
                            Behaviour = _playbackSource,
                            Handle = default,
                            UseBehaviour = true
                        };
                    }
                    else
                    {
                        float volume = _config.PlaybackVolume > 0 ? _config.PlaybackVolume : 1f;
                        var handle = AudioPlayback.CreateStream(volume: volume);
                        _playbackSink = new PlaybackSink
                        {
                            Behaviour = null,
                            Handle = handle,
                            UseBehaviour = false
                        };
                    }

                    _playbackInitialized = _playbackSink.IsValid;
                    _playbackSessionId = sessionId;

                    if (!_playbackSink.IsValid)
                    {
                        Debug.LogError("[ParallelTtsPipeline] Failed to create valid playback stream");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ParallelTtsPipeline] Playback creation failed: {ex.Message}");
                    _playbackSink = default;
                    _playbackInitialized = false;
                }

                return _playbackSink;
            }
        }

        private static void PreparePlaybackSource(PlaybackAudioSourceBehaviour source)
        {
            if (source == null)
            {
                return;
            }

            try
            {
                source.Loop = false;
                if (!source.IsPlaying)
                {
                    source.Play();
                }
            }
            catch (UnityEngine.MissingReferenceException)
            {
            }
        }

        private async Task WaitForBufferDrainAsync(long sessionId, CancellationToken token)
        {
            int stagnantCount = 0;
            double lastBuffered = double.MaxValue;
            const int maxStagnant = 100;

            while (!token.IsCancellationRequested)
            {
                lock (_playbackLock)
                {
                    if (_playbackSessionId != sessionId || !_playbackInitialized)
                    {
                        break;
                    }
                }

                if (!_playbackSink.IsValid)
                {
                    break;
                }

                double buffered = GetSinkBufferedSeconds(_playbackSink);
                SafeInvoke(() => OnBufferProgress?.Invoke((float)buffered));

                if (buffered <= PlaybackDrainEpsilon)
                {
                    break;
                }

                if (System.Math.Abs(buffered - lastBuffered) < 0.001)
                {
                    stagnantCount++;
                    if (stagnantCount > maxStagnant)
                    {
                        Debug.LogWarning("[ParallelTtsPipeline] Playback stagnation detected");
                        break;
                    }
                }
                else
                {
                    stagnantCount = 0;
                    lastBuffered = buffered;
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task WaitForBufferBudgetAsync(PlaybackSink sink, double budgetSeconds, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!sink.IsValid)
                {
                    break;
                }

                if (GetSinkBufferedSeconds(sink) <= budgetSeconds)
                {
                    break;
                }

                try
                {
                    await Task.Delay(PlaybackPollDelayMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void CompletePlaybackStream(long sessionId)
        {
            lock (_playbackLock)
            {
                if (_playbackSessionId != sessionId || !_playbackInitialized)
                {
                    return;
                }

                try
                {
                    if (_playbackSink.IsValid)
                    {
                        CompleteSinkStream(_playbackSink);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ParallelTtsPipeline] Complete stream error: {ex.Message}");
                }

                _playbackInitialized = false;
            }
        }

        private void DisposePlaybackUnsafe()
        {
            if (!_playbackInitialized)
            {
                return;
            }

            try
            {
                StopSink(_playbackSink);
                CompleteSinkStream(_playbackSink);
                DisposeSink(_playbackSink);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ParallelTtsPipeline] Dispose playback error: {ex.Message}");
            }

            _playbackSink = default;
            _playbackInitialized = false;
            _playbackSessionId = -1;
        }

        private double GetSinkBufferedSeconds(PlaybackSink sink)
        {
            if (!sink.IsValid)
            {
                return 0d;
            }
            return sink.BufferedSeconds;
        }

        private void EnqueueSinkSamples(PlaybackSink sink, float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
        {
            if (!sink.IsValid || samples == null || count <= 0)
            {
                return;
            }
            sink.Enqueue(samples, count, channels, sampleRate, markEndOfStream);
        }

        private async Task<float[]> FlushStreamingBatchAsync(
            PlaybackSink sink,
            float[] pendingBatch,
            int pendingBatchCount,
            int channels,
            int sampleRate,
            bool sentenceStarted,
            string sentence,
            CancellationToken token)
        {
            if (pendingBatchCount <= 0 || !sink.IsValid)
            {
                return pendingBatch;
            }

            double bufferBudget = GetAdaptiveBufferBudgetSeconds();
            await WaitForBufferBudgetAsync(sink, bufferBudget, token).ConfigureAwait(false);

            if (!sentenceStarted)
            {
                SafeInvoke(() => OnSentenceStarted?.Invoke(sentence));
            }

            EnqueueSinkSamples(sink, pendingBatch, pendingBatchCount, channels, sampleRate, false);
            ReportPlaybackBuffer(GetSinkBufferedSeconds(sink), trackAdaptive: true);

            return null;
        }

        private void ReportPlaybackBuffer(double bufferedSeconds, bool trackAdaptive)
        {
            SafeInvoke(() => OnBufferProgress?.Invoke((float)bufferedSeconds));

            if (!trackAdaptive)
            {
                return;
            }

            if (bufferedSeconds <= AdaptiveUnderrunThresholdSeconds)
            {
                ReportAdaptiveUnderrun();
            }
            else
            {
                ReportAdaptiveStability(bufferedSeconds);
            }
        }

        private static void AppendBatchSamples(ref float[] batch, ref int batchCount, float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return;
            }

            int required = batchCount + samples.Length;
            if (batch == null || batch.Length < required)
            {
                int newSize = batch == null ? required : Math.Max(required, batch.Length * 2);
                var expanded = new float[newSize];
                if (batchCount > 0 && batch != null)
                {
                    Array.Copy(batch, 0, expanded, 0, batchCount);
                }

                batch = expanded;
            }

            Array.Copy(samples, 0, batch, batchCount, samples.Length);
            batchCount += samples.Length;
        }

        private static float[] CopyChunk(float[] source, int offset, int count, ref float[] buffer)
        {
            if (buffer == null || buffer.Length < count)
            {
                buffer = new float[count];
            }

            Array.Copy(source, offset, buffer, 0, count);
            return buffer;
        }

        private void StopSink(PlaybackSink sink)
        {
            if (!sink.IsValid)
            {
                return;
            }

            if (!sink.UseBehaviour)
            {
                sink.Stop();
                return;
            }

            DispatchToMainThread(() =>
            {
                if (!sink.IsValid)
                {
                    return;
                }

                sink.Stop();
            }, waitForCompletion: true);
        }

        private void CompleteSinkStream(PlaybackSink sink)
        {
            if (!sink.IsValid)
            {
                return;
            }

            if (!sink.UseBehaviour)
            {
                sink.CompleteStream();
                return;
            }

            DispatchToMainThread(() =>
            {
                if (!sink.IsValid)
                {
                    return;
                }

                sink.CompleteStream();
            }, waitForCompletion: true);
        }

        private void DisposeSink(PlaybackSink sink)
        {
            if (!sink.UseBehaviour)
            {
                sink.Dispose();
                return;
            }

            DispatchToMainThread(sink.Dispose, waitForCompletion: true);
        }
    }
}
#endif
