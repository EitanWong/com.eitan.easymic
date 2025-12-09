using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Eitan.EasyMic.Runtime;
using Eitan.SherpaONNXUnity.Runtime;
using UnityEngine;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using System.Collections.Concurrent;

#region Helper Modules
namespace Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal
{
    internal static class TextProcessor
    {
        #region Regex Definitions
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex CodeBlockRegex = new Regex(@"```[\s\S]*?```", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new Regex(@"`[^`]*`", RegexOptions.Compiled);
        private static readonly Regex MdImageRegex = new Regex(@"!\[([^\\]*)\]\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex MdLinkRegex = new Regex(@"\[([^\\]*)\]\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex MdAutoLinkRegex = new Regex(@"<\s*https?://[^>]+>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BareUrlRegex = new Regex(@"(?:https?://|www\.)[^\s<>\]\}\}，。！？；：】》）]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StrongEmRegex = new Regex(@"(\$\*\*|__)(.*?)\1", RegexOptions.Compiled);
        private static readonly Regex EmRegex = new Regex(@"(\*|_)(.*?)\1", RegexOptions.Compiled);
        private static readonly Regex StrikeRegex = new Regex(@"~~(.*?)~~", RegexOptions.Compiled);
        private static readonly Regex HeadingRegex = new Regex(@"^\s*#{1,6}\s*", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex ListMarkerRegex = new Regex(@"^(\s*[-*+]|\s*\d+\.)\s+", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex SentenceSplitRegex = new Regex(@"(?<=[.!?\n。！？])(?=\s|\S)", RegexOptions.Compiled);
        #endregion

        public static string[] SplitSentences(string text) => SentenceSplitRegex.Split(text);

        public static string CleanTextForTts(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }


            string cleaned = text;
            cleaned = cleaned.Replace("{{ ", string.Empty).Replace(" }}", string.Empty);
            cleaned = CodeBlockRegex.Replace(cleaned, string.Empty);
            cleaned = InlineCodeRegex.Replace(cleaned, string.Empty);
            cleaned = HtmlTagRegex.Replace(cleaned, string.Empty);
            cleaned = MdImageRegex.Replace(cleaned, m => m.Groups[1].Value);
            cleaned = MdLinkRegex.Replace(cleaned, m => m.Groups[1].Value);
            cleaned = MdAutoLinkRegex.Replace(cleaned, string.Empty);
            cleaned = BareUrlRegex.Replace(cleaned, string.Empty);
            cleaned = StrongEmRegex.Replace(cleaned, m => m.Groups[2].Value);
            cleaned = EmRegex.Replace(cleaned, m => m.Groups[2].Value);
            cleaned = StrikeRegex.Replace(cleaned, m => m.Groups[1].Value);
            cleaned = HeadingRegex.Replace(cleaned, string.Empty);
            cleaned = ListMarkerRegex.Replace(cleaned, string.Empty);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        public static string ApplyPronunciationCorrection(string sentence) => sentence.Replace("海晟", "海胜");
    }

    internal sealed class SynthesisJob
    {
        private int _finalized;
        private readonly string _originalSentence;
        private readonly SpeechSynthesis _speechSynthesis;
        private readonly SpeechSynthesizerConfiguration _config;
        private readonly Action<string> _onStart;
        private readonly Action<string> _onFinish;
        private readonly Func<PlaybackHandle> _playbackHandleProvider;

        public bool IsDone { get; private set; }

        public SynthesisJob(
            string sentence,
            SpeechSynthesis speechSynthesis,
            SpeechSynthesizerConfiguration config,
            Action<string> onStart,
            Action<string> onFinish,
            Func<PlaybackHandle> playbackHandleProvider)
        {
            _originalSentence = sentence;
            _speechSynthesis = speechSynthesis;
            _config = config;
            _onStart = onStart;
            _onFinish = onFinish;
            _playbackHandleProvider = playbackHandleProvider;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            string ttsText = TextProcessor.CleanTextForTts(_originalSentence);

            try
            {
                _onStart?.Invoke(ttsText);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SynthesisJob] OnStart callback error: {ex.Message}");
            }

            if (string.IsNullOrEmpty(ttsText))
            {
                FinalizeJob(ttsText);
                return;
            }

            int sampleRate = Math.Max(8000, _config.SampleRates);
            const int channels = 1;

            var playback = _playbackHandleProvider?.Invoke() ?? default;
            if (!playback.IsValid)
            {
                Debug.LogError("[SynthesisJob] Invalid playback handle.");
                FinalizeJob(ttsText);
                return;
            }

            try
            {
                var ttsRequest = TextProcessor.ApplyPronunciationCorrection(ttsText);
                cancellationToken.ThrowIfCancellationRequested();

                await _speechSynthesis.GenerateWithProgressCallbackAsync(
                    ttsRequest,
                    _config.VoiceId,
                    _config.Speed,
                    (samplesPtr, count, progress) =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return 0;
                        }


                        if (samplesPtr == IntPtr.Zero || count <= 0)
                        {
                            return 1;
                        }


                        var buf = ArrayPool<float>.Shared.Rent(count);
                        try
                        {
                            Marshal.Copy(samplesPtr, buf, 0, count);
                            var currentHandle = _playbackHandleProvider?.Invoke() ?? default;
                            if (currentHandle.IsValid)
                            {
                                currentHandle.Enqueue(buf, count, channels, sampleRate, false);
                            }
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(buf);
                        }
                        return 1;
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SynthesisJob] Generation exception: {ex}");
            }
            finally
            {
                FinalizeJob(ttsText);
            }
        }

        private void FinalizeJob(string sentence)
        {
            if (Interlocked.Exchange(ref _finalized, 1) == 0)
            {
                IsDone = true;
                try
                {
                    _onFinish?.Invoke(sentence);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SynthesisJob] OnFinish callback error: {ex.Message}");
                }
            }
        }
    }

    internal sealed class ModelInitializationHandler : ISherpaFeedbackHandler
    {
        public event Action<string, float> OnProgress;
        public event Action<FailedFeedback> OnFailed;
        public event Action<SuccessFeedback> OnSuccess;

        private enum LoadStage { Unknown, Prepare, Download, Uncompress, Verify, Clean, Load, Cancel, Success, Failed }
        private readonly object _progressLock = new object();
        private LoadStage _currentStage = LoadStage.Unknown;
        private float _currentProgress;

        private static readonly (float min, float max) PrepareRange = (0.00f, 0.02f);
        private static readonly (float min, float max) DownloadRange = (0.02f, 0.60f);
        private static readonly (float min, float max) UncompressRange = (0.60f, 0.75f);
        private static readonly (float min, float max) VerifyRange = (0.75f, 0.85f);
        private static readonly (float min, float max) CleanRange = (0.85f, 0.95f);
        private static readonly (float min, float max) LoadRange = (0.95f, 0.99f);

        private static float MapStageProgress(LoadStage stage, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            (float min, float max) r = stage switch
            {
                LoadStage.Prepare => PrepareRange,
                LoadStage.Download => DownloadRange,
                LoadStage.Uncompress => UncompressRange,
                LoadStage.Verify => VerifyRange,
                LoadStage.Clean => CleanRange,
                LoadStage.Load => LoadRange,
                LoadStage.Success => (1f, 1f),
                _ => (0f, 0f)
            };
            return Mathf.Approximately(r.min, r.max) ? r.min : r.min + (r.max - r.min) * fraction;
        }

        private void PublishProgress(SherpaFeedback feedback)
        {
            if (feedback == null)
            {
                return;
            }


            var typeName = feedback.GetType().Name;
            LoadStage stage = typeName switch
            {
                var s when s.Contains("Prepare") => LoadStage.Prepare,
                var s when s.Contains("Download") => LoadStage.Download,
                var s when s.Contains("Uncompress") || s.Contains("Unzip") => LoadStage.Uncompress,
                var s when s.Contains("Verify") => LoadStage.Verify,
                var s when s.Contains("Clean") => LoadStage.Clean,
                var s when s.Contains("Load") => LoadStage.Load,
                var s when s.Contains("Cancel") => LoadStage.Cancel,
                var s when s.Contains("Success") => LoadStage.Success,
                var s when s.Contains("Failed") || s.Contains("Error") => LoadStage.Failed,
                _ => LoadStage.Unknown
            };

            float stageFraction = stage switch
            {
                LoadStage.Download => 0.25f,
                LoadStage.Uncompress or LoadStage.Verify or LoadStage.Clean => 0.5f,
                LoadStage.Load => 0.9f,
                _ => 0f
            };

            float globalProgress = MapStageProgress(stage, stageFraction);

            lock (_progressLock)
            {
                if (stage == LoadStage.Success) { _currentProgress = 1f; _currentStage = LoadStage.Success; }
                else if (stage == LoadStage.Failed) { _currentStage = LoadStage.Failed; }
                else if (stage == LoadStage.Cancel) { _currentStage = LoadStage.Cancel; }
                else if (globalProgress >= _currentProgress || stage != _currentStage)
                {
                    _currentProgress = Mathf.Clamp01(globalProgress);
                    _currentStage = stage;
                }
            }
            OnProgress?.Invoke(feedback.Message ?? feedback.ToString(), _currentProgress);
        }

        public void OnFeedback(PrepareFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(DownloadFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(DecompressFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(VerifyFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(CleanFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(LoadFeedback feedback) => PublishProgress(feedback);
        public void OnFeedback(CancelFeedback feedback) => PublishProgress(feedback);

        public void OnFeedback(SuccessFeedback feedback)
        {
            lock (_progressLock) { _currentProgress = 1f; _currentStage = LoadStage.Success; }
            OnProgress?.Invoke(feedback?.Message ?? "Loaded", 1f);
            OnSuccess?.Invoke(feedback);
        }

        public void OnFeedback(FailedFeedback feedback)
        {
            lock (_progressLock) { _currentStage = LoadStage.Failed; }
            OnProgress?.Invoke(feedback?.Message ?? "Failed", _currentProgress);
            OnFailed?.Invoke(feedback);
        }
    }
}
#endregion

#region Configuration Class
[Serializable]
public sealed class SpeechSynthesizerConfiguration
{
    [Serializable]
    public struct TTSPreset
    {
        public const string DefaultPresetId = "default";
        public string Id;
        public string DisplayName;
        public string modelId;
        public int voiceId;
        public float speed;
        public int sampleRates;

        public TTSPreset Clone() => (TTSPreset)MemberwiseClone();

        public static TTSPreset Create(string modelId, int voiceId, float speed, int sampleRate,
            string id = DefaultPresetId, string displayName = "Default")
        {
            return new TTSPreset
            {
                Id = id,
                DisplayName = displayName,
                modelId = modelId,
                voiceId = voiceId,
                speed = speed,
                sampleRates = sampleRate
            };
        }

        public static TTSPreset Default => Create("vits-melo-tts-zh_en", 1, 1f, 44100);
    }

    [SerializeField] private TTSPreset[] _presets = { TTSPreset.Default };
    [SerializeField] private string _activePresetId = TTSPreset.DefaultPresetId;

    public IReadOnlyList<TTSPreset> Presets => _presets ?? Array.Empty<TTSPreset>();
    public string ActivePresetId
    {
        get => string.IsNullOrWhiteSpace(_activePresetId) ? TTSPreset.DefaultPresetId : _activePresetId;
        private set => _activePresetId = value;
    }

    private TTSPreset GetActivePresetRaw()
    {
        if (Presets.Count == 0)
        {
            return TTSPreset.Default;
        }


        var presetId = ActivePresetId;
        if (!string.IsNullOrWhiteSpace(presetId))
        {
            foreach (var cfg in Presets)
            {
                if (string.Equals(cfg.Id, presetId, StringComparison.OrdinalIgnoreCase))
                {

                    return cfg;
                }

            }
        }
        return Presets[0];
    }

    public string ModelId => GetActivePresetRaw().modelId ?? TTSPreset.Default.modelId;
    public int VoiceId => GetActivePresetRaw().voiceId != 0 ? GetActivePresetRaw().voiceId : TTSPreset.Default.voiceId;
    public float Speed => GetActivePresetRaw().speed > 0 ? GetActivePresetRaw().speed : TTSPreset.Default.speed;
    public int SampleRates => GetActivePresetRaw().sampleRates != 0 ? GetActivePresetRaw().sampleRates : TTSPreset.Default.sampleRates;

    public bool SetActivePreset(string presetId)
    {
        if (Presets.Count == 0)
        {
            bool isDefault = string.IsNullOrWhiteSpace(presetId) ||
                           string.Equals(presetId, TTSPreset.DefaultPresetId, StringComparison.OrdinalIgnoreCase);
            if (isDefault)
            {
                ActivePresetId = TTSPreset.DefaultPresetId;
            }


            return isDefault;
        }

        if (string.IsNullOrWhiteSpace(presetId))
        {
            ActivePresetId = !string.IsNullOrWhiteSpace(Presets[0].Id) ? Presets[0].Id : TTSPreset.DefaultPresetId;
            return true;
        }

        foreach (var p in Presets)
        {
            if (string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase))
            {
                ActivePresetId = p.Id;
                return true;
            }
        }
        return false;
    }

    public static SpeechSynthesizerConfiguration CreateDefault() => new SpeechSynthesizerConfiguration();
}
#endregion

public class SpeechSynthesizer : MonoBehaviour
{
    #region Public Properties & Events
    [Header("Initialization")]
    [SerializeField] private bool _initOnAwake = true;
    public bool InitOnAwake => _initOnAwake;
    public bool Initialized { get; private set; }
    public bool IsProcessingTTS => _ttsInProgress > 0;

    [Header("Configuration")]
    [SerializeField] private SpeechSynthesizerConfiguration _ttsConfig;

    public event Action<string, float> OnLoadingProgressFeedback;
    public event Action<FailedFeedback> OnLoadingFailedFeedback;
    public event Action<SuccessFeedback> OnLoadingSuccessedFeedback;
    public event Action<bool> OnSynthesizerInitialized;
    public event Action<bool> OnTTSStateChanged;
    public event Action<string> OnSentenceStarted;
    public event Action<string> OnSentenceFinished;
    #endregion

    #region Private Fields
    private SpeechSynthesis _speechSynthesis;
    private volatile bool _initializing;
    private int _ttsInProgress; // 使用Interlocked操作

    private readonly ConcurrentQueue<string> _sentenceQueue = new ConcurrentQueue<string>();
    private Coroutine _ttsPumpCoroutine;

    private Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.ModelInitializationHandler _progressHandler;

    // 会话管理 - 核心修复
    private readonly object _sessionLock = new object();
    private CancellationTokenSource _sessionCts;
    private Task _currentSessionTask = Task.CompletedTask;
    private long _sessionId; // 用于识别当前会话

    private readonly object _playbackLock = new object();
    private PlaybackHandle _playbackHandle;
    private bool _playbackHandleInitialized;
    private long _playbackSessionId; // 跟踪playback属于哪个会话

    private string _logPrefix;
    private string LogPrefix => !string.IsNullOrEmpty(_logPrefix) ? _logPrefix : "[SpeechSynthesizer] ";
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        _ttsConfig ??= SpeechSynthesizerConfiguration.CreateDefault();
        _progressHandler = new Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.ModelInitializationHandler();
        _logPrefix = $"[SpeechSynthesizer:{name}] ";

        _progressHandler.OnProgress += (msg, progress) => OnLoadingProgressFeedback?.Invoke(msg, progress);
        _progressHandler.OnFailed += feedback => OnLoadingFailedFeedback?.Invoke(feedback);
        _progressHandler.OnSuccess += feedback => OnLoadingSuccessedFeedback?.Invoke(feedback);
    }

    private System.Collections.IEnumerator Start()
    {
        if (_initOnAwake)
        {
            Init();
            yield return new WaitUntil(() => Initialized || !_initializing);
        }
    }

    private async void OnDestroy()
    {
        await StopAndWaitAsync();

        if (_ttsPumpCoroutine != null)
        {
            StopCoroutine(_ttsPumpCoroutine);
            _ttsPumpCoroutine = null;
        }

        DisposePlaybackHandle();

        _speechSynthesis?.Dispose();
        _speechSynthesis = null;
    }
    #endregion

    #region Public API
    public void Init()
    {
        if (Initialized || _initializing)
        {
            return;
        }


        _initializing = true;
        try
        {
            var reporter = new SherpaONNXFeedbackReporter(null, _progressHandler);
            _speechSynthesis = new SpeechSynthesis(_ttsConfig.ModelId, _ttsConfig.SampleRates, reporter);

            if (_ttsPumpCoroutine == null)
            {
                _ttsPumpCoroutine = StartCoroutine(TTSPumpLoop());
            }
            StartCoroutine(WaitForModelInitialization());
        }
        catch (Exception ex)
        {
            Debug.LogError($"{LogPrefix}Init failed: {ex}");
            _initializing = false;
        }
    }

    /// <summary>
    /// 停止当前所有TTS任务，清空队列。用于中断。
    /// </summary>
    public void Stop()
    {
        _ = StopAndWaitAsync();
    }

    /// <summary>
    /// 异步停止并等待当前任务完成。
    /// </summary>
    public async Task StopAndWaitAsync()
    {
        Task taskToWait;
        long oldSessionId;

        lock (_sessionLock)
        {
            oldSessionId = _sessionId;

            // 取消当前会话
            if (_sessionCts != null)
            {
                try
                {
                    if (!_sessionCts.IsCancellationRequested)
                    {
                        _sessionCts.Cancel();
                    }

                }
                catch (ObjectDisposedException) { }
            }

            taskToWait = _currentSessionTask;
        }

        // 清空队列
        while (_sentenceQueue.TryDequeue(out _)) { }

        // 等待当前任务完成
        if (taskToWait != null && !taskToWait.IsCompleted)
        {
            try
            {
                await taskToWait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix}Error waiting for session to stop: {ex.Message}");
            }
        }

        // 停止并释放当前playback（在主线程）
        StopAndDisposeCurrentPlayback(oldSessionId);

        // 更新状态
        UpdateTtsState(false);
    }

    public void EnqueueSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }


        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }


        _sentenceQueue.Enqueue(trimmed);
    }

    public void EnqueueText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }


