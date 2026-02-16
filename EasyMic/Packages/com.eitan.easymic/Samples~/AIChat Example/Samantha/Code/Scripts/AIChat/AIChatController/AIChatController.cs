#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Eitan.EasyMic.Runtime.Mono.Components.ASR;
using Eitan.EasyMic.Runtime.Mono.Components.TTS;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Main controller for AI chat functionality with speech-to-text and text-to-speech support.
    /// Optimized for low latency with adaptive network handling and parallel TTS processing.
    /// </summary>
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
        public bool IsIdle => _lastIdleState;
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
        private AIChatControllerConfig Config => _config ??= new AIChatControllerConfig();

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
            if (_initializationFailed)
            {
                return;
            }
            LoadRuntimeConfigIfNeeded();
            if (_initializationFailed)
            {
                return;
            }
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
        }
        #endregion

        #region Nested Types
        public struct ChatMetrics
        {
            public int TotalRequests;
            public int FailedRequests;
            public float AverageResponseLatencyMs;
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

#endif
