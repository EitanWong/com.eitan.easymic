using System;
using System.Diagnostics;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class ProactiveConversationPlugin : MonoBehaviour, IAIChatPlugin, IAIChatLifecycleListener
    {
        private static readonly Stopwatch s_clock = Stopwatch.StartNew();
        private static readonly object s_randomLock = new object();
        private static readonly System.Random s_random = new System.Random();

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
        [SerializeField]
        private float _greetingDelaySeconds = 0.25f;
        [Header("Idle Proactive Chat")]
        [SerializeField]
        private float _minProactiveWaitSeconds = 1f;
        [SerializeField]
        private float _maxProactiveWaitSeconds = 6f;

        private IAIChatPluginContext _context;
        private bool _greetingSent;
        private float _scheduledGreetingTime;
        private float _lastProactiveTime;
        private bool _lastRequestWasProactive;
        private float _currentWaitSeconds;
        private bool _wasUserSpeaking;
        private bool _conversationStarted;
        public bool IsEnabled => _enabled && isActiveAndEnabled;

        public void Initialize(IAIChatPluginContext context)
        {
            _context = context;
            _greetingSent = false;
            _scheduledGreetingTime = 0f;
            _lastProactiveTime = -9999f;
            if (_sendGreetingOnReady && IsContextReadyForProactive())
            {
                ScheduleGreeting(includeMicStartupDelay: false);
            }
            _lastRequestWasProactive = false;
            _currentWaitSeconds = GetNextWaitSeconds();
            _wasUserSpeaking = false;
            _conversationStarted = _context != null && _context.HasConversationHistory;
        }

        public void Tick(float deltaTime)
        {
            if (_context == null)
            {
                return;
            }

            float now = NowSeconds();

            if (!IsContextReadyForProactive())
            {
                return;
            }

            if (_sendGreetingOnReady && !_greetingSent && _scheduledGreetingTime <= 0f)
            {
                ScheduleGreeting(includeMicStartupDelay: true);
            }

            bool isSpeaking = _context.IsUserSpeaking;
            if (isSpeaking)
            {
                if (!_wasUserSpeaking)
                {
                    HandleUserSpeakingStart(now);
                }

                _wasUserSpeaking = true;
                return;
            }

            if (_wasUserSpeaking)
            {
                HandleUserSpeakingEnd(now);
            }

            _wasUserSpeaking = false;

            if (_sendGreetingOnReady && !_greetingSent && _scheduledGreetingTime > 0f)
            {
                if (now >= _scheduledGreetingTime && CanSendProactive())
                {
                    if (_context.TrySendProactiveMessage(GetGreetingPrompt(), _recordPromptAsUserMessage))
                    {
                        _greetingSent = true;
                        _lastProactiveTime = now;
                    }
                }
            }

            if (_requireChatActive && !_context.IsChatActive)
            {
                return;
            }

            if (!_context.HasConversationHistory && !_conversationStarted)
            {
                return;
            }

            if (GetTimeSinceLastActivity(now) < GetActiveWaitSeconds())
            {
                return;
            }

            if (!CanSendProactive())
            {
                return;
            }

            if (_context.TrySendProactiveMessage(GetIdlePrompt(), _recordPromptAsUserMessage))
            {
                _lastProactiveTime = now;
                _currentWaitSeconds = GetNextWaitSeconds();
            }
        }

        private bool CanSendProactive()
        {
            return _context.IsIdle && !_context.IsUserSpeaking;
        }

        public void Shutdown()
        {
            _context = null;
        }

        public void OnChatActivated()
        {
            if (_sendGreetingOnReady && !_greetingSent && IsContextReadyForProactive())
            {
                ScheduleGreeting(includeMicStartupDelay: true);
            }
        }

        public void OnConversationStarted(bool isProactive)
        {
            _conversationStarted = true;
        }

        public void OnUserMessageSubmitted(string message, bool isProactive)
        {
            if (!_greetingSent)
            {
                _greetingSent = true;
                _scheduledGreetingTime = 0f;
            }
            _conversationStarted = true;

        }

        public void OnAssistantRequestStarted(string prompt, bool isProactive)
        {
            _lastRequestWasProactive = isProactive;
        }

        public void OnAssistantResponseFinished(string response, bool success, string errorMessage)
        {
            if (!success)
            {
                return;
            }

            if (_lastRequestWasProactive)
            {
                return;
            }

            _currentWaitSeconds = GetNextWaitSeconds();
        }

        public void OnIdleStateChanged(bool isIdle)
        {
            if (isIdle)
            {
                float now = NowSeconds();
                _lastProactiveTime = now;
                _currentWaitSeconds = GetNextWaitSeconds();
            }
        }

        private static float NowSeconds()
        {
            return (float)s_clock.Elapsed.TotalSeconds;
        }

        private float GetActiveWaitSeconds()
        {
            return Clamp(_currentWaitSeconds, _minProactiveWaitSeconds, _maxProactiveWaitSeconds);
        }

        private float GetNextWaitSeconds()
        {
            float min = Mathf.Max(0f, _minProactiveWaitSeconds);
            float max = Mathf.Max(min, _maxProactiveWaitSeconds);
            float random = NextFloat(min, max);
            return Clamp(random, min, max);
        }

        private float GetTimeSinceLastActivity(float now)
        {
            float timeSinceUser = _context.TimeSinceLastUserActivity;
            float timeSinceAssistant = _context.TimeSinceLastAssistantResponse;
            float timeSinceProactive = _lastProactiveTime > 0f
                ? Mathf.Max(0f, now - _lastProactiveTime)
                : float.MaxValue;
            float mostRecent = Mathf.Min(timeSinceUser, timeSinceAssistant);
            return Mathf.Min(mostRecent, timeSinceProactive);
        }

        private string GetIdlePrompt()
        {
            return _idlePrompts != null ? _idlePrompts.GetRandomText() : string.Empty;
        }

        private string GetGreetingPrompt()
        {
            return _greetingPrompts != null ? _greetingPrompts.GetRandomText() : string.Empty;
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

        private void HandleUserSpeakingStart(float now)
        {
            CancelScheduledGreeting();
            ResetProactiveTimer(now);
        }

        private void HandleUserSpeakingEnd(float now)
        {
            ResetProactiveTimer(now);
        }

        private void CancelScheduledGreeting()
        {
            if (_sendGreetingOnReady && !_greetingSent)
            {
                _scheduledGreetingTime = 0f;
            }
        }

        private void ResetProactiveTimer(float now)
        {
            _lastProactiveTime = now;
            _currentWaitSeconds = GetNextWaitSeconds();
        }

        private bool IsContextReadyForProactive()
        {
            if (_context == null || !_context.IsInitialized)
            {
                return false;
            }

            return !_requireChatActive || _context.IsChatActive;
        }

        private void ScheduleGreeting(bool includeMicStartupDelay)
        {
            if (_context == null)
            {
                return;
            }

            float delay = Mathf.Max(0f, _greetingDelaySeconds);
            if (includeMicStartupDelay)
            {
                delay += Mathf.Max(0f, _context.MicStartupDelaySeconds);
            }

            _scheduledGreetingTime = NowSeconds() + delay;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
