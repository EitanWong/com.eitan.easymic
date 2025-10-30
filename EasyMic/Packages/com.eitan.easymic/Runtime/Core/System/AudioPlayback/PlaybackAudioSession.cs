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
        private const float QueueSeconds = 0.2f;
        private const double BufferHighWaterSeconds = 0.12;
        private const double BufferLowWaterSeconds = 0.03;

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

        private bool _disposed;

        public PlaybackAudioSession(string name = null)
        {
            _name = string.IsNullOrEmpty(name) ? "PlaybackSession" : name;
        }

        public PlaybackAudioSource Source => _source;

        public event Action<float[], int, int> OnAudioPlaybackRead;
        public event Action<PlaybackAudioSession> OnPlaybackCompleted;

        public AudioClip Clip => _clip;

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
        }

        public void Enqueue(float[] samples, int count)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            if (samples == null)
            {
                return;
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

            Enqueue(samples, count, channels, sampleRate, false);
        }

        public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            if (samples == null || count <= 0 || channels <= 0 || sampleRate <= 0)
            {
                if (markEndOfStream)
                {
                    _source?.SignalEndOfStream();
                }
                return;
            }

            EnsureSourceReady();
            if (_source == null)
            {
                return;
            }

            ApplySourceProperties();

            int toCopy = Mathf.Min(count, samples.Length);
            if (toCopy <= 0)
            {
                if (markEndOfStream)
                {
                    _source.SignalEndOfStream();
                }
                return;
            }

            var span = new ReadOnlySpan<float>(samples, 0, toCopy);
            _source.Enqueue(span, channels, sampleRate, markEndOfStream);

            if (!_source.IsPlaying)
            {
                _source.Play();
            }
        }

        public void CompleteStream()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PlaybackAudioSession));
            }

            Loop = false;
            _source?.SignalEndOfStream();
            WakeFeeder();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
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
                    src.Enqueue(span, channels, _clipSampleRate, completing);

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

        private void HandleSourcePlaybackCompleted(PlaybackAudioSource source)
        {
            try { OnPlaybackCompleted?.Invoke(this); }
            catch { }
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

