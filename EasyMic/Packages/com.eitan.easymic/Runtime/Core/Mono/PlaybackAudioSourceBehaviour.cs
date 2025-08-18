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
        [SerializeField] private bool _playOnEnable = true;
        [SerializeField] private bool _loop = true;

        // REMOVED: No longer need manual chunking configuration.
        // [Header("Buffering")]
        // [Range(5, 60)] [SerializeField] private int _chunkMs = 10;

        [Header("Level")]
        [Range(0f, 2f)] [SerializeField] private float _volume = 1.0f;

        private PlaybackAudioSource _source;
        private float[] _readBuf;
        private int _positionFrames;
        private int _channels;
        private int _sampleRate;

        public PlaybackAudioSource Source => _source;

        public void SetVolume(float v)
        {
            _volume = Mathf.Clamp(v, 0f, 2f);
            if (_source != null) _source.Volume = _volume;
        }

        private void OnEnable()
        {
            if (_playOnEnable)
            {
                StartPlayback();
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

        public void Enqueue(float[] samples, int count)
        {
            if (_source == null || samples == null || count <= 0) return;
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

            _source = new PlaybackAudioSource(_channels, _sampleRate, 1, sys.MasterMixer);
            _source.name = this.name;
            _source.Volume = _volume;
            _positionFrames = 0;
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
                try { src.Dispose(); } catch { }
            }
            _positionFrames = 0;
        }

        private void Update()
        {
            if (_source == null || _clip == null) return;

            // Check if clip has finished playing
            int remainingFramesInClip = _clip.samples - _positionFrames;
            if (remainingFramesInClip <= 0)
            {
                if (_loop)
                {
                    _positionFrames = 0;
                    remainingFramesInClip = _clip.samples;
                }
                else return; // Playback is complete
            }

            // Determine how much data to feed based on available buffer space
            int inCh = Mathf.Max(1, _clip.channels);
            int freeSamples = _source.FreeSamples;
            if (freeSamples <= 0) return; // Buffer is full, wait for next Update

            int freeFrames = freeSamples / inCh;

            // Decide how many frames to read: the smaller of what's left in the clip vs. what fits in the buffer.
            int framesToRead = Mathf.Min(remainingFramesInClip, freeFrames);
            if (framesToRead <= 0) return;

            int samplesToRead = framesToRead * inCh;
            if (_readBuf == null || _readBuf.Length < samplesToRead)
            {
                _readBuf = new float[samplesToRead];
            }

            // Read data from the clip and enqueue it
            if (_clip.GetData(_readBuf, _positionFrames))
            {
                int writtenSamples = _source.Enqueue(_readBuf.AsSpan(0, samplesToRead));
                int writtenFrames = writtenSamples / inCh;
                _positionFrames += writtenFrames;
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
    }
}