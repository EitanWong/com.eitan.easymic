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

        // Cached frame parameters for dequeue sizing, volatile as it's written by audio thread, read by worker
        private volatile int _frameSize;

        // Current source format snapshot (written on audio thread, read on worker thread).
        protected volatile int CurrentSampleRate;
        protected volatile int CurrentChannelCount;

        // Reusable worker-side frame buffer to avoid allocations
        private float[] _workerFrame;

        // Capacity in seconds for the internal queue (default 1 second)
        private readonly int _capacitySeconds;

        // Endpoint signaling: a single zero-length frame request
        private volatile bool _hasPendingEmptyFrame;

        protected AudioReader(int capacitySeconds = 1)
        {
            _capacitySeconds = Math.Max(1, capacitySeconds);
        }

        public override void Initialize(AudioState state)
        {
            base.Initialize(state);

            // Initialize format snapshot
            CurrentSampleRate = state.SampleRate;
            CurrentChannelCount = state.ChannelCount;

            // Capacity: choose near power-of-two-1 to trigger mask fast-path in AudioBuffer (size=capacity+1 is power-of-two)
            int baseCap = Math.Max(1, state.SampleRate * state.ChannelCount * _capacitySeconds);
            int cap = ToPow2Minus1(baseCap);
            _queue = new AudioBuffer(cap);

            // Initial frame size is the upstream frame length (interleaved floats)
            _frameSize = Math.Max(1, state.Length);
            _workerFrame = new float[_frameSize];

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
            _running = false;
            _signal?.Set(); // Wake up worker thread to exit
            _worker?.Join(200);
            _signal?.Dispose();
            _worker = null;
            _signal = null;
            base.Dispose();
        }

        public sealed override void OnAudioPass(Span<float> audiobuffer, AudioState state)
        {
            if (!IsInitialized || !_running) return;

            // Update format snapshot each frame for consumers needing dynamic reconfiguration
            CurrentSampleRate = state.SampleRate;
            CurrentChannelCount = state.ChannelCount;

            int currentFrameSize = state.Length;
            _frameSize = currentFrameSize; // Update frame size for the worker

            if (currentFrameSize == 0)
            {
                // Signal an empty frame for endpoint detection
                _hasPendingEmptyFrame = true;
                _signal?.Set();
                return;
            }
            
            // Non-blocking write. If the queue is full, we drop the frame to prevent blocking the audio thread.
            // This ensures frame atomicity.
            var frame = audiobuffer.Slice(0, currentFrameSize);
            if (_queue.TryWriteExact(frame))
            {
                _signal?.Set();
            }
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                // Wait for a signal from the producer or timeout to handle shutdown gracefully.
                _signal?.WaitOne(100);

                if (!_running) break;

                // Prioritize handling the empty frame signal for endpoint detection.
                if (_hasPendingEmptyFrame)
                {
                    _hasPendingEmptyFrame = false;
                    try { OnAudioReadAsync(ReadOnlySpan<float>.Empty); }
                    catch { /* Protect worker thread loop */ }
                }

                // Process all available full frames in the queue.
                int needed = _frameSize;
                if (needed <= 0) continue;

                // Ensure worker buffer is large enough.
                if (_workerFrame == null || _workerFrame.Length < needed)
                {
                    _workerFrame = new float[needed];
                }

                // Drain the queue of all complete frames.
                while (_running && _queue.TryReadExact(_workerFrame, needed))
                {
                    try
                    {
                        OnAudioReadAsync(new ReadOnlySpan<float>(_workerFrame, 0, needed));
                    }
                    catch { /* Protect worker thread loop */ }
                    
                    // Update needed size for the next frame in case it changed.
                    needed = _frameSize;
                    if (needed <= 0) break;
                    
                    // Check buffer size again if frame size has changed mid-loop.
                    if (_workerFrame.Length < needed)
                    {
                        _workerFrame = new float[needed];
                    }
                }
            }
        }

        /// <summary>
        /// Runs on the dedicated worker thread with frames dequeued from the SPSC queue.
        /// Must be non-blocking with bounded CPU where possible.
        /// </summary>
        protected abstract void OnAudioReadAsync(ReadOnlySpan<float> audiobuffer);

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
