#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.AudioProcessing
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using Eitan.SherpaONNXUnity.Runtime.Utilities;
    using UnityEngine;

    /// <summary>
    /// EasyMic-facing facade for Sherpa offline source separation.
    /// This is intentionally not an audio pipeline worker because separation is a long-running offline job.
    /// </summary>
    public sealed class SherpaSourceSeparator : IDisposable
    {
        private readonly SourceSeparation _sourceSeparation;
        private int _disposed;

        public SherpaSourceSeparator(SourceSeparation sourceSeparation)
        {
            _sourceSeparation = sourceSeparation ?? throw new ArgumentNullException(nameof(sourceSeparation));
        }

        public int OutputSampleRate => _sourceSeparation.OutputSampleRate;
        public int NumberOfStems => _sourceSeparation.NumberOfStems;

        public Task<SourceSeparation.Result> SeparateAsync(
            float[] interleavedSamples,
            int channelCount,
            int sampleRate,
            AudioProcessingOptions? outputProcessingOptions = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _sourceSeparation.SeparateAsync(
                interleavedSamples,
                channelCount,
                sampleRate,
                outputProcessingOptions,
                cancellationToken);
        }

        public Task<SourceSeparation.Result> SeparateAsync(
            float[][] channels,
            int sampleRate,
            AudioProcessingOptions? outputProcessingOptions = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _sourceSeparation.SeparateAsync(channels, sampleRate, outputProcessingOptions, cancellationToken);
        }

        public Task<SourceSeparation.Result> SeparateAsync(
            AudioClip clip,
            AudioProcessingOptions? outputProcessingOptions = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _sourceSeparation.SeparateAsync(clip, outputProcessingOptions, cancellationToken);
        }

        public void Dispose()
        {
            _disposed = 1;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(SherpaSourceSeparator));
            }
        }
    }
}
#endif
