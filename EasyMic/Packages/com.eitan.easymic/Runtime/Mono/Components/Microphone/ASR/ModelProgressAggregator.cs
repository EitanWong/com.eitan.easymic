#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System.Collections.Generic;
using Eitan.SherpaONNXUnity.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components.ASR
{
    /// <summary>
    /// Aggregates model loading progress feedback from Sherpa-ONNX services.
    /// </summary>
    public sealed class ModelProgressAggregator
    {
        private const float DOWNLOAD_WEIGHT = 0.4f;
        private const float UNCOMPRESS_WEIGHT = 0.2f;
        private const float VERIFY_WEIGHT = 0.25f;
        private const float LOAD_WEIGHT = 0.15f;

        private readonly Dictionary<string, ModelProgressState> _states = new Dictionary<string, ModelProgressState>();
        private int _expectedModelLoads;

        /// <summary>
        /// Sets the expected number of model loads and clears accumulated state.
        /// </summary>
        public void Reset(int expectedModelLoads)
        {
            _states.Clear();
            _expectedModelLoads = Mathf.Max(0, expectedModelLoads);
        }

        /// <summary>
        /// Returns the aggregated loading progress in the range [0, 1].
        /// </summary>
        public float CalculateProgress()
        {
            if (_expectedModelLoads <= 0 || _states.Count == 0)
            {
                return 0f;
            }

            float sum = 0f;
            foreach (var state in _states.Values)
            {
                sum += state.GetProgress();
            }

            return Mathf.Clamp01(sum / Mathf.Max(1, _expectedModelLoads));
        }

        /// <summary>
        /// Marks that preparation of the provided model has started.
        /// </summary>
        public void RegisterPrepare(SherpaONNXModelMetadata metadata, string message)
        {
            var state = GetOrCreateState(metadata, initialize: true);
            state.EnsureStarted();
            state.PrepareMessage = message;
        }

        /// <summary>
        /// Updates download progress.
        /// </summary>
        public void RegisterDownload(SherpaONNXModelMetadata metadata, float progress, string message)
        {
            var state = GetOrCreateState(metadata);
            state.Download = Mathf.Max(state.Download, NormalizeProgress(progress));
            state.DownloadMessage = message;
        }

        /// <summary>
        /// Updates decompression progress.
        /// </summary>
        public void RegisterDecompress(SherpaONNXModelMetadata metadata, float progress, string message)
        {
            var state = GetOrCreateState(metadata);
            state.Uncompress = Mathf.Max(state.Uncompress, NormalizeProgress(progress));
            state.UncompressMessage = message;
        }

        /// <summary>
        /// Updates verification progress.
        /// </summary>
        public void RegisterVerify(SherpaONNXModelMetadata metadata, float progress, string message)
        {
            var state = GetOrCreateState(metadata);
            state.MarkDownloadComplete();
            state.MarkUncompressComplete();
            state.Verify = Mathf.Max(state.Verify, NormalizeProgress(progress));
            state.VerifyMessage = message;
        }

        /// <summary>
        /// Updates loading progress.
        /// </summary>
        public void RegisterLoad(SherpaONNXModelMetadata metadata, string message)
        {
            var state = GetOrCreateState(metadata);
            state.MarkDownloadComplete();
            state.MarkUncompressComplete();
            state.MarkVerifyComplete();
            state.Load = Mathf.Max(state.Load, DetermineLoadStageProgress(message));
            state.LoadMessage = message;
        }

        /// <summary>
        /// Marks the specified model as successfully loaded.
        /// </summary>
        public void RegisterSuccess(SherpaONNXModelMetadata metadata, string message)
        {
            var state = GetOrCreateState(metadata);
            state.MarkComplete();
            state.LoadMessage = message;
        }

        /// <summary>
        /// Ensures that a model entry exists in the aggregator.
        /// </summary>
        public void EnsureModelRegistered(SherpaONNXModelMetadata metadata)
        {
            GetOrCreateState(metadata);
        }

        private ModelProgressState GetOrCreateState(SherpaONNXModelMetadata metadata, bool initialize = false)
        {
            string key = GetModelKey(metadata);
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ModelProgressState();
                _states[key] = state;
            }

            if (initialize)
            {
                state.EnsureStarted();
            }

            return state;
        }

        private static string GetModelKey(SherpaONNXModelMetadata metadata)
        {
            return string.IsNullOrWhiteSpace(metadata?.modelId) ? "unknown" : metadata.modelId;
        }

        private static float NormalizeProgress(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            if (value > 1f && value <= 100f)
            {
                value *= 0.01f;
            }

            return Mathf.Clamp01(value);
        }

        private static float DetermineLoadStageProgress(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return 0f;
            }

            if (message.IndexOf("loaded", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("success", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 1f;
            }

            return 0f;
        }

        private sealed class ModelProgressState
        {
            public bool HasStarted;
            public float Download;
            public float Uncompress;
            public float Verify;
            public float Load;

            public string PrepareMessage;
            public string DownloadMessage;
            public string UncompressMessage;
            public string VerifyMessage;
            public string LoadMessage;

            public void EnsureStarted()
            {
                if (HasStarted)
                {
                    return;
                }

                HasStarted = true;
                Download = 0f;
                Uncompress = 0f;
                Verify = 0f;
                Load = 0f;
            }

            public void MarkDownloadComplete()
            {
                Download = 1f;
            }

            public void MarkUncompressComplete()
            {
                Uncompress = 1f;
            }

            public void MarkVerifyComplete()
            {
                Verify = 1f;
            }

            public void MarkComplete()
            {
                Download = 1f;
                Uncompress = 1f;
                Verify = 1f;
                Load = 1f;
            }

            public float GetProgress()
            {
                float total =
                    DOWNLOAD_WEIGHT * Download +
                    UNCOMPRESS_WEIGHT * Uncompress +
                    VERIFY_WEIGHT * Verify +
                    LOAD_WEIGHT * Load;
                return Mathf.Clamp01(total);
            }
        }
    }
}
#endif
