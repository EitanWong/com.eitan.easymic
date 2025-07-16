namespace Eitan.EasyMic.Runtime
{
    using System;
    using System.Collections.Generic;


    /// <summary>
    /// Manages an ordered sequence of audio workers (readers and writers) to process an audio stream.
    /// The pipeline itself acts as a single IAudioWorker that can be attached to a MicSystem recording session.
    /// This allows you to compose complex processing logic from smaller, reusable parts.
    /// </summary>
    public sealed class AudioPipeline : IAudioWorker
    {
        private readonly List<IAudioWorker> _workers = new List<IAudioWorker>();
        private readonly object _lock = new object();
        private bool _isDisposed = false;

        private AudioState _initializeState;
        private bool _isInitialized;

        /// <summary>
        /// Gets the number of workers currently in the pipeline.
        /// </summary>
        public int WorkerCount
        {
            get
            {
                lock (_lock)
                {
                    return _workers.Count;
                }
            }
        }

        /// <summary>
        /// Initializes the pipeline and all workers currently within it.
        /// This method is called by the MicSystem when the recording starts.
        /// </summary>
        public void Initialize(AudioState state)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPipeline));

            this._initializeState = state;

            lock (_lock)
            {
                // Initialize all workers that were added before the pipeline was started.
                foreach (var worker in _workers)
                {
                    worker.Initialize(this._initializeState);
                }
            }
            _isInitialized = true;
        }

        /// <summary>
        /// Adds a worker to the end of the pipeline.
        /// </summary>
        /// <param name="worker">The audio worker (Reader or Writer) to add.</param>
        public void AddWorker(IAudioWorker worker)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPipeline));
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            lock (_lock)
            {
                if (_workers.Contains(worker)) return;

                // If the pipeline is already running, initialize the new worker immediately.
                if (_isInitialized)
                {
                    worker.Initialize(this._initializeState);
                }
                _workers.Add(worker);

            }
        }

        /// <summary>
        /// Removes a worker from the pipeline and disposes it.
        /// </summary>
        /// <param name="worker">The audio worker to remove.</param>
        public void RemoveWorker(IAudioWorker worker)
        {
            if (worker == null) return;

            lock (_lock)
            {
                if (_workers.Remove(worker))
                {
                    // Dispose the worker once it's removed.
                    try { worker.Dispose(); } catch { /* Errors during disposal can be ignored */ }
                }
            }
        }

        /// <summary>
        /// Processes the audio buffer by passing it through each worker in sequence.
        /// </summary>
        public void OnAudioPass(Span<float> buffer, AudioState state)
        {
            if (!_isInitialized || _isDisposed) return;

            foreach (var worker in _workers)
            {
                try
                {
                    // 在将 buffer 传递给每个 worker 之前，都根据当前 state.Length 进行判断
                    Span<float> bufferForThisWorker = buffer;
                    if (state.Length > 0 && state.Length < buffer.Length)
                    {
                        bufferForThisWorker = buffer.Slice(0, state.Length);
                    }

                    // 将可能被切片后的 buffer 传递给 worker
                    worker.OnAudioPass(bufferForThisWorker, state);
                }
                catch (Exception)
                {
                    // Debug.LogError($"Error in audio worker '{worker.GetType().Name}': {ex.Message}");
                    // 使用 'throw;' 而不是 'throw ex;' 来保留原始的堆栈跟踪信息，便于调试
                    throw;
                }
            }
        }

        public bool Contains(IAudioWorker worker)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPipeline));
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            lock (_lock)
            {
                return _workers.Contains(worker);
            }
        }

        /// <summary>
        /// Disposes the pipeline and all workers contained within it.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            lock (_lock)
            {
                foreach (var worker in _workers)
                {
                    try { worker.Dispose(); } catch { /* ignored */ }
                }
                _workers.Clear();
            }
        }
    }


}