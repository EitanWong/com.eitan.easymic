#if EASYMIC_SHERPA_ONNX_INTEGRATION

namespace Eitan.EasyMic.Runtime.SherpaOnnxUnity
{
    using System;
    using System.Threading;
    using Eitan.SherpaOnnxUnity.Runtime;
    using UnityEngine;
    /// <summary>
    /// Voice gate using Sherpa-Onnx VAD.
    /// - Buffers a short lead-in to avoid clipping speech onsets.
    /// - Silences while idle; drains buffer when activation occurs.
    /// - Uses high-performance SPSC ring buffers on the audio thread.
    /// </summary>
    public sealed class SherpaVoiceFilter : AudioWriter, IDisposable
    {
        public event Action<bool> OnVoiceActivityChanged;

        // --- Configuration ---
        private readonly float _preBufferDurationInSeconds;
        private readonly int _vadSampleRate;

        // --- Services & Threading ---
        private readonly VoiceActivityDetection _vadService;
        private readonly SynchronizationContext _mainThreadContext;

        // --- VAD State Management ---
        public volatile bool IsVoiceActive;
        private bool _wasVoiceActiveLastFrame;

        // A reusable buffer for feeding audio data to the VAD service to avoid per-frame allocations.
        private float[] _vadBuffer;
        private float[] _monoBuffer;
        private float[] _resampleBuffer;
        private int _inputSampleRate;
        private int _inputChannelCount;
        private bool _requiresResample;

        // --- Buffering for Latency Compensation ---
        private AudioBuffer _preBuffer;
        private AudioBuffer _outBuffer;
        private int _preBufferCapacityInSamples;

        // Reusable buffer for efficiently transferring data between the two ring buffers.
        private float[] _transferBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="SherpaVoiceFilter"/> class.
        /// </summary>
        /// <param name="vadService">The voice activity detection service.</param>
        /// <param name="preBufferDurationInSeconds">The duration of audio to cache before speech starts. This helps prevent clipping the beginning of speech.</param>
        public SherpaVoiceFilter(VoiceActivityDetection vadService, float preBufferDurationInSeconds = 0.3f, int vadSampleRate = 16000)
        {
            _vadService = vadService ?? throw new ArgumentNullException(nameof(vadService));
            _preBufferDurationInSeconds = preBufferDurationInSeconds;
            _vadSampleRate = Math.Max(1, vadSampleRate);

            _mainThreadContext = SynchronizationContext.Current;

            _vadService.OnSpeakingStateChanged += HandleVoiceActivityChanged;
        }

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);
            _inputSampleRate = Math.Max(1, state.SampleRate);
            _inputChannelCount = Math.Max(1, state.ChannelCount);
            _requiresResample = _inputSampleRate != _vadSampleRate;

            // Calculate the total number of samples to hold in the pre-buffer based on the audio settings.
            _preBufferCapacityInSamples = Math.Max(1, (int)(state.SampleRate * state.ChannelCount * _preBufferDurationInSeconds));

            // Initialize ring buffers. Keep capacities tight to minimize post-speech drain latency.
            int tailCapacity = Math.Max(1, (state.SampleRate * state.ChannelCount) / 10); // ≈100 ms tail
            int preBufferCapacity = _preBufferCapacityInSamples;
            int outBufferCapacity = Math.Max(preBufferCapacity + tailCapacity, preBufferCapacity * 2);

            _preBuffer = new AudioBuffer(preBufferCapacity);
            _outBuffer = new AudioBuffer(outBufferCapacity);

