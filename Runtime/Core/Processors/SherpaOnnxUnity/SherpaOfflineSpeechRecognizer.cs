#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Threading;
using System.Threading.Tasks;
using Eitan.SherpaOnnxUnity.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.SherpaOnnxUnity
{
    /// <summary>
    /// Offline recognizer. Accumulates audio in a lock-free ring buffer while VAD (external) indicates speech,
    /// then runs batch recognition asynchronously on a worker task to keep the reader thread non-blocking.
    /// This version is optimized to avoid GC allocations on the audio thread.
    /// </summary>
    public sealed class SherpaOfflineSpeechRecognizer : AudioReader, IDisposable
    {
        public event Action<string> OnRecognitionResult;

        private readonly SpeechRecognition _svc;
        private readonly CancellationTokenSource _cts = new();
        private readonly SynchronizationContext _main = SynchronizationContext.Current;
        private int _sampleRate;
        private readonly int _maxSegmentSeconds = 15; // safety cap to avoid unbounded growth

        // High-performance, allocation-free ring buffer for audio accumulation.
        private AudioBuffer _audioAccumulator;

        private int _isProcessing; // 0 = idle, 1 = processing

        public SherpaOfflineSpeechRecognizer(SpeechRecognition service) : base(capacitySeconds: 4)
        {
            _svc = service ?? throw new ArgumentNullException(nameof(service));
            if (_svc.IsOnlineModel)
                throw new ArgumentException("Use offline model for offline recognizer.");
        }

        public override void Initialize(AudioState state)
        {
            _sampleRate = state.SampleRate;
            int channels = Math.Max(1, state.ChannelCount);
            int maxSamples = _sampleRate * channels * _maxSegmentSeconds;
            _audioAccumulator = new AudioBuffer(maxSamples);
            base.Initialize(state);
        }

        // This method is called on a background thread by the audio reader.
        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            if (_cts.IsCancellationRequested) return;

            try
            {
                if (!audiobuffer.IsEmpty)
                {
                    _audioAccumulator.Write(audiobuffer);

                    // If the accumulator is saturated, trigger processing immediately.
                    if (_audioAccumulator.IsFull && Interlocked.Exchange(ref _isProcessing, 1) == 0)
                    {
                        ProcessAccumulatedAudio();
                    }
                }
                else if (_audioAccumulator.ReadableCount > 0 && Interlocked.Exchange(ref _isProcessing, 1) == 0)
                {
                    // No new audio in this callback, flush what we have.
                    ProcessAccumulatedAudio();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(SherpaOfflineSpeechRecognizer)}] Error in OnAudioReadAsync: {ex.Message}");
            }
        }

        private void ProcessAccumulatedAudio()
        {
            int count = _audioAccumulator.ReadableCount;
            if (count == 0)
            {
                Volatile.Write(ref _isProcessing, 0);
                return;
            }

            // Create a copy of the audio data for the background task.
            // This is necessary because the audio buffer can be written to while the task is running.
            var dataForTask = new float[count];
            _audioAccumulator.Read(dataForTask);

            _ = Task.Run(async () =>
            {
                try
                {
                    var text = await _svc.SpeechTranscriptionAsync(dataForTask, _sampleRate, _cts.Token).ConfigureAwait(false);
                    if (!_cts.IsCancellationRequested)
                    {
                        _main?.Post(_ => OnRecognitionResult?.Invoke(text ?? string.Empty), null);
                    }
                }
                catch (OperationCanceledException) { /* Expected on dispose */ }
                catch (Exception ex)
                {
                    Debug.LogError($"[{nameof(SherpaOfflineSpeechRecognizer)}] Recognition task failed: {ex.Message}");
                }
                finally
                {
                    Volatile.Write(ref _isProcessing, 0);
                }
            }, _cts.Token);
        }

        public override void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            base.Dispose();
            OnRecognitionResult = null;
        }
    }
}
#endif
