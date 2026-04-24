namespace Eitan.EasyMic.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// 顺序化音频管线：严格按添加顺序执行。
    /// - 遇到 AudioWriter：在回调线程串行就地处理
    /// - 遇到 AudioReader：立即分发（Reader 内部仅快速入队，绝不阻塞）
    /// 使用不可变快照数组 + CAS 原子替换，避免回调线程加锁。
    /// </summary>
    public sealed class AudioPipeline : AudioWriter
    {
        // 不区分读写的顺序快照（通过 Volatile/Interlocked 保证可见性与原子性）
        private IAudioWorker[] _stagesSnap = Array.Empty<IAudioWorker>();
        private int _isDisposed;

        private AudioContext _initializeState;
        private int _isInitialized;

        private int _activeAudioPasses;
        private readonly object _retiredLock = new object();
        private readonly Queue<IAudioWorker> _retiredWorkers = new Queue<IAudioWorker>();
        private int _retiredDrainScheduled;

        public int WorkerCount => Volatile.Read(ref _stagesSnap).Length;

        public override void Initialize(AudioContext state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (Volatile.Read(ref _isDisposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AudioPipeline));
            }

            _initializeState = new AudioContext(state.ChannelCount, state.SampleRate, state.Length);
            var stages = Volatile.Read(ref _stagesSnap);
            for (int i = 0; i < stages.Length; i++)
            {
                stages[i].Initialize(_initializeState);
            }
            Volatile.Write(ref _isInitialized, 1);
            base.Initialize(state);
        }

        public void AddWorker(IAudioWorker worker)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AudioPipeline));
            }


            if (worker == null)
            {
                throw new ArgumentNullException(nameof(worker));
            }


            if (Volatile.Read(ref _isInitialized) != 0)
            {
                var initState = _initializeState;
                if (initState != null)
                {
                    worker.Initialize(initState);
                }
            }


            while (true)
            {
                var cur = Volatile.Read(ref _stagesSnap);
                // 去重
                if (Array.IndexOf(cur, worker) >= 0)
                {
                    return;
                }


                var next = new IAudioWorker[cur.Length + 1];
                Array.Copy(cur, next, cur.Length);
                next[^1] = worker;
                var prev = Interlocked.CompareExchange(ref _stagesSnap, next, cur);
                if (ReferenceEquals(prev, cur))
                {
                    break;
                }

            }
        }

        public void RemoveWorker(IAudioWorker worker)
        {
            if (worker == null)
            {
                return;
            }

            while (true)
            {
                var cur = Volatile.Read(ref _stagesSnap);
                int idx = Array.IndexOf(cur, worker);
                if (idx < 0)
                {
                    break;
                }


                var next = new IAudioWorker[cur.Length - 1];
                if (idx > 0)
                {
                    Array.Copy(cur, 0, next, 0, idx);
                }

                if (idx < cur.Length - 1)
                {
                    Array.Copy(cur, idx + 1, next, idx, cur.Length - idx - 1);
                }

                var prev = Interlocked.CompareExchange(ref _stagesSnap, next, cur);
                if (ReferenceEquals(prev, cur))
                {
                    RetireWorker(worker);
                    break;
                }
            }
        }

        protected override void OnAudioWrite(Span<float> buffer, AudioContext state)
        {
            if (Volatile.Read(ref _isInitialized) == 0 || Volatile.Read(ref _isDisposed) != 0)
            {
                return;
            }

            Interlocked.Increment(ref _activeAudioPasses);
            try
            {
                var stages = Volatile.Read(ref _stagesSnap);
                for (int i = 0; i < stages.Length; i++)
                {
                    var w = stages[i];
                    try
                    {
                        // 根据最新的 state 计算当下有效帧长度，允许前序 Writer 动态更新 state.Length
                        // 注意：必须严格尊重 state.Length==0 的语义（表示无数据/端点），不能退回到 buffer.Length
                        int curLen = state.Length;
                        if (curLen < 0)
                        {
                            curLen = 0;
                        }

                        if (curLen > buffer.Length)
                        {
                            curLen = buffer.Length;
                        }

                        var curFrame = buffer.Slice(0, curLen);
                        // Writer：就地修改；Reader：快速入队（内部异步消费）
                        w.OnAudioPass(curFrame, state);
                    }
                    catch { /* RT 安全：吞掉异常，保障音频回调不中断 */ }
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeAudioPasses);
            }
        }

        public bool Contains(IAudioWorker worker)
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AudioPipeline));
            }


            if (worker == null)
            {
                throw new ArgumentNullException(nameof(worker));
            }


            return Array.IndexOf(Volatile.Read(ref _stagesSnap), worker) >= 0;
        }

        /// <summary>
        /// Returns the first worker of type <typeparamref name="T"/> from the current pipeline snapshot.
        /// </summary>
        public T GetWorker<T>() where T : class, IAudioWorker
        {
            if (Volatile.Read(ref _isDisposed) != 0)
            {
                return null;
            }

            var stages = Volatile.Read(ref _stagesSnap);
            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i] is T typed)
                {
                    return typed;
                }
            }

            return null;
        }

        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            var stages = Interlocked.Exchange(ref _stagesSnap, Array.Empty<IAudioWorker>());
            if (Volatile.Read(ref _activeAudioPasses) == 0)
            {
                for (int i = 0; i < stages.Length; i++)
                {
                    SafeDispose(stages[i]);
                }
            }
            else
            {
                lock (_retiredLock)
                {
                    for (int i = 0; i < stages.Length; i++)
                    {
                        if (stages[i] != null)
                        {
                            _retiredWorkers.Enqueue(stages[i]);
                        }
                    }
                }

                ScheduleRetiredDrain();
            }

            if (!EasyMicThreading.IsAudioThread)
            {
                DrainRetiredWorkersNow();
            }
            base.Dispose();
        }

        private void RetireWorker(IAudioWorker worker)
        {
            if (worker == null)
            {
                return;
            }

            if (Volatile.Read(ref _activeAudioPasses) == 0)
            {
                SafeDispose(worker);
                return;
            }

            lock (_retiredLock)
            {
                _retiredWorkers.Enqueue(worker);
            }

            ScheduleRetiredDrain();
        }

        private void ScheduleRetiredDrain()
        {
            if (Interlocked.CompareExchange(ref _retiredDrainScheduled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                ThreadPool.QueueUserWorkItem(static s => ((AudioPipeline)s).DrainRetiredWorkersNow(), this);
            }
            catch
            {
                Interlocked.Exchange(ref _retiredDrainScheduled, 0);
            }
        }

        private void DrainRetiredWorkersNow()
        {
            if (EasyMicThreading.IsAudioThread)
            {
                Interlocked.Exchange(ref _retiredDrainScheduled, 0);
                ScheduleRetiredDrain();
                return;
            }

            try
            {
                var spinner = new SpinWait();
                while (Volatile.Read(ref _activeAudioPasses) != 0)
                {
                    spinner.SpinOnce();
                    if (spinner.NextSpinWillYield)
                    {
                        Thread.Sleep(0);
                    }
                }

                while (true)
                {
                    IAudioWorker worker;
                    lock (_retiredLock)
                    {
                        if (_retiredWorkers.Count == 0)
                        {
                            Interlocked.Exchange(ref _retiredDrainScheduled, 0);
                            return;
                        }

                        worker = _retiredWorkers.Dequeue();
                    }

                    SafeDispose(worker);
                }
            }
            catch
            {
                Interlocked.Exchange(ref _retiredDrainScheduled, 0);
            }
        }

        private static void SafeDispose(IAudioWorker worker)
        {
            try { worker?.Dispose(); } catch { }
        }
    }
}
