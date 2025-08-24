using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Loopback player backed by the new AudioSystem.
    /// - Receives upstream PCM frames and enqueues to a PlaybackAudioSource registered to the master mixer.
    /// - Optional volume and mute control.
    /// - Exposes a playback tap for AEC by reading back what was rendered.
    /// </summary>
    public sealed class LoopbackPlayer : AudioReader
    {
        private PlaybackAudioSource _source;
        private float[] _scaleWork;

        private uint _sampleRate;
        private uint _channels;
        private readonly object _lock = new object();

        /// <summary>Linear gain applied before enqueue (0..2).</summary>
        public float Volume { get; set; } = 1.0f;

        /// <summary>When true, no audio is enqueued (silence output).</summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>Queue size in seconds for the underlying player.</summary>
        // public int QueueSeconds { get; set; } = 2;

        /// <summary>Prebuffer time in ms to avoid initial underruns.</summary>
        public int PrebufferMs { get; set; } = 60;
        private readonly float QUEUE_SECONDS = 0.01f;

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);
            // Defer source creation to first frame to leverage dynamic format handling.
            _sampleRate = 0;
            _channels = 0;
        }

        protected override void OnAudioReadAsync(ReadOnlySpan<float> audioBuffer)
        {
            // Recreate the source if format changed or not yet created
            int curSR = Math.Max(1, CurrentSampleRate);
            int curCH = Math.Max(1, CurrentChannelCount);
            if (_source == null || _sampleRate != (uint)curSR || _channels != (uint)curCH)
            {
                var sys = AudioSystem.Instance;
                sys.Start();
                var newSource = new PlaybackAudioSource(curCH, curSR, QUEUE_SECONDS, AudioSystem.Instance.MasterMixer);
                newSource.Volume = Volume;

                PlaybackAudioSource old;
                lock (_lock)
                {
                    old = _source;
                    _source = newSource;
                    _sampleRate = (uint)curSR;
                    _channels = (uint)curCH;
                }
                // Attach new source and detach old
                // already attached via constructor
                try { if (old != null) { sys.MasterMixer.RemoveSource(old); old.Dispose(); } } catch { }
            }

            if (audioBuffer.Length == 0)
            {
                // Endpoint frame: nothing to do, device will output silence if queue drains.
                return;
            }

            if (IsMuted || Volume <= 0f)
            {
                // Drop frame to produce silence.
                return;
            }

            if (Volume != 1.0f)
            {
                // Scale into a reusable work buffer to avoid mutating upstream memory.
                int n = audioBuffer.Length;
                if (_scaleWork == null || _scaleWork.Length < n)
                {
                    _scaleWork = new float[n];
                }


                for (int i = 0; i < n; i++)
                {
                    _scaleWork[i] = audioBuffer[i] * Volume;
                }


                _source?.Enqueue(_scaleWork.AsSpan(0, n));
            }
            else
            {
                _source?.Enqueue(audioBuffer);
            }
        }

        /// <summary>
        /// Read back recently rendered samples for AEC reference.
        /// Returns the number of samples copied into destination.
        /// </summary>
        public int ReadPlayback(Span<float> destination) => 0; // Not supported in new system directly

        public override void Dispose()
        {
            try { lock (_lock) { if (_source != null) { AudioSystem.Instance.MasterMixer.RemoveSource(_source); _source.Dispose(); _source = null; } } } catch { }
            base.Dispose();
        }
    }
}
