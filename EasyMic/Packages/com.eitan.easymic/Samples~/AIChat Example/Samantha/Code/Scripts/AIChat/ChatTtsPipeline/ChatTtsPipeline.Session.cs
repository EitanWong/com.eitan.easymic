#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private void EnsureOrchestratorRunning()
        {
            if (GetConfigSnapshot().UseLocalTts)
            {
                return;
            }

            bool started;
            lock (_queueStateLock)
            {
                long turnId = Volatile.Read(ref _activeTurnId);
                if (_disposed || turnId <= 0)
                {
                    return;
                }

                started = _session.EnsureStarted(
                    (sessionId, token) => RunOrchestratorAsync(sessionId, turnId, token));
            }

            if (started)
            {
                Interlocked.Exchange(ref _restartAfterCurrentSessionRequested, 0);
                return;
            }

            RequestRestartAfterCurrentSession();
        }

        private void RequestRestartAfterCurrentSession()
        {
            if (_disposed || Interlocked.Exchange(ref _restartAfterCurrentSessionRequested, 1) == 1)
            {
                return;
            }

            Task currentTask = _session.GetTask();
            if (currentTask == null || currentTask.IsCompleted)
            {
                Interlocked.Exchange(ref _restartAfterCurrentSessionRequested, 0);
                EnsureOrchestratorRunning();
                return;
            }

            _ = RestartAfterCurrentSessionAsync(currentTask);
        }

        private async Task RestartAfterCurrentSessionAsync(Task currentTask)
        {
            try
            {
                await currentTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ParallelTtsPipeline] Previous session finished with error before restart: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _restartAfterCurrentSessionRequested, 0);
                if (!_disposed && !GetConfigSnapshot().UseLocalTts && (!_pendingJobs.IsEmpty || !_completedJobs.IsEmpty))
                {
                    EnsureOrchestratorRunning();
                }
            }
        }

        private async Task RunOrchestratorAsync(long sessionId, long turnId, CancellationToken token)
        {
            Task generationTask = Task.CompletedTask;
            Task playbackTask = null;

            try
            {
                NotifySpeakingState(turnId, true);
                generationTask = StartGenerationWorkers(sessionId, turnId, token);
                playbackTask = RunPlaybackWorkerAsync(
                    sessionId,
                    turnId,
                    () => !IsTurnInputComplete(turnId) || !generationTask.IsCompleted || !_pendingJobs.IsEmpty,
                    token);

                while (!token.IsCancellationRequested)
                {
                    if (generationTask.IsCompleted && !_pendingJobs.IsEmpty)
                    {
                        generationTask = StartGenerationWorkers(sessionId, turnId, token);
                    }

                    if (IsTurnInputComplete(turnId) &&
                        _pendingJobs.IsEmpty &&
                        generationTask.IsCompleted &&
                        _completedJobs.IsEmpty &&
                        (playbackTask == null || playbackTask.IsCompleted))
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
                }

                if (generationTask != null)
                {
                    try
                    {
                        await generationTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ParallelTtsPipeline] Generation task error: {ex.Message}");
                    }
                }

                if (playbackTask != null)
                {
                    try
                    {
                        await playbackTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ParallelTtsPipeline] Playback task error: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ParallelTtsPipeline] Orchestrator error: {ex}");
            }
            finally
            {
                SignalPlaybackStreamComplete(sessionId);
                await WaitForBufferDrainAsync(sessionId, turnId, token).ConfigureAwait(false);
                ReleaseCompletedPlaybackStream(sessionId);
                NotifySpeakingState(turnId, false);
                _resourceMonitor.Reset();
            }
        }
    }
}
#endif