            // Pre-allocate the transfer buffer to the maximum possible size to avoid allocations on the hot path.
            _transferBuffer = new float[_preBuffer.Capacity];
        }

        /// <summary>
        /// Processes audio data on the audio thread. It buffers audio during silence and
        /// releases the buffered audio followed by the live audio once speech is detected.
        /// This method is optimized to avoid GC allocations.
        /// </summary>
        protected override void OnAudioWrite(Span<float> audioBuffer, AudioState state)
        {
            if (IsDisposed)
            {
                state.Length = 0;
                return;
            }

            if (_inputSampleRate != state.SampleRate || _inputChannelCount != state.ChannelCount)
            {
                _inputSampleRate = Math.Max(1, state.SampleRate);
                _inputChannelCount = Math.Max(1, state.ChannelCount);
                _requiresResample = _inputSampleRate != _vadSampleRate;
            }

            // 1. Feed the VAD service with the latest audio data.
            if (audioBuffer.Length > 0)
            {
                int channels = Math.Max(1, state.ChannelCount);
                int frameCount = channels > 0 ? audioBuffer.Length / channels : audioBuffer.Length;

                if (frameCount > 0)
                {
                    var vadSpan = PrepareVadInput(audioBuffer, channels, frameCount);
                    if (!vadSpan.IsEmpty)
                    {
                        if (_vadBuffer == null || _vadBuffer.Length != vadSpan.Length)
                        {
                            _vadBuffer = new float[vadSpan.Length];
                        }

                        vadSpan.CopyTo(_vadBuffer);
                        _vadService.StreamDetect(_vadBuffer);
                    }
                }
            }

            // 2. Read the volatile VAD state once for this frame to ensure consistency.
            bool isCurrentlyActive = IsVoiceActive;
            bool justActivated = isCurrentlyActive && !_wasVoiceActiveLastFrame;

            // 3. Collect audio into the appropriate buffer based on VAD state.
            if (isCurrentlyActive)
            {
                // --- Voice is Active ---
                // If speech just started, efficiently move the cached audio from the pre-buffer to the output buffer.
                if (justActivated)
                {
                    int count = _preBuffer.ReadableCount;
                    if (count > 0)
                    {
                        var tempSpan = new Span<float>(_transferBuffer, 0, Math.Min(count, _transferBuffer.Length));
                        int readCount = _preBuffer.Read(tempSpan);
                        if (readCount > 0)
                        {
                            WriteAll(_outBuffer, tempSpan.Slice(0, readCount));
                        }
                    }
                }

                // Add the current audio frame to the output buffer.
                WriteAll(_outBuffer, audioBuffer);
            }
            else
            {
                // --- Voice is Inactive ---
                // We are not speaking, so cache this frame in the pre-buffer, making space if necessary.
                WriteAll(_preBuffer, audioBuffer);
            }

            // 4. Write the collected audio from the output buffer to the actual output audio span.
            int samplesToWrite = Math.Min(audioBuffer.Length, _outBuffer.ReadableCount);
            if (samplesToWrite > 0)
            {
                _outBuffer.Read(audioBuffer.Slice(0, samplesToWrite));
            }

            // If we wrote less than a full buffer, clear the remaining part to prevent stale audio.
            if (samplesToWrite < audioBuffer.Length)
            {
                audioBuffer.Slice(samplesToWrite).Clear();
            }

            // Update the state with the number of samples we actually wrote.
            state.Length = samplesToWrite;

            // 5. Update state for the next frame.
            _wasVoiceActiveLastFrame = isCurrentlyActive;
        }

        private ReadOnlySpan<float> PrepareVadInput(ReadOnlySpan<float> source, int channelCount, int frameCount)
        {
            if (frameCount <= 0)
            {
                return ReadOnlySpan<float>.Empty;
            }

            if (channelCount == 1 && !_requiresResample)
            {
                return source.Slice(0, frameCount);
            }

            EnsureBuffer(ref _monoBuffer, frameCount);
            DownmixToMono(_monoBuffer.AsSpan(0, frameCount), source, channelCount);

            var monoSpan = _monoBuffer.AsSpan(0, frameCount);
            if (!_requiresResample)
            {
                return monoSpan;
            }

            int estimated = GetResampledLength(frameCount, _inputSampleRate, _vadSampleRate);
            EnsureBuffer(ref _resampleBuffer, estimated);
            int actual = Resample(monoSpan, _inputSampleRate, _vadSampleRate, _resampleBuffer.AsSpan());

            return _resampleBuffer.AsSpan(0, actual);
        }

        private static void EnsureBuffer(ref float[] buffer, int requiredLength)
        {
            if (requiredLength <= 0)
            {
                return;
            }

            if (buffer == null || buffer.Length < requiredLength)
            {
                buffer = new float[requiredLength];
            }
        }

        private static void DownmixToMono(Span<float> destination, ReadOnlySpan<float> source, int channelCount)
        {
            if (channelCount <= 1)
            {
                source.Slice(0, destination.Length).CopyTo(destination);
                return;
            }

            int frames = Math.Min(destination.Length, source.Length / channelCount);
            int srcIndex = 0;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    sum += source[srcIndex++];
                }
                destination[frame] = sum / channelCount;
            }
        }

        private static int GetResampledLength(int inputLength, int sourceRate, int targetRate)
        {
            if (inputLength <= 0)
            {
                return 0;
            }

            if (sourceRate == targetRate)
            {
                return inputLength;
            }

            return Math.Max(1, (int)MathF.Round(inputLength * targetRate / (float)sourceRate));
        }

        private static int Resample(ReadOnlySpan<float> source, int sourceRate, int targetRate, Span<float> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            if (sourceRate == targetRate || source.Length <= 1)
            {
                int copyLength = Math.Min(source.Length, destination.Length);
                source.Slice(0, copyLength).CopyTo(destination);
                return copyLength;
            }

            int desiredLength = GetResampledLength(source.Length, sourceRate, targetRate);
            int outputLength = Math.Min(desiredLength, destination.Length);

            double step = sourceRate / (double)targetRate;
            double position = 0d;
            int lastIndex = source.Length - 1;

            for (int i = 0; i < outputLength; i++)
            {
                int index = (int)position;
                double fraction = position - index;

                if (index >= lastIndex)
                {
                    destination[i] = source[lastIndex];
                }
                else
                {
                    float s0 = source[index];
                    float s1 = source[index + 1];
                    destination[i] = s0 + (float)((s1 - s0) * fraction);
                }

                position += step;
            }

            return outputLength;
        }

        private static void WriteAll(AudioBuffer buffer, ReadOnlySpan<float> data)
        {
            if (buffer == null || data.IsEmpty)
            {
                return;
            }

            ReadOnlySpan<float> span = data;
            if (span.Length > buffer.Capacity)
            {
                span = span.Slice(span.Length - buffer.Capacity);
            }

            int offset = 0;
            while (offset < span.Length)
            {
                int written = buffer.Write(span.Slice(offset));
                if (written > 0)
                {
                    offset += written;
                    continue;
                }

                int remaining = span.Length - offset;
                int toSkip = Math.Min(remaining, buffer.ReadableCount);
                if (toSkip <= 0)
                {
                    toSkip = 1;
                }
                buffer.Skip(toSkip);
            }
        }

        #region Event Handlers & Thread Marshalling

        private void HandleVoiceActivityChanged(bool isActive)
        {
            if (IsDisposed)
            {
                return;
            }


            IsVoiceActive = isActive;
            PostVoiceActivityToMainThread(isActive);
        }

        private void PostVoiceActivityToMainThread(bool isActive)
        {
            _mainThreadContext?.Post(_ =>
            {
                if (!IsDisposed)
                {
                    OnVoiceActivityChanged?.Invoke(isActive);
                }

            }, null);
        }

        #endregion

        #region Dispose Pattern

        public override void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }


            _vadService.OnSpeakingStateChanged -= HandleVoiceActivityChanged;
            _preBuffer?.Clear();
            _outBuffer?.Clear();
            _transferBuffer = null;
            _monoBuffer = null;
            _resampleBuffer = null;
            _vadBuffer = null;

            OnVoiceActivityChanged = null;
            base.Dispose();
        }

        #endregion
    }
}
#endif

