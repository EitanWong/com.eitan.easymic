using System;
using System.Threading;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Pure C# playback controller that manages a <see cref="PlaybackAudioSource"/>.
    /// Handles clip-backed playback (with background feeder thread) as well as
    /// manual streaming enqueue without requiring a MonoBehaviour wrapper.
    /// </summary>
    public sealed class PlaybackAudioSession : IDisposable
    {
        private const float QueueSeconds = .2f;
        private const double BufferHighWaterSeconds = 0.12;
        private const double BufferLowWaterSeconds = 0.03;
        private const double DefaultLargeClipWarningSeconds = 300.0;

        private readonly string _name;

        private PlaybackAudioSource _source;
        private AudioClip _clip;
        private bool _loop = true;
        private float _volume = 1f;
        private bool _mute;
        private bool _solo;

        private float[] _clipCache;
        private int _clipChannels;
        private int _clipSampleRate;
        private int _clipTotalSamples;
        private long _clipConvertedFrames;
        private int _positionFrames;
        private int _outputChannels;
        private int _outputSampleRate;

        private Thread _feedThread;
        private ManualResetEventSlim _stopSignal;
        private AutoResetEvent _wakeSignal;

        private enum SessionState : byte
        {
            Idle = 0,
            Streaming = 1,
            Draining = 2,
            Disposed = 3
        }

        private const int DefaultRampInMs = 5;
        private const int DefaultRampOutMs = 7;

        private readonly object _stateLock = new object();
        private readonly object _enqueueLock = new object();

        private SessionState _state = SessionState.Idle;
        private int _batchVersion;
        private int _pendingCompletionVersion = -1;
        private int _completionRequested; // 0/1 flag
        private bool _pendingStartRamp = true;
        private bool _disposed;
        private float[] _enqueueScratch;
        private float[] _tailScratch;
        private float[] _lastFrame;
        private bool _hasLastFrame;
        private bool _hasStreamedSinceLastIdle;

        public PlaybackAudioSession(string name = null)
        {
            _name = string.IsNullOrEmpty(name) ? "PlaybackSession" : name;
        }

        public PlaybackAudioSource Source => _source;

        /// <summary>
        /// Raised with the PCM contribution this session mixed into the output buffer.
        /// Signature matches Unity's OnAudioFilterRead semantics.
        /// </summary>
        public event Action<float[], int, int> OnAudioPlaybackRead;
        /// <summary>
        /// Raised once the current batch drains after <see cref="CompleteStream"/> or an enqueue with end-of-stream.
        /// Safe to enqueue new audio immediately after this fires.
        /// </summary>
        public event Action<PlaybackAudioSession> OnBatchCompleted;
        /// <summary>
        /// Raised whenever the internal pipeline transitions back to an idle state with no buffered audio.
        /// </summary>
        public event Action<PlaybackAudioSession> OnStreamIdle;
        /// <summary>
        /// Raised exactly once when the session is disposed.
        /// </summary>
        public event Action<PlaybackAudioSession> OnSessionDisposed;

        public AudioClip Clip => _clip;
        public long EstimatedClipCacheBytes => _clip == null ? 0L : (long)_clip.samples * Math.Max(1, _clip.channels) * sizeof(float);
        public double LargeClipWarningSeconds { get; set; } = DefaultLargeClipWarningSeconds;

        public bool Loop
        {
            get => _loop;
            set
            {
                if (_loop == value)
                {
                    return;
                }

                _loop = value;
                WakeFeeder();
            }
        }

        public bool IsPlaying => _source != null && _source.IsPlaying;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp(value, 0f, 2f);
                if (_source != null)
                {
                    _source.Volume = _volume;
                }
            }
        }

        public bool Mute
        {
            get => _mute;
            set
            {
                _mute = value;
                if (_source != null)
                {
                    _source.Mute = _mute;
                }
            }
        }

        public bool Solo
        {
            get => _solo;
            set
            {
                _solo = value;
                if (_source != null)
                {
                    _source.Solo = _solo;
                }
            }
        }

        public float BufferedSeconds => _source != null ? (float)_source.BufferedSeconds : 0f;
        public float ProgressNormalized => _source != null ? _source.NormalizedProgress : 0f;

        public void PlayClip(AudioClip clip, bool loop, bool autoPlay)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            _clip = clip;
            _loop = loop;

            if (_clip == null)
            {
                Stop(clearQueue: true, clearClip: true);
                return;
            }

            ConfigureClipPlayback(autoPlay);
        }

        public void Play()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            ResetTimeline(clearQueue: true, clearClip: false);
            EnsureSourceReady();
            if (_source == null)
            {
                return;
            }

            if (_clip != null)
            {
                EnsureClipCached();
                _clipConvertedFrames = EstimateClipConvertedFrames();
                _source.TotalSourceFrames = _clipConvertedFrames > 0 ? _clipConvertedFrames : -1;
                if (_clipCache != null && _clipCache.Length > 0 && _feedThread == null)
                {
                    StartFeeder();
                }
            }

            _source.Play();
            WakeFeeder();
        }

        public void Resume()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            _source?.Play();
            WakeFeeder();
        }

        public void Pause()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            _source?.Pause();
        }

        public void Stop(bool clearQueue = true, bool clearClip = false)
        {
            if (_disposed)
            {
                return;
            }

            ResetTimeline(clearQueue, clearClip);
            _source?.Pause();
            RaiseStreamIdle();
        }

        public void Enqueue(float[] samples, int count)
        {
            TryEnqueue(samples, count);
        }

        public EasyMicEnqueueResult TryEnqueue(float[] samples, int count)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            if (samples == null || count <= 0)
            {
                return EasyMicEnqueueResult.InvalidFormat();
            }

            int channels = _source != null ? _source.Channels : _outputChannels;
            int sampleRate = _source != null ? _source.SampleRate : _outputSampleRate;
            if (channels <= 0)
            {
                channels = Mathf.Max(1, SpeakerModeToChannels(AudioSettings.speakerMode));
            }
            if (sampleRate <= 0)
            {
                sampleRate = AudioSettings.outputSampleRate;
            }

            int toCopy = Mathf.Min(count, samples.Length);
            if (toCopy <= 0)
            {
                return EasyMicEnqueueResult.InvalidFormat(count);
            }

            return SubmitSamples(new ReadOnlySpan<float>(samples, 0, toCopy), channels, sampleRate, false);
        }

        public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
        {
            TryEnqueue(samples, count, channels, sampleRate, markEndOfStream);
        }

        public EasyMicEnqueueResult TryEnqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            if (samples == null || count <= 0 || channels <= 0 || sampleRate <= 0)
            {
                if (markEndOfStream)
                {
                    RequestDrainForEmptyStream();
                }
                return EasyMicEnqueueResult.InvalidFormat(count);
            }

            int toCopy = Mathf.Min(count, samples.Length);
            if (toCopy <= 0)
            {
                if (markEndOfStream)
                {
                    RequestDrainForEmptyStream();
                }
                return EasyMicEnqueueResult.InvalidFormat(count);
            }

            return SubmitSamples(new ReadOnlySpan<float>(samples, 0, toCopy), channels, sampleRate, markEndOfStream);
        }

        public void CompleteStream()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            PlaybackAudioSource source = null;
            bool completeImmediately = false;

            lock (_stateLock)
            {
                if (_state == SessionState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(PlaybackAudioSession));
                }

                if (_state == SessionState.Idle || _source == null)
                {
                    completeImmediately = true;
                }
                else
                {
                    if (_completionRequested == 1 && _pendingCompletionVersion == _batchVersion)
                    {
                        return;
                    }

                    _completionRequested = 1;
                    _pendingCompletionVersion = _batchVersion;
                    _state = SessionState.Draining;
                    source = _source;
                }
            }

            if (completeImmediately)
            {
                FinalizeBatchCompletion();
                return;
            }

            if (source == null)
            {
                return;
            }

            if (!TryInjectCompletionTail(source))
            {
                source.SignalEndOfStream();
            }

            WakeFeeder();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lock (_stateLock)
            {
                _state = SessionState.Disposed;
            }
            try
            {
                ResetTimeline(clearQueue: true, clearClip: true);
            }
            catch { }

            try
            {
                ReleaseSource();
            }
            catch { }

            FinalizeEventsOnDispose();
        }

        private void ConfigureClipPlayback(bool autoPlay)
        {
            EnsureSourceReady();
            if (_source == null)
            {
                return;
            }

            ResetTimeline(clearQueue: true, clearClip: true);
            EnsureClipCached();

            _clipConvertedFrames = EstimateClipConvertedFrames();
            _source.TotalSourceFrames = _clipConvertedFrames > 0 ? _clipConvertedFrames : -1;

            if (_clipCache != null && _clipCache.Length > 0)
            {
                StartFeeder();
            }

            if (autoPlay)
            {
                _source.Play();
                WakeFeeder();
            }
            else
            {
                _source.Pause();
            }
        }

        private void EnsureClipCached()
        {
            if (_clipCache != null && _clipCache.Length > 0)
            {
                return;
            }

            CacheClipData();
        }

        private void CacheClipData()
        {
            _clipCache = null;
            _clipTotalSamples = 0;
            _clipChannels = 0;
            _clipSampleRate = 0;

            if (_clip == null)
            {
                return;
            }

            try
            {
                if (_clip.loadState == AudioDataLoadState.Unloaded)
                {
                    _clip.LoadAudioData();
                }

                if (_clip.loadState == AudioDataLoadState.Loading)
                {
                    var spinner = new SpinWait();
                    int safety = 0;
                    while (_clip.loadState == AudioDataLoadState.Loading && safety < 100)
                    {
                        spinner.SpinOnce();
                        safety++;
                    }
                }

                if (_clip.loadState != AudioDataLoadState.Loaded)
                {
                    Debug.LogWarning($"EasyMic: Clip '{_clip.name}' not ready for playback; falling back to realtime feed.");
                    return;
                }

                int samples = _clip.samples;
                int channels = Mathf.Max(1, _clip.channels);
                if (samples <= 0)
                {
                    return;
                }

                long estimatedBytes = (long)samples * channels * sizeof(float);
                double duration = _clip.frequency > 0 ? samples / (double)_clip.frequency : 0.0;
                if (duration > LargeClipWarningSeconds)
                {
                    Debug.LogWarning(
                        $"EasyMic: Clip '{_clip.name}' is {duration:0.0}s and will preload about {estimatedBytes / (1024.0 * 1024.0):0.0} MB. Use explicit streaming for long-form audio.");
                }

                var buffer = new float[samples * channels];
                if (_clip.GetData(buffer, 0))
                {
                    _clipCache = buffer;
                    _clipChannels = channels;
                    _clipSampleRate = Mathf.Max(8000, _clip.frequency);
                    _clipTotalSamples = buffer.Length;
                    Volatile.Write(ref _positionFrames, 0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"EasyMic: Failed to cache clip '{_clip.name}'. {ex.Message}");
                _clipCache = null;
            }
        }

        private void StartFeeder()
        {
            if (_source == null)
            {
                return;
            }

            if (_clipCache == null || _clipCache.Length == 0)
            {
                return;
            }

            if (_feedThread != null)
            {
                return;
            }

            _stopSignal = new ManualResetEventSlim(false);
            _wakeSignal = new AutoResetEvent(false);
            _feedThread = new Thread(FeedLoop)
            {
                Name = $"EasyMicPlaybackFeed-{_name}",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.Highest
            };
            _feedThread.Start();
        }

        private void StopFeeder()
        {
            var thread = _feedThread;
            if (thread == null)
            {
                return;
            }

            try
            {
                _stopSignal?.Set();
                _wakeSignal?.Set();
                if (!thread.Join(100))
                {
                    thread.Join(500);
                }
            }
            catch { }
            finally
            {
                _feedThread = null;
                _stopSignal?.Dispose();
                _wakeSignal?.Dispose();
                _stopSignal = null;
                _wakeSignal = null;
            }
        }

        private void FeedLoop()
        {
            int clipFrames = _clipChannels > 0 ? _clipTotalSamples / Math.Max(1, _clipChannels) : 0;
            int cursorFrames = 0;

            while (!_stopSignal.IsSet)
            {
                try
                {
                    if (_stopSignal.IsSet)
                    {
                        break;
                    }

                    var src = _source;
                    if (src == null)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (!src.IsPlaying)
                    {
                        WaitFeeder();
                        continue;
                    }

                    if (_clipCache == null || _clipCache.Length == 0 || clipFrames <= 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (cursorFrames >= clipFrames)
                    {
                        if (Loop)
                        {
                            cursorFrames = 0;
                            Volatile.Write(ref _positionFrames, 0);
                            src.AnnounceLoopBoundary();
                            continue;
                        }

                        break;
                    }

                    double buffered = src.BufferedSeconds;
                    if (buffered > BufferHighWaterSeconds)
                    {
                        WaitFeeder();
                        continue;
                    }

                    int channels = Math.Max(1, _clipChannels);
                    int freeSamples = src.FreeSamples;
                    if (freeSamples <= channels)
                    {
                        WaitFeeder();
                        continue;
                    }

                    int framesBudget = Math.Min(freeSamples / channels, 2048);
                    if (framesBudget <= 0)
                    {
                        Thread.Sleep(2);
                        continue;
                    }

                    int framesRemaining = clipFrames - cursorFrames;
                    int framesToCopy = Math.Min(framesBudget, framesRemaining);
                    if (framesToCopy <= 0)
                    {
                        Thread.Sleep(2);
                        continue;
                    }

                    bool completing = !Loop && framesToCopy == framesRemaining;
                    int samplesToCopy = framesToCopy * channels;
                    var span = new ReadOnlySpan<float>(_clipCache, cursorFrames * channels, samplesToCopy);
                    SubmitSamples(span, channels, _clipSampleRate, completing);

                    cursorFrames += framesToCopy;
                    Volatile.Write(ref _positionFrames, Math.Min(cursorFrames, clipFrames));

                    if (completing)
                    {
                        continue;
                    }

                    if (Loop && cursorFrames >= clipFrames)
                    {
                        continue;
                    }

                    if (src.BufferedSeconds >= BufferLowWaterSeconds)
                    {
                        WaitFeeder();
                    }
                }
                catch
                {
                    Thread.Sleep(5);
                }
            }
        }

        private void WaitFeeder()
        {
            var wake = _wakeSignal;
            if (wake == null)
            {
                Thread.Sleep(4);
                return;
            }

            if (wake.WaitOne(4))
            {
                wake.Reset();
            }
        }

        private void WakeFeeder()
        {
            _wakeSignal?.Set();
        }

        private void EnsureAudioSystem()
        {
            var sys = AudioSystem.Instance;
            if (!sys.IsRunning)
            {
                sys.PreferNativeFormat();
                sys.Start();
            }
        }

        private void EnsureSourceReady()
        {
            EnsureAudioSystem();
            var sys = AudioSystem.Instance;

            int desiredChannels = sys.IsRunning ? (int)Math.Max(1u, sys.Channels) : Mathf.Max(1, SpeakerModeToChannels(AudioSettings.speakerMode));
            int desiredSampleRate = sys.IsRunning ? (int)Math.Max(8000u, sys.SampleRate) : Math.Max(8000, AudioSettings.outputSampleRate);

            if (_source != null && _source.Channels == desiredChannels && _source.SampleRate == desiredSampleRate)
            {
                ApplySourceProperties();
                return;
            }

            ReleaseSource();

            _outputChannels = desiredChannels;
            _outputSampleRate = desiredSampleRate;

            var mixer = sys.MasterMixer;
            _source = new PlaybackAudioSource(_outputChannels, _outputSampleRate, QueueSeconds, mixer)
            {
                name = _name,
                Volume = _volume,
                Mute = _mute,
                Solo = _solo
            };

            _source.OnAudioPlayback += HandleSourceAudioPlaybackRead;
            _source.OnPlaybackCompleted += HandleSourcePlaybackCompleted;
            _source.TotalSourceFrames = -1;
        }

        private void ApplySourceProperties()
        {
            if (_source == null)
            {
                return;
            }

            _source.Volume = _volume;
            _source.Mute = _mute;
            _source.Solo = _solo;
        }

        private void ReleaseSource()
        {
            if (_source == null)
            {
                return;
            }

            try { _source.OnAudioPlayback -= HandleSourceAudioPlaybackRead; } catch { }
            try { _source.OnPlaybackCompleted -= HandleSourcePlaybackCompleted; } catch { }

            try
            {
                var sys = AudioSystem.Instance;
                if (sys.IsRunning && sys.MasterMixer != null)
                {
                    sys.MasterMixer.RemoveSource(_source);
                }
            }
            catch { }

            try { _source.Dispose(); } catch { }
            _source = null;
        }

        private void ResetClipState(bool clearCache)
        {
            if (clearCache)
            {
                _clipCache = null;
                _clipTotalSamples = 0;
                _clipChannels = 0;
                _clipSampleRate = 0;
            }

            _clipConvertedFrames = 0;
            Volatile.Write(ref _positionFrames, 0);
            _hasLastFrame = false;
            _hasStreamedSinceLastIdle = false;
        }

        private void ResetTimeline(bool clearQueue, bool clearClip)
        {
            StopFeeder();
            if (_source != null && clearQueue)
            {
                _source.Stop();
                _source.ResetProgress();
                _source.TotalSourceFrames = -1;
            }

            ResetClipState(clearClip);

            lock (_stateLock)
            {
                if (_state != SessionState.Disposed)
                {
                    _state = SessionState.Idle;
                    _completionRequested = 0;
                    _pendingCompletionVersion = -1;
                    _pendingStartRamp = true;
                }
            }
        }

        private long EstimateClipConvertedFrames()
        {
            if (_clip == null || _source == null)
            {
                return 0;
            }

            if (_clipSampleRate <= 0)
            {
                return 0;
            }

            double ratio = (double)_source.SampleRate / Math.Max(1, _clipSampleRate);
            double frames = Math.Ceiling(_clip.samples * ratio);
            return frames < 1 ? 0 : (long)frames;
        }

        private EasyMicEnqueueResult SubmitSamples(ReadOnlySpan<float> samples, int channels, int sampleRate, bool markEndOfStream)
        {
            int requestedSamples = channels > 0 ? (samples.Length / channels) * channels : samples.Length;
            if (samples.IsEmpty)
            {
                if (markEndOfStream)
                {
                    RequestDrainForEmptyStream();
                }
                return EasyMicEnqueueResult.InvalidFormat(requestedSamples);
            }

            EnsureSourceReady();
            if (_source == null)
            {
                return new EasyMicEnqueueResult(EasyMicQueueStatus.DeviceLost, requestedSamples, 0);
            }

            bool applyStartRamp;
            if (!PrepareStreamingState(samples.Length, out applyStartRamp))
            {
                if (markEndOfStream)
                {
                    RequestDrainForEmptyStream();
                }
                return new EasyMicEnqueueResult(EasyMicQueueStatus.Stopped, requestedSamples, 0);
            }

            ApplySourceProperties();

            ReadOnlySpan<float> spanToEnqueue = samples;
            bool applyEndRamp = markEndOfStream;

            if (applyStartRamp || applyEndRamp)
            {
                Span<float> scratch = GetScratchSpan(samples.Length);
                samples.CopyTo(scratch);
                if (applyStartRamp)
                {
                    ApplyFadeIn(scratch, channels, sampleRate);
                }
                if (applyEndRamp)
                {
                    ApplyFadeOut(scratch, channels, sampleRate);
                }
                spanToEnqueue = scratch;
            }

            int written;
            lock (_enqueueLock)
            {
                written = _source.Enqueue(spanToEnqueue, channels, sampleRate, markEndOfStream);
                if (written <= 0)
                {
                    return EasyMicEnqueueResult.FromWrite(requestedSamples, 0);
                }
            }

            UpdateLastFrame(spanToEnqueue.Slice(0, Math.Min(written, spanToEnqueue.Length)), channels);
            _hasStreamedSinceLastIdle = true;

            if (!_source.IsPlaying)
            {
                _source.Play();
            }

            if (markEndOfStream && _source.HasPendingStreamEnd)
            {
                MarkCompletionRequested();
            }

            return EasyMicEnqueueResult.FromWrite(requestedSamples, written);
        }

        private bool PrepareStreamingState(int sampleCount, out bool applyStartRamp)
        {
            applyStartRamp = false;
            if (sampleCount <= 0)
            {
                return false;
            }

            lock (_stateLock)
            {
                if (_state == SessionState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(PlaybackAudioSession));
                }

                if (_state != SessionState.Streaming)
                {
                    _state = SessionState.Streaming;
                    _batchVersion++;
                    _completionRequested = 0;
                    _pendingCompletionVersion = -1;
                    _pendingStartRamp = true;
                    _hasLastFrame = false;
                    _hasStreamedSinceLastIdle = false;
                }

                if (_pendingStartRamp)
                {
                    applyStartRamp = true;
                    _pendingStartRamp = false;
                }
            }

            return true;
        }

        private void RequestDrainForEmptyStream()
        {
            var source = _source;
            if (source == null)
            {
                FinalizeBatchCompletion();
                return;
            }

            lock (_stateLock)
            {
                if (_state == SessionState.Disposed)
                {
                    return;
                }

                if (_completionRequested == 1 && _pendingCompletionVersion == _batchVersion)
                {
                    return;
                }

                _completionRequested = 1;
                _pendingCompletionVersion = _batchVersion;
                _state = SessionState.Draining;
            }

            source.SignalEndOfStream();
        }

        private void MarkCompletionRequested()
        {
            lock (_stateLock)
            {
                if (_state == SessionState.Disposed)
                {
                    return;
                }

                _completionRequested = 1;
                _pendingCompletionVersion = _batchVersion;
                if (_state != SessionState.Draining)
                {
                    _state = SessionState.Draining;
                }
            }
        }

        private Span<float> GetScratchSpan(int sampleCount)
        {
            if (sampleCount <= 0)
            {
                return Span<float>.Empty;
            }

            EnsureScratchCapacity(ref _enqueueScratch, sampleCount);
            return new Span<float>(_enqueueScratch, 0, sampleCount);
        }

        private static void EnsureScratchCapacity(ref float[] buffer, int requiredSamples)
        {
            if (requiredSamples <= 0)
            {
                return;
            }

            if (buffer != null && buffer.Length >= requiredSamples)
            {
                return;
            }

            int newSize = buffer == null || buffer.Length == 0 ? 1024 : buffer.Length;
            while (newSize < requiredSamples)
            {
                newSize *= 2;
            }

            buffer = new float[newSize];
        }

        private static int MillisecondsToSampleCount(int milliseconds, int sampleRate, int channels)
        {
            if (milliseconds <= 0 || sampleRate <= 0 || channels <= 0)
            {
                return 0;
            }

            double frames = (double)sampleRate * milliseconds / 1000.0;
            int samples = (int)Math.Ceiling(frames) * channels;
            int stride = Math.Max(1, channels);
            int remainder = samples % stride;
            if (remainder != 0)
            {
                samples += stride - remainder;
            }

            return samples;
        }

        private void ApplyFadeIn(Span<float> buffer, int channels, int sampleRate)
        {
            int totalSamples = buffer.Length;
            int fadeSamples = Math.Min(totalSamples, MillisecondsToSampleCount(DefaultRampInMs, sampleRate, Math.Max(1, channels)));
            if (fadeSamples <= 0)
            {
                return;
            }

            int stride = Math.Max(1, channels);
            int frames = fadeSamples / stride;
            if (frames <= 0)
            {
                return;
            }

            for (int frame = 0; frame < frames; frame++)
            {
                float gain = (frame + 1f) / (frames + 1f);
                int baseIndex = frame * stride;
                for (int ch = 0; ch < stride; ch++)
                {
                    int idx = baseIndex + ch;
                    if (idx < buffer.Length)
                    {
                        buffer[idx] *= gain;
                    }
                }
            }
        }

        private void ApplyFadeOut(Span<float> buffer, int channels, int sampleRate)
        {
            int totalSamples = buffer.Length;
            int fadeSamples = Math.Min(totalSamples, MillisecondsToSampleCount(DefaultRampOutMs, sampleRate, Math.Max(1, channels)));
            if (fadeSamples <= 0)
            {
                return;
            }

            int stride = Math.Max(1, channels);
            int frames = fadeSamples / stride;
            if (frames <= 0)
            {
                return;
            }

            int startIndex = Math.Max(0, totalSamples - frames * stride);
            for (int frame = 0; frame < frames; frame++)
            {
                float gain = 1f - ((frame + 1f) / (frames + 1f));
                int baseIndex = startIndex + frame * stride;
                for (int ch = 0; ch < stride; ch++)
                {
                    int idx = baseIndex + ch;
                    if (idx < buffer.Length)
                    {
                        buffer[idx] *= gain;
                    }
                }
            }
        }

        private void UpdateLastFrame(ReadOnlySpan<float> buffer, int channels)
        {
            int stride = Math.Max(1, channels);
            if (buffer.Length < stride)
            {
                return;
            }

            EnsureLastFrameCapacity(stride);
            int start = buffer.Length - stride;
            for (int ch = 0; ch < stride; ch++)
            {
                _lastFrame[ch] = buffer[start + ch];
            }
            _hasLastFrame = true;
        }

        private void EnsureLastFrameCapacity(int channels)
        {
            if (_lastFrame != null && _lastFrame.Length >= channels)
            {
                return;
            }

            _lastFrame = new float[channels];
        }

        private bool TryInjectCompletionTail(PlaybackAudioSource source)
        {
            if (source == null)
            {
                return false;
            }

            int channels = Math.Max(1, source.Channels);
            int sampleRate = Math.Max(8000, source.SampleRate);
            if (!_hasLastFrame)
            {
                return false;
            }

            int samples = MillisecondsToSampleCount(DefaultRampOutMs, sampleRate, channels);
            if (samples <= 0)
            {
                return false;
            }

            bool success = false;
            lock (_enqueueLock)
            {
                EnsureScratchCapacity(ref _tailScratch, samples);
                var span = new Span<float>(_tailScratch, 0, samples);
                int stride = channels;
                int frames = samples / stride;
                int cachedChannels = _lastFrame != null ? _lastFrame.Length : 0;
                if (cachedChannels > 0)
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float gain = 1f - ((frame + 1f) / (frames + 1f));
                        int baseIndex = frame * stride;
                        for (int ch = 0; ch < stride; ch++)
                        {
                            int sampleIndex = ch < cachedChannels
                                ? ch
                                : cachedChannels == 1 ? 0 : ch % cachedChannels;
                            span[baseIndex + ch] = _lastFrame[sampleIndex] * gain;
                        }
                    }

                    source.Enqueue(span, channels, sampleRate, markEndOfStream: true);
                    success = source.HasPendingStreamEnd;
                }
            }

            if (success)
            {
                _hasLastFrame = false;
            }
            return success;
        }

        private void FinalizeBatchCompletion()
        {
            bool raiseBatch = false;
            bool raiseIdle = false;

            lock (_stateLock)
            {
                if (_state == SessionState.Disposed)
                {
                    return;
                }

                raiseBatch = _hasStreamedSinceLastIdle;
                _state = SessionState.Idle;
                _completionRequested = 0;
                _pendingCompletionVersion = -1;
                _pendingStartRamp = true;
                _hasLastFrame = false;
                _hasStreamedSinceLastIdle = false;
                raiseIdle = true;
            }

            if (raiseBatch)
            {
                RaiseBatchCompleted();
            }

            if (raiseIdle)
            {
                RaiseStreamIdle();
            }
        }

        private void RaiseBatchCompleted()
        {
            try { OnBatchCompleted?.Invoke(this); } catch { }
        }

        private void RaiseStreamIdle()
        {
            try { OnStreamIdle?.Invoke(this); } catch { }
        }

        private void FinalizeEventsOnDispose()
        {
            RaiseStreamIdle();
            try { OnSessionDisposed?.Invoke(this); } catch { }
        }

        private void HandleSourcePlaybackCompleted(PlaybackAudioSource source)
        {
            bool finalize = false;
            bool raiseIdle = false;

            lock (_stateLock)
            {
                if (_state == SessionState.Disposed)
                {
                    return;
                }

                if (_completionRequested == 1 && _pendingCompletionVersion == _batchVersion)
                {
                    finalize = true;
                }
                else if (_state != SessionState.Idle)
                {
                    _state = SessionState.Idle;
                    _pendingStartRamp = true;
                    _completionRequested = 0;
                    _pendingCompletionVersion = -1;
                    _hasLastFrame = false;
                    _hasStreamedSinceLastIdle = false;
                    raiseIdle = true;
                }
            }

            if (finalize)
            {
                FinalizeBatchCompletion();
            }
            else if (raiseIdle)
            {
                RaiseStreamIdle();
            }
        }

        private static int SpeakerModeToChannels(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        private void HandleSourceAudioPlaybackRead(float[] data, int channels, int sampleRate)
        {
            try { OnAudioPlaybackRead?.Invoke(data, channels, sampleRate); }
            catch { }
        }
    }
}
