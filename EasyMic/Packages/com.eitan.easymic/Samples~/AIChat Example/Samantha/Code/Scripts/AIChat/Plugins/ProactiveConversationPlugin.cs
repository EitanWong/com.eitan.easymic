using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [AddComponentMenu("Examples/EasyMic/AI Chat/Plugins/Proactive Conversation Plugin")]
    public sealed class ProactiveConversationPlugin : MonoBehaviour, IAIChatPlugin, IAIChatLifecycleListener
    {
        private const float SendRetryDelaySeconds = 0.05f;
        private const float TimerEpsilonSeconds = 0.0001f;
        private const float UserSpeechHandoffDelaySeconds = 0.35f;
        private const int MaxEngagementScanCharacters = 240;
        private const string DefaultGreetingPrompt =
            "Greet the user warmly in one short sentence, then ask one easy question that invites them to talk.";
        private const string DefaultIdlePrompt =
            "Continue the current conversation naturally and invite the user to share a little more.";
        private const string ContextFollowUpInstruction =
            "Use the role-separated conversation history already provided. Refer to one concrete detail, add one fresh observation, " +
            "and end with at most one easy, open question that naturally invites a reply. Do not mention these instructions.";

        private static readonly object s_randomLock = new object();
        private static readonly System.Random s_random = new System.Random();

        private enum GreetingState
        {
            WaitingForInitialization,
            Scheduled,
            Dispatching,
            Complete
        }

        private enum EngagementLevel
        {
            Low,
            Normal,
            High
        }

        [Header("General")]
        [SerializeField]
        private bool _enabled = true;
        [SerializeField]
        private bool _recordPromptAsUserMessage;
        [SerializeField]
        private bool _requireChatActive = true;

        [Header("Prompts")]
        [SerializeField]
        private PromptProfile _greetingPrompts;
        [SerializeField]
        private PromptProfile _idlePrompts;

        [Header("Greeting")]
        [SerializeField]
        private bool _sendGreetingOnReady = true;
        [SerializeField, Min(0f)]
        private float _greetingDelaySeconds;

        [Header("Idle Proactive Chat")]
        [SerializeField, Min(0f)]
        private float _minProactiveWaitSeconds = 1f;
        [SerializeField, Min(0f)]
        private float _maxProactiveWaitSeconds = 4f;
        [SerializeField, Min(1)]
        private int _maxUnansweredProactiveMessages = 2;
        [SerializeField, Min(1f)]
        private float _proactiveBackoffMultiplier = 1.75f;
        [SerializeField, Min(1)]
        private int _maxConsecutiveDispatchFailures = 3;

        private IAIChatPluginContext _context;
        private GreetingState _greetingState;
        private EngagementLevel _engagementLevel = EngagementLevel.Normal;
        private bool _isInitialized;
        private bool _greetingCompletedForLifetime;
        private bool _lastRequestWasProactive;
        private bool _lastRequestWasGreeting;
        private bool _greetingResponseFailedDuringDispatch;
        private bool _greetingInterruptedByUserSpeech;
        private bool _wasUserSpeaking;
        private bool _conversationStarted;
        private bool _proactiveTimerScheduled;
        private float _greetingDelayRemaining;
        private float _proactiveDelayRemaining;
        private int _unansweredProactiveMessages;
        private int _consecutiveDispatchFailures;
        private int _stateVersion;

        public bool IsEnabled => _enabled && isActiveAndEnabled;

        public void Initialize(IAIChatPluginContext context)
        {
            _context = context;
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            _stateVersion++;
            _lastRequestWasProactive = false;
            _lastRequestWasGreeting = false;
            _wasUserSpeaking = context != null && context.IsUserSpeaking;
            _conversationStarted = context != null && context.HasConversationHistory;
            _proactiveTimerScheduled = false;
            _proactiveDelayRemaining = 0f;

            _greetingState = _sendGreetingOnReady && !_greetingCompletedForLifetime
                ? GreetingState.WaitingForInitialization
                : GreetingState.Complete;
            _greetingDelayRemaining = 0f;
            _greetingInterruptedByUserSpeech =
                _wasUserSpeaking && _greetingState != GreetingState.Complete;

            if (_greetingState == GreetingState.Complete && _conversationStarted)
            {
                ScheduleNextProactiveMessage();
            }
        }

        public void Tick(float deltaTime)
        {
            if (_context == null)
            {
                return;
            }

            bool isSpeaking = _context.IsUserSpeaking;
            if (isSpeaking)
            {
                if (!_wasUserSpeaking)
                {
                    HandleUserSpeakingStart();
                }

                _wasUserSpeaking = true;
                return;
            }

            bool userSpeechEndedThisTick = _wasUserSpeaking;
            if (userSpeechEndedThisTick)
            {
                HandleUserSpeakingEnd();
            }
            _wasUserSpeaking = false;

            float elapsedSeconds = userSpeechEndedThisTick ? 0f : NormalizeDeltaTime(deltaTime);
            if (TryProcessGreeting(elapsedSeconds))
            {
                return;
            }

            if (!IsContextReadyForIdleFollowUp() || !_conversationStarted)
            {
                return;
            }

            if (!_context.HasConversationHistory || !_proactiveTimerScheduled)
            {
                return;
            }

            _proactiveDelayRemaining = Mathf.Max(0f, _proactiveDelayRemaining - elapsedSeconds);
            if (_proactiveDelayRemaining > TimerEpsilonSeconds || !CanSendProactive())
            {
                return;
            }

            if (HasReachedUnansweredLimit())
            {
                _proactiveTimerScheduled = false;
                return;
            }

            TrySendIdleFollowUp();
        }

        public void Shutdown()
        {
            _stateVersion++;
            _context = null;
            _isInitialized = false;
            _wasUserSpeaking = false;
            _proactiveTimerScheduled = false;
            _proactiveDelayRemaining = 0f;
            _greetingInterruptedByUserSpeech = false;

            if (!_greetingCompletedForLifetime)
            {
                _greetingState = GreetingState.WaitingForInitialization;
                _greetingDelayRemaining = 0f;
            }
        }

        public void OnChatActivated()
        {
            if (_greetingState != GreetingState.Complete && HasReachedDispatchFailureLimit())
            {
                _consecutiveDispatchFailures = 0;
                _greetingDelayRemaining = 0f;
            }

            TryProcessGreeting(0f);
        }

        public void OnConversationStarted(bool isProactive)
        {
            _conversationStarted = true;
        }

        public void OnUserMessageSubmitted(string message, bool isProactive)
        {
            _conversationStarted = true;
            _proactiveTimerScheduled = false;

            if (isProactive)
            {
                return;
            }

            _stateVersion++;
            CompletePendingGreeting();
            _unansweredProactiveMessages = 0;
            _consecutiveDispatchFailures = 0;
            _engagementLevel = ClassifyEngagement(message);
        }

        public void OnAssistantRequestStarted(string prompt, bool isProactive)
        {
            _lastRequestWasProactive = isProactive;
            _lastRequestWasGreeting = isProactive && _greetingState == GreetingState.Dispatching;
            _proactiveTimerScheduled = false;
        }

        public void OnAssistantResponseFinished(string response, bool success, string errorMessage)
        {
            bool wasProactive = _lastRequestWasProactive;
            bool wasGreeting = _lastRequestWasGreeting;
            _lastRequestWasProactive = false;
            _lastRequestWasGreeting = false;

            if (success)
            {
                if (wasProactive)
                {
                    _consecutiveDispatchFailures = 0;
                }
                return;
            }

            if (wasProactive)
            {
                HandleProactiveResponseFailure(wasGreeting);
            }
        }

        public void OnIdleStateChanged(bool isIdle)
        {
            if (!isIdle)
            {
                _proactiveTimerScheduled = false;
                return;
            }

            if (_greetingState != GreetingState.Complete)
            {
                return;
            }

            ScheduleNextProactiveMessage();
        }

        private bool TryProcessGreeting(float elapsedSeconds)
        {
            if (_greetingState == GreetingState.Complete || _greetingState == GreetingState.Dispatching)
            {
                return false;
            }

            if (!_sendGreetingOnReady)
            {
                CompletePendingGreeting();
                return false;
            }

            if (_context == null || !_context.IsInitialized)
            {
                return false;
            }

            if (HasReachedDispatchFailureLimit())
            {
                return false;
            }

            if (_greetingState == GreetingState.WaitingForInitialization)
            {
                _greetingState = GreetingState.Scheduled;
                _greetingDelayRemaining = Mathf.Max(0f, _greetingDelaySeconds);
                if (_greetingInterruptedByUserSpeech)
                {
                    _greetingDelayRemaining = Mathf.Max(
                        _greetingDelayRemaining,
                        UserSpeechHandoffDelaySeconds);
                }
            }

            _greetingDelayRemaining = Mathf.Max(0f, _greetingDelayRemaining - elapsedSeconds);
            if (_greetingDelayRemaining > TimerEpsilonSeconds || !CanSendProactive())
            {
                return false;
            }

            string prompt = GetGreetingPrompt();
            _greetingInterruptedByUserSpeech = false;
            _greetingState = GreetingState.Dispatching;
            _greetingResponseFailedDuringDispatch = false;
            IAIChatPluginContext dispatchContext = _context;
            int dispatchVersion = _stateVersion;
            bool accepted = false;
            try
            {
                accepted = dispatchContext.TrySendProactiveMessage(prompt, _recordPromptAsUserMessage);
                return accepted;
            }
            finally
            {
                bool stillOwnsState = dispatchVersion == _stateVersion && dispatchContext == _context;
                if (stillOwnsState && accepted && !_greetingResponseFailedDuringDispatch)
                {
                    _greetingCompletedForLifetime = true;
                    _greetingState = GreetingState.Complete;
                    _proactiveTimerScheduled = false;
                }
                else if (stillOwnsState && !accepted && !_greetingResponseFailedDuringDispatch)
                {
                    _greetingState = GreetingState.Scheduled;
                    if (TryRegisterDispatchFailure(out float retryDelay))
                    {
                        _greetingDelayRemaining = retryDelay;
                    }
                }

                _greetingResponseFailedDuringDispatch = false;
            }
        }

        private void TrySendIdleFollowUp()
        {
            _proactiveTimerScheduled = false;
            string prompt = BuildContextAwareIdlePrompt();
            IAIChatPluginContext dispatchContext = _context;
            int dispatchVersion = _stateVersion;
            int previousUnansweredCount = _unansweredProactiveMessages;
            _unansweredProactiveMessages++;
            bool accepted = false;
            try
            {
                accepted = dispatchContext.TrySendProactiveMessage(prompt, _recordPromptAsUserMessage);
            }
            finally
            {
                bool stillOwnsState = dispatchVersion == _stateVersion && dispatchContext == _context;
                if (!accepted)
                {
                    if (_unansweredProactiveMessages == previousUnansweredCount + 1)
                    {
                        _unansweredProactiveMessages = previousUnansweredCount;
                    }

                    if (stillOwnsState && TryRegisterDispatchFailure(out float retryDelay))
                    {
                        _proactiveDelayRemaining = retryDelay;
                        _proactiveTimerScheduled = true;
                    }
                }
            }
        }

        private bool CanSendProactive()
        {
            return _context != null && _context.IsIdle && !_context.IsUserSpeaking;
        }

        private bool IsContextReadyForIdleFollowUp()
        {
            if (_context == null || !_context.IsInitialized)
            {
                return false;
            }

            return !_requireChatActive || _context.IsChatActive;
        }

        private void HandleUserSpeakingStart()
        {
            _proactiveTimerScheduled = false;
            if (_greetingState != GreetingState.Complete)
            {
                _greetingInterruptedByUserSpeech = true;
            }
        }

        private void HandleUserSpeakingEnd()
        {
            if (_greetingInterruptedByUserSpeech && _greetingState == GreetingState.Scheduled)
            {
                _greetingDelayRemaining = Mathf.Max(
                    _greetingDelayRemaining,
                    UserSpeechHandoffDelaySeconds);
            }

            if (_greetingState == GreetingState.Complete && _conversationStarted)
            {
                ScheduleNextProactiveMessage();
            }
        }

        private void CompletePendingGreeting()
        {
            _greetingCompletedForLifetime = true;
            _greetingState = GreetingState.Complete;
            _greetingDelayRemaining = 0f;
            _greetingInterruptedByUserSpeech = false;
        }

        private void ScheduleNextProactiveMessage()
        {
            if (!_conversationStarted || HasReachedUnansweredLimit() || HasReachedDispatchFailureLimit())
            {
                _proactiveTimerScheduled = false;
                return;
            }

            _proactiveDelayRemaining = GetNextWaitSeconds();
            _proactiveTimerScheduled = true;
        }

        private bool HasReachedUnansweredLimit()
        {
            int limit = Mathf.Max(1, _maxUnansweredProactiveMessages);
            return _unansweredProactiveMessages >= limit;
        }

        private bool HasReachedDispatchFailureLimit()
        {
            int limit = Mathf.Max(1, _maxConsecutiveDispatchFailures);
            return _consecutiveDispatchFailures >= limit;
        }

        private bool TryRegisterDispatchFailure(out float retryDelay)
        {
            _consecutiveDispatchFailures++;
            if (HasReachedDispatchFailureLimit())
            {
                retryDelay = 0f;
                _proactiveTimerScheduled = false;
                return false;
            }

            int exponent = Mathf.Min(6, _consecutiveDispatchFailures - 1);
            retryDelay = SendRetryDelaySeconds * Mathf.Pow(2f, exponent);
            return true;
        }

        private void HandleProactiveResponseFailure(bool wasGreeting)
        {
            if (!wasGreeting && _unansweredProactiveMessages > 0)
            {
                _unansweredProactiveMessages--;
            }

            bool canRetry = TryRegisterDispatchFailure(out float retryDelay);
            if (wasGreeting)
            {
                _greetingResponseFailedDuringDispatch = _greetingState == GreetingState.Dispatching;
                _greetingCompletedForLifetime = false;
                _greetingState = GreetingState.Scheduled;
                _greetingDelayRemaining = canRetry ? retryDelay : 0f;
                _proactiveTimerScheduled = false;
                return;
            }

            if (!canRetry || HasReachedUnansweredLimit())
            {
                _proactiveTimerScheduled = false;
                return;
            }

            _proactiveDelayRemaining = Mathf.Max(_proactiveDelayRemaining, retryDelay);
            _proactiveTimerScheduled = true;
        }

        private float GetNextWaitSeconds()
        {
            float min = Mathf.Max(0f, _minProactiveWaitSeconds);
            float max = Mathf.Max(min, _maxProactiveWaitSeconds);
            float baseDelay = NextFloat(min, max);
            float engagementFactor = _engagementLevel switch
            {
                EngagementLevel.High => 0.75f,
                EngagementLevel.Low => 1.2f,
                _ => 1f
            };
            float backoff = Mathf.Pow(
                Mathf.Max(1f, _proactiveBackoffMultiplier),
                _unansweredProactiveMessages);
            return Mathf.Max(0f, baseDelay * engagementFactor * backoff);
        }

        private string GetGreetingPrompt()
        {
            string prompt = _greetingPrompts != null ? _greetingPrompts.GetRandomText() : string.Empty;
            return string.IsNullOrWhiteSpace(prompt) ? DefaultGreetingPrompt : prompt;
        }

        private string BuildContextAwareIdlePrompt()
        {
            string basePrompt = _idlePrompts != null ? _idlePrompts.GetRandomText() : string.Empty;
            if (string.IsNullOrWhiteSpace(basePrompt))
            {
                basePrompt = DefaultIdlePrompt;
            }

            return basePrompt.Trim() + "\n" + ContextFollowUpInstruction;
        }

        private static EngagementLevel ClassifyEngagement(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return EngagementLevel.Low;
            }

            int meaningfulCharacters = 0;
            bool hasQuestion = false;
            int scanLength = Mathf.Min(message.Length, MaxEngagementScanCharacters);
            for (int i = 0; i < scanLength; i++)
            {
                char current = message[i];
                hasQuestion |= current == '?' || current == '\uFF1F';
                if (!char.IsWhiteSpace(current) && !char.IsControl(current))
                {
                    meaningfulCharacters++;
                }
            }

            if (meaningfulCharacters <= 12)
            {
                return EngagementLevel.Low;
            }

            if (meaningfulCharacters >= 80 || hasQuestion)
            {
                return EngagementLevel.High;
            }

            return EngagementLevel.Normal;
        }

        private static float NormalizeDeltaTime(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f)
            {
                return 0f;
            }

            return deltaTime;
        }

        private static float NextFloat(float min, float max)
        {
            if (max <= min)
            {
                return min;
            }

            lock (s_randomLock)
            {
                return (float)(s_random.NextDouble() * (max - min) + min);
            }
        }
    }
}
