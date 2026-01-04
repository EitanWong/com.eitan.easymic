using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal sealed partial class ChatTtsPipeline
    {
        private void EnsureOrchestratorRunning()
        {
            if (_config.UseLocalTts)
            {
                return;
            }

            lock (_sessionLock)
            {
                if (!_orchestratorTask.IsCompleted)
                {
                    return;
                }

                _sessionId++;
                var currentSessionId = _sessionId;

                _sessionCts?.Dispose();
                _sessionCts = new CancellationTokenSource();
                var token = _sessionCts.Token;

                _orchestratorTask = RunOrchestratorAsync(currentSessionId, token);
            }
        }

        private async Task RunOrchestratorAsync(long sessionId, CancellationToken token)
        {
            var generationTasks = new List<Task>();
            Task playbackTask = null;

            try
            {
                NotifySpeakingState(true);

                playbackTask = RunPlaybackWorkerAsync(sessionId, token);

                int parallelism = _resourceMonitor.CurrentParallelism;
                if (!_config.UseLocalTts)
                {
                    parallelism = 1;
                }

                for (int i = 0; i < parallelism; i++)
                {
                    generationTasks.Add(RunGenerationWorkerAsync(sessionId, token));
                }

                while (!token.IsCancellationRequested)
                {
                    if (_pendingJobs.IsEmpty &&
                        generationTasks.TrueForAll(t => t.IsCompleted) &&
                        _completedJobs.IsEmpty)
                    {
                        break;
                    }

                    _resourceMonitor.AdjustBasedOnLoad();
                    int targetParallelism = _resourceMonitor.CurrentParallelism;
                    if (!_config.UseLocalTts)
                    {
                        targetParallelism = 1;
                    }

                    generationTasks.RemoveAll(t => t.IsCompleted);

                    while (generationTasks.Count < targetParallelism && !_pendingJobs.IsEmpty)
                    {
                        generationTasks.Add(RunGenerationWorkerAsync(sessionId, token));
                    }

                    try
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (generationTasks.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(generationTasks).ConfigureAwait(false);
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
                CompletePlaybackStream(sessionId);
                NotifySpeakingState(false);
                _resourceMonitor.Reset();
            }
        }
    }
}