        string[] sentences = Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.TextProcessor.SplitSentences(text);
        foreach (string sentence in sentences)
        {
            EnqueueSentence(sentence);
        }
    }
    #endregion

    #region Playback Management
    private PlaybackHandle EnsurePlaybackHandle(long sessionId)
    {
        lock (_playbackLock)
        {
            // 如果是不同的会话，需要重新创建
            if (_playbackHandleInitialized && _playbackHandle.IsValid && _playbackSessionId == sessionId)
            {
                return _playbackHandle;
            }

            // 释放旧的handle
            DisposePlaybackHandleUnsafe();

            try
            {
                _playbackHandle = AudioPlayback.CreateStream(volume: 1f);
                _playbackHandleInitialized = _playbackHandle.IsValid;
                _playbackSessionId = sessionId;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LogPrefix}Failed to create playback stream: {ex}");
                _playbackHandle = default;
                _playbackHandleInitialized = false;
            }

            return _playbackHandle;
        }
    }

    private PlaybackHandle GetCurrentPlaybackHandle()
    {
        lock (_playbackLock)
        {
            return _playbackHandle;
        }
    }

    private void StopAndDisposeCurrentPlayback(long sessionId)
    {
        lock (_playbackLock)
        {
            if (!_playbackHandleInitialized || _playbackSessionId != sessionId)
            {
                return;
            }


            DisposePlaybackHandleUnsafe();
        }
    }

    private void CompleteCurrentPlaybackStream()
    {
        lock (_playbackLock)
        {
            if (!_playbackHandleInitialized || !_playbackHandle.IsValid)
            {
                return;
            }


            try
            {
                _playbackHandle.CompleteStream();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogPrefix}Failed to complete playback stream: {ex.Message}");
            }

            // CompleteStream后handle不能再用于新数据，标记为需要重新创建
            DisposePlaybackHandleUnsafe();
        }
    }

    private void DisposePlaybackHandleUnsafe()
    {
        if (!_playbackHandleInitialized)
        {
            return;
        }


        try
        {
            if (_playbackHandle.IsValid)
            {
                _playbackHandle.Stop();
                _playbackHandle.CompleteStream();
                _playbackHandle.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{LogPrefix}Error disposing playback handle: {ex.Message}");
        }

        _playbackHandle = default;
        _playbackHandleInitialized = false;
        _playbackSessionId = -1;
    }

    private void DisposePlaybackHandle()
    {
        lock (_playbackLock)
        {
            DisposePlaybackHandleUnsafe();
        }
    }
    #endregion

    #region TTS Processing
    private System.Collections.IEnumerator WaitForModelInitialization()
    {
        while (_speechSynthesis != null && !_speechSynthesis.Initialized)
        {
            yield return null;
        }

        if (_speechSynthesis != null && _speechSynthesis.Initialized)
        {
            Initialized = true;
            OnSynthesizerInitialized?.Invoke(true);
        }
        else
        {
            Debug.LogError($"{LogPrefix}Model initialization failed.");
            OnSynthesizerInitialized?.Invoke(false);
        }
        _initializing = false;
    }

    private System.Collections.IEnumerator TTSPumpLoop()
    {
        while (true)
        {
            // 等待：没有活动任务 + 队列有数据 + 已初始化
            yield return new WaitUntil(() =>
                !IsSessionRunning() &&
                !_sentenceQueue.IsEmpty &&
                Initialized);

            // 开始新的处理会话
            StartNewProcessingSession();
        }
    }

    private bool IsSessionRunning()
    {
        lock (_sessionLock)
        {
            return _currentSessionTask != null && !_currentSessionTask.IsCompleted;
        }
    }

    private void StartNewProcessingSession()
    {
        lock (_sessionLock)
        {
            // 创建新会话
            _sessionId++;
            var currentSessionId = _sessionId;

            // 清理旧的CTS
            if (_sessionCts != null)
            {
                try { _sessionCts.Dispose(); } catch { }
            }
            _sessionCts = new CancellationTokenSource();
            var token = _sessionCts.Token;

            // 启动处理任务
            _currentSessionTask = ProcessSentenceQueueAsync(currentSessionId, token);
        }
    }

    private async Task ProcessSentenceQueueAsync(long sessionId, CancellationToken cancellationToken)
    {
        UpdateTtsState(true);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _sentenceQueue.TryDequeue(out string sentence))
            {
                if (string.IsNullOrWhiteSpace(sentence))
                {
                    continue;
                }

                // 确保有playback handle

                var playbackHandle = EnsurePlaybackHandle(sessionId);
                if (!playbackHandle.IsValid)
                {
                    Debug.LogError($"{LogPrefix}Failed to obtain playback stream.");
                    continue;
                }

                // 创建并运行合成任务
                var job = new Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.SynthesisJob(
                    sentence,
                    _speechSynthesis,
                    _ttsConfig,
                    s => SafeInvokeOnMainThread(() => OnSentenceStarted?.Invoke(s)),
                    s => SafeInvokeOnMainThread(() => OnSentenceFinished?.Invoke(s)),
                    () => GetPlaybackHandleForSession(sessionId)
                );

                try
                {
                    await job.RunAsync(cancellationToken).ConfigureAwait(false);

                    // 等待音频播放完成
                    await WaitForPlaybackDrainAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LogPrefix}Synthesis job failed: {ex}");
                }
            }
        }
        finally
        {
            // 会话结束，完成playback stream
            CompletePlaybackStreamForSession(sessionId);
            UpdateTtsState(false);
        }
    }

    private PlaybackHandle GetPlaybackHandleForSession(long sessionId)
    {
        lock (_playbackLock)
        {
            if (_playbackSessionId == sessionId && _playbackHandleInitialized && _playbackHandle.IsValid)
            {

                return _playbackHandle;
            }


            return default;
        }
    }

    private void CompletePlaybackStreamForSession(long sessionId)
    {
        lock (_playbackLock)
        {
            if (_playbackSessionId != sessionId)
            {
                return;
            }


            if (_playbackHandleInitialized && _playbackHandle.IsValid)
            {
                try
                {
                    _playbackHandle.CompleteStream();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix}Failed to complete stream: {ex.Message}");
                }
            }

            // 标记需要重新创建
            _playbackHandleInitialized = false;
        }
    }

    private async Task WaitForPlaybackDrainAsync(long sessionId, CancellationToken cancellationToken)
    {
        const double epsilon = 0.05;
        const int pollDelayMs = 30;
        int stagnantCount = 0;
        double lastBuffered = double.MaxValue;

        while (!cancellationToken.IsCancellationRequested)
        {
            PlaybackHandle handle;
            lock (_playbackLock)
            {
                if (_playbackSessionId != sessionId || !_playbackHandleInitialized)
                {
                    break;
                }


                handle = _playbackHandle;
            }

            if (!handle.IsValid)
            {
                break;
            }


            double buffered = handle.BufferedSeconds;
            if (buffered <= epsilon)
            {
                break;
            }


            if (Math.Abs(buffered - lastBuffered) < 0.001)
            {
                stagnantCount++;
                if (stagnantCount > 100) // 约3秒无变化
                {
                    break;
                }

            }
            else
            {
                stagnantCount = 0;
                lastBuffered = buffered;
            }

            try
            {
                await Task.Delay(pollDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateTtsState(bool isSpeaking)
    {
        int oldValue = isSpeaking
            ? Interlocked.Exchange(ref _ttsInProgress, 1)
            : Interlocked.Exchange(ref _ttsInProgress, 0);

        bool changed = (oldValue != 0) != isSpeaking;
        if (changed)
        {
            SafeInvokeOnMainThread(() => OnTTSStateChanged?.Invoke(isSpeaking));
        }
    }

    private void SafeInvokeOnMainThread(Action action)
    {
        if (action == null)
        {
            return;
        }


        try
        {
            // 如果需要确保在主线程执行，可以使用UnityMainThreadDispatcher
            // 这里简化处理，直接调用
            action();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"{LogPrefix}Callback error: {ex.Message}");
        }
    }
    #endregion
}
