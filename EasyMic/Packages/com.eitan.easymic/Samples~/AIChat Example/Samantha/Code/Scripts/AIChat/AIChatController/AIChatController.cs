#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Main controller for AI chat functionality with speech-to-text and text-to-speech support.
    /// Optimized for low latency with adaptive network handling and parallel TTS processing.
    /// </summary>
    [AddComponentMenu("Examples/EasyMic/AI Chat/Controller")]
    public partial class AIChatController : MonoBehaviour
    {
        #region Serialized Fields
        [Header("Configuration")]
        [SerializeField]
        private AIChatControllerConfig _config = new AIChatControllerConfig();

        [Header("Plugins")]
        [SerializeField]
        private List<MonoBehaviour> _pluginBehaviours = new List<MonoBehaviour>();
        #endregion

        #region Enums and Events
        public enum ChatState
        {
            Idle,
            UserInput,
            AssistantResponseStreaming,
            AssistantResponseFinish,
            Failed
        }

        public event Action<ChatState, string> OnChatStateChanged;
        public event Action<string[]> OnWebLinksExtracted;
        public event Action<float> OnLoadingCallback;
        public event Action<bool> OnIdleStateChanged;
        public event Action<bool> OnUserSpeakingStateChanged;
        public event Action<NetworkQualityInfo> OnNetworkQualityChanged;
        #endregion

        #region Public Properties
        public bool IsIdle
        {
            get
            {
                // Use live state (!_llmInFlight && !_isAssistantSpeaking) instead of
                // the cached _lastIdleState to avoid a race condition during barge-in:
                //
                // After SignalCancelActiveResponse sets _llmInFlight=false and
                // _isAssistantSpeaking=false on the VAD worker thread, the cached
                // _lastIdleState is NOT updated until UpdateIdleState runs on the
                // Unity thread. If OnAsrSubmitHandler fires on the ASR thread before
                // the Unity thread processes the posted delegate, TryDispatchBufferedInput
                // sees a stale _lastIdleState=false and refuses to dispatch — the transcript
                // is "lost" until the next Update() call at best, and under edge conditions
                // (e.g. nested barge-ins) it can be permanently dropped.
                if (_initializationFailed) return false;
                return !_llmInFlight && !_isAssistantSpeaking;
            }
        }
        public bool IsChatActive => _isChatActive;
        public bool IsUserSpeaking => Microphone?.IsSpeaking ?? false;
        public bool IsAssistantSpeaking => _isAssistantSpeaking;
        public bool IsInitialized => _initialized;
        public float MicStartupDelaySeconds => Config.MicStartupDelay;
        public NetworkQualityInfo CurrentNetworkQuality => _networkHandler?.GetCurrentInfo() ?? NetworkQualityInfo.Default;
        public float TimeSinceLastUserActivity => Mathf.Max(0f, Time.realtimeSinceStartup - _lastUserActivityTime);
        public float TimeSinceLastAssistantResponse => Mathf.Max(0f, Time.realtimeSinceStartup - _lastAssistantResponseTime);
        public float LastLoadingProgress => _lastLoadingProgress;
        public bool HasConversationHistory => _requestOrchestrator?.HasConversationHistory ?? false;
        public string RuntimeConfigPath
            => Path.Combine(Application.persistentDataPath,
                string.IsNullOrWhiteSpace(Config.RuntimeConfigFileName) ? "ai_chat_config.json" : Config.RuntimeConfigFileName);
        public string LastErrorMessage => _lastErrorMessage;
        public AIChatControllerConfig CurrentConfig => Config;
        public bool HasConfigurationPolicy => _fixedSettingsOverride != null && _fixedSettingsOverride.EnabledOverride;
        public AIChatConfigurationPolicy.PolicyPreset ConfigurationPolicyPreset =>
            _fixedSettingsOverride != null ? _fixedSettingsOverride.Preset : AIChatConfigurationPolicy.PolicyPreset.Custom;
        public PipelineDebugTracker LatencyTracker => _latencyTracker;

        public void SetApiKey(string apiKey)
        {
            Config.SetApiKeyOverride(apiKey);
            InitializeOpenAiClient();
            EnsureTtsPipelineConfigured();
        }
        #endregion

        #region Constants
        private static readonly char[] SentenceEndMarkers = { '.', '!', '?', '。', '！', '？' };

        private static readonly Regex WebLinkRegex = new Regex(
            @"https?://[^\s<>\)\]\}，。！？；：】》）]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private const string SERVICE_ASR_INIT_KEY = "SERVICE_ASR";
        private const string SERVICE_TTS_INIT_KEY = "SERVICE_TTS";
        private const int MaxResponseBufferSize = 50000;
        private const int MaxUserInputBufferSize = 5000;
        #endregion

        #region Private Fields
        private AIChatControllerConfig Config => _config;

        private VoiceMicrophone Microphone => Config.Microphone;
        private SpeechSynthesizer SpeechSynthesizer => Config.SpeechSynthesizer;

        private readonly AIChatControllerState _controllerState = new AIChatControllerState();
        private readonly object _stateLock = new object();
        private readonly StringBuilder _userInputBuffer = new StringBuilder(256);
        private string _lastErrorMessage = string.Empty;

        private AIChatRequestOrchestrator _requestOrchestrator;
        private NetworkAdaptiveHandler _networkHandler;
        private IAIChatRuntimeConfigStore _runtimeConfigStore;
        private Dictionary<string, float> _serviceLoadingRecord;
        private OpenAICompatibleClient _openAiClient;
        private CancellationTokenSource _responseCts;
        private ChatTtsPipeline _ttsPipeline;
        private Coroutine _pendingMicStartupCoroutine;
        private AIChatConfigurationPolicy _fixedSettingsOverride;

        private bool _localTtsCallbacksRegistered;
        private bool _conversationStarted;
        private float _lastMainThreadTime;
        private int _unityThreadId;
        private SynchronizationContext _unityContext;
        private string _systemPromptCache = string.Empty;
        private PromptProfile _cachedSystemPromptProfile;

        private int _totalRequestCount;
        private int _failedRequestCount;
        private float _averageResponseLatencyMs;
        private float _lastUserActivityTime;
        private float _lastAssistantResponseTime;
        private AIChatPluginHost _pluginHost;
        private AIChatPluginContext _pluginContext;
        private SiliconFlowExpressiveTtsInputPlugin _activeSiliconFlowTtsInputPlugin;
        private SiliconFlowExpressiveTtsInputPlugin.RuntimeBinding _activeSiliconFlowTtsInputBinding;
        private long _responseGeneration;
        private Stopwatch _activeResponseStopwatch;
        private float _lastFirstTokenLatencyMs;
        private float _lastFirstSentenceLatencyMs;
        private float _lastFirstAudioLatencyMs;
        private float _lastPlaybackBufferedSeconds;
        private int _interruptionCount;
        private float _lastResponseStartRealtime;
        private readonly System.Threading.ManualResetEventSlim _drainCompleteGate = new System.Threading.ManualResetEventSlim(true);
        private PipelineDebugTracker _latencyTracker;

        private bool _llmInFlight
        {
            get => _controllerState.LlmInFlight;
            set => _controllerState.LlmInFlight = value;
        }

        private bool _isAssistantSpeaking
        {
            get => _controllerState.IsAssistantSpeaking;
            set => _controllerState.IsAssistantSpeaking = value;
        }

        private bool _isChatActive
        {
            get => _controllerState.IsChatActive;
            set => _controllerState.IsChatActive = value;
        }

        private bool _initialized
        {
            get => _controllerState.IsInitialized;
            set => _controllerState.IsInitialized = value;
        }

        private bool _initializationFailed
        {
            get => _controllerState.InitializationFailed;
            set => _controllerState.InitializationFailed = value;
        }

        private bool _lastIdleState
        {
            get => _controllerState.IsIdle;
            set => _controllerState.IsIdle = value;
        }

        private float _lastLoadingProgress
        {
            get => _controllerState.LastLoadingProgress;
            set => _controllerState.LastLoadingProgress = value;
        }

        private bool _isShuttingDown
        {
            get => _controllerState.IsShuttingDown;
            set => _controllerState.IsShuttingDown = value;
        }
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            CaptureUnityThreadContext();
            InitializeComponents();
            _latencyTracker = new PipelineDebugTracker();
            // Auto-connect PipelineDebugPanel if present in scene
            var panel = FindObjectOfType<PipelineDebugPanel>();
            if (panel != null)
                panel.AssignTracker(_latencyTracker);
            if (_initializationFailed)
            {
                return;
            }
            LoadRuntimeConfigIfNeeded();
            if (_initializationFailed)
            {
                return;
            }
            ApplyFixedSettingsOverrideIfPresent();
            InitializeOpenAiClient();
            if (_initializationFailed)
            {
                return;
            }
            InitializeMicrophone();
            if (_initializationFailed)
            {
                return;
            }
            InitializeSpeechSynthesizer();
            if (_initializationFailed)
            {
                return;
            }
            EnsureTtsPipelineConfigured();
            if (_initializationFailed)
            {
                return;
            }
            _lastMainThreadTime = Time.realtimeSinceStartup;
            _lastUserActivityTime = _lastMainThreadTime;
            _lastAssistantResponseTime = _lastMainThreadTime;
            RefreshSystemPromptCache();
            _pluginContext = new AIChatPluginContext(this);
            _pluginHost = new AIChatPluginHost(_pluginContext, ResolvePluginBehaviours());
            InitializeCursorAutoHideState();
            UpdateIdleState();
        }

        private void Update()
        {
            if (_initializationFailed)
            {
                ResetCursorAutoHideState();
                return;
            }

            UpdateIdleState();
            UpdateCursorAutoHideState();
            _lastMainThreadTime = Time.realtimeSinceStartup;
            RefreshSystemPromptCache();
            _pluginHost?.Tick(Time.unscaledDeltaTime);

            if (_networkHandler != null && Time.frameCount % 60 == 0)
            {
                var quality = _networkHandler.GetCurrentInfo();
                OnNetworkQualityChanged?.Invoke(quality);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _config ??= new AIChatControllerConfig();
        }
#endif

        private void OnDisable()
        {
            ResetCursorAutoHideState();
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            ResetCursorAutoHideState();
            CancelActiveResponseForTeardown();
            _pluginHost?.Shutdown();
            TeardownMicrophone();
            TeardownSpeechSynthesizer();
            TeardownTtsPipeline();

            _openAiClient?.Dispose();
            _openAiClient = null;
            _drainCompleteGate?.Dispose();
        }
        #endregion

        #region Nested Types
        public struct ChatMetrics
        {
            public int TotalRequests;
            public int FailedRequests;
            public float AverageResponseLatencyMs;
            public float LastFirstTokenLatencyMs;
            public float LastFirstSentenceLatencyMs;
            public float LastFirstAudioLatencyMs;
            public float LastPlaybackBufferedSeconds;
            public int InterruptionCount;
            public NetworkQualityInfo NetworkQuality;
        }
        #endregion

        #region Plugins
        private List<MonoBehaviour> ResolvePluginBehaviours()
        {
            if (_pluginBehaviours != null && _pluginBehaviours.Count > 0)
            {
                return _pluginBehaviours;
            }

            var discovered = new List<MonoBehaviour>();
            GetComponents(discovered);

            for (int i = discovered.Count - 1; i >= 0; i--)
            {
                if (!(discovered[i] is IAIChatPlugin))
                {
                    discovered.RemoveAt(i);
                }
            }

            return discovered;
        }

        private void CaptureUnityThreadContext()
        {
            if (_unityThreadId != 0)
            {
                return;
            }

            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            _unityContext = SynchronizationContext.Current;
        }

        private bool IsOnUnityThread =>
            _unityThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _unityThreadId;

        private void PostToUnityThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            var context = _unityContext;
            if (context == null)
            {
                if (IsOnUnityThread)
                {
                    TryInvokeUnityAction(action);
                }

                return;
            }

            if (IsOnUnityThread)
            {
                TryInvokeUnityAction(action);
                return;
            }

            context.Post(_ =>
            {
                TryInvokeUnityAction(action);
            }, null);
        }

        private CancellationTokenSource TakeResponseCancellationTokenSource()
        {
            lock (_stateLock)
            {
                CancellationTokenSource current = _responseCts;
                _responseCts = null;
                return current;
            }
        }

        private void ReplaceResponseCancellationTokenSource(CancellationTokenSource nextCts)
        {
            CancellationTokenSource previous;
            lock (_stateLock)
            {
                previous = _responseCts;
                _responseCts = nextCts;
            }

            CancelAndDisposeCts(previous);
        }

        private static void CancelAndDisposeCts(CancellationTokenSource cts)
        {
            if (cts == null)
            {
                return;
            }

            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private void CancelActiveResponseForTeardown()
        {
            CancelAndDisposeCts(TakeResponseCancellationTokenSource());
            _requestOrchestrator?.ResetCurrentResponse();

            _llmInFlight = false;
            _isAssistantSpeaking = false;
        }

        private void SafeFireAndForget(Func<Task> asyncFunc, string context)
        {
            Task.Run(async () =>
            {
                try
                {
                    await asyncFunc().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError($"[AIChat] Unhandled exception in fire-and-forget ({context}): {ex}");
                }
            });
        }

        private void SafeFireAndForget(Task task, string context)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    UnityEngine.Debug.LogError($"[AIChat] Unhandled exception in fire-and-forget ({context}): {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private bool IsUnityObjectOperational()
        {
            if (_isShuttingDown || this == null)
            {
                return false;
            }

            try
            {
                return gameObject != null;
            }
            catch (MissingReferenceException)
            {
                return false;
            }
        }

        private void TryInvokeUnityAction(Action action)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (MissingReferenceException)
            {
            }
        }
        #endregion
    }
}

