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
        private AudioBuffer _queue;
        private Thread _worker;
        private volatile bool _running;
        private AutoResetEvent _signal;

        // Current source format snapshot (written on audio thread, read on worker thread).
        protected volatile int CurrentSampleRate;
        protected volatile int CurrentChannelCount;

        // Reusable worker-side frame buffer to avoid allocations
        private float[] _workerFrame;

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

            // Capacity: choose near power-of-two-1 to trigger mask fast-path in AudioBuffer (size=capacity+1 is power-of-two)
            int baseCap = Math.Max(1, state.SampleRate * state.ChannelCount * _capacitySeconds);
            int cap = ToPow2Minus1(baseCap);
            _queue = new AudioBuffer(cap);

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

                if (!_running)
                {
                    break;
                }

                // Drain all complete messages in the queue.
                while (_running)
                {
                    if (_queue.ReadableCount < 1)
                    {
                        break;
                    }

                    Span<float> header = stackalloc float[1];
                    if (_queue.Peek(header) < 1)
                    {
                        break;
                    }

                    int needed = (int)header[0];
                    if (needed < 0)
                    {
                        _queue.Skip(1);
                        continue;
                    }

                    if (_queue.ReadableCount < 1 + needed)
                    {
                        break; // wait for full payload
                    }

                    _queue.Skip(1); // consume header

                    if (needed == 0)
                    {
                        try { OnAudioReadAsync(ReadOnlySpan<float>.Empty); } catch { }
                        continue;
                    }

                    if (_workerFrame == null || _workerFrame.Length < needed)
                    {
                        _workerFrame = new float[needed];
                    }

                    if (_queue.TryReadExact(_workerFrame, needed))
                    {
                        try { OnAudioReadAsync(new ReadOnlySpan<float>(_workerFrame, 0, needed)); } catch { }
                    }
                }
            }
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
                        worker.Join(200);
                    }
                }
                catch { }
            }

            if (signal != null)
            {
                try { signal.Dispose(); } catch { }
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
