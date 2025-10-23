using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Eitan.EasyMic.Runtime;
using Eitan.SherpaOnnxUnity.Runtime;
using UnityEngine;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

#region Helper Modules
namespace Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal
{
    /// <summary>
    /// Handles all text processing tasks like cleaning, splitting, and corrections.
    /// </summary>
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

        public static string[] SplitSentences(string text)
        {
            return SentenceSplitRegex.Split(text);
        }

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
        public static string ApplyPronunciationCorrection(string sentence)
        {
            return sentence.Replace("海晟", "海胜"); // 纠错发音
        }
    }

    internal interface IPlaybackProvider
    {
        PlaybackHandle Acquire(int channels, int sampleRate);
    }

    internal class SynthesisJob
    {
        public bool IsDone { get; private set; }
        private int _finalized; // 0 = no, 1 = yes

        private readonly string _originalSentence;
        private readonly SpeechSynthesis _speechSynthesis;
        private readonly SpeechSynthesizerConfiguration _config;
        private readonly Action<string> _onStart;
        private readonly Action<string> _onFinish;
        private readonly IPlaybackProvider _playbackProvider;

        public SynthesisJob(
            string sentence,
            SpeechSynthesis speechSynthesis,
            SpeechSynthesizerConfiguration config,
            Action<string> onStart,
            Action<string> onFinish,
            IPlaybackProvider playbackProvider)
        {
            _originalSentence = sentence;
            _speechSynthesis = speechSynthesis;
            _config = config;
            _onStart = onStart;
            _onFinish = onFinish;
            _playbackProvider = playbackProvider;
        }

        public async Task RunAsync(Func<bool> isCancelled)
        {
            string ttsText = TextProcessor.CleanTextForTts(_originalSentence);
            _onStart?.Invoke(ttsText);

            if (string.IsNullOrEmpty(ttsText))
            {
                FinalizeJob(ttsText, false);
                return;
            }

            int sampleRate = Math.Max(8000, _config.SampleRates);
            int channels = 1;

            var playback = _playbackProvider != null ? _playbackProvider.Acquire(channels, sampleRate) : default;
            if (!playback.IsValid)
            {
                Debug.LogError("[SpeechSynthesizer] Failed to obtain playback handle for TTS streaming.");
                FinalizeJob(ttsText, true);
                return;
            }

            try
            {
                var ttsRequest = TextProcessor.ApplyPronunciationCorrection(ttsText);

                // Generate with progress; copy directly from ptr -> pooled float[] -> Enqueue
                await _speechSynthesis.GenerateWithProgressCallbackAsync(ttsRequest, _config.VoiceId, _config.Speed, (samplesPtr, count, progress) =>
                {
                    if (isCancelled())
                    {
                        // Stop further generation
                        return 0;
                    }

                    if (samplesPtr == IntPtr.Zero || count <= 0)
                    {
                        return 1;
                    }

                    // Rent, copy, enqueue, return
                    var buf = ArrayPool<float>.Shared.Rent(count);
                    try
                    {
                        Marshal.Copy(samplesPtr, buf, 0, count);
                        if (playback.IsValid)
                        {
                            playback.Enqueue(buf, count, channels, sampleRate, false);
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(buf);
                    }

                    return 1; // continue
                });

                FinalizeJob(ttsText, false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SpeechSynthesizer] Generation exception: {ex}");
                FinalizeJob(ttsText, true);
            }
        }

        private void FinalizeJob(string sentence, bool errored)
        {
            if (Interlocked.Exchange(ref _finalized, 1) == 0)
            {
                _onFinish?.Invoke(sentence);
                IsDone = true;
            }
        }
    }

    /// <summary>
    /// Handles model loading feedback and progress reporting.
    /// </summary>
    internal class ModelInitializationHandler : ISherpaFeedbackHandler
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

            float stageFraction = -1f; // ExtractStageFractionFromFeedback(feedback); // Assuming this helper exists
            if (stageFraction < 0f)
            {
                stageFraction = stage switch
                {
                    LoadStage.Download => 0.25f,
                    LoadStage.Uncompress or LoadStage.Verify or LoadStage.Clean => 0.5f,
                    LoadStage.Load => 0.9f,
                    _ => 0f
                };
            }

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
        public void OnFeedback(UncompressFeedback feedback) => PublishProgress(feedback);
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

        public static TTSPreset Create(string modelId, int voiceId, float speed, int sampleRate, string id = DefaultPresetId, string displayName = "Default")
        {
            return new TTSPreset { Id = id, DisplayName = displayName, modelId = modelId, voiceId = voiceId, speed = speed, sampleRates = sampleRate };
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
            bool isDefault = string.IsNullOrWhiteSpace(presetId) || string.Equals(presetId, TTSPreset.DefaultPresetId, StringComparison.OrdinalIgnoreCase);
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


public class SpeechSynthesizer : MonoBehaviour, Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.IPlaybackProvider
{
    #region Public Properties & Events
    [Header("Initialization")]
    [SerializeField] private bool _initOnAwake = true;
    public bool InitOnAwake => _initOnAwake;
    public bool Initialized { get; private set; }
    public bool IsProcessingTTS => _ttsInProgress;

    [Header("Configuration")]
    [SerializeField] private SpeechSynthesizerConfiguration _ttsConfig;

    // Model loading feedback events
    public event Action<string, float> OnLoadingProgressFeedback;
    public event Action<FailedFeedback> OnLoadingFailedFeedback;
    public event Action<SuccessFeedback> OnLoadingSuccessedFeedback;

    // Synthesizer lifecycle / TTS events
    public event Action<bool> OnSynthesizerInitialized;
    public event Action<bool> OnTTSStateChanged;
    public event Action<string> OnSentenceStarted;
    public event Action<string> OnSentenceFinished;
    #endregion

    #region Private Fields
    private SpeechSynthesis _speechSynthesis;
    private volatile bool _initializing;
    private volatile bool _ttsInProgress;
    private volatile bool _ttsCancelRequested;

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _sentenceQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
    private Coroutine _ttsPumpCoroutine;

    // Helper Modules
    private Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.ModelInitializationHandler _progressHandler;
    private CancellationTokenSource _ttsCts;
    private SharedPlaybackManager _playbackManager;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        _ttsConfig ??= SpeechSynthesizerConfiguration.CreateDefault();
        _progressHandler = new Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.ModelInitializationHandler();
        _playbackManager = new SharedPlaybackManager(this);
        _progressHandler.OnProgress += (msg, progress) => OnLoadingProgressFeedback?.Invoke(msg, progress);
        _progressHandler.OnFailed += feedback => OnLoadingFailedFeedback?.Invoke(feedback);
        _progressHandler.OnSuccess += feedback => OnLoadingSuccessedFeedback?.Invoke(feedback);
    }

    private System.Collections.IEnumerator Start()
    {
        if (_initOnAwake)
        {
            Init();
            yield return new WaitUntil(() => Initialized);
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            this.EnqueueSentence("测试一下语音识别");
        }
    }

    private void OnDestroy()
    {
        Stop();
        if (_ttsPumpCoroutine != null)
        {
            StopCoroutine(_ttsPumpCoroutine);
            _ttsPumpCoroutine = null;
        }
        _playbackManager?.Shutdown(true);
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
            var reporter = new SherpaOnnxFeedbackReporter(null, _progressHandler);
            _speechSynthesis = new SpeechSynthesis(_ttsConfig.ModelId, _ttsConfig.SampleRates, reporter);

            if (_ttsPumpCoroutine == null)
            {
                _ttsPumpCoroutine = StartCoroutine(TTSPumpLoop());
            }
            StartCoroutine(WaitForModelInitialization());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SpeechSynthesizer] Init failed: {ex}");
            _initializing = false;
        }
    }

    public void Stop()
    {
        _ttsCancelRequested = true;
        _ttsCts?.Cancel();
        _ttsCts?.Dispose();
        _ttsCts = null;

        _sentenceQueue.Clear();
        _playbackManager?.StopActive();
        _ttsInProgress = false;
        OnTTSStateChanged?.Invoke(false);
    }

    public void EnqueueSentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }


        string[] sentences = Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.TextProcessor.SplitSentences(text);
        foreach (string sentence in sentences)
        {
            string trimmed = sentence.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                _sentenceQueue.Enqueue(trimmed);
            }
        }
    }
    #endregion

