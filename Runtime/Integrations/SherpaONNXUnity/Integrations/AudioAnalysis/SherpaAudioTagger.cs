#if EITAN_SHERPA_ONNX_UNITY_PRESENT

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.AudioAnalysis
{
    using System;
    using System.Threading;
    using Eitan.SherpaONNXUnity.Runtime.Modules;
    using UnityEngine;

    /// <summary>
    /// Streams EasyMic capture chunks into Sherpa audio tagging on a reader worker.
    /// Results are dispatched to the synchronization context captured at construction time.
    /// </summary>
    public sealed class SherpaAudioTagger : AudioReader, IDisposable
    {
        public event Action<AudioTagging.AudioTag[]> OnTagsReady;
        public event Action<Exception> OnTaggingFailed;

        private readonly AudioTagging _tagging;
        private readonly SynchronizationContext _mainThreadContext;
        private readonly int _taggingSampleRate;
        private readonly int _topK;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private float[] _monoBuffer;
        private float[] _resampleBuffer;
        private int _inputSampleRate;
        private int _inputChannelCount;
        private int _disposed;

        public SherpaAudioTagger(AudioTagging tagging, int taggingSampleRate = 16000, int topK = -1)
            : base(capacitySeconds: 2)
        {
            _tagging = tagging ?? throw new ArgumentNullException(nameof(tagging));
            _taggingSampleRate = Math.Max(1, taggingSampleRate);
            _topK = topK;
            _mainThreadContext = SynchronizationContext.Current;
        }

        public override void Initialize(AudioContext state)
        {
            _inputSampleRate = Math.Max(1, state.SampleRate);
            _inputChannelCount = Math.Max(1, state.ChannelCount);
            _tagging.ClearStreamingBuffer();
            base.Initialize(state);
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer)
        {
            if (Volatile.Read(ref _disposed) != 0 || audiobuffer.IsEmpty)
            {
                return;
            }

            var token = _cts.Token;
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                int sampleRate = Math.Max(1, CurrentSampleRate);
                int channels = Math.Max(1, CurrentChannelCount);
                int frameCount = audiobuffer.Length / channels;
                if (frameCount <= 0)
                {
                    return;
                }

                var taggingInput = PrepareTaggingInput(audiobuffer, channels, frameCount, sampleRate);
                if (taggingInput.Length == 0)
                {
                    return;
                }

                var tags = _tagging.TagStreamAsync(taggingInput, _topK, token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();

                if (tags == null || tags.Length == 0 || token.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                PostTags(tags);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                PostFailure(ex);
            }
        }

        public void ResetStreamingBuffer()
        {
            _tagging.ClearStreamingBuffer();
        }

        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
            base.Dispose();
            OnTagsReady = null;
            OnTaggingFailed = null;
        }

        private float[] PrepareTaggingInput(ReadOnlySpan<float> source, int channels, int frameCount, int sampleRate)
        {
            EnsureExactBuffer(ref _monoBuffer, frameCount);
            DownmixToMono(_monoBuffer.AsSpan(0, frameCount), source, channels);

            if (sampleRate == _taggingSampleRate)
            {
                return _monoBuffer;
            }

            int resampledLength = GetResampledLength(frameCount, sampleRate, _taggingSampleRate);
            EnsureExactBuffer(ref _resampleBuffer, resampledLength);
            int actual = Resample(_monoBuffer.AsSpan(0, frameCount), sampleRate, _taggingSampleRate, _resampleBuffer.AsSpan(0, resampledLength));

            if (actual == _resampleBuffer.Length)
            {
                return _resampleBuffer;
            }

            EnsureExactBuffer(ref _resampleBuffer, actual);
            Resample(_monoBuffer.AsSpan(0, frameCount), sampleRate, _taggingSampleRate, _resampleBuffer.AsSpan(0, actual));
            return _resampleBuffer;
        }

        private void PostTags(AudioTagging.AudioTag[] tags)
        {
            if (_mainThreadContext != null)
            {
                _mainThreadContext.Post(_ =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        OnTagsReady?.Invoke(tags);
                    }
                }, null);
                return;
            }

            OnTagsReady?.Invoke(tags);
        }

        private void PostFailure(Exception exception)
        {
            if (_mainThreadContext != null)
            {
                _mainThreadContext.Post(_ =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        OnTaggingFailed?.Invoke(exception);
                    }
                }, null);
                return;
            }

            OnTaggingFailed?.Invoke(exception);
        }

        private static void EnsureExactBuffer(ref float[] buffer, int requiredLength)
        {
            requiredLength = Math.Max(1, requiredLength);
            if (buffer == null || buffer.Length != requiredLength)
            {
                buffer = new float[requiredLength];
            }
        }

        private static void DownmixToMono(Span<float> destination, ReadOnlySpan<float> source, int channels)
        {
            if (channels <= 1)
            {
                source.Slice(0, Math.Min(destination.Length, source.Length)).CopyTo(destination);
                return;
            }

            int frames = Math.Min(destination.Length, source.Length / channels);
            int index = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += source[index++];
                }

                destination[frame] = sum / channels;
            }
        }

        private static int GetResampledLength(int inputLength, int sourceRate, int targetRate)
        {
            return Math.Max(1, (int)MathF.Round(inputLength * targetRate / (float)sourceRate));
        }

        private static int Resample(ReadOnlySpan<float> source, int sourceRate, int targetRate, Span<float> destination)
        {
            if (source.IsEmpty || destination.IsEmpty)
            {
                return 0;
            }

            if (sourceRate == targetRate)
            {
                int count = Math.Min(source.Length, destination.Length);
                source.Slice(0, count).CopyTo(destination);
                return count;
            }

            float ratio = sourceRate / (float)targetRate;
            int outputLength = Math.Min(destination.Length, GetResampledLength(source.Length, sourceRate, targetRate));
            for (int i = 0; i < outputLength; i++)
            {
                float sourcePosition = i * ratio;
                int left = Mathf.Clamp((int)sourcePosition, 0, source.Length - 1);
                int right = Mathf.Min(left + 1, source.Length - 1);
                float t = sourcePosition - left;
                destination[i] = Mathf.Lerp(source[left], source[right], t);
            }

            return outputLength;
        }
    }
}
#endif
