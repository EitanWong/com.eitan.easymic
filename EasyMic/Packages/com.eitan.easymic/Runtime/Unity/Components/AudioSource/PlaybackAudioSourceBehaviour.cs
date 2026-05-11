
using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components
{
    /// <summary>
    /// Unity MonoBehaviour wrapper for <see cref="PlaybackAudioSession"/>.
    /// Keeps the authoring-friendly component surface while delegating
    /// playback mechanics to a pure C# session that is reusable outside Unity.
    /// </summary>
    [AddComponentMenu("Audio/EasyMic/Playback Audio Source")]
    public sealed class PlaybackAudioSourceBehaviour : MonoBehaviour
    {
        [Header("Clip Playback")]
        [SerializeField] private AudioClip _clip;
        [SerializeField] private bool _playOnAwake = true;
        [SerializeField] private bool _loop = true;

        [Header("Level")]
        [Range(0f, 2f)]
        [SerializeField] private float _volume = 1.0f;
        [SerializeField] private bool _mute = false;

        [SerializeField] private bool _solo;

        private PlaybackAudioSession _session;
        private string _cachedSessionName = "Playback";

        public PlaybackAudioSource Source => _session?.Source;

        /// <summary>
        /// 实时回采事件（仿 Unity OnAudioFilterRead）：在音频线程触发 但只读。
        /// 提供当前此源对输出缓冲贡献的样本数据（输出通道布局）。
        /// 参数：(data, channels, sampleRate)
        /// </summary>
        public event Action<float[], int, int> OnAudioPlaybackRead;

        /// <summary>
        /// Invoked when the underlying playback source drains all audio after an explicit end-of-stream signal.
        /// Works for clip playback and manual streaming enqueues.
        /// </summary>
        public event Action<PlaybackAudioSourceBehaviour> OnPlaybackCompleted;

        public bool IsPlaying => _session != null && _session.IsPlaying;

        public bool Loop
        {
            get => _loop;
            set
            {
                _loop = value;
                if (_session != null)
                {
                    _session.Loop = value;
                }
            }
        }

        public float BufferedSeconds => _session != null ? _session.BufferedSeconds : 0f;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp(value, 0f, 2f);
                if (_session != null)
                {
                    _session.Volume = _volume;
                }
            }
        }

        public bool Mute
        {
            get => _mute;
            set
            {
                _mute = value;
                if (_session != null)
                {
                    _session.Mute = _mute;
                }
            }
        }

        public bool Solo
        {
            get => _solo;
            set
            {
                _solo = value;
                if (_session != null)
                {
                    _session.Solo = _solo;
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
                    StartPlayback();
                }
            }
        }

        public bool PlayOnAwake
        {
            get => _playOnAwake;
            set
            {
                _playOnAwake = value;
                if (!isActiveAndEnabled || _session == null)
                {
                    return;
                }

                if (_playOnAwake)
                {
                    if (_clip != null)
                    {
                        _session.PlayClip(_clip, _loop, true);
                    }
                    else
                    {
                        _session.Play();
                    }
                }
                else
                {
                    _session.Pause();
                }
            }
        }

        public float ProgressNormalized => _session != null ? _session.ProgressNormalized : 0f;

        private void OnEnable()
        {
            CacheSessionName();
            StartPlayback();
            if (!_playOnAwake)
            {
                _session?.Pause();
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
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!EnsureSession())
            {
                return;
            }

            ApplySessionProperties();
            _session.PlayClip(_clip, _loop, autoPlay: true);
        }

        public void Stop()
        {
            _session?.Stop(clearQueue: true, clearClip: false);
            _session?.Pause();
        }

        public void Play()
        {
            if (!EnsureSession())
            {
                return;
            }

            ApplySessionProperties();
            _session.Play();
        }

        public void Resume()
        {
            _session?.Resume();
        }

        public void Pause()
        {
            _session?.Pause();
        }

        public void Enqueue(float[] samples, int count)
        {
            TryEnqueue(samples, count);
        }

        public EasyMicEnqueueResult TryEnqueue(float[] samples, int count)
        {
            if (!EnsureSession())
            {
                return EasyMicEnqueueResult.Disposed(count);
            }

            ApplySessionProperties();
            return _session.TryEnqueue(samples, count);
        }

        public void Enqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
        {
            TryEnqueue(samples, count, channels, sampleRate, markEndOfStream);
        }

        public EasyMicEnqueueResult TryEnqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
        {
            if (!EnsureSession())
            {
                return EasyMicEnqueueResult.Disposed(count);
            }

            ApplySessionProperties();
            return _session.TryEnqueue(samples, count, channels, sampleRate, markEndOfStream);
        }

        public void CompleteStream()
        {
            Loop = false;
            _session?.CompleteStream();
        }

        private void StartPlayback()
        {
            if (!EnsureSession())
            {
                return;
            }

            ApplySessionProperties();

            if (_clip != null)
            {
                _session.PlayClip(_clip, _loop, _playOnAwake);
            }
            else if (_playOnAwake)
            {
                _session.Play();
            }
            else
            {
                _session.Stop(clearQueue: true, clearClip: false);
            }
        }

        private void StopPlayback()
        {
            if (_session == null)
            {
                return;
            }

            try { _session.Stop(clearQueue: true, clearClip: true); } catch { }
            DisposeSession();
        }

        private bool EnsureSession()
        {
            if (_session != null)
            {
                return true;
            }

            if (!IsUnityObjectAlive())
            {
                return false;
            }

            string nameForSession = string.IsNullOrWhiteSpace(_cachedSessionName) ? "Playback" : _cachedSessionName;
            _session = new PlaybackAudioSession(nameForSession);
            _session.OnAudioPlaybackRead += HandleSessionAudioPlaybackRead;
            _session.OnBatchCompleted += HandleSessionPlaybackBatchCompleted;
            return true;
        }

        private void DisposeSession()
        {
            if (_session == null)
            {
                return;
            }

            try { _session.OnAudioPlaybackRead -= HandleSessionAudioPlaybackRead; } catch { }
            try { _session.OnBatchCompleted -= HandleSessionPlaybackBatchCompleted; } catch { }
            try { _session.Dispose(); } catch { }
            _session = null;
        }

        private void ApplySessionProperties()
        {
            if (_session == null)
            {
                return;
            }

            _session.Volume = _volume;
            _session.Mute = _mute;
            _session.Solo = _solo;
            _session.Loop = _loop;
        }

        private void CacheSessionName()
        {
            if (!IsUnityObjectAlive())
            {
                return;
            }

            string resolvedName = null;

            try
            {
                resolvedName = gameObject != null ? gameObject.name : null;
            }
            catch (MissingReferenceException)
            {
            }

            if (!string.IsNullOrWhiteSpace(resolvedName))
            {
                _cachedSessionName = resolvedName;
            }
        }

        private bool IsUnityObjectAlive()
        {
            if (this == null)
            {
                return false;
            }

            try
            {
                return gameObject != null;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }

        private void HandleSessionAudioPlaybackRead(float[] data, int channels, int sampleRate)
        {
            try { OnAudioPlaybackRead?.Invoke(data, channels, sampleRate); }
            catch { }
        }

        private void HandleSessionPlaybackBatchCompleted(PlaybackAudioSession session)
        {
            try { OnPlaybackCompleted?.Invoke(this); }
            catch { }
        }
    }
}
