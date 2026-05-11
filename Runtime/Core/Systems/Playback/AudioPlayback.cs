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
        private static readonly Dictionary<int, PlaybackAudioSession> s_playbacks = new Dictionary<int, PlaybackAudioSession>();
        private static int s_nextId = 1;

        public static EasyMicLatencyProfile DefaultLatencyProfile
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return EasyMicLatencyProfile.Balanced;
#else
                return EasyMicLatencyProfile.LowLatency;
#endif
            }
        }

        private static void EnsureAudioSystem()
        {
            EnsureAudioSystem(DefaultLatencyProfile);
        }

        private static void EnsureAudioSystem(EasyMicLatencyProfile latencyProfile)
        {
            var sys = AudioSystem.Instance;
            if (!sys.IsRunning)
            {
                sys.LatencyProfile = latencyProfile;
                sys.PreferNativeFormat();
                sys.Start();
            }
        }


        public static PlaybackHandle PlayClip(AudioClip clip, bool loop = false, float volume = 1f, bool autoDisposeOnComplete = true)
        {
            return PlayClip(clip, loop, volume, autoDisposeOnComplete, DefaultLatencyProfile);
        }

        public static PlaybackHandle PlayClip(
            AudioClip clip,
            bool loop,
            float volume,
            bool autoDisposeOnComplete,
            EasyMicLatencyProfile latencyProfile)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }
            var handle = CreatePlayback(volume, loop, latencyProfile, out var session);
            session.Volume = volume;
            session.PlayClip(clip, loop, autoPlay: true);

            if (autoDisposeOnComplete)
            {
                void AutoDispose(PlaybackAudioSession source)
                {
                    source.OnBatchCompleted -= AutoDispose;
                    handle.Dispose();
                }

                session.OnBatchCompleted += AutoDispose;
            }
            return handle;
        }

        public static PlaybackHandle CreateStream(float volume = 1f)
        {
            return CreateStream(volume, DefaultLatencyProfile);
        }

        public static PlaybackHandle CreateStream(float volume, EasyMicLatencyProfile latencyProfile)
        {
            var handle = CreatePlayback(volume, loop: true, latencyProfile, out var session);
            session.Volume = volume;
            session.Play();
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
            PlaybackAudioSession session;
            lock (s_lock)
            {
                if (!s_playbacks.TryGetValue(id, out session))
                {
                    return;
                }

                s_playbacks.Remove(id);
            }
            session.Dispose();
        }

        internal static void Enqueue(int id, float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
        {
            TryEnqueue(id, samples, count, channels, sampleRate, markEndOfStream);
        }

        internal static EasyMicEnqueueResult TryEnqueue(int id, float[] samples, int count, int channels, int sampleRate, bool markEndOfStream)
        {
            if (TryGet(id, out var session))
            {
                return session.TryEnqueue(samples, count, channels, sampleRate, markEndOfStream);
            }

            return EasyMicEnqueueResult.Disposed(count);
        }

        internal static void CompleteStream(int id)
        {
            if (TryGet(id, out var session))
            {
                session.CompleteStream();
            }
        }

        internal static bool IsPlaying(int id)
        {
            return TryGet(id, out var session) && session.IsPlaying;
        }

        internal static double GetBufferedSeconds(int id)
        {
            return TryGet(id, out var session) ? session.BufferedSeconds : 0.0;
        }

        internal static float GetVolume(int id)
        {
            return TryGet(id, out var session) ? session.Volume : 0f;
        }

        internal static void SetVolume(int id, float value)
        {
            if (TryGet(id, out var session))
            {
                session.Volume = value;
            }
        }

        internal static void RegisterCompletionCallback(int id, Action callback)
        {
            if (callback == null)
            {
                return;
            }

            if (TryGet(id, out var session))
            {
                session.OnBatchCompleted += (source) =>
                {
                    callback?.Invoke();
                };
            }
        }

        private static PlaybackHandle CreatePlayback(float volume, bool loop, out PlaybackAudioSession session)
        {
            return CreatePlayback(volume, loop, DefaultLatencyProfile, out session);
        }

        private static PlaybackHandle CreatePlayback(float volume, bool loop, EasyMicLatencyProfile latencyProfile, out PlaybackAudioSession session)
        {
            int id;
            lock (s_lock)
            {
                id = s_nextId++;

                EnsureAudioSystem(latencyProfile);

                session = new PlaybackAudioSession($"Playback-{id}")
                {
                    Volume = volume,
                    Loop = loop
                };

                s_playbacks[id] = session;
            }

            return new PlaybackHandle(id);
        }

        private static bool TryGet(int id, out PlaybackAudioSession session)
        {
            lock (s_lock)
            {
                return s_playbacks.TryGetValue(id, out session);
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

        public EasyMicEnqueueResult TryEnqueue(float[] samples, int count, int channels, int sampleRate, bool markEndOfStream = false)
            => AudioPlayback.TryEnqueue(_id, samples, count, channels, sampleRate, markEndOfStream);

        public bool IsPlaying => AudioPlayback.IsPlaying(_id);
        public double BufferedSeconds => AudioPlayback.GetBufferedSeconds(_id);

        public float Volume
        {
            get => AudioPlayback.GetVolume(_id);
            set => AudioPlayback.SetVolume(_id, value);
        }

        public void RegisterCompletedCallback(Action callback)
            => AudioPlayback.RegisterCompletionCallback(_id, callback);
    }

}
