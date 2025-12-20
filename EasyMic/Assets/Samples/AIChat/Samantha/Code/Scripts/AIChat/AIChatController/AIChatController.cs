using System;
using System.Collections.Generic;
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
        public NetworkQualityInfo CurrentNetworkQuality => _networkHandler?.GetCurrentInfo() ?? NetworkQualityInfo.Default;
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
        private volatile bool _lastIdleState;
        private bool _localTtsCallbacksRegistered;

        private int _totalRequestCount;
        private int _failedRequestCount;
        private float _averageResponseLatencyMs;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            InitializeComponents();
            InitializeOpenAiClient();
            InitializeMicrophone();
            InitializeSpeechSynthesizer();
            EnsureTtsPipelineConfigured();
            UpdateIdleState();
        }

        private void Update()
        {
            UpdateIdleState();

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
    }
}