#region Playback Management
    PlaybackHandle Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.IPlaybackProvider.Acquire(int channels, int sampleRate)
    {
        if (_playbackManager == null)
        {
            _playbackManager = new SharedPlaybackManager(this);
        }
        return _playbackManager.Acquire(channels, sampleRate);
    }

    private sealed class SharedPlaybackManager
    {
        private readonly string _logPrefix;
        private readonly object _lock = new object();

        private PlaybackHandle _handle;
        private bool _initialized;
        private int _channels = -1;
        private int _sampleRate = -1;

        public SharedPlaybackManager(SpeechSynthesizer owner)
        {
            _logPrefix = owner != null ? $"[SpeechSynthesizer:{owner.name}] " : "[SpeechSynthesizer] ";
        }

        public PlaybackHandle Acquire(int channels, int sampleRate)
        {
            if (channels <= 0 || sampleRate <= 0)
            {
                Debug.LogWarning($"{_logPrefix}Invalid playback format requested.");
                return default;
            }

            PlaybackHandle oldHandle = default;
            bool disposeOld = false;
            bool needsCreation = false;

            lock (_lock)
            {
                bool hasValid = _initialized && _handle.IsValid;
                bool matchesFormat = hasValid && _channels == channels && _sampleRate == sampleRate;
                if (matchesFormat)
                {
                    return _handle;
                }

                if (hasValid)
                {
                    oldHandle = _handle;
                    disposeOld = true;
                }

                _handle = default;
                _initialized = false;
                _channels = -1;
                _sampleRate = -1;
                needsCreation = true;
            }

            if (disposeOld && oldHandle.IsValid)
            {
                try
                {
                    oldHandle.CompleteStream();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{_logPrefix}Failed to finalize previous playback stream: {ex.Message}");
                }

                try
                {
                    oldHandle.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{_logPrefix}Failed to dispose previous playback stream: {ex.Message}");
                }
            }

            PlaybackHandle newHandle = default;
            if (needsCreation)
            {
                try
                {
                    newHandle = AudioPlayback.CreateStream(
                        preferredChannels: channels,
                        preferredSampleRate: sampleRate,
                        volume: 1f,
                        autoDisposeOnComplete: false);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{_logPrefix}Failed to create playback stream: {ex}");
                }
            }

            lock (_lock)
            {
                if (_initialized && _handle.IsValid)
                {
                    if (newHandle.IsValid)
                    {
                        try
                        {
                            newHandle.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"{_logPrefix}Failed to dispose redundant playback stream: {ex.Message}");
                        }
                    }

                    return _handle;
                }

                if (newHandle.IsValid)
                {
                    _handle = newHandle;
                    _initialized = true;
                    _channels = channels;
                    _sampleRate = sampleRate;
                    return _handle;
                }

                _handle = default;
                _initialized = false;
                _channels = -1;
                _sampleRate = -1;
                return default;
            }
        }

        public void StopActive()
        {
            PlaybackHandle handleSnapshot = default;
            lock (_lock)
            {
                if (!_initialized || !_handle.IsValid)
                {
                    return;
                }

                handleSnapshot = _handle;
            }

            try
            {
                handleSnapshot.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{_logPrefix}Failed to stop playback: {ex.Message}");
            }
        }

        public void Shutdown(bool completeStream)
        {
            PlaybackHandle handleSnapshot = default;
            lock (_lock)
            {
                if (_initialized && _handle.IsValid)
                {
                    handleSnapshot = _handle;
                }

                _handle = default;
                _initialized = false;
                _channels = -1;
                _sampleRate = -1;
            }

            if (!handleSnapshot.IsValid)
            {
                return;
            }

            if (completeStream)
            {
                try
                {
                    handleSnapshot.CompleteStream();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{_logPrefix}Failed to finalize playback during shutdown: {ex.Message}");
                }
            }

            try
            {
                handleSnapshot.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{_logPrefix}Failed to dispose playback during shutdown: {ex.Message}");
            }
        }
    }
