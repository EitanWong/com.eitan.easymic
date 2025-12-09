#if EASYMIC_SHERPA_ONNX_INTEGRATION

namespace Eitan.EasyMic.Runtime.SherpaONNXUnity
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Eitan.SherpaONNXUnity.Runtime.Modules;

    /// <summary>
    /// Realtime speech recognizer that consumes audio on a dedicated reader thread
    /// and dispatches transcription work asynchronously, keeping the audio path non-blocking.
    /// </summary>
    public sealed class SherpaRealtimeSpeechRecognizer : AudioReader, IDisposable
    {
        public event Action<string> OnRecognitionResult;

        private readonly SpeechRecognition _svc;
        private readonly SynchronizationContext _mainThreadContext = SynchronizationContext.Current;

        private CancellationTokenSource _cts;
        private int _sampleRate;
        private int _inferenceBusy;  // 0 = idle, 1 = busy
        private int _disposed;       // 0 = active, 1 = disposed

        // Accumulation buffer to avoid dropping frames under load
        private float[] _acc = Array.Empty<float>();
        private int _accLen;

        public SherpaRealtimeSpeechRecognizer(SpeechRecognition service) : base(capacitySeconds: 2)
        {
            _svc = service ?? throw new ArgumentNullException(nameof(service));
            if (!_svc.IsOnlineModel)
            {
                throw new ArgumentException("Use online model for realtime recognizer.");
            }
            _cts = new CancellationTokenSource();
        }

        public override void Initialize(AudioContext state)
        {
            _sampleRate = state.SampleRate;
            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            // Early exit if disposed
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            // Capture CTS reference locally to avoid race conditions
            var cts = Volatile.Read(ref _cts);
            if (cts == null)
            {
                return;
            }

            // Try to get token safely
            CancellationToken token;
            try
            {
                token = cts.Token;
                if (token.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            // Accumulate audio data
            if (!audiobuffer.IsEmpty)
            {
                EnsureCapacity(_accLen + audiobuffer.Length);
                audiobuffer.CopyTo(new Span<float>(_acc, _accLen, audiobuffer.Length));
                _accLen += audiobuffer.Length;
            }

            if (_accLen == 0)
            {
                return;
            }

            // Try to start inference if not already busy
            if (Interlocked.Exchange(ref _inferenceBusy, 1) == 0)
            {
                // Copy accumulated data
                var data = new float[_accLen];
                Array.Copy(_acc, 0, data, 0, _accLen);
                _accLen = 0;

                // Capture context for the async task
                var context = _mainThreadContext;
                var sampleRate = _sampleRate;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var text = await _svc.SpeechTranscriptionAsync(data, sampleRate, token)
                            .ConfigureAwait(false);

                        // Skip if disposed or cancelled or empty result
                        if (Volatile.Read(ref _disposed) == 1 ||
                            token.IsCancellationRequested ||
                            string.IsNullOrEmpty(text))
                        {
                            return;
                        }

                        // Dispatch result to main thread
                        if (context != null)
                        {
                            context.Post(_ =>
                            {
                                if (Volatile.Read(ref _disposed) == 0)
                                {
                                    OnRecognitionResult?.Invoke(text);
                                }
                            }, null);
                        }
                        else
                        {
                            OnRecognitionResult?.Invoke(text);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected if CTS disposed during operation
                    }
                    catch (Exception)
                    {
                        // Swallow other exceptions to prevent crashes
                    }
                    finally
                    {
                        Volatile.Write(ref _inferenceBusy, 0);
                    }
                }, CancellationToken.None);  // Use None here to ensure finally block runs
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (_acc.Length >= needed)
            {
                return;
            }

            int next = Math.Max(needed, Math.Max(8192, _acc.Length * 2));
            Array.Resize(ref _acc, next);
        }

        public override void Dispose()
        {
            // Ensure single disposal using atomic exchange
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return; // Already disposed
            }

            // Atomically swap out the CTS
            var cts = Interlocked.Exchange(ref _cts, null);
            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }

                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
            }

            base.Dispose();
            OnRecognitionResult = null;
        }
    }
}
#endif
