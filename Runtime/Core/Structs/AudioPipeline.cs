namespace Eitan.EasyMic.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;


    /// <summary>
    /// Manages an ordered sequence of audio workers with hybrid execution strategy:
    /// - AudioWriters: Execute synchronously and serially (they modify data)
    /// - AudioReaders: Execute asynchronously and in parallel (they only read data)
    /// This prevents blocking while maintaining proper data flow.
    /// Note: AudioPipeline itself extends AudioWriter to allow nesting.
    /// </summary>
    public sealed class AudioPipeline : AudioWriter
    {
        private readonly List<AudioWriter> _writers = new List<AudioWriter>();
        private readonly List<AudioReader> _readers = new List<AudioReader>();
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
                    return _writers.Count + _readers.Count;
                }
            }
        }

        /// <summary>
        /// Initializes the pipeline and all workers currently within it.
        /// This method is called by the MicSystem when the recording starts.
        /// </summary>
        public override void Initialize(AudioState state)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPipeline));

            this._initializeState = state;

            lock (_lock)
            {
                // Initialize all writers
                foreach (var writer in _writers)
                {
                    writer.Initialize(this._initializeState);
                }
                
                // Initialize all readers
                foreach (var reader in _readers)
                {
                    reader.Initialize(this._initializeState);
                }
            }
            _isInitialized = true;
            
            // Call base initialization
            base.Initialize(state);
        }

        /// <summary>
        /// Adds a worker to the pipeline. Automatically categorizes by type:
        /// - AudioWriters are added to the serial execution chain
        /// - AudioReaders are added to the parallel execution pool
        /// </summary>
        /// <param name="worker">The audio worker (Reader or Writer) to add.</param>
        public void AddWorker(IAudioWorker worker)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPipeline));
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            lock (_lock)
            {
                // Automatic categorization based on worker type
                if (worker is AudioWriter writer)
                {
                    if (_writers.Contains(writer)) return;
                    
                    if (_isInitialized)
                    {
                        writer.Initialize(this._initializeState);
                    }
                    _writers.Add(writer);
                }
                else if (worker is AudioReader reader)
                {
                    if (_readers.Contains(reader)) return;
                    
                    if (_isInitialized)
                    {
                        reader.Initialize(this._initializeState);
                    }
                    _readers.Add(reader);
                }
                else if (worker is AudioPipeline pipeline)
                {
                    // Handle nested pipelines - this allows AudioPipeline to be added to AudioPipeline
                    // We treat nested pipelines as writers since they may contain writers that modify data
                    if (_writers.Contains(pipeline)) return;
                    
                    if (_isInitialized)
                    {
                        pipeline.Initialize(this._initializeState);
                    }
                    _writers.Add(pipeline);
                }
                else
                {
                    throw new ArgumentException($"Worker must be either AudioWriter, AudioReader, or AudioPipeline. Got: {worker.GetType().Name}");
                }
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
                bool removed = false;
                
                if (worker is AudioWriter writer)
                {
                    removed = _writers.Remove(writer);
                }
                else if (worker is AudioReader reader)
                {
                    removed = _readers.Remove(reader);
                }
                
                if (removed)
                {
                    try { worker.Dispose(); } catch { /* Errors during disposal can be ignored */ }
                }
            }
        }

        /// <summary>
        /// Processes the audio buffer using hybrid execution strategy:
        /// 1. Writers execute synchronously and serially (modify data)
        /// 2. Readers execute asynchronously and in parallel (read-only analysis)
        /// </summary>
        public override void OnAudioWrite(Span<float> buffer, AudioState state)
        {
            if (!_isInitialized || _isDisposed) return;

            // Phase 1: Process AudioWriters synchronously and serially
            // These modify the buffer, so they must execute in order and block
            ProcessWritersSync(buffer, state);
            
            // Phase 2: Process AudioReaders asynchronously and in parallel
            // These only read data, so they can run concurrently without blocking
            ProcessReadersAsync(buffer, state);
        }

        /// <summary>
        /// Executes all AudioWriters synchronously in order.
        /// Each writer can modify the buffer, so they must be serial.
        /// </summary>
        private void ProcessWritersSync(Span<float> buffer, AudioState state)
        {
            foreach (var writer in _writers)
            {
                try
                {
                    Span<float> bufferForWriter = buffer;
                    if (state.Length > 0 && state.Length < buffer.Length)
                    {
                        bufferForWriter = buffer.Slice(0, state.Length);
                    }

                    // Synchronous execution - writer can modify buffer and state
                    writer.OnAudioPass(bufferForWriter, state);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"Error in audio writer '{writer.GetType().Name}': {ex.Message}");
                    // Continue processing other writers
                }
            }
        }

        /// <summary>
        /// Executes all AudioReaders asynchronously in parallel.
        /// Since they're read-only, they can process concurrently without conflicts.
        /// </summary>
        private void ProcessReadersAsync(Span<float> buffer, AudioState state)
        {
            if (_readers.Count == 0) return;

            // Create copies for async processing since readers only need read access
            float[] bufferCopy = buffer.ToArray();
            AudioState stateCopy = state;

            // Fire-and-forget async processing for all readers
            _ = Task.Run(() =>
            {
                // Process all readers in parallel
                Parallel.ForEach(_readers, reader =>
                {
                    try
                    {
                        Span<float> readerBuffer = bufferCopy.AsSpan();
                        if (stateCopy.Length > 0 && stateCopy.Length < readerBuffer.Length)
                        {
                            readerBuffer = readerBuffer.Slice(0, stateCopy.Length);
                        }
                        
                        // Async execution - reader gets read-only access
                        reader.OnAudioPass(readerBuffer, stateCopy);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogError($"Error in audio reader '{reader.GetType().Name}': {ex.Message}");
                    }
                });
            });
        }

        public bool Contains(IAudioWorker worker)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioPipeline));
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            lock (_lock)
            {
                if (worker is AudioWriter writer)
                {
                    return _writers.Contains(writer);
                }
                else if (worker is AudioReader reader)
                {
                    return _readers.Contains(reader);
                }
                return false;
            }
        }

        /// <summary>
        /// Disposes the pipeline and all workers contained within it.
        /// </summary>
        public override void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            lock (_lock)
            {
                foreach (var writer in _writers)
                {
                    try { writer.Dispose(); } catch { /* ignored */ }
                }
                foreach (var reader in _readers)
                {
                    try { reader.Dispose(); } catch { /* ignored */ }
                }
                
                _writers.Clear();
                _readers.Clear();
            }
            
            // Call base dispose
            base.Dispose();
        }
    }


}