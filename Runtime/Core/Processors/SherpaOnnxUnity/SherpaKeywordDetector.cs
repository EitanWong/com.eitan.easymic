#if EASYMIC_SHERPA_ONNX_INTEGRATION

namespace Eitan.EasyMic.Runtime.SherpaOnnxUnity
{
    using System;
    using System.Threading;
    using Eitan.SherpaOnnxUnity.Runtime;
    public class SherpaKeywordDetector : AudioReader, IDisposable
    {

        public event Action<string> OnKeywordDetected;

        private readonly KeywordSpotting _kws;

        private readonly CancellationTokenSource _cts = new();
        private readonly SynchronizationContext _mainThreadContext = SynchronizationContext.Current;

        private int _sampleRate;
        private int _inferenceBusy; // 0 = idle, 1 = busy

        // Accumulation buffer to avoid dropping frames under load
        private float[] _acc = Array.Empty<float>();
        private int _accLen;

        public SherpaKeywordDetector(KeywordSpotting service) : base(capacitySeconds: 2)
        {
            _kws = service ?? throw new ArgumentNullException(nameof(service));
        }

        public override void Initialize(AudioState state)
        {
            _sampleRate = state.SampleRate;
            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

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

            if (Interlocked.Exchange(ref _inferenceBusy, 1) == 0)
            {
                var data = new float[_accLen];
                Array.Copy(_acc, 0, data, 0, _accLen);
                _accLen = 0;

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var keyword = await _kws.DetectAsync(data, _sampleRate, _cts.Token).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(keyword) && !string.IsNullOrWhiteSpace(keyword))
                        {
                            if (_mainThreadContext != null)
                            {
                                _mainThreadContext.Post(_ =>
                                {
                                    if (!_cts.IsCancellationRequested)
                                    {
                                        OnKeywordDetected?.Invoke(keyword);
                                    }
                                }, null);
                            }
                            else if (!_cts.IsCancellationRequested)
                            {
                                OnKeywordDetected?.Invoke(keyword);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        Volatile.Write(ref _inferenceBusy, 0);
                    }
                });
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
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
            }
            OnKeywordDetected = null;
        }
    }
}
#endif