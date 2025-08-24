using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Unity MonoBehaviour wrapper for a PlaybackAudioSource.
    /// - Plays a Unity AudioClip (decompressed) or accepts streaming samples.
    /// - Registers itself with AudioSystem and feeds data in Update.
    /// </summary>
    [AddComponentMenu("Audio/Playback Audio Source")]
    public sealed class PlaybackAudioSourceBehaviour : MonoBehaviour
    {
        [Header("Clip Playback")]
        [SerializeField] private AudioClip _clip;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;

        [Header("Level")]
        [Range(0f, 2f)] [SerializeField] private float _volume = 1.0f;
        [SerializeField] private bool _mute = false;
        
        private readonly float QUEUE_SECONDS = 0.01f;

        private PlaybackAudioSource _source;
        private float[] _readBuf;
        private int _positionFrames;
        private int _channels;
        private int _sampleRate;
        private bool _solo;

        public PlaybackAudioSource Source => _source;

        /// <summary>
        /// 实时回采事件（仿 Unity OnAudioFilterRead）：在音频线程触发 但只读。
        /// 提供当前此源对输出缓冲贡献的样本数据（输出通道布局）。
        /// 参数：(data, channels, sampleRate)
        /// </summary>
        public event Action<float[], int, int> OnAudioPlaybackRead;

        public bool IsPlaying => _source != null && _source.IsPlaying;
        public bool Loop { get => _loop; set => _loop = value; }
        public float BufferedSeconds => _source != null ? (float)_source.BufferedSeconds : 0f;
        public float Volume { get => _volume; set {
            _volume = Mathf.Clamp(value, 0f, 2f);
            if (_source != null)
                {
                    _source.Volume = _volume;
                }
            }  }
        public bool Mute { get => _mute; set { _mute = value; if (_source != null) { _source.Mute = _mute; } } }
        public bool Solo { get => _solo; set { _solo = value; if (_source != null) { _source.Solo = _solo; } } }
        /// <summary>
        /// 获取或设置当前播放的 AudioClip。设置时会在激活状态下重启播放源以应用更改。
        /// </summary>
        public AudioClip Clip
        {
            get => _clip;
            set
            {
                if (_clip == value)
                {
                    _clip = value;
                    return;
                }
                _clip = value;
                if (isActiveAndEnabled)
                {
                    StopPlayback();
                    StartPlayback();
                    if (!_playOnAwake) { _source?.Pause(); }
                }
            }
        }

        /// <summary>
        /// 获取或设置 PlayOnAwake。设置为 true/false 时，如果组件已激活会相应地播放/暂停。
        /// </summary>
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
                    float p = (float)_positionFrames / Mathf.Max(1, _clip.samples);
                    if (p < 0f)
                    {
                        p = 0f;
                    }
                    else if (p > 1f)
                    {
                        p = 1f;
                    }


                    return p;
                }
                return _source != null ? _source.NormalizedProgress : 0f;
            }
        }

        private void OnEnable()
        {
            StartPlayback();
            if (!_playOnAwake) { _source?.Pause(); }
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
        }

        private void StartPlayback()
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

            var sys = AudioSystem.Instance;
            if (!sys.IsRunning)
            {
                sys.PreferNativeFormat();
                sys.Start();
            }

            _source = new PlaybackAudioSource(_channels, _sampleRate, QUEUE_SECONDS, sys.MasterMixer);
            _source.name = this.name;
            _source.Volume = _volume;
            _source.Mute = _mute;
            _source.Solo = _solo;
            if (_clip != null)
            {
                _source.TotalSourceFrames = _clip.samples;
            }
            _positionFrames = 0;
            // 订阅底层回采并向外转发
            _source.OnAudioPlayback += HandleSourceAudioPlaybackRead;
        }

        private void StopPlayback()
        {
            var src = _source;
            _source = null;
            if (src != null)
            {
                try
                {
                    var sys = AudioSystem.Instance;
                    if (sys.IsRunning && sys.MasterMixer != null)
                    {
                        sys.MasterMixer.RemoveSource(src);
                    }
                }
                catch { /* Swallow during shutdown/disable */ }
                try { src.OnAudioPlayback -= HandleSourceAudioPlaybackRead; } catch { }
                try { src.Dispose(); } catch { }
            }
            _positionFrames = 0;
        }

        private void Update()
        {
            if (_source != null && _source.IsPlaying)
            {
                FeedFromClip(iterations: 2);
            }
        }

        private void LateUpdate()
        {
            // Top-off in LateUpdate to mitigate editor UI stalls (e.g., window resizing)
            if (_source != null && _source.IsPlaying)
            {
                FeedFromClip(iterations: 1);
            }
        }

        private void FeedFromClip(int iterations)
        {
            if (_source == null || _clip == null)
            {
                return;
            }


            int inCh = Mathf.Max(1, _clip.channels);

            for (int it = 0; it < iterations; it++)
            {
                int remainingFramesInClip = _clip.samples - _positionFrames;
                if (remainingFramesInClip <= 0)
                {
                    if (_loop)
                    {
                        _positionFrames = 0;
                        remainingFramesInClip = _clip.samples;
                    }
                    else
                    {
                        break; // complete
                    }

                }

                int freeSamples = _source.FreeSamples;
                if (freeSamples <= 0)
                {
                    break; // full
                }


                int freeFrames = freeSamples / inCh;
                if (freeFrames <= 0)
                {
                    break;
                }

                // Read a moderate chunk to reduce GC and keep audio fed even under stalls

                int framesToRead = Mathf.Min(remainingFramesInClip, freeFrames);
                // Cap chunk size to avoid very large allocations/read operations
                framesToRead = Mathf.Min(framesToRead, 8192 / inCh); // ~8192 samples cap
                if (framesToRead <= 0)
                {
                    break;
                }


                int samplesToRead = framesToRead * inCh;
                if (_readBuf == null || _readBuf.Length < samplesToRead)
                {
                    _readBuf = new float[samplesToRead];
                }

                if (_clip.GetData(_readBuf, _positionFrames))
                {
                    int writtenSamples = _source.Enqueue(_readBuf.AsSpan(0, samplesToRead));
                    int writtenFrames = writtenSamples / inCh;
                    _positionFrames += writtenFrames;
                    if (!_loop && _positionFrames >= _clip.samples)
                    {
                        _positionFrames = _clip.samples;
                        break;
                    }
                    if (writtenFrames <= 0)
                    {
                        break; // nothing enqueued
                    }
                }
                else
                {
                    break;
                }

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

        // 底层回采转发（注意：音频线程回调）
        private void HandleSourceAudioPlaybackRead(float[] data, int channels, int sampleRate)
        {
            try { OnAudioPlaybackRead?.Invoke(data, channels, sampleRate); } catch { }
        }
    }
}
