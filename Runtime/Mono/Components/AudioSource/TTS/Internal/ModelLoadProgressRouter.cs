using System;
using Eitan.SherpaONNXUnity.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components.TTS.Internal
{
    internal sealed class ModelLoadProgressRouter : ISherpaFeedbackHandler
    {
        public event Action<string, float> OnProgress;
        public event Action<FailedFeedback> OnFailed;
        public event Action<SuccessFeedback> OnSuccess;

        private enum LoadStage { Unknown, Prepare, Download, Uncompress, Verify, Clean, Load, Cancel, Success, Failed }
        private readonly object _progressLock = new object();
        private LoadStage _currentStage = LoadStage.Unknown;
        private float _currentProgress;

        private static readonly (float min, float max) PrepareRange = (0.00f, 0.02f);
        private static readonly (float min, float max) DownloadRange = (0.02f, 0.60f);
        private static readonly (float min, float max) UncompressRange = (0.60f, 0.75f);
        private static readonly (float min, float max) VerifyRange = (0.75f, 0.85f);
        private static readonly (float min, float max) CleanRange = (0.85f, 0.95f);
        private static readonly (float min, float max) LoadRange = (0.95f, 0.99f);

        private static float MapStageProgress(LoadStage stage, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            (float min, float max) r = stage switch
            {
                LoadStage.Prepare => PrepareRange,
                LoadStage.Download => DownloadRange,
                LoadStage.Uncompress => UncompressRange,
                LoadStage.Verify => VerifyRange,
                LoadStage.Clean => CleanRange,
                LoadStage.Load => LoadRange,
                LoadStage.Success => (1f, 1f),
                _ => (0f, 0f)
            };
            return Mathf.Approximately(r.min, r.max) ? r.min : r.min + (r.max - r.min) * fraction;
        }

        private void PublishProgress(SherpaFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }

            var typeName = feedback.GetType().Name;
            LoadStage stage = typeName switch
            {
                var s when s.Contains("Prepare") => LoadStage.Prepare,
                var s when s.Contains("Download") => LoadStage.Download,
                var s when s.Contains("Uncompress") || s.Contains("Unzip") => LoadStage.Uncompress,
                var s when s.Contains("Verify") => LoadStage.Verify,
                var s when s.Contains("Clean") => LoadStage.Clean,
                var s when s.Contains("Load") => LoadStage.Load,
                var s when s.Contains("Cancel") => LoadStage.Cancel,
                var s when s.Contains("Success") => LoadStage.Success,
                var s when s.Contains("Failed") || s.Contains("Error") => LoadStage.Failed,
                _ => LoadStage.Unknown
            };

            float stageFraction = stage switch
            {
                LoadStage.Download => 0.25f,
                LoadStage.Uncompress or LoadStage.Verify or LoadStage.Clean => 0.5f,
                LoadStage.Load => 0.9f,
                _ => 0f
            };

            float globalProgress = MapStageProgress(stage, stageFraction);

            lock (_progressLock)
            {
                if (stage == LoadStage.Success)
                {
                    _currentProgress = 1f;
                    _currentStage = LoadStage.Success;
                }
                else if (stage == LoadStage.Failed)
                {
                    _currentStage = LoadStage.Failed;
                }
                else if (stage == LoadStage.Cancel)
                {
                    _currentStage = LoadStage.Cancel;
                }
                else if (globalProgress >= _currentProgress || stage != _currentStage)
                {
                    _currentProgress = Mathf.Clamp01(globalProgress);
                    _currentStage = stage;
                }
            }
            OnProgress?.Invoke(feedback.Message ?? feedback.ToString(), _currentProgress);
        }

        public void OnFeedback(PrepareFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(DownloadFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(DecompressFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(VerifyFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(CleanFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(LoadFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(CancelFeedback feedback) => PublishProgress(feedback);

        public void OnFeedback(SuccessFeedback feedback)
        {
            lock (_progressLock)
            {
                _currentProgress = 1f;
                _currentStage = LoadStage.Success;
            }
            OnProgress?.Invoke(feedback?.Message ?? "Loaded", 1f);
            OnSuccess?.Invoke(feedback);
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            lock (_progressLock)
            {
                _currentStage = LoadStage.Failed;
            }
            OnProgress?.Invoke(feedback?.Message ?? "Failed", _currentProgress);
            OnFailed?.Invoke(feedback);
        }
    }
}
