using System;
using System.Threading;

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
        private const int MaxMeterChannels = 8;

        public string name;
        private readonly AudioBuffer _queue;
        private readonly float[] _work; // temp buffer for per-frame processing
        private readonly float[] _rtOutChunk; // RT-safe scratch for event/telemetry chunking
        private readonly float[] _rtHeadScratch; // RT-safe scratch for small per-frame head writes
        private readonly AudioPipeline _pipeline;
        private readonly AudioContext _state;
        private readonly int _eventSourceId;

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
        private int _lastMeterChannelCount;

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
            _rtOutChunk = new float[Math.Max(_work.Length, EnqueueMaxCopySamples)];
            _rtHeadScratch = new float[MaxMeterChannels];
            _pipeline = new AudioPipeline();
            _state = new AudioContext(Channels, SampleRate, 0);
            _pipeline.Initialize(_state);
            int resSamples = AlignToFrameSamples(Math.Max(Channels * 512, 4096));
            _resBuf = new float[resSamples];
            _resFrames = 0;
            _phase = 0.0;
            _playedSourceFrames = 0;
            _meterPeak = new float[MaxMeterChannels];
            _meterRms = new float[MaxMeterChannels];
            _meterPeakScratch = new float[MaxMeterChannels];
            _meterSumSqScratch = new double[MaxMeterChannels];
            _lastOutputPerChannel = new float[MaxMeterChannels];
            _eventSourceId = EasyMicAudioEventPump.RegisterPlaybackSource(this);
            try { EasyMicAudioEventPump.SetMainThreadContext(SynchronizationContext.Current); } catch { }
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
            int totalSamples = destination.Length;
            if (totalSamples <= 0)
            {
                UpdateGainEnvelope(ref gainEnvelope, 0f, rampSamples);
                TryRaiseCompletionIfDrained();
                TryDispatchLoopBoundary();
                return;
            }

            int outChannels = Math.Max(1, sysChannels);
            int totalFrames = totalSamples / outChannels;

            int meterChannels = Math.Min(outChannels, MaxMeterChannels);
            if (meterChannels > 0)
            {
                Array.Clear(_meterPeakScratch, 0, meterChannels);
                Array.Clear(_meterSumSqScratch, 0, meterChannels);
            }

            bool allowPlayback = _isPlaying;
            bool mixToDestination = allowPlayback && !Mute;

            bool wantsPlaybackFrame = OnAudioPlayback != null && _eventSourceId != 0;
            var writer = wantsPlaybackFrame
                ? EasyMicAudioEventPump.TryBeginPlaybackFrame(_eventSourceId, outChannels, sysSampleRate, totalSamples)
                : default;

            int framesFromSource = 0;
            if (allowPlayback)
            {
                if (sysChannels == Channels && sysSampleRate == SampleRate)
                {
                    framesFromSource = RenderNativeRate(destination, outChannels, totalFrames, mixToDestination, ref gainEnvelope, targetGain, rampSamples, meterChannels, ref writer);
                }
                else
                {
                    framesFromSource = RenderResampled(destination, outChannels, sysSampleRate, totalFrames, mixToDestination, ref gainEnvelope, targetGain, rampSamples, meterChannels, ref writer);
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

            ApplyUnderflowFade(destination, framesFromSource, totalFrames, outChannels, mixToDestination, meterChannels, ref writer);
            FinalizeMeters(meterChannels, totalFrames);

            if (writer.IsValid)
            {
                writer.Commit();
            }

            TryRaiseCompletionIfDrained();
            TryDispatchLoopBoundary();
        }

        private int RenderNativeRate(
            Span<float> destination,
            int outChannels,
            int totalFrames,
            bool mixToDestination,
            ref MixerGainEnvelope gainEnvelope,
            float targetGain,
            int rampSamples,
            int meterChannels,
            ref EasyMicAudioEventPump.AudioEventWriter writer)
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
                int framesRead = read / outChannels;
                for (int frame = 0; frame < framesRead; frame++)
                {
                    int baseIndex = frame * outChannels;
                    for (int ch = 0; ch < outChannels; ch++)
                    {
                        int i = baseIndex + ch;
                        float sample = processedSpan[i];
                        sample += DenormalGuard;
                        sample -= DenormalGuard;
                        float scaled = sample * currentGain;
                        processedSpan[i] = scaled;

                        if (mixToDestination)
                        {
                            destSlice[i] += scaled;
                        }

                        if (ch < meterChannels)
                        {
                            float abs = MathF.Abs(scaled);
                            if (abs > _meterPeakScratch[ch])
                            {
                                _meterPeakScratch[ch] = abs;
                            }
                            _meterSumSqScratch[ch] += scaled * scaled;
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
                    }
                }

                if (framesRead > 0)
                {
                    int lastBase = (framesRead - 1) * outChannels;
                    int lastChannels = Math.Min(outChannels, _lastOutputPerChannel.Length);
                    for (int ch = 0; ch < lastChannels; ch++)
                    {
                        _lastOutputPerChannel[ch] = processedSpan[lastBase + ch];
                    }
                }

                if (writer.IsValid)
                {
                    if (!writer.Write(processedSpan))
                    {
                        writer.WriteZeros(read);
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

        private int RenderResampled(
            Span<float> destination,
            int outChannels,
            int sysSampleRate,
            int totalFrames,
            bool mixToDestination,
            ref MixerGainEnvelope gainEnvelope,
            float targetGain,
            int rampSamples,
            int meterChannels,
            ref EasyMicAudioEventPump.AudioEventWriter writer)
        {
            if (totalFrames <= 0)
            {
                UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);
                return 0;
            }

            double step = (double)SampleRate / Math.Max(1, sysSampleRate);
            double startPhase = _phase;
            double phase = startPhase;

            int neededSrcFrames = (int)Math.Ceiling(startPhase + totalFrames * step) + ResamplerGuardFrames;
            if (!EnsureResBufCapacity(neededSrcFrames * Channels))
            {
                UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);
                return 0;
            }
            FillResBuffer(neededSrcFrames);

            int initialResFrames = _resFrames;
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

            UpdateGainEnvelope(ref gainEnvelope, targetGain, rampSamples);

            float currentGain = gainEnvelope.Current;
            float gainStep = gainEnvelope.Step;
            int remaining = gainEnvelope.SamplesRemaining;
            float gainTarget = gainEnvelope.Target;

            int destIndex = 0;
            int chunkIndex = 0;
            int chunkCapacity = _rtOutChunk.Length - (_rtOutChunk.Length % Math.Max(1, outChannels));
            if (chunkCapacity <= 0)
            {
                chunkCapacity = Math.Max(1, outChannels);
            }

            int lastRealFrame = framesFromSource - 1;
            int lastChannels = Math.Min(outChannels, _lastOutputPerChannel.Length);

            for (int frame = 0; frame < framesFromSource; frame++)
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
                        _rtOutChunk[chunkIndex + ch] = scaled;

                        if (ch < meterChannels)
                        {
                            float abs = MathF.Abs(scaled);
                            if (abs > _meterPeakScratch[ch]) { _meterPeakScratch[ch] = abs; }
                            _meterSumSqScratch[ch] += scaled * scaled;
                        }

                        if (frame == lastRealFrame && ch < lastChannels)
                        {
                            _lastOutputPerChannel[ch] = scaled;
                        }
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
                    _rtOutChunk[chunkIndex] = scaled;
                    _rtOutChunk[chunkIndex + 1] = scaled;

                    if (meterChannels > 0)
                    {
                        float abs = MathF.Abs(scaled);
                        if (abs > _meterPeakScratch[0]) { _meterPeakScratch[0] = abs; }
                        _meterSumSqScratch[0] += scaled * scaled;
                        if (meterChannels > 1)
                        {
                            if (abs > _meterPeakScratch[1]) { _meterPeakScratch[1] = abs; }
                            _meterSumSqScratch[1] += scaled * scaled;
                        }
                    }

                    if (frame == lastRealFrame && lastChannels >= 2)
                    {
                        _lastOutputPerChannel[0] = scaled;
                        _lastOutputPerChannel[1] = scaled;
                    }
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
                    _rtOutChunk[chunkIndex] = scaled;

                    if (meterChannels > 0)
                    {
                        float abs = MathF.Abs(scaled);
                        if (abs > _meterPeakScratch[0]) { _meterPeakScratch[0] = abs; }
                        _meterSumSqScratch[0] += scaled * scaled;
                    }

                    if (frame == lastRealFrame && lastChannels >= 1)
                    {
                        _lastOutputPerChannel[0] = scaled;
                    }
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
                        _rtOutChunk[chunkIndex + ch] = scaled;

                        if (ch < meterChannels)
                        {
                            float abs = MathF.Abs(scaled);
                            if (abs > _meterPeakScratch[ch]) { _meterPeakScratch[ch] = abs; }
                            _meterSumSqScratch[ch] += scaled * scaled;
                        }

                        if (frame == lastRealFrame && ch < lastChannels)
                        {
                            _lastOutputPerChannel[ch] = scaled;
                        }
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
                chunkIndex += outChannels;

                if (writer.IsValid && chunkIndex >= chunkCapacity)
                {
                    if (!writer.Write(new ReadOnlySpan<float>(_rtOutChunk, 0, chunkIndex)))
                    {
                        writer.WriteZeros(chunkIndex);
                    }
                    chunkIndex = 0;
                }
                phase += step;
            }

            if (writer.IsValid && chunkIndex > 0)
            {
                if (!writer.Write(new ReadOnlySpan<float>(_rtOutChunk, 0, chunkIndex)))
                {
                    writer.WriteZeros(chunkIndex);
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

        private void ApplyUnderflowFade(
            Span<float> destination,
            int framesFromSource,
            int totalFrames,
            int outChannels,
            bool mixToDestination,
            int meterChannels,
            ref EasyMicAudioEventPump.AudioEventWriter writer)
        {
            if (totalFrames <= 0 || outChannels <= 0)
            {
                return;
            }

            if (framesFromSource >= totalFrames)
            {
                return;
            }

            int lastChannels = Math.Min(outChannels, _lastOutputPerChannel.Length);
            int fadeFrames = Math.Min(StarvationFadeFrames, totalFrames - framesFromSource);
            for (int f = 0; f < fadeFrames; f++)
            {
                float t = (float)(f + 1) / (fadeFrames + 1);
                int sampleIndex = (framesFromSource + f) * outChannels;

                int headChannels = Math.Min(outChannels, MaxMeterChannels);
                var head = _rtHeadScratch;

                for (int ch = 0; ch < outChannels; ch++)
                {
                    float sample = (ch < lastChannels ? _lastOutputPerChannel[ch] : 0f) * (1f - t);
                    sample += DenormalGuard;
                    sample -= DenormalGuard;

                    if (mixToDestination)
                    {
                        destination[sampleIndex + ch] += sample;
                    }

                    if (ch < headChannels)
                    {
                        head[ch] = sample;
                    }

                    if (ch < meterChannels)
                    {
                        float abs = MathF.Abs(sample);
                        if (abs > _meterPeakScratch[ch])
                        {
                            _meterPeakScratch[ch] = abs;
                        }
                        _meterSumSqScratch[ch] += sample * sample;
                    }
                }

                if (writer.IsValid)
                {
                    if (!writer.Write(new ReadOnlySpan<float>(head, 0, headChannels)))
                    {
                        writer.WriteZeros(headChannels);
                    }

                    if (outChannels > headChannels)
                    {
                        writer.WriteZeros(outChannels - headChannels);
                    }
                }
            }

            int remainingFrames = totalFrames - framesFromSource - fadeFrames;
            int remainingSamples = remainingFrames > 0 ? remainingFrames * outChannels : 0;
            if (writer.IsValid && remainingSamples > 0)
            {
                writer.WriteZeros(remainingSamples);
            }

            if (lastChannels > 0)
            {
                Array.Clear(_lastOutputPerChannel, 0, lastChannels);
            }
        }

        private void FinalizeMeters(int meterChannels, int frames)
        {
            int channels = Math.Min(meterChannels, Math.Min(_meterPeak.Length, _meterRms.Length));
            if (channels <= 0 || frames <= 0)
            {
                Volatile.Write(ref _lastMeterChannelCount, 0);
                return;
            }

            double denom = Math.Max(1, frames);
            for (int ch = 0; ch < channels; ch++)
            {
                _meterPeak[ch] = _meterPeakScratch[ch];
                _meterRms[ch] = (float)Math.Sqrt(_meterSumSqScratch[ch] / denom);
            }

            Volatile.Write(ref _lastMeterChannelCount, channels);
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
            var writer = EasyMicAudioEventPump.TryBeginPlaybackCompleted(_eventSourceId, Channels, SampleRate);
            if (writer.IsValid)
            {
                writer.Commit();
            }
        }

        private void TryDispatchLoopBoundary()
        {
            if (System.Threading.Interlocked.Exchange(ref _loopBoundaryPending, 0) == 1)
            {
                var writer = EasyMicAudioEventPump.TryBeginPlaybackCompleted(_eventSourceId, Channels, SampleRate);
                if (writer.IsValid)
                {
                    writer.Commit();
                }
            }
        }

        private bool EnsureResBufCapacity(int neededSamples)
        {
            neededSamples = AlignToFrameSamples(neededSamples);
            return _resBuf != null && _resBuf.Length >= neededSamples;
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
            try { EasyMicAudioEventPump.UnregisterPlaybackSource(_eventSourceId); } catch { }
            try { _pipeline.Dispose(); } catch { }
        }

        public void GetMeters(out float[] peak, out float[] rms)
        {
            int channels = Volatile.Read(ref _lastMeterChannelCount);
            if (channels <= 0)
            {
                peak = Array.Empty<float>();
                rms = Array.Empty<float>();
                return;
            }

            var p = _meterPeak;
            var r = _meterRms;
            int count = Math.Min(channels, Math.Min(p?.Length ?? 0, r?.Length ?? 0));
            if (count <= 0)
            {
                peak = Array.Empty<float>();
                rms = Array.Empty<float>();
                return;
            }

            peak = new float[count];
            rms = new float[count];
            Array.Copy(p, 0, peak, 0, count);
            Array.Copy(r, 0, rms, 0, count);
        }

        internal void DispatchAudioPlaybackFrame(float[] data, int channels, int sampleRate)
        {
            var handler = OnAudioPlayback;
            if (handler == null || data == null)
            {
                return;
            }

            try { handler(data, channels, sampleRate); } catch { }
        }

        internal void DispatchPlaybackCompleted()
        {
            var handler = OnPlaybackCompleted;
            if (handler == null)
            {
                return;
            }

            try { handler(this); } catch { }
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
