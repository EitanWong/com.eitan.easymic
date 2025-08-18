namespace Eitan.EasyMic.Runtime
{
    using System;

    /// <summary>
    /// 顺序化音频管线：严格按添加顺序执行。
    /// - 遇到 AudioWriter：在回调线程串行就地处理
    /// - 遇到 AudioReader：立即分发（Reader 内部仅快速入队，绝不阻塞）
    /// 使用不可变快照数组 + CAS 原子替换，避免回调线程加锁。
    /// </summary>
    public sealed class AudioPipeline : AudioWriter
    {
        // 不区分读写的顺序快照
        private IAudioWorker[] _stagesSnap = Array.Empty<IAudioWorker>();
        private bool _isDisposed = false;

        private AudioState _initializeState;
        private bool _isInitialized;

        public int WorkerCount => System.Threading.Volatile.Read(ref _stagesSnap).Length;

        public override void Initialize(AudioState state)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AudioPipeline));
            }


            _initializeState = state;
            var stages = System.Threading.Volatile.Read(ref _stagesSnap);
            for (int i = 0; i < stages.Length; i++)
            {
                stages[i].Initialize(_initializeState);
            }
            _isInitialized = true;
            base.Initialize(state);
        }

        public void AddWorker(IAudioWorker worker)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AudioPipeline));
            }


            if (worker == null)
            {
                throw new ArgumentNullException(nameof(worker));
            }


            if (_isInitialized)
            {
                worker.Initialize(_initializeState);
            }


            while (true)
            {
                var cur = System.Threading.Volatile.Read(ref _stagesSnap);
                // 去重
                if (Array.IndexOf(cur, worker) >= 0)
                {
                    return;
                }


                var next = new IAudioWorker[cur.Length + 1];
                Array.Copy(cur, next, cur.Length);
                next[^1] = worker;
                var prev = System.Threading.Interlocked.CompareExchange(ref _stagesSnap, next, cur);
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
                var cur = System.Threading.Volatile.Read(ref _stagesSnap);
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


                var prev = System.Threading.Interlocked.CompareExchange(ref _stagesSnap, next, cur);
                if (ReferenceEquals(prev, cur)) { try { worker.Dispose(); } catch { } break; }
            }
        }

        protected override void OnAudioWrite(Span<float> buffer, AudioState state)
        {
            if (!_isInitialized || _isDisposed)
            {
                return;
            }


            var stages = System.Threading.Volatile.Read(ref _stagesSnap);
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

        public bool Contains(IAudioWorker worker)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(AudioPipeline));
            }


            if (worker == null)
            {
                throw new ArgumentNullException(nameof(worker));
            }


            return Array.IndexOf(System.Threading.Volatile.Read(ref _stagesSnap), worker) >= 0;
        }

        public override void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }


            _isDisposed = true;

            var stages = System.Threading.Volatile.Read(ref _stagesSnap);
            for (int i = 0; i < stages.Length; i++)
            {
                try { stages[i].Dispose(); } catch { }
            }
            Array.Clear(stages, 0, stages.Length);
            base.Dispose();
        }
    }
}