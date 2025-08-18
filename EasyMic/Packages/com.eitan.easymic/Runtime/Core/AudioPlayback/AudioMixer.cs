using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Hierarchical mixer: contains child mixers and playback sources, sums all children
    /// into the destination buffer, then applies master volume and post effects via Pipeline.
    /// </summary>
    public sealed class AudioMixer : IDisposable
    {
        public float MasterVolume { get; set; } = 1.0f;
        public AudioPipeline Pipeline { get; } = new AudioPipeline();
        private AudioState _state;
        // Reusable accumulation buffer to avoid allocator interaction on audio threads
        private float[] _accumBuf = Array.Empty<float>();

        private volatile AudioMixer[] _mixersSnap = Array.Empty<AudioMixer>();
        private volatile PlaybackAudioSource[] _sourcesSnap = Array.Empty<PlaybackAudioSource>();

        public bool Mute { get; set; } = false;
        public bool Solo { get; set; } = false;

        public string name;

        public void Initialize(int channels, int sampleRate)
        {
            _state = new AudioState(channels, sampleRate, 0);
            Pipeline.Initialize(_state);
        }

        public void RenderAdditive(Span<float> buffer, int systemChannels, int systemSampleRate)
        {
            if (buffer.IsEmpty) return;
            bool anySolo = HasSoloRecursive();

            // Skip entire branch if muted or filtered by solo logic
            if (Mute) return;
            if (anySolo && !Solo && !HasSoloInChildren()) return;

            // Accumulate locally, then post-process, then add to parent buffer
            // Use a persistent buffer to avoid Rent/Return on the audio thread, which can
            // conflict with engine allocator expectations and trigger warnings/errors.
            if (_accumBuf.Length < buffer.Length)
            {
                // Grow to the next power-of-two-ish to limit reallocs under varying device buffers.
                int newSize = _accumBuf.Length == 0 ? 1024 : _accumBuf.Length;
                while (newSize < buffer.Length) newSize *= 2;
                _accumBuf = new float[newSize];
            }
            float[] local = _accumBuf;
            try
            {
                var acc = new Span<float>(local, 0, buffer.Length);
                acc.Clear();

                // Child mixers
                var mixers = _mixersSnap;
                for (int i = 0; i < mixers.Length; i++)
                {
                    try { mixers[i]?.RenderAdditive(acc, systemChannels, systemSampleRate); } catch { }
                }

                // Sources
                var sources = _sourcesSnap;
                for (int i = 0; i < sources.Length; i++)
                {
                    var s = sources[i];
                    if (s == null) continue;
                    if (anySolo && !s.Solo) continue;
                    try { s.RenderAdditive(acc, systemChannels, systemSampleRate); } catch { }
                }

                // Apply master volume and post pipeline to this mixer's local accumulation
                if (MasterVolume != 1.0f)
                {
                    for (int i = 0; i < acc.Length; i++) acc[i] *= MasterVolume;
                }
                _state.Length = acc.Length;
                try { Pipeline.OnAudioPass(acc, _state); } catch { }

                // Add to parent buffer
                for (int i = 0; i < acc.Length; i++) buffer[i] += acc[i];
            }
            finally { }
        }

        public void AddSource(PlaybackAudioSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            // Ensure source format aligns with mixer state; resample handled inside source if needed.
            while (true)
            {
                var cur = _sourcesSnap;
                if (Array.IndexOf(cur, source) >= 0) return;
                var next = new PlaybackAudioSource[cur.Length + 1];
                Array.Copy(cur, next, cur.Length);
                next[^1] = source;
                if (System.Threading.Interlocked.CompareExchange(ref _sourcesSnap, next, cur) == cur) break;
            }
        }

        public void RemoveSource(PlaybackAudioSource source)
        {
            if (source == null) return;
            while (true)
            {
                var cur = _sourcesSnap;
                int idx = Array.IndexOf(cur, source);
                if (idx < 0) break;
                var next = new PlaybackAudioSource[cur.Length - 1];
                if (idx > 0) Array.Copy(cur, 0, next, 0, idx);
                if (idx < cur.Length - 1) Array.Copy(cur, idx + 1, next, idx, cur.Length - idx - 1);
                if (System.Threading.Interlocked.CompareExchange(ref _sourcesSnap, next, cur) == cur) break;
            }
        }

        public void AddMixer(AudioMixer mixer)
        {
            if (mixer == null) throw new ArgumentNullException(nameof(mixer));
            while (true)
            {
                var cur = _mixersSnap;
                if (Array.IndexOf(cur, mixer) >= 0) return;
                var next = new AudioMixer[cur.Length + 1];
                Array.Copy(cur, next, cur.Length);
                next[^1] = mixer;
                if (System.Threading.Interlocked.CompareExchange(ref _mixersSnap, next, cur) == cur) break;
            }
        }

        public void RemoveMixer(AudioMixer mixer)
        {
            if (mixer == null) return;
            while (true)
            {
                var cur = _mixersSnap;
                int idx = Array.IndexOf(cur, mixer);
                if (idx < 0) break;
                var next = new AudioMixer[cur.Length - 1];
                if (idx > 0) Array.Copy(cur, 0, next, 0, idx);
                if (idx < cur.Length - 1) Array.Copy(cur, idx + 1, next, idx, cur.Length - idx - 1);
                if (System.Threading.Interlocked.CompareExchange(ref _mixersSnap, next, cur) == cur) break;
            }
        }

        public void Dispose()
        {
            try { Pipeline.Dispose(); } catch { }
        }

        // Diagnostics helpers (snapshots)
        public AudioMixer[] GetChildren()
        {
            var cur = _mixersSnap;
            var copy = new AudioMixer[cur.Length];
            Array.Copy(cur, copy, cur.Length);
            return copy;
        }

        public PlaybackAudioSource[] GetSources()
        {
            var cur = _sourcesSnap;
            var copy = new PlaybackAudioSource[cur.Length];
            Array.Copy(cur, copy, cur.Length);
            return copy;
        }

        public bool HasSoloRecursive()
        {
            if (Solo) return true;
            var sources = _sourcesSnap;
            for (int i = 0; i < sources.Length; i++) if (sources[i]?.Solo == true) return true;
            var mixers = _mixersSnap;
            for (int i = 0; i < mixers.Length; i++) if (mixers[i]?.HasSoloRecursive() == true) return true;
            return false;
        }

        private bool HasSoloInChildren()
        {
            var sources = _sourcesSnap;
            for (int i = 0; i < sources.Length; i++) if (sources[i]?.Solo == true) return true;
            var mixers = _mixersSnap;
            for (int i = 0; i < mixers.Length; i++) if (mixers[i]?.HasSoloRecursive() == true) return true;
            return false;
        }
    }
}
