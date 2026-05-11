#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.AudioAnalysis
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using UnityEngine;

    /// <summary>
    /// EasyMic-facing facade for Sherpa offline speaker diarization.
    /// </summary>
    public sealed class SherpaSpeakerDiarizer : IDisposable
    {
        private readonly SpeakerDiarization _speakerDiarization;
        private int _disposed;

        public SherpaSpeakerDiarizer(SpeakerDiarization speakerDiarization)
        {
            _speakerDiarization = speakerDiarization ?? throw new ArgumentNullException(nameof(speakerDiarization));
        }

        public int SampleRate => _speakerDiarization.SampleRate;
        public string EmbeddingModelId => _speakerDiarization.EmbeddingModelId;

        public SpeakerDiarization.Options CurrentOptions => _speakerDiarization.CurrentOptions;

        public void UpdateOptions(SpeakerDiarization.Options options)
        {
            ThrowIfDisposed();
            _speakerDiarization.UpdateOptions(options);
        }

        public Task<SpeakerDiarization.DiarizationSegment[]> DiarizeAsync(
            float[] monoSamples,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _speakerDiarization.DiarizeAsync(monoSamples, cancellationToken);
        }

        public Task<SpeakerDiarization.DiarizationSegment[]> DiarizeAsync(
            float[] monoSamples,
            int sampleRate,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _speakerDiarization.DiarizeAsync(monoSamples, sampleRate, cancellationToken);
        }

        public Task<SpeakerDiarization.DiarizationSegment[]> DiarizeAsync(
            AudioClip clip,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _speakerDiarization.DiarizeAsync(clip, cancellationToken);
        }

        public void Dispose()
        {
            _disposed = 1;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(SherpaSpeakerDiarizer));
            }
        }
    }
}
#endif
