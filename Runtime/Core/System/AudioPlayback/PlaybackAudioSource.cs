using System;
using System.Collections.Generic;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// A playback source that accepts interleaved float PCM samples and renders them
    /// to the AudioSystem via additive mixing. Supports per-source pipeline and volume.
    /// </summary>
    public sealed class PlaybackAudioSource : IDisposable, IMixNode
    {
        private const float DenormalGuard = 1e-20f;
        private const int StarvationFadeFrames = 32;
        private const int ResamplerGuardFrames = 2;
        private const int EnqueueSpinBeforeSleep = 512;
        private const int EnqueueSleepMilliseconds = 1;
        private const int EnqueueMaxCopySamples = 16384;

        public string name;
        private readonly AudioBuffer _queue;
        private readonly float[] _work; // temp buffer for per-frame processing
        private readonly AudioPipeline _pipeline;
        private readonly AudioContext _state;
        // Real-time callback buffer
        private float[] _callbackBuffer;
        private readonly Dictionary<int, float[]> _callbackBufferCache = new Dictionary<int, float[]>(4);

        /// <summary>
        /// Real-time playback callback similar to Unity's OnAudioFilterRead. but readonly
        /// Invoked on the audio thread with the samples that this source contributed
        /// to the current output block. The data is in the output (system) channel
        /// layout and sample rate, and its length equals frames * channels.
        /// Signature: (samples, channels, sampleRate)
        /// </summary>
        public event Action<float[], int, int> OnAudioPlayback;

        /// <summary>
        /// Raised once the source has finished draining all scheduled audio and an end-of-stream was signalled.
        /// Consumers can use this to track clip or stream completion.
        /// </summary>
        public event Action<PlaybackAudioSource> OnPlaybackCompleted;
        // Resampler state
        private float[] _resBuf;     // interleaved source frames cache for resampling
        private int _resFrames;      // frames currently in _resBuf
        private double _phase;       // fractional source frame index
        // Playback/progress state
        private long _playedSourceFrames; // advanced source-domain frames
        private volatile bool _isPlaying = true;
        private long _totalSourceFrames = -1; // unknown when < 0

        // Conversion helpers for format negotiation (enqueue path). Allocated lazily.
        private float[] _convertBuffer;

        // End-of-stream tracking.
        private int _pendingStreamEnd; // 0/1 flag when callers request completion once drained
        private int _playbackCompleted; // 0/1 guard to dispatch completion once per drain
        private int _loopBoundaryPending; // 0/1 guard to surface loop boundary callback on audio thread

        // Meters (snapshot by last render)
        private float[] _meterPeak; // per-output-channel of last render
        private float[] _meterRms;  // per-output-channel of last render
        private float[] _meterPeakScratch;
        private double[] _meterSumSqScratch;
        private float[] _lastOutputPerChannel;

        private float _volume = 1.0f;
        private bool _mute;
        private bool _solo;

        private Action<IMixNode> _stateChanged;

        public float Volume
        {
            get => _volume;
            set
            {
                float clamped = value < 0f ? 0f : value;
                if (MathF.Abs(_volume - clamped) <= 1e-5f)
                {
                    return;
                }

                _volume = clamped;
                NotifyStateChanged();
            }
        }

        public bool Mute
        {
            get => _mute;
            set
            {
                if (_mute == value)
                {
                    return;
                }

                _mute = value;
                NotifyStateChanged();
            }
        }

        public bool Solo
        {
            get => _solo;
            set
            {
                if (_solo == value)
                {
                    return;
                }

                _solo = value;
                NotifyStateChanged();
            }
        }

        private void NotifyStateChanged()
        {
            _stateChanged?.Invoke(this);
        }

        public int Channels { get; }
        public int SampleRate { get; }

        public AudioPipeline Pipeline => _pipeline;
        /// <summary>
        /// Whether this source is actively contributing audio to the mix.
        /// </summary>
        public bool IsPlaying
        {
            get => _isPlaying;
            set => _isPlaying = value;
        }

        /// <summary>
        /// Optional total source frames for progress calculation (e.g., AudioClip length).
        /// Set to a positive value to enable NormalizedProgress.
        /// </summary>
        public long TotalSourceFrames
        {
            get => _totalSourceFrames;
            set => _totalSourceFrames = value;
        }

        /// <summary>
        /// Source-domain frames that have been rendered (accounting for resampling).
        /// </summary>
        public long PlayedSourceFrames => System.Threading.Interlocked.Read(ref _playedSourceFrames);

        /// <summary>
        /// Normalized playback progress in [0,1]. Returns 0 when TotalSourceFrames is unknown.
        /// </summary>
        public float NormalizedProgress
        {
            get
            {
                var total = _totalSourceFrames;
                if (total <= 0)
                {
                    return 0f;
                }


                var played = PlayedSourceFrames;
                if (played <= 0)
                {
                    return 0f;
                }


                double p = (double)played / (double)total;
                if (p < 0)
                {
                    p = 0;
                }
                else if (p > 1)
                {
                    p = 1;
                }


                return (float)p;
            }
        }

        public PlaybackAudioSource(int channels, int sampleRate, float queueSeconds = 1f, AudioMixer attachTo = null)
        {
            Channels = Math.Max(1, channels);
            SampleRate = Math.Max(8000, sampleRate);
            int cap = AlignToFrameSamples((int)Math.Max(Channels * SampleRate * queueSeconds, Channels * SampleRate / 2));
            _queue = new AudioBuffer(cap, Channels);
            int workSamples = AlignToFrameSamples(Math.Max(Channels * SampleRate / 50, 256));
            _work = new float[workSamples > 0 ? workSamples : Channels]; // ~20ms default min
            _pipeline = new AudioPipeline();
            _state = new AudioContext(Channels, SampleRate, 0);
            _pipeline.Initialize(_state);
            int resSamples = AlignToFrameSamples(Math.Max(Channels * 512, 4096));
            _resBuf = new float[resSamples];
            _resFrames = 0;
            _phase = 0.0;
            _playedSourceFrames = 0;
            try { (attachTo ?? AudioSystem.Instance.MasterMixer).AddSource(this); } catch { }
        }

        public int Enqueue(ReadOnlySpan<float> interleaved)
        {
            if (interleaved.IsEmpty)
            {
                return 0;
            }

            int alignedSamples = interleaved.Length - (interleaved.Length % Channels);
            if (alignedSamples <= 0)
            {
                return 0;
            }

            ResetCompletionGuard();
            return _queue.Write(interleaved.Slice(0, alignedSamples));
        }

        public int Enqueue(ReadOnlySpan<float> interleaved, int channels, int sampleRate, bool markEndOfStream = false)
        {
            return Enqueue(interleaved, channels, sampleRate, tailFadeSamples: 0, markEndOfStream: markEndOfStream);
        }

        public int Enqueue(ReadOnlySpan<float> interleaved, int channels, int sampleRate, int tailFadeSamples, bool markEndOfStream = false)
        {
            if (interleaved.IsEmpty)
            {
                if (markEndOfStream)
                {
                    SignalEndOfStream();
                }
                return 0;
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(channels));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            ResetCompletionGuard();

            int srcFrames = interleaved.Length / channels;
            if (srcFrames <= 0)
            {
                if (markEndOfStream)
                {
                    SignalEndOfStream();
                }
                return 0;
            }

            int srcSamples = srcFrames * channels;
            var src = interleaved.Slice(0, srcSamples);

            int normalizedFadeSamples = NormalizeTailFadeSamples(tailFadeSamples, channels, srcSamples);

            return (channels == Channels && sampleRate == SampleRate)
                ? WriteToQueue(src, markEndOfStream, normalizedFadeSamples)
                : ConvertAndEnqueue(src, channels, sampleRate, markEndOfStream, normalizedFadeSamples);
        }

        public void SignalEndOfStream()
        {
            System.Threading.Interlocked.Exchange(ref _pendingStreamEnd, 1);
        }

        private int WriteToQueue(ReadOnlySpan<float> samples, bool markEndOfStream, int tailFadeSamples = 0)
        {
            if (samples.IsEmpty)
            {
                if (markEndOfStream)
                {
                    SignalEndOfStream();
                }
                return 0;
            }

            int totalSamples = samples.Length;
            if (tailFadeSamples <= 0)
            {
                return WriteToQueueDirect(samples, markEndOfStream, totalSamples);
            }

            int fadeSamples = Math.Min(tailFadeSamples, totalSamples);
            if (fadeSamples <= 0)
            {
                return WriteToQueueDirect(samples, markEndOfStream, totalSamples);
            }

            int fadeStart = totalSamples - fadeSamples;
            int written = 0;
            var spinner = new System.Threading.SpinWait();
            int stallIterations = 0;

            while (written < totalSamples)
            {
                int writable = _queue.WritableCount;
                if (writable <= 0)
                {
                    stallIterations++;
                    if (stallIterations >= EnqueueSpinBeforeSleep)
                    {
                        System.Threading.Thread.Sleep(EnqueueSleepMilliseconds);
                    }
                    else
                    {
                        spinner.SpinOnce();
                    }
                    continue;
                }

                int remaining = totalSamples - written;
                int chunkSamples = Math.Min(remaining, Math.Min(writable, AlignToFrameSamples(EnqueueMaxCopySamples)));
                if (chunkSamples <= 0)
                {
                    stallIterations++;
                    if (stallIterations >= EnqueueSpinBeforeSleep)
                    {
                        System.Threading.Thread.Sleep(EnqueueSleepMilliseconds);
                    }
                    else
                    {
                        spinner.SpinOnce();
                    }
                    continue;
                }

                EnsureConvertBufferCapacity(chunkSamples);
                var scratch = new Span<float>(_convertBuffer, 0, chunkSamples);
                samples.Slice(written, chunkSamples).CopyTo(scratch);
                ApplyTailFade(scratch, written, fadeStart, totalSamples, fadeSamples);

                int justWritten = _queue.Write(scratch.Slice(0, chunkSamples));
                if (justWritten <= 0)
                {
                    stallIterations++;
                    if (stallIterations >= EnqueueSpinBeforeSleep)
                    {
                        System.Threading.Thread.Sleep(EnqueueSleepMilliseconds);
                    }
                    else
                    {
                        spinner.SpinOnce();
                    }
                    continue;
                }

                written += justWritten;
                stallIterations = 0;
                spinner.Reset();
            }

            if (markEndOfStream)
            {
                SignalEndOfStream();
            }

            return written;
        }

        private int WriteToQueueDirect(ReadOnlySpan<float> samples, bool markEndOfStream, int totalSamples)
        {
            int written = 0;
            var spinner = new System.Threading.SpinWait();
            int stallIterations = 0;

            while (written < totalSamples)
            {
                int justWritten = _queue.Write(samples.Slice(written));
                if (justWritten <= 0)
                {
                    stallIterations++;
                    if (stallIterations >= EnqueueSpinBeforeSleep)
                    {
                        System.Threading.Thread.Sleep(EnqueueSleepMilliseconds);
                    }
                    else
                    {
                        spinner.SpinOnce();
                    }
                    continue;
                }

                written += justWritten;
                stallIterations = 0;
                spinner.Reset();
            }

            if (markEndOfStream)
            {
                SignalEndOfStream();
            }

            return written;
        }

        private static int NormalizeTailFadeSamples(int tailFadeSamples, int channels, int totalSamples)
        {
            if (tailFadeSamples <= 0)
            {
                return 0;
            }

            int aligned = tailFadeSamples - (tailFadeSamples % Math.Max(1, channels));
            if (aligned <= 0)
            {
                return 0;
            }

            return Math.Min(aligned, totalSamples);
        }

        private void ApplyTailFade(Span<float> chunk, int globalOffset, int fadeStart, int totalSamples, int fadeSamples)
        {
            if (fadeSamples <= 0)
            {
                return;
            }

            for (int i = 0; i < chunk.Length; i++)
            {
                int sampleIndex = globalOffset + i;
                if (sampleIndex < fadeStart)
                {
                    continue;
                }

                int fadeIndex = sampleIndex - fadeStart;
                if (fadeIndex >= fadeSamples)
                {
                    fadeIndex = fadeSamples - 1;
                }

                float gain = 1f - ((fadeIndex + 1f) / fadeSamples);
                if (gain < 0f)
                {
                    gain = 0f;
                }
                chunk[i] *= gain;
            }
        }

        public void AnnounceLoopBoundary()
        {
            System.Threading.Interlocked.Exchange(ref _loopBoundaryPending, 1);
        }

        public int QueuedSamples => _queue.ReadableCount;
        public int FreeSamples => _queue.WritableCount;

        /// <summary>
        /// Current buffered duration in seconds, based on this source's format.
        /// </summary>
        public double BufferedSeconds => (double)QueuedSamples / Math.Max(1, Channels * SampleRate);

        /// <summary>
        /// Resume playback (IsPlaying = true).
        /// </summary>
        public void Play() => _isPlaying = true;

        /// <summary>
        /// Pause playback (IsPlaying = false). Buffer stays intact.
        /// </summary>
        public void Pause() => _isPlaying = false;

        /// <summary>
        /// Stop playback and reset progress, clearing internal buffers.
        /// </summary>
        public void Stop()
        {
            _isPlaying = false;
            ResetProgress();
        }

        /// <summary>
        /// Reset progress counters and, optionally, clear queued data.
        /// </summary>
        public void ResetProgress()
        {
            System.Threading.Interlocked.Exchange(ref _playedSourceFrames, 0);
            _phase = 0.0;
            _resFrames = 0;
            _queue.Clear();
            _totalSourceFrames = 0;
            System.Threading.Interlocked.Exchange(ref _pendingStreamEnd, 0);
            System.Threading.Interlocked.Exchange(ref _playbackCompleted, 0);
            System.Threading.Interlocked.Exchange(ref _loopBoundaryPending, 0);
        }

        /// <summary>
        /// Renders and mixes this source into the provided destination buffer (additive).
        /// Must be called from the AudioSystem callback thread.
        /// </summary>
        internal void RenderAdditive(Span<float> destination, int sysChannels, int sysSampleRate, ref MixerGainEnvelope gainEnvelope, float targetGain, int rampSamples)
        {
            TryDispatchLoopBoundary();

            int totalSamples = destination.Length;
            if (totalSamples == 0)
            {
                UpdateGainEnvelope(ref gainEnvelope, 0f, rampSamples);
                TryRaiseCompletionIfDrained();
                TryDispatchLoopBoundary();
                return;
            }

            EnsureCallbackBuffer(totalSamples);
            var callbackSlice = new Span<float>(_callbackBuffer, 0, totalSamples);
            callbackSlice.Clear();

            int outChannels = Math.Max(1, sysChannels);
            int totalFrames = outChannels > 0 ? totalSamples / outChannels : 0;
            bool allowPlayback = _isPlaying;
            bool mixToDestination = allowPlayback && !Mute;

            int framesFromSource = 0;
            if (allowPlayback)
            {
                if (sysChannels == Channels && sysSampleRate == SampleRate)
                {
                    framesFromSource = RenderNativeRate(destination, callbackSlice, outChannels, totalFrames, mixToDestination, ref gainEnvelope, targetGain, rampSamples);
                }
                else
                {
                    framesFromSource = RenderResampled(destination, callbackSlice, sysChannels, sysSampleRate, totalFrames, mixToDestination, ref gainEnvelope, targetGain, rampSamples);
                }
            }
            else
            {
                UpdateGainEnvelope(ref gainEnvelope, 0f, rampSamples);
            }

            if (framesFromSource > totalFrames)
            {
                framesFromSource = totalFrames;
            }

            ApplyUnderflowFade(destination, callbackSlice, framesFromSource, totalFrames, outChannels, mixToDestination);
            ApplyMeters(callbackSlice, outChannels, totalFrames);

            TryRaiseCompletionIfDrained();
            TryDispatchLoopBoundary();
            DispatchCallback(callbackSlice, sysChannels, sysSampleRate);
        }

        private int RenderNativeRate(Span<float> destination, Span<float> callbackSlice, int outChannels, int totalFrames, bool mixToDestination, ref MixerGainEnvelope gainEnvelope, float targetGain, int rampSamples)
        {
            if (totalFrames <= 0)
            {
                UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);
                return 0;
            }

            UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);

            int totalSamples = totalFrames * outChannels;
            int processedSamples = 0;
            int framesFromSource = 0;

            float currentGain = gainEnvelope.Current;
            float gainStep = gainEnvelope.Step;
            int remaining = gainEnvelope.SamplesRemaining;
            float gainTarget = gainEnvelope.Target;

            while (processedSamples < totalSamples)
            {
                int remainingSamples = totalSamples - processedSamples;
                int chunkSamples = Math.Min(_work.Length, remainingSamples);
                chunkSamples -= chunkSamples % Channels;
                if (chunkSamples <= 0)
                {
                    break;
                }

                Span<float> workSpan = new Span<float>(_work, 0, chunkSamples);
                int read = _queue.Read(workSpan);
                read -= read % Channels;
                if (read <= 0)
                {
                    break;
                }

                var processedSpan = workSpan.Slice(0, read);

                _state.Length = read;
                _pipeline.OnAudioPass(processedSpan, _state);

                var destSlice = destination.Slice(processedSamples, read);
                var cbSlice = callbackSlice.Slice(processedSamples, read);

                for (int i = 0; i < read; i++)
                {
                    float sample = processedSpan[i];
                    sample += DenormalGuard;
                    sample -= DenormalGuard;
                    float scaled = sample * currentGain;
                    if (mixToDestination)
                    {
                        destSlice[i] += scaled;
                    }
                    cbSlice[i] = scaled;

                    if (remaining > 0)
                    {
                        currentGain += gainStep;
                        remaining--;
                        if (remaining == 0)
                        {
                            currentGain = gainTarget;
                        }
                    }
                }

                processedSamples += read;
                int framesAdvanced = read / Channels;
                framesFromSource += framesAdvanced;
                System.Threading.Interlocked.Add(ref _playedSourceFrames, framesAdvanced);
            }

            gainEnvelope.Current = currentGain;
            gainEnvelope.SamplesRemaining = remaining;
            if (remaining <= 0)
            {
                gainEnvelope.Step = 0f;
                gainEnvelope.Current = gainEnvelope.Target;
            }

            return framesFromSource;
        }

        private int RenderResampled(Span<float> destination, Span<float> callbackSlice, int sysChannels, int sysSampleRate, int totalFrames, bool mixToDestination, ref MixerGainEnvelope gainEnvelope, float targetGain, int rampSamples)
        {
            if (totalFrames <= 0)
            {
                UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);
                return 0;
            }

            int outChannels = Math.Max(1, sysChannels);
            double step = (double)SampleRate / Math.Max(1, sysSampleRate);
            double startPhase = _phase;
            double phase = startPhase;

            int neededSrcFrames = (int)Math.Ceiling(startPhase + totalFrames * step) + ResamplerGuardFrames;
            EnsureResBufCapacity(neededSrcFrames * Channels);
            FillResBuffer(neededSrcFrames);

            int initialResFrames = _resFrames;
            if (_resFrames < neededSrcFrames)
            {
                int missing = neededSrcFrames - _resFrames;
                new Span<float>(_resBuf, _resFrames * Channels, missing * Channels).Clear();
                _resFrames += missing;
            }

            UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);

            float currentGain = gainEnvelope.Current;
            float gainStep = gainEnvelope.Step;
            int remaining = gainEnvelope.SamplesRemaining;
            float gainTarget = gainEnvelope.Target;

            int destIndex = 0;
            int cbIndex = 0;

            for (int frame = 0; frame < totalFrames; frame++)
            {
                int i0 = (int)phase;
                if (i0 >= _resFrames)
                {
                    i0 = _resFrames - 1;
                }

                int i1 = Math.Min(_resFrames - 1, i0 + 1);
                double frac = phase - i0;

                if (Channels == outChannels)
                {
                    for (int ch = 0; ch < outChannels; ch++)
                    {
                        int idx0 = i0 * Channels + ch;
                        int idx1 = i1 * Channels + ch;
                        float sample = _resBuf[idx0] + (float)((_resBuf[idx1] - _resBuf[idx0]) * frac);
                        sample += DenormalGuard;
                        sample -= DenormalGuard;
                        float scaled = sample * currentGain;
                        if (mixToDestination)
                        {
                            destination[destIndex + ch] += scaled;
                        }
                        callbackSlice[cbIndex + ch] = scaled;
                    }
                }
                else if (Channels == 1 && outChannels == 2)
                {
                    float s0 = _resBuf[i0];
                    float s1 = _resBuf[i1];
                    float sample = s0 + (float)((s1 - s0) * frac);
                    sample += DenormalGuard;
                    sample -= DenormalGuard;
                    float scaled = sample * currentGain;
                    if (mixToDestination)
                    {
                        destination[destIndex] += scaled;
                        destination[destIndex + 1] += scaled;
                    }
                    callbackSlice[cbIndex] = scaled;
                    callbackSlice[cbIndex + 1] = scaled;
                }
                else if (Channels == 2 && outChannels == 1)
                {
                    int base0 = i0 * Channels;
                    int base1 = i1 * Channels;
                    float l = _resBuf[base0] + (float)((_resBuf[base1] - _resBuf[base0]) * frac);
                    float r = _resBuf[base0 + 1] + (float)((_resBuf[base1 + 1] - _resBuf[base0 + 1]) * frac);
                    float sample = (l + r) * 0.5f;
                    sample += DenormalGuard;
                    sample -= DenormalGuard;
                    float scaled = sample * currentGain;
                    if (mixToDestination)
                    {
                        destination[destIndex] += scaled;
                    }
                    callbackSlice[cbIndex] = scaled;
                }
                else
                {
                    float acc = 0f;
                    for (int ch = 0; ch < Channels; ch++)
                    {
                        int idx0 = i0 * Channels + ch;
                        int idx1 = i1 * Channels + ch;
                        acc += _resBuf[idx0] + (float)((_resBuf[idx1] - _resBuf[idx0]) * frac);
                    }
                    float sample = acc / Channels;
                    sample += DenormalGuard;
                    sample -= DenormalGuard;
                    float scaled = sample * currentGain;
                    if (mixToDestination)
                    {
                        for (int ch = 0; ch < outChannels; ch++)
                        {
                            destination[destIndex + ch] += scaled;
                        }
                    }
                    for (int ch = 0; ch < outChannels; ch++)
                    {
                        callbackSlice[cbIndex + ch] = scaled;
                    }
                }

                if (remaining > 0)
                {
                    currentGain += gainStep;
                    remaining--;
                    if (remaining == 0)
                    {
                        currentGain = gainTarget;
                    }
                }

                destIndex += outChannels;
                cbIndex += outChannels;
                phase += step;
            }

            int framesFromSource = 0;
            if (initialResFrames > 0 && step > 0.0)
            {
                double maxSourceIndex = Math.Max(0.0, initialResFrames - 1 - startPhase);
                double framesAvailable = (maxSourceIndex / step) + 1.0;
                if (framesAvailable > 0.0)
                {
                    framesFromSource = (int)Math.Min(totalFrames, Math.Floor(framesAvailable));
                }
            }

            int consumed = (int)Math.Floor(phase);
            int drop = Math.Max(0, consumed - ResamplerGuardFrames);
            if (drop > 0)
            {
                int remainingFrames = Math.Max(0, _resFrames - drop);
                if (remainingFrames > 0)
                {
                    Buffer.BlockCopy(_resBuf, drop * Channels * sizeof(float), _resBuf, 0, remainingFrames * Channels * sizeof(float));
                }
                _resFrames = remainingFrames;
                phase -= drop;
                if (phase < 0.0)
                {
                    phase = 0.0;
                }
                System.Threading.Interlocked.Add(ref _playedSourceFrames, drop);
            }

            _phase = phase;

            gainEnvelope.Current = currentGain;
            gainEnvelope.SamplesRemaining = remaining;
            if (remaining <= 0)
            {
                gainEnvelope.Step = 0f;
                gainEnvelope.Current = gainEnvelope.Target;
            }

            return framesFromSource;
        }

        private static void UpdateGainEnvelope(ref MixerGainEnvelope envelope, float targetGain, int rampSamples)
        {
            if (MathF.Abs(envelope.Target - targetGain) <= 1e-6f)
            {
                if (envelope.SamplesRemaining <= 0)
                {
                    envelope.Current = targetGain;
                    envelope.Target = targetGain;
                    envelope.Step = 0f;
                }
                return;
            }

            envelope.Target = targetGain;
            if (rampSamples <= 0)
            {
                envelope.Current = targetGain;
                envelope.Step = 0f;
                envelope.SamplesRemaining = 0;
                return;
            }

            float delta = targetGain - envelope.Current;
            if (MathF.Abs(delta) <= 1e-6f)
            {
                envelope.Current = targetGain;
                envelope.Step = 0f;
                envelope.SamplesRemaining = 0;
                return;
            }

            envelope.Step = delta / rampSamples;
            envelope.SamplesRemaining = rampSamples;
        }

        private void FillResBuffer(int neededFrames)
        {
            while (_resFrames < neededFrames)
            {
                int capacitySamples = _resBuf.Length - _resFrames * Channels;
                if (capacitySamples <= 0)
                {
                    break;
                }

                int readable = _queue.ReadableCount;
                if (readable <= 0)
                {
                    break;
                }

                int toReadSamples = Math.Min(Math.Min(capacitySamples, _work.Length), readable);
                toReadSamples -= toReadSamples % Channels;
                if (toReadSamples <= 0)
                {
                    break;
                }

                int read = _queue.Read(new Span<float>(_work, 0, toReadSamples));
                read -= read % Channels;
                if (read <= 0)
                {
                    break;
                }

                new ReadOnlySpan<float>(_work, 0, read).CopyTo(new Span<float>(_resBuf, _resFrames * Channels, read));
                _resFrames += read / Channels;
            }
        }

        private void ApplyUnderflowFade(Span<float> destination, Span<float> callbackSlice, int framesFromSource, int totalFrames, int outChannels, bool mixToDestination)
        {
            if (totalFrames <= 0 || outChannels <= 0)
            {
                return;
            }

            EnsureLastOutputBuffer(outChannels);

            if (framesFromSource > 0)
            {
                int lastIndex = (framesFromSource - 1) * outChannels;
                for (int ch = 0; ch < outChannels; ch++)
                {
                    _lastOutputPerChannel[ch] = callbackSlice[lastIndex + ch];
                }
            }

            if (framesFromSource >= totalFrames)
            {
                return;
            }

            int fadeFrames = Math.Min(StarvationFadeFrames, totalFrames - framesFromSource);
            for (int f = 0; f < fadeFrames; f++)
            {
                float t = (float)(f + 1) / (fadeFrames + 1);
                int sampleIndex = (framesFromSource + f) * outChannels;
                for (int ch = 0; ch < outChannels; ch++)
                {
                    float sample = _lastOutputPerChannel[ch] * (1f - t);
                    sample += DenormalGuard;
                    sample -= DenormalGuard;
                    if (mixToDestination)
                    {
                        destination[sampleIndex + ch] += sample;
                    }
                    callbackSlice[sampleIndex + ch] = sample;
                }
            }

            int zeroStart = (framesFromSource + fadeFrames) * outChannels;
            if (zeroStart < callbackSlice.Length)
            {
                callbackSlice.Slice(zeroStart).Clear();
            }

            Array.Clear(_lastOutputPerChannel, 0, outChannels);
        }

        private void EnsureLastOutputBuffer(int channelCount)
        {
            if (_lastOutputPerChannel == null || _lastOutputPerChannel.Length < channelCount)
            {
                _lastOutputPerChannel = new float[channelCount];
            }
        }

        private void EnsureCallbackBuffer(int sampleCount)
        {
            if (_callbackBuffer != null && _callbackBuffer.Length == sampleCount)
            {
                return;
            }

            if (_callbackBufferCache.TryGetValue(sampleCount, out var cached))
            {
                _callbackBuffer = cached;
                return;
            }

            var buffer = new float[sampleCount];
            _callbackBufferCache[sampleCount] = buffer;
            _callbackBuffer = buffer;
        }

        private void DispatchCallback(Span<float> callbackSlice, int sysChannels, int sysSampleRate)
        {
            var handler = OnAudioPlayback;
            if (handler == null)
            {
                return;
            }

            try { handler(_callbackBuffer, sysChannels, sysSampleRate); } catch { }
        }

        private void ApplyMeters(Span<float> data, int channels, int frames)
        {
            if (channels <= 0 || frames <= 0)
            {
                if (_meterPeak != null && _meterPeak.Length >= channels && channels > 0)
                {
                    Array.Clear(_meterPeak, 0, channels);
                }
                if (_meterRms != null && _meterRms.Length >= channels && channels > 0)
                {
                    Array.Clear(_meterRms, 0, channels);
                }
                return;
            }

            EnsureMeterScratch(channels);

            for (int frame = 0; frame < frames; frame++)
            {
                int baseIndex = frame * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    float sample = data[baseIndex + ch];
                    float abs = MathF.Abs(sample);
                    if (abs > _meterPeakScratch[ch])
                    {
                        _meterPeakScratch[ch] = abs;
                    }
                    _meterSumSqScratch[ch] += sample * sample;
                }
            }

            if (_meterPeak == null || _meterPeak.Length != channels)
            {
                _meterPeak = new float[channels];
            }
            if (_meterRms == null || _meterRms.Length != channels)
            {
                _meterRms = new float[channels];
            }

            for (int ch = 0; ch < channels; ch++)
            {
                _meterPeak[ch] = _meterPeakScratch[ch];
                _meterRms[ch] = (float)Math.Sqrt(_meterSumSqScratch[ch] / frames);
            }
        }

        private void EnsureMeterScratch(int channelCount)
        {
            if (_meterPeakScratch == null || _meterPeakScratch.Length < channelCount)
            {
                _meterPeakScratch = new float[channelCount];
            }
            if (_meterSumSqScratch == null || _meterSumSqScratch.Length < channelCount)
            {
                _meterSumSqScratch = new double[channelCount];
            }

            Array.Clear(_meterPeakScratch, 0, channelCount);
            Array.Clear(_meterSumSqScratch, 0, channelCount);
        }

        private int AlignToFrameSamples(int sampleCount)
        {
            int stride = Math.Max(1, Channels);
            int remainder = sampleCount % stride;
            int aligned = remainder == 0 ? sampleCount : sampleCount + (stride - remainder);
            return aligned <= 0 ? stride : aligned;
        }

        private void ResetCompletionGuard()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _playbackCompleted, 0, 1) == 1)
            {
                System.Threading.Interlocked.Exchange(ref _pendingStreamEnd, 0);
            }
            else
            {
                System.Threading.Volatile.Write(ref _playbackCompleted, 0);
            }
        }

        private int ConvertAndEnqueue(ReadOnlySpan<float> source, int srcChannels, int srcSampleRate, bool markEndOfStream, int tailFadeSamples)
        {
            int srcFrames = source.Length / srcChannels;
            if (srcFrames <= 0)
            {
                if (markEndOfStream)
                {
                    SignalEndOfStream();
                }
                return 0;
            }

            int targetFrames = (srcSampleRate == SampleRate)
                ? srcFrames
                : (int)Math.Ceiling(srcFrames * (double)SampleRate / Math.Max(1, srcSampleRate));
            if (targetFrames <= 0)
            {
                if (markEndOfStream)
                {
                    SignalEndOfStream();
                }
                return 0;
            }

            int targetSamples = targetFrames * Channels;
            EnsureConvertBufferCapacity(targetSamples);
            var dest = new Span<float>(_convertBuffer, 0, targetSamples);

            if (srcSampleRate == SampleRate)
            {
                ConvertChannelsOnly(source, srcChannels, dest, srcFrames);
            }
            else
            {
                ConvertWithResample(source, srcChannels, srcSampleRate, dest, targetFrames);
            }

            int destFadeSamples = 0;
            if (tailFadeSamples > 0)
            {
                int fadeFrames = tailFadeSamples / Math.Max(1, srcChannels);
                if (fadeFrames > 0)
                {
                    if (srcSampleRate != SampleRate)
                    {
                        fadeFrames = (int)Math.Ceiling(fadeFrames * (double)SampleRate / Math.Max(1, srcSampleRate));
                    }

                    destFadeSamples = fadeFrames * Channels;
                    destFadeSamples = NormalizeTailFadeSamples(destFadeSamples, Channels, targetSamples);
                }
            }

            return WriteToQueue(dest.Slice(0, targetSamples), markEndOfStream, destFadeSamples);
        }

        private void EnsureConvertBufferCapacity(int neededSamples)
        {
            if (_convertBuffer != null && _convertBuffer.Length >= neededSamples)
            {
                return;
            }

            int newSize = _convertBuffer == null || _convertBuffer.Length == 0 ? 1024 : _convertBuffer.Length;
            while (newSize < neededSamples)
            {
                newSize *= 2;
            }

            _convertBuffer = new float[newSize];
        }

        private void ConvertChannelsOnly(ReadOnlySpan<float> source, int srcChannels, Span<float> dest, int frames)
        {
            int copySamples = Math.Min(source.Length, frames * srcChannels);
            source = source.Slice(0, copySamples);

            if (srcChannels == Channels)
            {
                source.CopyTo(dest);
                return;
            }

            for (int frame = 0; frame < frames; frame++)
            {
                int srcIndex = frame * srcChannels;
                int dstIndex = frame * Channels;

                if (srcChannels == 1)
                {
                    float s = source[srcIndex];
                    for (int ch = 0; ch < Channels; ch++)
                    {
                        dest[dstIndex + ch] = s;
                    }
                    continue;
                }

                if (Channels == 1)
                {
                    float acc = 0f;
                    for (int ch = 0; ch < srcChannels; ch++)
                    {
                        acc += source[srcIndex + ch];
                    }
                    dest[dstIndex] = acc / srcChannels;
                    continue;
                }

                float avg = 0f;
                for (int ch = 0; ch < srcChannels; ch++)
                {
                    avg += source[srcIndex + ch];
                }
                avg /= srcChannels;
                for (int ch = 0; ch < Channels; ch++)
                {
                    dest[dstIndex + ch] = avg;
                }
            }
        }

        private void ConvertWithResample(ReadOnlySpan<float> source, int srcChannels, int srcSampleRate, Span<float> dest, int targetFrames)
        {
            int srcFrames = source.Length / srcChannels;
            if (srcFrames <= 0)
            {
                dest.Slice(0, targetFrames * Channels).Clear();
                return;
            }

            double step = (double)srcSampleRate / SampleRate;
            for (int frame = 0; frame < targetFrames; frame++)
            {
                double srcPos = frame * step;

                int dstIndex = frame * Channels;
                if (srcChannels == Channels)
                {
                    for (int ch = 0; ch < Channels; ch++)
                    {
                        dest[dstIndex + ch] = SampleChannelLinear(source, srcFrames, srcChannels, srcPos, ch);
                    }
                    continue;
                }

                if (srcChannels == 1)
                {
                    float s = SampleChannelLinear(source, srcFrames, 1, srcPos, 0);
                    for (int ch = 0; ch < Channels; ch++)
                    {
                        dest[dstIndex + ch] = s;
                    }
                    continue;
                }

                if (Channels == 1)
                {
                    float acc = 0f;
                    for (int ch = 0; ch < srcChannels; ch++)
                    {
                        acc += SampleChannelLinear(source, srcFrames, srcChannels, srcPos, ch);
                    }
                    dest[dstIndex] = acc / srcChannels;
                    continue;
                }

                float avg = 0f;
                for (int ch = 0; ch < srcChannels; ch++)
                {
                    avg += SampleChannelLinear(source, srcFrames, srcChannels, srcPos, ch);
                }
                avg /= srcChannels;
                for (int ch = 0; ch < Channels; ch++)
                {
                    dest[dstIndex + ch] = avg;
                }
            }
        }

        private static float SampleChannelLinear(ReadOnlySpan<float> source, int frames, int channels, double position, int channel)
        {
            if (frames <= 0)
            {
                return 0f;
            }

            double maxIndex = Math.Max(0, frames - 1);
            double clamped = position;
            if (clamped < 0.0)
            {
                clamped = 0.0;
            }
            else if (clamped > maxIndex)
            {
                clamped = maxIndex;
            }
            int i0 = (int)clamped;
            int i1 = Math.Min(frames - 1, i0 + 1);
            double frac = clamped - i0;

            int idx0 = i0 * channels + Math.Min(channel, channels - 1);
            int idx1 = i1 * channels + Math.Min(channel, channels - 1);
            float s0 = source[idx0];
            float s1 = source[idx1];
            return s0 + (float)((s1 - s0) * frac);
        }

        private void TryRaiseCompletionIfDrained()
        {
            if (System.Threading.Volatile.Read(ref _pendingStreamEnd) == 0)
            {
                return;
            }

            if (!_queue.IsEmpty)
            {
                return;
            }

            if (_resFrames > ResamplerGuardFrames)
            {
                return;
            }

            if (System.Threading.Interlocked.CompareExchange(ref _playbackCompleted, 1, 0) != 0)
            {
                return;
            }

            _isPlaying = false;
            if (_lastOutputPerChannel != null)
            {
                Array.Clear(_lastOutputPerChannel, 0, _lastOutputPerChannel.Length);
            }

            try { OnPlaybackCompleted?.Invoke(this); } catch { }
        }

        private void TryDispatchLoopBoundary()
        {
            if (System.Threading.Interlocked.Exchange(ref _loopBoundaryPending, 0) == 1)
            {
                try { OnPlaybackCompleted?.Invoke(this); } catch { }
            }
        }

        private void EnsureResBufCapacity(int neededSamples)
        {
            neededSamples = AlignToFrameSamples(neededSamples);
            if (_resBuf.Length >= neededSamples)
            {
                return;
            }

            int newSize = _resBuf.Length;
            if (newSize == 0)
            {
                newSize = AlignToFrameSamples(Channels * 4);
            }
            while (newSize < neededSamples)
            {
                newSize *= 2;
            }


            var nb = new float[newSize];
            Array.Copy(_resBuf, nb, _resFrames * Channels);
            _resBuf = nb;
        }

        public void AddProcessor(AudioWorkerBlueprint blueprint)
        {
            if (blueprint == null)
            {
                throw new ArgumentNullException(nameof(blueprint));
            }


            _pipeline.AddWorker(blueprint.Create());
        }

        public void Dispose()
        {
            try { _pipeline.Dispose(); } catch { }
        }

        public void GetMeters(out float[] peak, out float[] rms)
        {
            var p = _meterPeak;
            var r = _meterRms;
            peak = p != null ? (float[])p.Clone() : Array.Empty<float>();
            rms = r != null ? (float[])r.Clone() : Array.Empty<float>();
        }

        string IMixNode.Name => name ?? string.Empty;

        float IMixNode.Volume => _volume;

        bool IMixNode.Mute => _mute;

        bool IMixNode.Solo => _solo;

        bool IMixNode.HasSoloInTree => _solo;

        bool IMixNode.IsActive => _isPlaying && !_mute;

        event Action<IMixNode> IMixNode.StateChanged
        {
            add => _stateChanged += value;
            remove => _stateChanged -= value;
        }

        void IMixNode.RenderInto(
            Span<float> destination,
            int systemChannels,
            int systemSampleRate,
            ref MixerGainEnvelope envelope,
            float targetGain,
            int rampSamples,
            Span<float> scratch)
        {
            RenderAdditive(destination, systemChannels, systemSampleRate, ref envelope, targetGain, rampSamples);
        }
    }
}
