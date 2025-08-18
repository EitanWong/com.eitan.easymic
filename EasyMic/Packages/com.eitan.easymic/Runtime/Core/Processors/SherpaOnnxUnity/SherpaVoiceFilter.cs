#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Threading;
using Eitan.SherpaOnnxUnity.Runtime;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.SherpaOnnxUnity
{
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

        // --- Services & Threading ---
        private readonly VoiceActivityDetection _vadService;
        private readonly SynchronizationContext _mainThreadContext;

        // --- VAD State Management ---
        public volatile bool IsVoiceActive;
        private bool _wasVoiceActiveLastFrame;
        
        // A reusable buffer for feeding audio data to the VAD service to avoid per-frame allocations.
        private float[] _vadBuffer;

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
        public SherpaVoiceFilter(VoiceActivityDetection vadService, float preBufferDurationInSeconds = 0.3f)
        {
            _vadService = vadService ?? throw new ArgumentNullException(nameof(vadService));
            _preBufferDurationInSeconds = preBufferDurationInSeconds;
            
            _mainThreadContext = SynchronizationContext.Current;
            
            _vadService.OnSpeakingStateChanged += HandleVoiceActivityChanged;
        }

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);
            // Calculate the total number of samples to hold in the pre-buffer based on the audio settings.
            _preBufferCapacityInSamples = (int)(state.SampleRate * state.ChannelCount * _preBufferDurationInSeconds);

            // Initialize ring buffers. Output buffer is larger to accommodate pre-buffer + live audio.
            _preBuffer = new AudioBuffer(_preBufferCapacityInSamples);
            _outBuffer = new AudioBuffer(_preBufferCapacityInSamples * 2);
            
            // Pre-allocate the transfer buffer to the maximum possible size to avoid allocations on the hot path.
            _transferBuffer = new float[_preBufferCapacityInSamples];
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

            // 1. Feed the VAD service with the latest audio data.
            if (audioBuffer.Length > 0)
            {
                if (_vadBuffer == null || _vadBuffer.Length != audioBuffer.Length)
                {
                    _vadBuffer = new float[audioBuffer.Length];
                }
                audioBuffer.CopyTo(_vadBuffer);
                _vadService.StreamDetect(_vadBuffer);
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
                        var tempSpan = new Span<float>(_transferBuffer, 0, count);
                        int readCount = _preBuffer.Read(tempSpan);
                        if (readCount > 0)
                        {
                            _outBuffer.Write(tempSpan.Slice(0, readCount));
                        }
                    }
                }
                
                // Add the current audio frame to the output buffer.
                _outBuffer.Write(audioBuffer);
            }
            else
            {
                // --- Voice is Inactive ---
                // We are not speaking, so cache this frame in the pre-buffer, making space if necessary.
                int spaceNeeded = audioBuffer.Length;
                int writable = _preBuffer.WritableCount;
                if (spaceNeeded > writable)
                {
                    _preBuffer.Skip(spaceNeeded - writable);
                }
                _preBuffer.Write(audioBuffer);
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

        #region Event Handlers & Thread Marshalling

        private void HandleVoiceActivityChanged(bool isActive)
        {
            if (IsDisposed) return;
            IsVoiceActive = isActive;
            PostVoiceActivityToMainThread(isActive);
        }
        
        private void PostVoiceActivityToMainThread(bool isActive)
        {
            _mainThreadContext?.Post(_ => {
                if (!IsDisposed)
                    OnVoiceActivityChanged?.Invoke(isActive);
            }, null);
        }
        
        #endregion

        #region Dispose Pattern
        
        public override void Dispose()
        {
            if (IsDisposed) return;

            _vadService.OnSpeakingStateChanged -= HandleVoiceActivityChanged;
            _preBuffer?.Clear();
            _outBuffer?.Clear();
            _transferBuffer = null;

            OnVoiceActivityChanged = null;
            base.Dispose();
        }
        
        #endregion
    }
}
#endif
