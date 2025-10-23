using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Global static entry point for EasyMic playback. Provides lightweight handles for
    /// clip-backed or streaming playback without requiring a MonoBehaviour.
    /// </summary>
    public static class AudioPlayback
    {
        private static readonly object s_lock = new object();
        private static readonly Dictionary<int, ManagedPlayback> s_playbacks = new Dictionary<int, ManagedPlayback>();
        private static int s_nextId = 1;

        public static PlaybackHandle PlayClip(AudioClip clip, bool loop = false, float volume = 1f, bool autoDisposeOnComplete = true)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            bool dispose = autoDisposeOnComplete && !loop;
            var handle = CreatePlayback(dispose, out var playback);
            playback.Volume = volume;
            playback.ConfigureClip(clip, loop, autoPlay: true);
            return handle;
        }

        public static PlaybackHandle CreateStream(int? preferredChannels = null, int? preferredSampleRate = null, float volume = 1f, bool autoDisposeOnComplete = false)
        {
            var handle = CreatePlayback(autoDisposeOnComplete, out var playback);
            playback.Volume = volume;
            playback.ConfigureStream(preferredChannels, preferredSampleRate);
            return handle;
        }

        internal static bool IsValid(int id)
        {
            lock (s_lock)
            {
                return s_playbacks.ContainsKey(id);
            }
        }

        internal static void Play(int id)
        {
            if (TryGet(id, out var playback))
            {
                playback.Play();
            }
        }

        internal static void Pause(int id)
        {
            if (TryGet(id, out var playback))
            {
                playback.Pause();
            }
        }

        internal static void Stop(int id)
        {
            if (TryGet(id, out var playback))
            {
                playback.Stop();
            }
        }

        internal static void DisposeHandle(int id)
        {
            ManagedPlayback playback;
            lock (s_lock)
            {
                if (!s_playbacks.TryGetValue(id, out playback))
                {
                    return;
                }

                s_playbacks.Remove(id);
            }

            playback.Dispose();
        }

        internal static void Enqueue(int id, float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
        {
            if (TryGet(id, out var playback))
            {
                playback.Enqueue(samples, count, channels, sampleRate, markEndOfStream);
            }
        }

        internal static void CompleteStream(int id)
        {
            if (TryGet(id, out var playback))
            {
                playback.CompleteStream();
            }
        }

        internal static bool IsPlaying(int id)
        {
            return TryGet(id, out var playback) && playback.IsPlaying;
        }

        internal static double GetBufferedSeconds(int id)
        {
            return TryGet(id, out var playback) ? playback.BufferedSeconds : 0.0;
        }

        internal static float GetVolume(int id)
        {
            return TryGet(id, out var playback) ? playback.Volume : 0f;
        }

        internal static void SetVolume(int id, float value)
        {
            if (TryGet(id, out var playback))
            {
                playback.Volume = value;
            }
        }

        internal static void RegisterCompletionCallback(int id, Action callback, bool invokeIfCompleted)
        {
            if (callback == null)
            {
                return;
            }

            if (TryGet(id, out var playback))
            {
                playback.RegisterCompletionCallback(callback, invokeIfCompleted);
            }
        }

        private static PlaybackHandle CreatePlayback(bool autoDisposeOnComplete, out ManagedPlayback playback)
        {
            int id;
            lock (s_lock)
            {
                id = s_nextId++;
                playback = new ManagedPlayback(id, autoDisposeOnComplete, HandlePlaybackCompleted);
                s_playbacks[id] = playback;
            }

            return new PlaybackHandle(id);
        }

        private static bool TryGet(int id, out ManagedPlayback playback)
        {
            lock (s_lock)
            {
                return s_playbacks.TryGetValue(id, out playback);
            }
        }

        private static void HandlePlaybackCompleted(int id)
        {
            ManagedPlayback playback;
            bool remove;
            lock (s_lock)
            {
                if (!s_playbacks.TryGetValue(id, out playback))
                {
                    return;
                }

                remove = playback.AutoDisposeOnComplete;
                if (remove)
                {
                    s_playbacks.Remove(id);
                }
            }

            playback.NotifyCompletion();

            if (remove)
            {
                playback.Dispose();
            }
        }
    }

    public readonly struct PlaybackHandle : IDisposable
    {
        private readonly int _id;

        internal PlaybackHandle(int id)
        {
            _id = id;
        }

        public bool IsValid => AudioPlayback.IsValid(_id);

        public void Play() => AudioPlayback.Play(_id);
        public void Pause() => AudioPlayback.Pause(_id);
        public void Stop() => AudioPlayback.Stop(_id);
        public void Dispose() => AudioPlayback.DisposeHandle(_id);
        public void CompleteStream() => AudioPlayback.CompleteStream(_id);

        public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
            => AudioPlayback.Enqueue(_id, samples, count, channels, sampleRate, markEndOfStream);

        public bool IsPlaying => AudioPlayback.IsPlaying(_id);
        public double BufferedSeconds => AudioPlayback.GetBufferedSeconds(_id);

        public float Volume
        {
            get => AudioPlayback.GetVolume(_id);
            set => AudioPlayback.SetVolume(_id, value);
        }

        public void RegisterCompletedCallback(Action callback, bool invokeIfCompleted = true)
            => AudioPlayback.RegisterCompletionCallback(_id, callback, invokeIfCompleted);
    }

    internal sealed class ManagedPlayback : IDisposable
    {
        private const string LogPrefix = "[AudioPlayback.ManagedPlayback] ";
        private const float QueueSeconds = 1f;
        // Mirror the proven MonoBehaviour feeder thresholds for steady buffering.
        private const double BufferHighWaterSeconds = 0.12;
        private const double BufferLowWaterSeconds = 0.03;

        private readonly int _id;
        private readonly Action<int> _onCompleted;
        private readonly object _callbacksLock = new object();
        private readonly List<Action> _completionCallbacks = new List<Action>();

        private PlaybackAudioSource _source;
        private volatile bool _disposed;

        private AudioClip _clip;
        private bool _loop;
        private float[] _clipCache;
        private int _clipChannels;
        private int _clipSampleRate;
        private int _clipTotalSamples;
        private long _clipConvertedFrames;
        private bool _clipFirstChunkLogged;
        private bool _clipDropLogged;
        private bool _clipEndLogged;

        private Thread _feedThread;
        private ManualResetEventSlim _stopSignal;
        private AutoResetEvent _wakeSignal;

        private int _positionFrames;
        private volatile bool _completed;

        // --- Streaming-specific state ---
        private volatile bool _streamMode;
        private volatile bool _endOfStreamSignaled;
        private volatile bool _streamFormatLocked;
        private volatile bool _streamPlaybackStarted;
        private volatile bool _eosSignaledToSource;

        public ManagedPlayback(int id, bool autoDisposeOnComplete, Action<int> onCompleted)
        {
            _id = id;
            AutoDisposeOnComplete = autoDisposeOnComplete;
            _onCompleted = onCompleted;
            EnsureSourceReady();
        }

        public bool AutoDisposeOnComplete { get; }

        public bool IsPlaying => _source != null && _source.IsPlaying;
        public double BufferedSeconds => _source != null ? _source.BufferedSeconds : 0.0;

        public float Volume
        {
            get => _source != null ? _source.Volume : _volume;
            set
            {
                _volume = Mathf.Clamp(value, 0f, 2f);
                if (_source != null)
                {
                    _source.Volume = _volume;
                }
            }
        }

        private float _volume = 1f;

        public void ConfigureClip(AudioClip clip, bool loop, bool autoPlay)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            _streamMode = false;
            _loop = loop;

            ResetTimeline(clearQueue: true, clearClip: true);
            _clip = clip;
            CacheClipData();
            _clipConvertedFrames = EstimateClipConvertedFrames();
            if (_source != null)
            {
                _source.TotalSourceFrames = _clipConvertedFrames > 0 ? _clipConvertedFrames : -1;
            }

            if (_clipCache != null && _clipCache.Length > 0)
            {
                StartFeeder();
            }

            if (autoPlay)
            {
                Play();
            }
        }

        public void ConfigureStream(int? preferredChannels, int? preferredSampleRate)
        {
            _streamMode = true;
            _endOfStreamSignaled = false;
            _streamFormatLocked = false;
            _streamPlaybackStarted = false;
            _eosSignaledToSource = false;

            ResetTimeline(clearQueue: true, clearClip: true);

            // Recreate source with preferred format, but it can be overridden by the first chunk.
            if (_source != null)
            {
                int channels = preferredChannels.HasValue ? Mathf.Max(1, preferredChannels.Value) : _source.Channels;
                int sampleRate = preferredSampleRate.HasValue ? Mathf.Max(8000, preferredSampleRate.Value) : _source.SampleRate;
                if (_source.Channels != channels || _source.SampleRate != sampleRate)
                {
                    RecreateSource(channels, sampleRate);
                }
            }

            _clip = null;
            _clipCache = null;
            _clipChannels = 0;
            _clipSampleRate = 0;
            _clipTotalSamples = 0;
            _clipConvertedFrames = 0;
            _completed = false;
            _loop = false;

            StartFeeder();
        }

        public void Play()
        {
            if (_source == null)
            {
                return;
            }

            if (_streamMode)
            {
                if (!_streamPlaybackStarted)
                {
                    _streamPlaybackStarted = true;
                    Debug.Log($"{LogPrefix}Handle {_id}: Manually starting stream playback.");
                }
            }
            else if (_clip != null && _clipCache != null && _clipCache.Length > 0 && _feedThread == null)
            {
                StartFeeder();
            }

            _source.Play();
            WakeFeeder();
        }

        public void Pause()
        {
            _source?.Pause();
        }

        public void Stop()
        {
            ResetTimeline(clearQueue: true, clearClip: false);
            _source?.Pause();
        }

        public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
        {
            if (_disposed || _source == null)
            {
                return;
            }


            if (markEndOfStream && !_endOfStreamSignaled)
            {
                _endOfStreamSignaled = true;
                Debug.Log($"{LogPrefix}Handle {_id}: End-of-stream marker received.");
                WakeFeeder();
            }

            if (samples == null || count <= 0)
            {
                return;
            }


            if (_eosSignaledToSource)
            {
                Debug.LogWarning($"{LogPrefix}Handle {_id}: Audio chunk received after end-of-stream signal. Dropping.");
                return;
            }

            // 锁定/校验流格式（保持你原有逻辑）
            if (!_streamFormatLocked)
            {
                if (channels <= 0 || sampleRate <= 0)
                {
                    Debug.LogWarning($"{LogPrefix}Handle {_id}: Dropping initial invalid chunk (0 channels or 0 sample rate).");
                    return;
                }
                if (_source.Channels != channels || _source.SampleRate != sampleRate)
                {
                    Debug.Log($"{LogPrefix}Handle {_id}: First chunk received. Recreating source to match stream format: {channels}ch @ {sampleRate}Hz.");
                    RecreateSource(channels, sampleRate);
                }
                else
                {
                    Debug.Log($"{LogPrefix}Handle {_id}: First chunk received. Source format matches stream: {channels}ch @ {sampleRate}Hz.");
                }
                _streamFormatLocked = true;
            }
            else if (channels != _source.Channels || sampleRate != _source.SampleRate)
            {
                Debug.LogWarning($"{LogPrefix}Handle {_id}: Dropping chunk with mismatched audio format. Expected: {_source.Channels}ch @ {_source.SampleRate}Hz, Received: {channels}ch @ {sampleRate}Hz.");
                return;
            }

            if (!_streamPlaybackStarted)
            {
                _source.Play();
                _streamPlaybackStarted = true;
                Debug.Log($"{LogPrefix}Handle {_id}: Starting stream playback on first chunk.");
            }

            int capped = Mathf.Min(count, samples.Length);

            // ① 帧齐整：确保样本数是通道数的整数倍
            int remainder = capped % channels;
            if (remainder != 0)
            {
                int aligned = capped - remainder;
                if (aligned <= 0)
                {
                    if (markEndOfStream)
                    {
                        _source.SignalEndOfStream();   // 极端情况下也要把 EOS 发出去
                        _eosSignaledToSource = true;
                    }
                    Debug.LogWarning($"{LogPrefix}Handle {_id}: Dropping {remainder} trailing samples to preserve frame alignment ({channels} ch).");
                    return;
                }
                Debug.LogWarning($"{LogPrefix}Handle {_id}: Dropping {remainder} trailing samples to preserve frame alignment ({channels} ch).");
                capped = aligned;
            }

            // ② 发送
            int written = _source.Enqueue(new ReadOnlySpan<float>(samples, 0, capped), channels, sampleRate, markEndOfStream);
            if (markEndOfStream)
            {
                _eosSignaledToSource = true;
            }

            // ③ 可选：记录部分写入（有助定位丢帧）
            if (written <= 0)
            {
                Debug.LogWarning($"{LogPrefix}Handle {_id}: Stream chunk not written (written <= 0). FreeSamples={_source.FreeSamples}, Buffered={_source.BufferedSeconds:F3}s.");
            }
        }

        public void CompleteStream()
        {
            _loop = false;
            if (!_endOfStreamSignaled)
            {
                _endOfStreamSignaled = true;
                Debug.Log($"{LogPrefix}Handle {_id}: CompleteStream() called explicitly.");
            }

            // 立即把 EOS 发给底层，避免只靠 watchdog
            if (_source != null && !_eosSignaledToSource)
            {
                _source.SignalEndOfStream();
                _eosSignaledToSource = true;
            }

            WakeFeeder();
        }
        public void RegisterCompletionCallback(Action callback, bool invokeIfCompleted)
        {
            if (callback == null)
            {
                return;
            }

            bool alreadyCompleted;
            lock (_callbacksLock)
            {
                alreadyCompleted = _completed;
                if (!alreadyCompleted)
                {
                    _completionCallbacks.Add(callback);
                }
            }

            if (alreadyCompleted && invokeIfCompleted)
            {
                try { callback(); } catch { }
            }
        }

        public void NotifyCompletion()
        {
            List<Action> callbacks = null;
            lock (_callbacksLock)
            {
                if (_loop)
                {
                    if (_completionCallbacks.Count > 0)
                    {
                        callbacks = new List<Action>(_completionCallbacks);
                    }
                }
                else if (!_completed)
                {
                    _completed = true;
                    callbacks = new List<Action>(_completionCallbacks);
                    _completionCallbacks.Clear();
                    Debug.Log($"{LogPrefix}Handle {_id}: Notifying {callbacks.Count} completion callbacks.");
                }
            }

            if (callbacks != null)
            {
                for (int i = 0; i < callbacks.Count; i++)
                {
                    var cb = callbacks[i];
                    if (cb == null)
                    {
                        continue;
                    }

                    try { cb(); } catch { }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ResetTimeline(clearQueue: true, clearClip: true);

            if (_source != null)
            {
                try { _source.OnPlaybackCompleted -= HandlePlaybackCompleted; } catch { }
                try { _source.OnAudioPlayback -= HandlePlaybackAudio; } catch { }

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

            lock (_callbacksLock)
            {
                _completionCallbacks.Clear();
            }
        }

        private void EnsureSourceReady()
        {
            var sys = AudioSystem.Instance;
            if (!sys.IsRunning)
            {
                sys.PreferNativeFormat();
                sys.Start();
            }

            int channels = sys.IsRunning ? (int)Math.Max(1u, sys.Channels) : Mathf.Max(1, SpeakerModeToChannels(AudioSettings.speakerMode));
            int sampleRate = sys.IsRunning ? (int)Math.Max(8000u, sys.SampleRate) : Math.Max(8000, AudioSettings.outputSampleRate);

            RecreateSource(channels, sampleRate);
        }

        private void RecreateSource(int channels, int sampleRate)
        {
            if (_source != null && _source.Channels == channels && _source.SampleRate == sampleRate)
            {
                return;
            }

            if (_source != null)
            {
                try { _source.OnPlaybackCompleted -= HandlePlaybackCompleted; } catch { }
                try { _source.OnAudioPlayback -= HandlePlaybackAudio; } catch { }

                try
                {
                    var sysPrev = AudioSystem.Instance;
                    if (sysPrev.IsRunning && sysPrev.MasterMixer != null)
                    {
                        sysPrev.MasterMixer.RemoveSource(_source);
                    }
                }
                catch { }

                try { _source.Dispose(); } catch { }
                _source = null;
            }

            var sys = AudioSystem.Instance;
            var mixer = sys.MasterMixer;
            _source = new PlaybackAudioSource(channels, sampleRate, QueueSeconds, mixer)
            {
                Volume = _volume
            };

            _source.OnPlaybackCompleted += HandlePlaybackCompleted;
            _source.OnAudioPlayback += HandlePlaybackAudio;
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

            if (clearClip)
            {
                _clipCache = null;
                _clipChannels = 0;
                _clipSampleRate = 0;
                _clipTotalSamples = 0;
                _clipConvertedFrames = 0;
                _clip = null;
            }

            Volatile.Write(ref _positionFrames, 0);
            _completed = false;
            _clipFirstChunkLogged = false;
            _clipDropLogged = false;
            _clipEndLogged = false;
        }

        private void CacheClipData()
        {
            _clipCache = null;
            _clipChannels = 0;
            _clipSampleRate = 0;
            _clipTotalSamples = 0;
            Volatile.Write(ref _positionFrames, 0);

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
                    Debug.LogWarning($"EasyMic: Clip '{_clip.name}' not ready for static playback.");
                    return;
                }

                int samples = _clip.samples;
                int channels = Mathf.Max(1, _clip.channels);
                if (samples <= 0)
                {
                    return;
                }

                var buffer = new float[samples * channels];
                if (_clip.GetData(buffer, 0))
                {
                    _clipCache = buffer;
                    _clipChannels = channels;
                    _clipSampleRate = Mathf.Max(8000, _clip.frequency);
                    _clipTotalSamples = buffer.Length;
                    Volatile.Write(ref _positionFrames, 0);
                    _clipFirstChunkLogged = false;
                    _clipDropLogged = false;
                    _clipEndLogged = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"EasyMic: Failed to cache clip '{_clip.name}'. {ex.Message}");
                _clipCache = null;
            }
        }

        private long EstimateClipConvertedFrames()
        {
            if (_clip == null || _source == null || _clipSampleRate <= 0)
            {
                return 0;
            }

            int clipFrames = Mathf.Max(0, _clip.samples);
            if (clipFrames == 0)
            {
                return 0;
            }

            double ratio = (double)_source.SampleRate / Math.Max(1, _clipSampleRate);
            double frames = Math.Ceiling(clipFrames * ratio);
            return frames < 1 ? 0 : (long)frames;
        }

        private void StartFeeder()
        {
            if (_source == null)
            {
                return;
            }

            bool shouldStart = _streamMode || (_clipCache != null && _clipCache.Length > 0);
            if (!shouldStart)
            {
                return;
            }

            StopFeeder();
            _stopSignal = new ManualResetEventSlim(false);
            _wakeSignal = new AutoResetEvent(false);
            _feedThread = new Thread(UnifiedFeedLoop)
            {
                Name = $"EasyMicPlayback-Feed-{_id}",
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
                WakeFeeder();
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

        private void UnifiedFeedLoop()
        {

            if (_streamMode)
            {
                StreamWatchdogLoop();
            }
            else
            {
                ClipFeedLoop();
            }

        }

        private void StreamWatchdogLoop()
        {
            Debug.Log($"{LogPrefix}Stream watchdog started for handle {_id}.");
            while (!_stopSignal.IsSet)
            {
                try
                {
                    var src = _source;
                    if (src == null || _disposed)
                    {
                        break;
                    }

                    if (_endOfStreamSignaled && !_eosSignaledToSource)
                    {
                        src.SignalEndOfStream();
                        _eosSignaledToSource = true;
                        Debug.Log($"{LogPrefix}Handle {_id}: Watchdog has signaled end-of-stream to the underlying source.");
                    }

                    if (_endOfStreamSignaled && !src.IsPlaying && src.BufferedSeconds < 0.01)
                    {
                        Debug.Log($"{LogPrefix}Stream watchdog detected completion for handle {_id}. Buffered: {src.BufferedSeconds:F3}s, IsPlaying: {src.IsPlaying}.");
                        HandlePlaybackCompleted(src);
                        break;
                    }

                    // 空指针安全等待
                    var wake = _wakeSignal;
                    if (wake != null)
                    {
                        wake.WaitOne(20);
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }

                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix}Stream watchdog loop for handle {_id} encountered an error: {ex.Message}");
                    Thread.Sleep(50);
                }
            }
            Debug.Log($"{LogPrefix}Stream watchdog stopped for handle {_id}.");
        }
        private void ClipFeedLoop()
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
                        if (_loop)
                        {
                            cursorFrames = 0;
                            Volatile.Write(ref _positionFrames, 0);
                            src.AnnounceLoopBoundary();
                            continue;
                        }
                        else
                        {
                            break;
                        }
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

                    bool completing = !_loop && framesToCopy == framesRemaining;
                    int samplesToCopy = framesToCopy * channels;
                    var span = new ReadOnlySpan<float>(_clipCache, cursorFrames * channels, samplesToCopy);
                    LogClipFirstChunk(src);
                    int expectedTargetSamples = EstimateTargetSamples(src, framesToCopy);
                    int written = src.Enqueue(span, channels, _clipSampleRate, completing);
                    if (expectedTargetSamples > 0 && written < expectedTargetSamples)
                    {
                        LogClipDrop(framesToCopy, expectedTargetSamples, written, src);
                    }
                    else if (written <= 0)
                    {
                        LogClipDrop(framesToCopy, expectedTargetSamples, written, src);
                    }

                    cursorFrames += framesToCopy;
                    Volatile.Write(ref _positionFrames, Math.Min(cursorFrames, clipFrames));

                    if (completing)
                    {
                        LogClipEndOfStream();
                        continue;
                    }

                    if (_loop && cursorFrames >= clipFrames)
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

            try
            {
                if (wake.WaitOne(4))
                {
                    wake.Reset();
                }
            }
            catch (ObjectDisposedException)
            {
                // Feeder is tearing down; nothing to wait on.
            }
        }

        private void WakeFeeder()
        {
            try
            {
                _wakeSignal?.Set();
            }
            catch (ObjectDisposedException)
            {
                // Feeder already stopped.
            }
        }

        private void LogClipFirstChunk(PlaybackAudioSource source)
        {
            if (_clipFirstChunkLogged || source == null)
            {
                return;
            }

            _clipFirstChunkLogged = true;
            string clipName = _clip != null ? _clip.name : "<clip>";
            Debug.Log($"{LogPrefix}Handle {_id}: First clip chunk enqueued. Clip '{clipName}' {_clipChannels}ch @{_clipSampleRate}Hz -> Output {source.Channels}ch @{source.SampleRate}Hz.");
        }

        private void LogClipDrop(int clipFramesRequested, int expectedSamples, int writtenSamples, PlaybackAudioSource source)
        {
            if (_clipDropLogged)
            {
                return;
            }

            _clipDropLogged = true;
            double buffered = source != null ? source.BufferedSeconds : 0.0;
            int free = source != null ? source.FreeSamples : 0;
            Debug.LogWarning($"{LogPrefix}Handle {_id}: Clip chunk truncated. Requested {clipFramesRequested} frames (~{expectedSamples} samples), wrote {writtenSamples}. Buffered={buffered:F3}s, FreeSamples={free}.");
        }

        private void LogClipEndOfStream()
        {
            if (_clipEndLogged)
            {
                return;
            }

            _clipEndLogged = true;
            Debug.Log($"{LogPrefix}Handle {_id}: Clip playback enqueued end-of-stream marker.");
        }

        private int EstimateTargetSamples(PlaybackAudioSource source, int clipFrames)
        {
            if (source == null || clipFrames <= 0 || _clipSampleRate <= 0)
            {
                return 0;
            }

            int destChannels = Math.Max(1, source.Channels);
            int clipRate = Math.Max(1, _clipSampleRate);
            double ratio = (double)Math.Max(1, source.SampleRate) / clipRate;
            long targetFrames = source.SampleRate == clipRate
                ? clipFrames
                : (long)Math.Ceiling(clipFrames * ratio);
            if (targetFrames <= 0)
            {
                return 0;
            }

            long targetSamples = targetFrames * destChannels;
            if (targetSamples <= 0)
            {
                return 0;
            }

            return targetSamples > int.MaxValue ? int.MaxValue : (int)targetSamples;
        }

        private void HandlePlaybackCompleted(PlaybackAudioSource source)
        {
            // This can be called by the source's event or by the stream watchdog.
            // The _onCompleted call chain is responsible for ensuring it only runs once via the _completed flag.
            if (_completed)
            {
                return;
            }


            Debug.Log($"{LogPrefix}Handle {_id}: HandlePlaybackCompleted called. AutoDispose: {AutoDisposeOnComplete}, Loop: {_loop}");
            _onCompleted?.Invoke(_id);
        }

        private void HandlePlaybackAudio(float[] data, int channels, int sampleRate)
        {
            // placeholder for future diagnostics. Intentionally empty.
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
    }

}
