namespace Eitan.EasyMic.Runtime
{
    using System;
    using System.Threading;

    /// <summary>
    /// High-performance reader that decouples heavy work from the audio thread via an SPSC ring buffer
    /// and a dedicated worker thread. The audio thread never blocks; the worker is signaled when data arrives.
    /// </summary>
    public abstract class AudioReader : AudioWorkerBase
    {
        private UnsafeAudioRingBuffer _queue;
        private Thread _worker;
        private volatile bool _running;
        private AutoResetEvent _signal;

        // Current source format snapshot (written on audio thread, read on worker thread).
        protected volatile int CurrentSampleRate;
        protected volatile int CurrentChannelCount;

        // Reusable worker-side frame buffer to avoid allocations
        private float[] _workerFrame;
        private readonly float[] _workerHeader = new float[1];

        // Capacity in seconds for the internal queue (default 1 second)
        private readonly int _capacitySeconds;

        protected AudioReader(int capacitySeconds = 1)
        {
            _capacitySeconds = Math.Max(1, capacitySeconds);
        }

        public override void Initialize(AudioContext state)
        {
            StopWorkerThread();
            base.Initialize(state);

            // Initialize format snapshot
            CurrentSampleRate = state.SampleRate;
            CurrentChannelCount = state.ChannelCount;

            // Capacity: choose near power-of-two-1 so the unmanaged SPSC ring uses mask wrapping.
            int baseCap = Math.Max(1, state.SampleRate * state.ChannelCount * _capacitySeconds);
            int cap = ToPow2Minus1(baseCap);
            _queue = new UnsafeAudioRingBuffer(cap);

            // Initial worker buffer uses the current upstream frame length (may vary at runtime).
            int initial = Math.Max(1, state.Length);
            _workerFrame = new float[initial];

            _signal = new AutoResetEvent(false);
            _running = true;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"AsyncAudioReader-{GetType().Name}"
            };
            _worker.Start();
        }

        public override void Dispose()
        {
            StopWorkerThread();
            _queue = null;
            _workerFrame = null;
            base.Dispose();
        }

        public sealed override void OnAudioPass(Span<float> audiobuffer, AudioContext state)
        {
            if (!IsInitialized || !_running || !Enabled)
            {
                return;
            }

            // Update format snapshot each frame for consumers needing dynamic reconfiguration
            CurrentSampleRate = state.SampleRate;
            CurrentChannelCount = state.ChannelCount;

            int currentFrameSize = state.Length;
            if (currentFrameSize < 0)
            {
                currentFrameSize = 0;
            }
            if (currentFrameSize > audiobuffer.Length)
            {
                currentFrameSize = audiobuffer.Length;
            }

            // Message framing: [len(float)] + payload(len floats). This supports variable frame sizes.
            int required = 1 + currentFrameSize;

            // Non-blocking write. If the queue is full, drop to protect the audio thread.
            if (_queue.WritableCount < required)
            {
                return;
            }

            Span<float> header = stackalloc float[1];
            header[0] = currentFrameSize;
            if (!_queue.TryWriteExact(header))
            {
                return;
            }

            if (currentFrameSize > 0)
            {
                var frame = audiobuffer.Slice(0, currentFrameSize);
                if (!_queue.TryWriteExact(frame))
                {
                    return;
                }
            }

            _signal?.Set();
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                // Wait for a signal from the producer or timeout to handle shutdown gracefully.
                _signal?.WaitOne(100);
                DrainAvailableMessages(discardIncomplete: false);
            }

            // The producer has stopped at this point. Drain complete messages to preserve the
            // tail of short recordings, then discard any malformed partial frame so shutdown
            // cannot spin forever.
            DrainAvailableMessages(discardIncomplete: true);
        }

        private void DrainAvailableMessages(bool discardIncomplete)
        {
            var queue = _queue;
            while (TryDrainOneMessage(queue, discardIncomplete))
            {
                // Intentionally empty: TryDrainOneMessage performs the bounded unit of work.
            }
        }

        private bool TryDrainOneMessage(UnsafeAudioRingBuffer queue, bool discardIncomplete)
        {
            if (queue == null || queue.ReadableCount < 1 || queue.Peek(_workerHeader) < 1)
            {
                return false;
            }

            int needed = (int)_workerHeader[0];
            if (needed < 0)
            {
                queue.Skip(1);
                return true;
            }

            if (queue.ReadableCount < 1 + needed)
            {
                if (discardIncomplete)
                {
                    queue.Skip(queue.ReadableCount);
                }

                return false;
            }

            queue.Skip(1); // consume header

            if (needed == 0)
            {
                try { OnAudioReadAsync(ReadOnlySpan<float>.Empty); } catch { }
                return true;
            }

            var workerFrame = _workerFrame;
            if (workerFrame == null || workerFrame.Length < needed)
            {
                workerFrame = new float[needed];
                _workerFrame = workerFrame;
            }

            if (!queue.TryReadExact(workerFrame, needed))
            {
                return false;
            }

            try { OnAudioReadAsync(new ReadOnlySpan<float>(workerFrame, 0, needed)); } catch { }
            return true;
        }

        /// <summary>
        /// Runs on the dedicated worker thread with frames dequeued from the SPSC queue.
        /// Must be non-blocking with bounded CPU where possible.
        /// </summary>
        protected abstract void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer);

        private void StopWorkerThread()
        {
            _running = false;

            var signal = _signal;
            if (signal != null)
            {
                try { signal.Set(); } catch { }
            }

            var worker = _worker;
            if (worker != null)
            {
                try
                {
                    if (worker.IsAlive)
                    {
                        worker.Join(1000);
                    }
                }
                catch { }
            }

            if (signal != null)
            {
                try { signal.Dispose(); } catch { }
            }

            var queue = _queue;
            if (queue != null)
            {
                try { queue.Dispose(); } catch { }
            }

            _worker = null;
            _signal = null;
            _queue = null;
            _workerFrame = null;
        }

        private static int ToPow2Minus1(int n)
        {
            // returns (2^k - 1) >= n, k>=1
            uint v = (uint)Math.Max(1, n + 1); // we want size=cap+1 to be pow2
            v--; v |= v >> 1; v |= v >> 2; v |= v >> 4; v |= v >> 8; v |= v >> 16; v++;
            int size = (int)v; // power-of-two
            return size - 1;
        }
    }
}
