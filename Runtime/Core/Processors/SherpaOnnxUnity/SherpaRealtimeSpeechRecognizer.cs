#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Threading;
using System.Threading.Tasks;
using Eitan.SherpaOnnxUnity.Runtime;

namespace Eitan.EasyMic.Runtime.SherpaOnnxUnity
{
    /// <summary>
    /// Realtime speech recognizer that consumes audio on a dedicated reader thread
    /// and dispatches transcription work asynchronously, keeping the audio path non-blocking.
    /// </summary>
    public sealed class SherpaRealtimeSpeechRecognizer : AudioReader, IDisposable
    {
        public event Action<string> OnRecognitionResult;

        private readonly SpeechRecognition _svc;
        private readonly CancellationTokenSource _cts = new();
        private readonly SynchronizationContext _main = SynchronizationContext.Current;
        private int _sampleRate;
        private int _inferenceBusy; // 0 = idle, 1 = busy

        // Accumulation buffer to avoid dropping frames under load
        private float[] _acc = Array.Empty<float>();
        private int _accLen = 0;

        public SherpaRealtimeSpeechRecognizer(SpeechRecognition service) : base(capacitySeconds: 2)
        {
            _svc = service ?? throw new ArgumentNullException(nameof(service));
            if (!_svc.IsOnlineModel)
                throw new ArgumentException("Use online model for realtime recognizer.");
        }

        public override void Initialize(AudioState state)
        {
            _sampleRate = state.SampleRate;
            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            if (_cts.IsCancellationRequested) return;
            if (!audiobuffer.IsEmpty)
            {
                EnsureCapacity(_accLen + audiobuffer.Length);
                audiobuffer.CopyTo(new Span<float>(_acc, _accLen, audiobuffer.Length));
                _accLen += audiobuffer.Length;
            }

            if (_accLen == 0) return;

            if (Interlocked.Exchange(ref _inferenceBusy, 1) == 0)
            {
                var data = new float[_accLen];
                Array.Copy(_acc, 0, data, 0, _accLen);
                _accLen = 0;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var text = await _svc.SpeechTranscriptionAsync(data, _sampleRate, _cts.Token).ConfigureAwait(false);
                        if (text != null)
                        {
                            if (_main != null)
                                _main.Post(_ => { if (!_cts.IsCancellationRequested) OnRecognitionResult?.Invoke(text); }, null);
                            else if (!_cts.IsCancellationRequested)
                                OnRecognitionResult?.Invoke(text);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch { }
                    finally
                    {
                        Volatile.Write(ref _inferenceBusy, 0);
                    }
                });
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (_acc.Length >= needed) return;
            int next = Math.Max(needed, Math.Max(8192, _acc.Length * 2));
            Array.Resize(ref _acc, next);
        }

        public override void Dispose()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            base.Dispose();
            OnRecognitionResult = null;
        }
    }
}
#endif
