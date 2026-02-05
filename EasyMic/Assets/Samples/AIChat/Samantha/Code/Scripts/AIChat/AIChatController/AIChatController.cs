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
        public bool HasConversationHistory
        {
            get
            {
                lock (_stateLock)
                {
                    return _conversationHistory.Count > 0;
                }
            }
        }
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

        private readonly object _stateLock = new object();
        private readonly StringBuilder _responseBuffer = new StringBuilder(1024);
        private readonly StringBuilder _userInputBuffer = new StringBuilder(256);
        private readonly List<OpenAIChatMessage> _conversationHistory = new List<OpenAIChatMessage>();
        private string _streamedResponseSnapshot = string.Empty;
        private string _lastErrorMessage = string.Empty;

        private StreamingSentenceAssembler _sentenceAssembler;
        private NetworkAdaptiveHandler _networkHandler;
        private Dictionary<string, float> _serviceLoadingRecord;
        private OpenAICompatibleClient _openAiClient;
        private CancellationTokenSource _responseCts;
        private ChatTtsPipeline _ttsPipeline;

        private volatile bool _llmInFlight;
        private volatile bool _isAssistantSpeaking;
        private volatile bool _isChatActive;
        private volatile bool _initialized;
        private volatile bool _initializationFailed;
        private volatile bool _lastIdleState;
        private volatile float _lastLoadingProgress;
        private bool _localTtsCallbacksRegistered;
        private bool _conversationStarted;
        private volatile float _lastMainThreadTime;
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
            UpdateIdleState();
        }

        private void Update()
        {
            if (_initializationFailed)
            {
                return;
            }

            UpdateIdleState();
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

        private async void OnDestroy()
        {
            await CancelActiveResponseAsync().ConfigureAwait(false);
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
                    action();
                }

                return;
            }

            if (IsOnUnityThread)
            {
                action();
                return;
            }

            context.Post(static state => ((Action)state)(), action);
        }
        #endregion
    }
}
