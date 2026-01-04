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

            public double BufferedSeconds => UseBehaviour ? Behaviour.BufferedSeconds : Handle.BufferedSeconds;

            public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
            {
                if (UseBehaviour)
                {
                    Behaviour.Enqueue(samples, count, channels, sampleRate, markEndOfStream);
                }
                else
                {
                    Handle.Enqueue(samples, count, channels, sampleRate, markEndOfStream);
                }
            }

            public void CompleteStream()
            {
                if (UseBehaviour)
                {
                    Behaviour.CompleteStream();
                }
                else
                {
                    Handle.CompleteStream();
                }
            }

            public void Stop()
            {
                if (UseBehaviour)
                {
                    Behaviour.Stop();
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

            if (_mainThreadDispatcher != null)
            {
                _mainThreadDispatcher(() => PreparePlaybackSource(_playbackSource));
            }
            else
            {
                PreparePlaybackSource(_playbackSource);
            }
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

            sink.Enqueue(job.AudioSamples, job.AudioSamples.Length, job.Channels, job.SampleRate, false);

            await WaitForBufferDrainAsync(sessionId, token).ConfigureAwait(false);
        }

        private async Task PlayStreamingJobAsync(long sessionId, TtsJob job, CancellationToken token)
        {
            var sink = EnsurePlaybackHandle(sessionId);
            if (!sink.IsValid)
            {
                Debug.LogWarning("[ParallelTtsPipeline] Invalid playback handle for streaming audio, skipping.");
                return;
            }

            double bufferBudget = Math.Max(0.05, _targetBufferedSeconds);
            int idleCycles = 0;
            const int maxIdleCycles = 300;
            int channels = job.Channels > 0 ? job.Channels : RemoteDefaultChannels;
            int sampleRate = job.SampleRate > 0 ? job.SampleRate : RemoteDefaultSampleRate;

            while (!token.IsCancellationRequested)
            {
                if (job.IsFailed && !job.HasPendingChunks)
                {
                    Debug.LogWarning("[ParallelTtsPipeline] Streaming job failed before completion.");
                    break;
                }

                if (job.TryDequeueStreamChunk(out var samples))
                {
                    await WaitForBufferBudgetAsync(sink, bufferBudget, token).ConfigureAwait(false);
                    sink.Enqueue(samples, samples.Length, channels, sampleRate, false);
                    idleCycles = 0;
                    continue;
                }

                if (job.StreamingCompleted && !job.HasPendingChunks)
                {
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

            await WaitForBufferDrainAsync(sessionId, token).ConfigureAwait(false);
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
                        if (_mainThreadDispatcher != null)
                        {
                            _mainThreadDispatcher(() => PreparePlaybackSource(_playbackSource));
                        }
                        else
                        {
                            PreparePlaybackSource(_playbackSource);
                        }

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

            source.Loop = false;
            if (!source.IsPlaying)
            {
                source.Play();
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

                double buffered = _playbackSink.BufferedSeconds;
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

                if (sink.BufferedSeconds <= budgetSeconds)
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
                        _playbackSink.CompleteStream();
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
                _playbackSink.Stop();
                _playbackSink.CompleteStream();
                _playbackSink.Dispose();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ParallelTtsPipeline] Dispose playback error: {ex.Message}");
            }

            _playbackSink = default;
            _playbackInitialized = false;
            _playbackSessionId = -1;
        }
    }
}
