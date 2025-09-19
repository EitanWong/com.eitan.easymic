using System;
using System.Threading;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Unity MonoBehaviour wrapper for a PlaybackAudioSource.
    /// Provides clip-backed playback with a dedicated feeder thread so audio keeps flowing
    /// when the Unity player is unfocused or running in the background.
    /// </summary>
    [AddComponentMenu("Audio/Playback Audio Source")]
    public sealed class PlaybackAudioSourceBehaviour : MonoBehaviour
    {
        private const float QueueSeconds = 0.2f;
        private const double BufferHighWaterSeconds = 0.12;
        private const double BufferLowWaterSeconds = 0.03;

        [Header("Clip Playback")]
        [SerializeField] private AudioClip _clip;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;

        [Header("Level")]
        [Range(0f, 2f)]
        [SerializeField] private float _volume = 1.0f;
        [SerializeField] private bool _mute = false;

        private PlaybackAudioSource _source;
        private float[] _clipCache;
        private int _clipChannels;
        private int _clipSampleRate;
        private int _clipTotalSamples;
        private int _positionFrames;

        private bool _solo;
        private int _channels;
        private int _sampleRate;

        private Thread _feedThread;
        private ManualResetEventSlim _stopSignal;
        private AutoResetEvent _wakeSignal;

        public PlaybackAudioSource Source => _source;

        /// <summary>
        /// 实时回采事件（仿 Unity OnAudioFilterRead）：在音频线程触发 但只读。
        /// 提供当前此源对输出缓冲贡献的样本数据（输出通道布局）。
        /// 参数：(data, channels, sampleRate)
        /// </summary>
        public event Action<float[], int, int> OnAudioPlaybackRead;

        public bool IsPlaying => _source != null && _source.IsPlaying;
        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                WakeFeeder();
            }
        }

        public float BufferedSeconds => _source != null ? (float)_source.BufferedSeconds : 0f;

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

        public AudioClip Clip
        {
            get => _clip;
            set
            {
                if (_clip == value)
                {
                    return;
                }

                _clip = value;
                if (isActiveAndEnabled)
                {
                    StopPlayback();
                    StartPlayback();
                    if (!_playOnAwake)
                    {
                        _source?.Pause();
                    }
                }
            }
        }

        public bool PlayOnAwake
        {
            get => _playOnAwake;
            set
            {
                _playOnAwake = value;
                if (!isActiveAndEnabled || _source == null)
                {
                    return;
                }

                if (_playOnAwake)
                {
                    _source.Play();
                    WakeFeeder();
                }
                else
                {
                    _source.Pause();
                }
            }
        }

        public float ProgressNormalized
        {
            get
            {
                if (_clip != null && _clip.samples > 0)
                {
                    int frames = Volatile.Read(ref _positionFrames);
                    float p = (float)frames / Mathf.Max(1, _clip.samples);
                    return Mathf.Clamp01(p);
                }

                return _source != null ? _source.NormalizedProgress : 0f;
            }
        }

        private void OnEnable()
        {
            StartPlayback();
            if (!_playOnAwake)
            {
                _source?.Pause();
            }
        }

        private void OnDisable()
        {
            StopPlayback();
        }

        public void PlayClip(AudioClip clip, bool loop = true)
        {
            _clip = clip;
            _loop = loop;
            if (isActiveAndEnabled)
            {
                StopPlayback();
                StartPlayback();
            }
        }

        public void Stop()
        {
            StopPlayback();
        }

        public void Play()
        {
            if (_source == null)
            {
                StartPlayback();
            }

            _source?.Play();
            WakeFeeder();
        }

        public void Pause()
        {
            _source?.Pause();
        }

        public void Enqueue(float[] samples, int count)
        {
            if (_source == null || samples == null || count <= 0)
            {
                return;
            }

            _source.Enqueue(samples.AsSpan(0, count));
            WakeFeeder();
        }

        private void StartPlayback()
        {
            StopPlayback();

            DetermineFormat();

            var sys = AudioSystem.Instance;
            if (!sys.IsRunning)
            {
                sys.PreferNativeFormat();
                sys.Start();
            }

            _source = new PlaybackAudioSource(_channels, _sampleRate, QueueSeconds, sys.MasterMixer)
            {
                name = gameObject.name,
                Volume = _volume,
                Mute = _mute,
                Solo = _solo
            };

            if (_clip != null)
            {
                _source.TotalSourceFrames = _clip.samples;
            }

            _source.OnAudioPlayback += HandleSourceAudioPlaybackRead;
            Volatile.Write(ref _positionFrames, 0);

            CacheClipData();
            StartFeeder();

            if (_playOnAwake)
            {
                _source.Play();
                WakeFeeder();
            }
        }

        private void StopPlayback()
        {
            StopFeeder();

            if (_source != null)
            {
                try
                {
                    var sys = AudioSystem.Instance;
                    if (sys.IsRunning && sys.MasterMixer != null)
                    {
                        sys.MasterMixer.RemoveSource(_source);
                    }
                }
                catch { }

                try { _source.OnAudioPlayback -= HandleSourceAudioPlaybackRead; } catch { }
                try { _source.Dispose(); } catch { }
                _source = null;
            }

            _clipCache = null;
            _clipTotalSamples = 0;
            _clipChannels = 0;
            Volatile.Write(ref _positionFrames, 0);
        }

        private void DetermineFormat()
        {
            if (_clip != null)
            {
                _channels = Mathf.Max(1, _clip.channels);
                _sampleRate = Mathf.Max(8000, _clip.frequency);
            }
            else
            {
                _sampleRate = AudioSettings.outputSampleRate;
                _channels = Mathf.Max(1, SpeakerModeToChannels(AudioSettings.speakerMode));
            }
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
                    // Wait briefly for asynchronous prepare without blocking frame for too long.
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
            if (_clipCache == null || _clipCache.Length == 0)
            {
                return;
            }

            _stopSignal = new ManualResetEventSlim(false);
            _wakeSignal = new AutoResetEvent(false);
            _feedThread = new Thread(FeedLoop)
            {
                Name = $"EasyMicPlaybackFeed-{name}",
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
            var temp = new float[Math.Max(4096, (_clipChannels > 0 ? _clipChannels : 1) * 1024)];
            int clipFrames = _clipChannels > 0 ? _clipTotalSamples / _clipChannels : 0;
            int cursorFrames = 0;

            while (!_stopSignal.IsSet)
            {
                try
                {
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

                    double buffered = src.BufferedSeconds;
                    if (buffered > BufferHighWaterSeconds)
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
                        }
                        else
                        {
                            src.Pause();
                            continue;
                        }
                    }

                    int freeSamples = src.FreeSamples;
                    if (freeSamples <= _clipChannels)
                    {
                        WaitFeeder();
                        continue;
                    }

                    int framesBudget = Math.Min(freeSamples / Math.Max(1, _clipChannels), temp.Length / Math.Max(1, _clipChannels));
                    if (framesBudget <= 0)
                    {
                        Thread.Sleep(2);
                        continue;
                    }

                    int framesRemaining = clipFrames - cursorFrames;
                    int framesToCopy = Math.Min(framesRemaining, framesBudget);
                    if (framesToCopy <= 0)
                    {
                        Thread.Sleep(2);
                        continue;
                    }

                    int samplesToCopy = framesToCopy * _clipChannels;
                    Array.Copy(_clipCache, cursorFrames * _clipChannels, temp, 0, samplesToCopy);
                    src.Enqueue(new ReadOnlySpan<float>(temp, 0, samplesToCopy));

                    cursorFrames += framesToCopy;
                    Volatile.Write(ref _positionFrames, cursorFrames);

                    if (buffered < BufferLowWaterSeconds)
                    {
                        continue;
                    }

                    WaitFeeder();
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
