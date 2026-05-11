#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.AudioAnalysis
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using UnityEngine;

    /// <summary>
    /// EasyMic-facing facade for Sherpa spoken language identification.
    /// </summary>
    public sealed class SherpaSpokenLanguageIdentifier : IDisposable
    {
        private readonly SpokenLanguageIdentification _identifier;
        private int _disposed;

        public SherpaSpokenLanguageIdentifier(SpokenLanguageIdentification identifier)
        {
            _identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        }

        public int SampleRate => _identifier.SampleRate;

        public Task<string> IdentifyAsync(float[] monoSamples, int sampleRate, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _identifier.IdentifyAsync(monoSamples, sampleRate, cancellationToken);
        }

        public Task<string> IdentifyAsync(AudioClip clip, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _identifier.IdentifyAsync(clip, cancellationToken);
        }

        public void Dispose()
        {
            _disposed = 1;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(SherpaSpokenLanguageIdentifier));
            }
        }
    }
}
#endif