#else
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [DisallowMultipleComponent]
    public partial class AIChatController : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private AIChatControllerConfig _config = new AIChatControllerConfig();

        [Header("Plugins")]
        [SerializeField] private List<MonoBehaviour> _pluginBehaviours = new List<MonoBehaviour>();

        public enum ChatState
        {
            Idle,
            UserInput,
            AssistantResponseStreaming,
            AssistantResponseFinish,
            Failed
        }

        public event Action<ChatState, string> OnChatStateChanged;
        public event Action<string[]> OnWebLinksExtracted;
        public event Action<float> OnLoadingCallback;
        public event Action<bool> OnIdleStateChanged;
        public event Action<bool> OnUserSpeakingStateChanged;
        public event Action<NetworkQualityInfo> OnNetworkQualityChanged;

        public bool IsIdle => true;
        public bool IsChatActive => false;
        public bool IsUserSpeaking => false;
        public bool IsAssistantSpeaking => false;
        public bool IsInitialized => false;
        public float MicStartupDelaySeconds => CurrentConfig.MicStartupDelay;
        public NetworkQualityInfo CurrentNetworkQuality => NetworkQualityInfo.Default;
        public float TimeSinceLastUserActivity => 0f;
        public float TimeSinceLastAssistantResponse => 0f;
        public float LastLoadingProgress => 0f;
        public bool HasConversationHistory => false;
        public string RuntimeConfigPath =>
            Path.Combine(
                Application.persistentDataPath,
                string.IsNullOrWhiteSpace(CurrentConfig.RuntimeConfigFileName) ? "ai_chat_config.json" : CurrentConfig.RuntimeConfigFileName);
        public string LastErrorMessage => GetMissingDependencyMessage();
        public AIChatControllerConfig CurrentConfig => _config ??= new AIChatControllerConfig();
        public bool HasConfigurationPolicy => TryGetComponent(out AIChatConfigurationPolicy policy) && policy.EnabledOverride;
        public AIChatConfigurationPolicy.PolicyPreset ConfigurationPolicyPreset =>
            TryGetComponent(out AIChatConfigurationPolicy policy) ? policy.Preset : AIChatConfigurationPolicy.PolicyPreset.Custom;

        private void Start()
        {
            ReportMissingDependency();
        }

        public void SetApiKey(string apiKey)
        {
            CurrentConfig.SetApiKeyOverride(apiKey);
        }

        private void ReportMissingDependency()
        {
            string message = GetMissingDependencyMessage();
            UnityEngine.Debug.LogWarning($"[AIChat] {message}", this);
            OnLoadingCallback?.Invoke(0f);
            OnIdleStateChanged?.Invoke(true);
            OnNetworkQualityChanged?.Invoke(NetworkQualityInfo.Default);
            OnChatStateChanged?.Invoke(ChatState.Failed, message);
        }

        private static string GetMissingDependencyMessage()
        {
            return "AI Chat sample is in compatibility mode because com.eitan.sherpa-onnx-unity is not installed. " +
                   "Scene references are preserved, but ASR and local TTS features are unavailable.";
        }
    }
}

#endif
