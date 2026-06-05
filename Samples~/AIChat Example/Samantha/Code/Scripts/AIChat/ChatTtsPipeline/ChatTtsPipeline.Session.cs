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
            if (_config.UseLocalTts)
            {
                return;
            }

            _session.EnsureStarted(RunOrchestratorAsync);
        }

        private async Task RunOrchestratorAsync(long sessionId, CancellationToken token)
        {
            Task generationTask = Task.CompletedTask;
            Task playbackTask = null;

            try
            {
                NotifySpeakingState(true);
                playbackTask = RunPlaybackWorkerAsync(sessionId, token);
                generationTask = RunGenerationWorkerAsync(sessionId, token);

                while (!token.IsCancellationRequested)
                {
                    if (_pendingJobs.IsEmpty && generationTask.IsCompleted && _completedJobs.IsEmpty)
                    {
                        break;
                    }

                    if (generationTask.IsCompleted && !_pendingJobs.IsEmpty)
                    {
                        generationTask = RunGenerationWorkerAsync(sessionId, token);
                    }

                    using var reg = token.Register(() => { /* wake on cancel */ });
                    try
                    {
                        await _jobArrivalSignal.WaitAsync(token).ConfigureAwait(false);
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
                CompletePlaybackStream(sessionId);
                NotifySpeakingState(false);
                _resourceMonitor.Reset();
            }
        }
    }
}
#endif