#endregion

    #region Coroutines & Internal Logic
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
            Debug.LogError("[SpeechSynthesizer] Model initialization failed.");
            OnSynthesizerInitialized?.Invoke(false);
        }
        _initializing = false;
    }

    private System.Collections.IEnumerator TTSPumpLoop()
    {
        while (true)
        {
            yield return new WaitUntil(() => !_ttsInProgress && _sentenceQueue.Count > 0 && Initialized);

            if (_sentenceQueue.TryDequeue(out string sentence))
            {
                _ttsInProgress = true;
                _ttsCancelRequested = false;
                OnTTSStateChanged?.Invoke(true);

                _ttsCts?.Dispose();
                _ttsCts = new CancellationTokenSource();
                var localCts = _ttsCts;

                var job = new Eitan.EasyMic.Runtime.Components.SpeechSynthesizerInternal.SynthesisJob(
                    sentence, _speechSynthesis, _ttsConfig,
                    OnSentenceStarted,
                    (finishedSentence) =>
                    {
                        OnSentenceFinished?.Invoke(finishedSentence);
                    },
                    this
                );

                // Kick off async job and wait until completion inside the coroutine loop
                var task = job.RunAsync(() => _ttsCancelRequested || localCts.IsCancellationRequested);
                while (!task.IsCompleted)
                {
                    yield return null;
                }

                _ttsInProgress = false;
                OnTTSStateChanged?.Invoke(false);
            }
        }
    }
    #endregion
}
