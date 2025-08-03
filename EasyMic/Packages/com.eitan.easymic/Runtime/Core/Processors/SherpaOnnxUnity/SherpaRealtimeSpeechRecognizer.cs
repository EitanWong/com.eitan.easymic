#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Threading;
using System.Threading.Tasks;
using Eitan.SherpaOnnxUnity.Runtime;

namespace Eitan.EasyMic.Runtime.SherpaOnnxUnity
{
    /// <summary>
    /// A robust, real-time speech recognizer that processes audio streams off the main thread.
    /// This simplified version directly leverages the underlying async service without an intermediate queue.
    /// </summary>
    public sealed class SherpaRealtimeSpeechRecognizer : AudioReader, IDisposable
    {
        public event Action<string> OnRecognitionResult;
        
        private readonly SpeechRecognition _speechRecognitionService;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SynchronizationContext _mainThreadContext;
        
        // 使用 volatile 关键字确保多线程间的可见性
        private volatile bool _isDisposed;
        private volatile string _lastResult = string.Empty;
        
        public SherpaRealtimeSpeechRecognizer(SpeechRecognition recognitionService)
        {
            _speechRecognitionService = recognitionService
                ?? throw new ArgumentNullException(nameof(recognitionService));
            _cancellationTokenSource = new CancellationTokenSource();
            
            // 捕获主线程的同步上下文
            _mainThreadContext = SynchronizationContext.Current;
        }
        
        /// <summary>
        /// 生产者: 在高优先级的音频线程上被频繁调用。
        /// 优化后，此方法直接调用异步处理，而不会阻塞音频线程。
        /// </summary>
        public override void OnAudioRead(ReadOnlySpan<float> audioData, AudioState state)
        {
            // 如果正在销毁，则不再处理新的音频数据
            if (_isDisposed) return;
            
            // 必须复制数据，因为 ReadOnlySpan 的生命周期很短，而处理是异步的。
            // 为了简单，我们直接创建新数组，而不是使用 ArrayPool。
            float[] audioCopy = audioData.ToArray();
            
            // "Fire-and-forget": 启动一个异步任务但不用等待它完成。
            // 这可以防止阻塞关键的音频线程。
            // 使用下划线 `_ =` 是向编译器和开发者明确表示我们不等待此任务。
            _ = ProcessChunkAsync(audioCopy, state.SampleRate);
        }
        
        /// <summary>
        /// 消费者: 每个音频块都会触发一次此异步方法的执行。
        /// </summary>
        private async Task ProcessChunkAsync(float[] audioChunk, int sampleRate)
        {
            try
            {
                if (!_isDisposed && _speechRecognitionService != null)
                {
                    // 在后台线程执行识别
                    string resultText = await _speechRecognitionService.SpeechTranscriptionAsync(
                        audioChunk,
                        sampleRate,
                        _cancellationTokenSource.Token
                    );
                    
                    // 只有当结果与上次不同时才触发事件，减少调用次数
                    if (resultText != _lastResult)
                    {
                        _lastResult = resultText;
                        
                        // 切换到主线程执行事件回调
                        if (_mainThreadContext != null)
                        {
                            _mainThreadContext.Post(_ => 
                            {
                                if (!_isDisposed)
                                {
                                    OnRecognitionResult?.Invoke(resultText);
                                }
                            }, null);
                        }
                        else
                        {
                            // 如果没有主线程上下文，直接调用（可能在测试环境中）
                            OnRecognitionResult?.Invoke(resultText);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 当 CancellationToken 被取消时，这是预期的异常，无需处理。
            }
            catch (Exception ex)
            {
                // 捕获其他可能的识别错误。
                // 使用 Post 确保错误日志也在主线程输出
                if (_mainThreadContext != null)
                {
                    _mainThreadContext.Post(_ => 
                    {
                        UnityEngine.Debug.LogError($"[SherpaRealtimeSpeechRecognizer] Recognition error: {ex.Message}");
                    }, null);
                }
                else
                {
                    UnityEngine.Debug.LogError($"[SherpaRealtimeSpeechRecognizer] Recognition error: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// 销毁资源，停止所有正在进行的识别任务。
        /// </summary>
        public override void Dispose()
        {
            // 使用标准的 Dispose 模式
            if (_isDisposed) return;
            _isDisposed = true;
            
            // 向上通知取消所有正在进行的异步任务
            try
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SherpaRealtimeSpeechRecognizer] Error during dispose: {ex.Message}");
            }
            
            // 调用父类的 Dispose (如果存在)
            base.Dispose();
            
            // 可选，如果需要确保事件订阅者被清理
            OnRecognitionResult = null;
        }
    }
}
#endif