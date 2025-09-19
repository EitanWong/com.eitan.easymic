using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// A playback source that accepts interleaved float PCM samples and renders them
    /// to the AudioSystem via additive mixing. Supports per-source pipeline and volume.
    /// </summary>
    public sealed class PlaybackAudioSource : IDisposable
    {
        public string name;
        private readonly AudioBuffer _queue;
        private readonly float[] _work; // temp buffer for per-frame processing
        private readonly AudioPipeline _pipeline;
        private readonly AudioState _state;
        // Real-time callback buffer
        private float[] _callbackBuffer;
        
        /// <summary>
        /// Real-time playback callback similar to Unity's OnAudioFilterRead. but readonly
        /// Invoked on the audio thread with the samples that this source contributed
        /// to the current output block. The data is in the output (system) channel
        /// layout and sample rate, and its length equals frames * channels.
        /// Signature: (samples, channels, sampleRate)
        /// </summary>
        public event Action<float[], int, int> OnAudioPlayback;
        // Resampler state
        private float[] _resBuf;     // interleaved source frames cache for resampling
        private int _resFrames;      // frames currently in _resBuf
        private double _phase;       // fractional source frame index
        // Playback/progress state
        private long _playedSourceFrames; // advanced source-domain frames
        private volatile bool _isPlaying = true;
        private long _totalSourceFrames = -1; // unknown when < 0

        // Meters (snapshot by last render)
        private float[] _meterPeak; // per-output-channel of last render
        private float[] _meterRms;  // per-output-channel of last render

        public float Volume { get; set; } = 1.0f;
        public bool Mute { get; set; } = false;
        public bool Solo { get; set; } = false;

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

        public PlaybackAudioSource(int channels, int sampleRate, float queueSeconds = 0.01f, AudioMixer attachTo = null)
        {
            Channels = Math.Max(1, channels);
            SampleRate = Math.Max(8000, sampleRate);
            int cap = (int)Math.Max(Channels * SampleRate * queueSeconds, Channels * SampleRate / 2);
            _queue = new AudioBuffer(cap);
            _work = new float[Math.Max(Channels * SampleRate / 50, 256)]; // ~20ms default min
            _pipeline = new AudioPipeline();
            _state = new AudioState(Channels, SampleRate, 0);
            _pipeline.Initialize(_state);
            _resBuf = new float[Math.Max(Channels * 512, 4096)];
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

            return _queue.Write(interleaved);
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
        }

        /// <summary>
        /// Renders and mixes this source into the provided destination buffer (additive).
        /// Must be called from the AudioSystem callback thread.
        /// </summary>
        internal void RenderAdditive(Span<float> destination, int sysChannels, int sysSampleRate)
        {
            if (destination.IsEmpty)
            {
                return;
            }


            if (!_isPlaying)
            {
                return;
            }


            if (Mute || Volume <= 0f)
            {
                return;
            }

            // Local meter accumulation for this render pass (in output channel domain)

            int outCh = Math.Max(1, sysChannels);
            float[] peakLocal = null;
            double[] sumSqLocal = null;
            int framesAccum = 0;
            void EnsureMeterBuffers()
            {
                if (peakLocal == null || peakLocal.Length != outCh)
                {
                    peakLocal = new float[outCh];
                    sumSqLocal = new double[outCh];
                    for (int i = 0; i < outCh; i++) { peakLocal[i] = 0f; sumSqLocal[i] = 0.0; }
                }
            }

            if (sysChannels == Channels && sysSampleRate == SampleRate)
            {
                int needed = destination.Length;
                int processed = 0;
                while (processed < needed)
                {
                    int chunk = Math.Min(_work.Length, needed - processed);
                    // Align to whole frames to avoid drifting on partial reads.
                    chunk -= (chunk % Channels);
                    if (chunk <= 0)
                    {
                        break;
                    }


                    int read = _queue.Read(new Span<float>(_work, 0, chunk));
                    // Safety: enforce frame alignment on the returned count as well.
                    int readAligned = read - (read % Channels);
                    if (readAligned <= 0)
                    {
                        break;
                    }


                    _state.Length = readAligned;
                    _pipeline.OnAudioPass(new Span<float>(_work, 0, readAligned), _state);
                    if (Volume != 1.0f)
                    {
                        // meters over this chunk
                        EnsureMeterBuffers();
                        int frames = readAligned / Channels;
                        // ensure callback buffer
                        if (_callbackBuffer == null || _callbackBuffer.Length < readAligned)
                        {
                            _callbackBuffer = new float[readAligned];
                        }


                        for (int f = 0; f < frames; f++)
                        {
                            int b = f * Channels;
                            for (int ch = 0; ch < Channels; ch++)
                            {
                                float s = _work[b + ch] * Volume;
                                _callbackBuffer[b + ch] = s;
                                float a = MathF.Abs(s);
                                if (a > peakLocal[ch])
                                {
                                    peakLocal[ch] = a;
                                }


                                sumSqLocal[ch] += s * s;
                            }
                        }
                        framesAccum += frames;
                        // mix and invoke callback for this chunk
                        for (int i = 0; i < readAligned; i++)
                        {
                            destination[processed + i] += _callbackBuffer[i];
                        }


                        try { OnAudioPlayback?.Invoke(_callbackBuffer, sysChannels, sysSampleRate); } catch { }
                    }
                    else
                    {
                        EnsureMeterBuffers();
                        int frames = readAligned / Channels;
                        if (_callbackBuffer == null || _callbackBuffer.Length < readAligned)
                        {
                            _callbackBuffer = new float[readAligned];
                        }

                        for (int f = 0; f < frames; f++)
                        {
                            int b = f * Channels;
                            for (int ch = 0; ch < Channels; ch++)
                            {
                                float s = _work[b + ch];
                                _callbackBuffer[b + ch] = s;
                                float a = MathF.Abs(s);
                                if (a > peakLocal[ch])
                                {
                                    peakLocal[ch] = a;
                                }


                                sumSqLocal[ch] += s * s;
                            }
                        }
                        framesAccum += frames;
                        for (int i = 0; i < readAligned; i++)
                        {
                            destination[processed + i] += _callbackBuffer[i];
                        }


                        try { OnAudioPlayback?.Invoke(_callbackBuffer, sysChannels, sysSampleRate); } catch { }
                    }
                    processed += readAligned;
                    // progress (source domain)
                    System.Threading.Interlocked.Add(ref _playedSourceFrames, readAligned / Channels);
                }
                // Commit meters snapshot
                if (framesAccum > 0)
                {
                    var rms = new float[outCh];
                    for (int ch = 0; ch < outCh; ch++)
                    {
                        rms[ch] = (float)Math.Sqrt(sumSqLocal[ch] / framesAccum);
                    }
                    _meterPeak = peakLocal;
                    _meterRms = rms;
                }
                return;
            }

            // Resample + channel map path
            int outFrames = destination.Length / Math.Max(1, sysChannels);
            if (outFrames <= 0)
            {
                return;
            }

            double step = (double)SampleRate / Math.Max(1, sysSampleRate);

            int neededSrcFrames = (int)Math.Ceiling(_phase + outFrames * step) + 3; // lookahead for cubic
            EnsureResBufCapacity(neededSrcFrames * Channels);
            while (_resFrames < neededSrcFrames)
            {
                // Only read full frames to avoid losing leftover samples which would
                // cause cumulative drift (perceived as speed-up over time).
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
                int toReadAligned = toReadSamples - (toReadSamples % Channels);
                if (toReadAligned <= 0)
                {
                    break;
                }


                int read = _queue.Read(new Span<float>(_work, 0, toReadAligned));
                if (read <= 0)
                {
                    break;
                }
                // Safety: align to full frames in case underlying buffer returns an odd count.

                int readAligned = read - (read % Channels);
                if (readAligned <= 0)
                {
                    break;
                }


                new ReadOnlySpan<float>(_work, 0, readAligned).CopyTo(new Span<float>(_resBuf, _resFrames * Channels, readAligned));
                _resFrames += readAligned / Channels;
            }

            EnsureMeterBuffers();
            int outIndex = 0;
            // Prepare a scratch buffer for callback accumulation. We will trim to actual size later.
            float[] cbScratch = null;
            if (outFrames > 0)
            {
                int alloc = Math.Max(0, outFrames * Math.Max(1, sysChannels));
                cbScratch = (alloc > 0) ? new float[alloc] : Array.Empty<float>();
            }
            for (int f = 0; f < outFrames; f++)
            {
                int i0 = (int)Math.Floor(_phase);
                double t = _phase - i0;

                if (Channels == sysChannels)
                {
                    for (int ch = 0; ch < sysChannels; ch++)
                    {
                        float s = CubicAtChannel(_resBuf, _resFrames, Channels, i0, ch, t) * Volume;
                        destination[outIndex++] += s;
                        if (cbScratch != null)
                        {
                            cbScratch[outIndex - 1] = s;
                        }


                        float a = MathF.Abs(s);
                        if (a > peakLocal[ch])
                        {
                            peakLocal[ch] = a;
                        }


                        sumSqLocal[ch] += s * s;
                    }
                }
                else if (Channels == 1 && sysChannels == 2)
                {
                    float s = CubicAtChannel(_resBuf, _resFrames, 1, i0, 0, t) * Volume;
                    destination[outIndex++] += s;
                    destination[outIndex++] += s;
                    if (cbScratch != null)
                    {
                        cbScratch[outIndex - 2] = s;
                        cbScratch[outIndex - 1] = s;
                    }
                    float a = MathF.Abs(s);
                    if (a > peakLocal[0])
                    {
                        peakLocal[0] = a;
                    }


                    if (a > peakLocal[1])
                    {
                        peakLocal[1] = a;
                    }


                    sumSqLocal[0] += s * s;
                    sumSqLocal[1] += s * s;
                }
                else if (Channels == 2 && sysChannels == 1)
                {
                    float l = CubicAtChannel(_resBuf, _resFrames, 2, i0, 0, t);
                    float r = CubicAtChannel(_resBuf, _resFrames, 2, i0, 1, t);
                    float s = (l + r) * 0.5f * Volume;
                    destination[outIndex++] += s;
                    if (cbScratch != null)
                    {
                        cbScratch[outIndex - 1] = s;
                    }


                    float a = MathF.Abs(s);
                    if (a > peakLocal[0])
                    {
                        peakLocal[0] = a;
                    }


                    sumSqLocal[0] += s * s;
                }
                else
                {
                    float avg = 0f;
                    for (int ch = 0; ch < Channels; ch++)
                    {
                        avg += CubicAtChannel(_resBuf, _resFrames, Channels, i0, ch, t);
                    }

                    float s = (avg / Channels) * Volume;
                    for (int ch = 0; ch < sysChannels; ch++)
                    {
                        destination[outIndex++] += s;
                        if (cbScratch != null)
                        {
                            cbScratch[outIndex - 1] = s;
                        }

                        float a = MathF.Abs(s);
                        if (a > peakLocal[ch])
                        {
                            peakLocal[ch] = a;
                        }


                        sumSqLocal[ch] += s * s;
                    }
                }
                _phase += step;
                if ((int)Math.Floor(_phase) + 2 >= _resFrames)
                {
                    break;
                }


                framesAccum++;
            }

            int consumed = Math.Max(0, Math.Min(_resFrames, (int)Math.Floor(_phase) - 1));
            const int keep = 3;
            int drop = Math.Max(0, consumed - keep);
            if (drop > 0)
            {
                int remainingFrames = _resFrames - drop;
                Buffer.BlockCopy(_resBuf, drop * Channels * 4, _resBuf, 0, remainingFrames * Channels * 4);
                _resFrames = remainingFrames;
                _phase -= drop;
                if (_phase < 0)
                {
                    _phase = 0;
                }
                // progress (source domain)

                System.Threading.Interlocked.Add(ref _playedSourceFrames, drop);
            }

            if (framesAccum > 0)
            {
                var rms = new float[outCh];
                for (int ch = 0; ch < outCh; ch++)
                {
                    rms[ch] = (float)Math.Sqrt(sumSqLocal[ch] / framesAccum);
                }


                _meterPeak = peakLocal;
                _meterRms = rms;
            }

            // Dispatch callback with exactly the contributed samples for this pass
            if (outIndex > 0 && cbScratch != null)
            {
                // Reuse persistent callback buffer if sizes match; otherwise create new exact-size array
                if (_callbackBuffer == null || _callbackBuffer.Length != outIndex)
                {
                    _callbackBuffer = new float[outIndex];
                }
                Array.Copy(cbScratch, 0, _callbackBuffer, 0, outIndex);
                try { OnAudioPlayback?.Invoke(_callbackBuffer, sysChannels, sysSampleRate); } catch { }
            }
        }

        private void EnsureResBufCapacity(int neededSamples)
        {
            if (_resBuf.Length >= neededSamples)
            {
                return;
            }


            int newSize = _resBuf.Length;
            while (newSize < neededSamples)
            {
                newSize *= 2;
            }


            var nb = new float[newSize];
            Array.Copy(_resBuf, nb, _resFrames * Channels);
            _resBuf = nb;
        }

        private static float CubicAtChannel(float[] buf, int frames, int chCount, int baseIndex, int ch, double t)
        {
            int i_1 = Math.Max(0, baseIndex - 1);
            int i0 = Math.Max(0, baseIndex);
            int i1 = Math.Min(frames - 1, baseIndex + 1);
            int i2 = Math.Min(frames - 1, baseIndex + 2);
            float y_1 = buf[i_1 * chCount + ch];
            float y0 = buf[i0 * chCount + ch];
            float y1 = buf[i1 * chCount + ch];
            float y2 = buf[i2 * chCount + ch];
            return CatmullRom(y_1, y0, y1, y2, (float)t);
        }

        private static float CatmullRom(float y0, float y1, float y2, float y3, float t)
        {
            float a0 = -0.5f*y0 + 1.5f*y1 - 1.5f*y2 + 0.5f*y3;
            float a1 = y0 - 2.5f*y1 + 2.0f*y2 - 0.5f*y3;
            float a2 = -0.5f*y0 + 0.5f*y2;
            float a3 = y1;
            return ((a0 * t + a1) * t + a2) * t + a3;
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
    }
}
