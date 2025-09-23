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
        private const float QueueSeconds = 0.2f;
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

        private Thread _feedThread;
        private ManualResetEventSlim _stopSignal;
        private AutoResetEvent _wakeSignal;

        private volatile int _positionFrames;
        private volatile bool _completed;

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
            ResetTimeline(clearQueue: true, clearClip: true);
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
        }

        public void Play()
        {
            if (_source == null)
            {
                return;
            }

            if (_clip != null && _clipCache != null && _clipCache.Length > 0 && _feedThread == null)
            {
                StartFeeder();
            }

            _source.Play();
            _wakeSignal?.Set();
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
            if (_source == null)
            {
                return;
            }

            if (samples == null || count <= 0)
            {
                if (markEndOfStream)
                {
                    _source.SignalEndOfStream();
                }
                return;
            }

            int capped = Mathf.Min(count, samples.Length);
            if (capped <= 0)
            {
                if (markEndOfStream)
                {
                    _source.SignalEndOfStream();
                }
                return;
            }

            _source.Enqueue(new ReadOnlySpan<float>(samples, 0, capped), channels, sampleRate, markEndOfStream);
            if (!_source.IsPlaying)
            {
                _source.Play();
            }
        }

        public void CompleteStream()
        {
            _loop = false;
            _source?.SignalEndOfStream();
            _wakeSignal?.Set();
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

            _positionFrames = 0;
            _completed = false;
        }

        private void CacheClipData()
        {
            _clipCache = null;
            _clipChannels = 0;
            _clipSampleRate = 0;
            _clipTotalSamples = 0;
            _positionFrames = 0;

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
            if (_clipCache == null || _clipCache.Length == 0 || _clipSampleRate <= 0 || _source == null)
            {
                return 0;
            }

            int frames = _clipTotalSamples / Math.Max(1, _clipChannels);
            double ratio = (double)_source.SampleRate / Math.Max(1, _clipSampleRate);
            double converted = Math.Ceiling(frames * ratio);
            return converted < 1 ? 0 : (long)converted;
        }

        private void StartFeeder()
        {
            if (_source == null || _clipCache == null || _clipCache.Length == 0)
            {
                return;
            }

            StopFeeder();
            _stopSignal = new ManualResetEventSlim(false);
            _wakeSignal = new AutoResetEvent(false);
            _feedThread = new Thread(FeedLoop)
            {
                Name = $"EasyMicPlaybackFeed-Global-{_id}",
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
                        if (_loop)
                        {
                            cursorFrames = 0;
                            _positionFrames = 0;
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
                    src.Enqueue(span, channels, _clipSampleRate, completing);

                    cursorFrames += framesToCopy;
                    _positionFrames = Math.Min(cursorFrames, clipFrames);

                    if (completing)
                    {
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

            if (wake.WaitOne(4))
            {
                wake.Reset();
            }
        }

        private void HandlePlaybackCompleted(PlaybackAudioSource source)
        {
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
