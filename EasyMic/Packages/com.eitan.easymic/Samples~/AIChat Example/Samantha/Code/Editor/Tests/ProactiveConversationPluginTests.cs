using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class ProactiveConversationPluginTests
    {
        [Test]
        public void Initialize_WhenContextIsReady_ShouldSendGreetingImmediatelyOnce()
        {
            var root = new GameObject(nameof(Initialize_WhenContextIsReady_ShouldSendGreetingImmediatelyOnce));
            var greetingProfile = CreatePromptProfile("Offer one warm opening question.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);

                var context = new TestPluginContext
                {
                    IsInitialized = true,
                    IsIdle = true,
                    IsChatActive = false
                };

                plugin.Initialize(context);
                plugin.Tick(1f);
                plugin.OnChatActivated();
                plugin.Tick(1f);

                Assert.AreEqual(1, context.SendCount);
                Assert.AreEqual("Offer one warm opening question.", context.LastPrompt);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenInitializationCompletes_ShouldGreetBeforeChatActivation()
        {
            var root = new GameObject(nameof(Tick_WhenInitializationCompletes_ShouldGreetBeforeChatActivation));
            var greetingProfile = CreatePromptProfile("Say hello and invite a reply.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);

                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsChatActive = false
                };

                plugin.Initialize(context);
                plugin.Tick(0.016f);
                Assert.AreEqual(0, context.SendCount);

                context.IsInitialized = true;
                plugin.Tick(0.016f);
                plugin.Tick(0.016f);

                Assert.AreEqual(1, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_GreetingDelay_ShouldNotIncludeMicrophoneStartupDelay()
        {
            var root = new GameObject(nameof(Tick_GreetingDelay_ShouldNotIncludeMicrophoneStartupDelay));
            var greetingProfile = CreatePromptProfile("Greet now.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0.05f);

                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = false,
                    IsChatActive = false,
                    MicStartupDelaySeconds = 10f
                };

                plugin.Initialize(context);
                context.IsInitialized = true;
                plugin.Tick(0.049f);
                Assert.AreEqual(0, context.SendCount);

                plugin.Tick(0.001f);

                Assert.AreEqual(1, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_AfterConversationPause_ShouldSendContextAwareFollowUp()
        {
            var root = new GameObject(nameof(Tick_AfterConversationPause_ShouldSendContextAwareFollowUp));
            var idleProfile = CreatePromptProfile("Continue the conversation naturally.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_sendGreetingOnReady", false);
                SetField(plugin, "_idlePrompts", idleProfile);
                SetField(plugin, "_minProactiveWaitSeconds", 0.1f);
                SetField(plugin, "_maxProactiveWaitSeconds", 0.1f);

                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    IsChatActive = true,
                    HasConversationHistory = true,
                    TimeSinceLastUserActivity = 10f,
                    TimeSinceLastAssistantResponse = 10f
                };

                plugin.Initialize(context);
                plugin.OnUserMessageSubmitted("I finally finished the project.", false);
                plugin.OnAssistantRequestStarted("I finally finished the project.", false);
                plugin.OnAssistantResponseFinished("That must feel good.", true, null);
                plugin.OnIdleStateChanged(true);
                plugin.Tick(0.1f);

                Assert.AreEqual(1, context.SendCount);
                Assert.That(context.LastPrompt, Does.Contain("one concrete detail"));
                Assert.That(context.LastPrompt, Does.Contain("one easy, open question"));
                Assert.That(context.LastPrompt, Does.Contain("Continue the conversation naturally."));
                Assert.That(context.LastPrompt, Does.Not.Contain("I finally finished the project."));
                Assert.That(context.LastPrompt, Does.Not.Contain("That must feel good."));
            }
            finally
            {
                Object.DestroyImmediate(idleProfile.Prompts[0]);
                Object.DestroyImmediate(idleProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhileUserIsSpeaking_ShouldNotSendFollowUp()
        {
            var root = new GameObject(nameof(Tick_WhileUserIsSpeaking_ShouldNotSendFollowUp));
            var idleProfile = CreatePromptProfile("Continue the conversation naturally.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_sendGreetingOnReady", false);
                SetField(plugin, "_idlePrompts", idleProfile);
                SetField(plugin, "_minProactiveWaitSeconds", 0f);
                SetField(plugin, "_maxProactiveWaitSeconds", 0f);

                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    IsChatActive = true,
                    IsUserSpeaking = true,
                    HasConversationHistory = true,
                    TimeSinceLastUserActivity = 10f,
                    TimeSinceLastAssistantResponse = 10f
                };

                plugin.Initialize(context);
                plugin.OnConversationStarted(false);
                plugin.OnIdleStateChanged(true);
                plugin.Tick(10f);

                Assert.AreEqual(0, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(idleProfile.Prompts[0]);
                Object.DestroyImmediate(idleProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Initialize_WhenCalledRepeatedly_ShouldKeepOneGreeting()
        {
            var root = new GameObject(nameof(Initialize_WhenCalledRepeatedly_ShouldKeepOneGreeting));
            var greetingProfile = CreatePromptProfile("Hello once.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true
                };

                plugin.Initialize(context);
                plugin.Initialize(context);
                plugin.Shutdown();
                plugin.Initialize(context);
                plugin.Tick(1f);

                Assert.AreEqual(1, context.SendCount);
                Assert.AreEqual(1, context.SendAttemptCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Shutdown_BeforeGreetingDue_ShouldCancelOldSchedule()
        {
            var root = new GameObject(nameof(Shutdown_BeforeGreetingDue_ShouldCancelOldSchedule));
            var greetingProfile = CreatePromptProfile("Hello after restart.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 1f);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true
                };

                plugin.Initialize(context);
                plugin.Tick(0.5f);
                plugin.Shutdown();
                plugin.Tick(10f);
                Assert.AreEqual(0, context.SendCount);

                plugin.Initialize(context);
                plugin.Tick(0.5f);
                Assert.AreEqual(0, context.SendCount);
                plugin.Tick(0.5f);

                Assert.AreEqual(1, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenSendIsRejected_ShouldThrottleRetryAndCommitOnce()
        {
            var root = new GameObject(nameof(Tick_WhenSendIsRejected_ShouldThrottleRetryAndCommitOnce));
            var greetingProfile = CreatePromptProfile("Retry this greeting.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    AcceptSends = false
                };

                plugin.Initialize(context);
                plugin.Tick(0f);
                Assert.AreEqual(1, context.SendAttemptCount);
                plugin.Tick(0.01f);
                Assert.AreEqual(1, context.SendAttemptCount);

                plugin.Tick(0.04f);
                Assert.AreEqual(2, context.SendAttemptCount);
                context.AcceptSends = true;
                plugin.Tick(0.1f);
                plugin.Tick(1f);

                Assert.AreEqual(3, context.SendAttemptCount);
                Assert.AreEqual(1, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenSendKeepsBeingRejected_ShouldStopAtFailureLimit()
        {
            var root = new GameObject(nameof(Tick_WhenSendKeepsBeingRejected_ShouldStopAtFailureLimit));
            var greetingProfile = CreatePromptProfile("Bound this retry.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);
                SetField(plugin, "_maxConsecutiveDispatchFailures", 3);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    AcceptSends = false
                };

                plugin.Initialize(context);
                plugin.Tick(0.05f);
                plugin.Tick(0.1f);
                plugin.Tick(100f);

                Assert.AreEqual(3, context.SendAttemptCount);
                Assert.AreEqual(0, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenProactiveResponsesKeepFailing_ShouldWaitForUserAfterLimit()
        {
            var root = new GameObject(nameof(Tick_WhenProactiveResponsesKeepFailing_ShouldWaitForUserAfterLimit));
            var idleProfile = CreatePromptProfile("Bound failed follow-ups.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_sendGreetingOnReady", false);
                SetField(plugin, "_idlePrompts", idleProfile);
                SetField(plugin, "_minProactiveWaitSeconds", 0f);
                SetField(plugin, "_maxProactiveWaitSeconds", 0f);
                SetField(plugin, "_maxConsecutiveDispatchFailures", 3);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    IsChatActive = true,
                    HasConversationHistory = true
                };

                plugin.Initialize(context);
                plugin.Tick(0f);
                plugin.OnAssistantRequestStarted("first", true);
                plugin.OnIdleStateChanged(true);
                plugin.OnAssistantResponseFinished(null, false, "failed");
                plugin.Tick(0.05f);
                plugin.OnAssistantRequestStarted("second", true);
                plugin.OnIdleStateChanged(true);
                plugin.OnAssistantResponseFinished(null, false, "failed");
                plugin.Tick(0.1f);
                plugin.OnAssistantRequestStarted("third", true);
                plugin.OnIdleStateChanged(true);
                plugin.OnAssistantResponseFinished(null, false, "failed");
                plugin.Tick(100f);

                Assert.AreEqual(3, context.SendCount);

                plugin.OnUserMessageSubmitted("Try again now.", false);
                plugin.OnIdleStateChanged(true);
                plugin.Tick(0f);

                Assert.AreEqual(4, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(idleProfile.Prompts[0]);
                Object.DestroyImmediate(idleProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Host_FirstTick_ShouldActivatePluginBeforeGreetingLifecycleCallbacks()
        {
            var root = new GameObject(nameof(Host_FirstTick_ShouldActivatePluginBeforeGreetingLifecycleCallbacks));
            var greetingProfile = CreatePromptProfile("Track this greeting lifecycle.");
            AIChatPluginHost host = null;
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true
                };
                host = new AIChatPluginHost(context, new MonoBehaviour[] { plugin });
                context.OnAcceptedSend = prompt => host.NotifyAssistantRequestStarted(prompt, true);

                host.Tick(0f);
                Assert.AreEqual(1, context.SendCount);

                host.NotifyAssistantResponseFinished(null, false, "failed");
                context.AcceptSends = false;
                host.Tick(0.05f);

                Assert.AreEqual(2, context.SendAttemptCount);
            }
            finally
            {
                host?.Shutdown();
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenUserSpeechEndsBeforeAsrSubmit_ShouldKeepGreetingPending()
        {
            var root = new GameObject(nameof(Tick_WhenUserSpeechEndsBeforeAsrSubmit_ShouldKeepGreetingPending));
            var greetingProfile = CreatePromptProfile("Do not race the transcript.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    IsUserSpeaking = true
                };

                plugin.Initialize(context);
                plugin.Tick(0f);
                context.IsUserSpeaking = false;
                plugin.Tick(1f);

                Assert.AreEqual(0, context.SendAttemptCount);

                plugin.OnUserMessageSubmitted("I was speaking first.", false);
                plugin.Tick(1f);

                Assert.AreEqual(0, context.SendAttemptCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_WhenAssistantIsBusy_ShouldWaitBeforeGreeting()
        {
            var root = new GameObject(nameof(Tick_WhenAssistantIsBusy_ShouldWaitBeforeGreeting));
            var greetingProfile = CreatePromptProfile("Wait for idle.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_greetingPrompts", greetingProfile);
                SetField(plugin, "_greetingDelaySeconds", 0f);
                var context = new TestPluginContext
                {
                    IsIdle = false,
                    IsInitialized = true
                };

                plugin.Initialize(context);
                plugin.Tick(1f);
                Assert.AreEqual(0, context.SendAttemptCount);

                context.IsIdle = true;
                plugin.Tick(0f);

                Assert.AreEqual(1, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(greetingProfile.Prompts[0]);
                Object.DestroyImmediate(greetingProfile);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Tick_AfterUnansweredLimit_ShouldWaitForNewUserMessage()
        {
            var root = new GameObject(nameof(Tick_AfterUnansweredLimit_ShouldWaitForNewUserMessage));
            var idleProfile = CreatePromptProfile("Invite another reply.");
            try
            {
                var plugin = root.AddComponent<ProactiveConversationPlugin>();
                SetField(plugin, "_sendGreetingOnReady", false);
                SetField(plugin, "_idlePrompts", idleProfile);
                SetField(plugin, "_minProactiveWaitSeconds", 0f);
                SetField(plugin, "_maxProactiveWaitSeconds", 0f);
                SetField(plugin, "_maxUnansweredProactiveMessages", 2);
                var context = new TestPluginContext
                {
                    IsIdle = true,
                    IsInitialized = true,
                    IsChatActive = true,
                    HasConversationHistory = true
                };

                plugin.Initialize(context);
                plugin.Tick(0f);
                plugin.OnIdleStateChanged(true);
                plugin.Tick(0f);
                plugin.OnIdleStateChanged(true);
                plugin.Tick(10f);

                Assert.AreEqual(2, context.SendCount);

                plugin.OnUserMessageSubmitted("Here is something new.", false);
                plugin.OnIdleStateChanged(true);
                plugin.Tick(0f);

                Assert.AreEqual(3, context.SendCount);
            }
            finally
            {
                Object.DestroyImmediate(idleProfile.Prompts[0]);
                Object.DestroyImmediate(idleProfile);
                Object.DestroyImmediate(root);
            }
        }

        private static PromptProfile CreatePromptProfile(string prompt)
        {
            var profile = ScriptableObject.CreateInstance<PromptProfile>();
            profile.Prompts = new[] { new TextAsset(prompt) };
            return profile;
        }

        private static void SetField<T>(ProactiveConversationPlugin plugin, string fieldName, T value)
        {
            FieldInfo field = typeof(ProactiveConversationPlugin).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing field {fieldName}");
            field.SetValue(plugin, value);
        }

        private sealed class TestPluginContext : IAIChatPluginContext
        {
            public bool IsIdle { get; set; }
            public bool IsChatActive { get; set; }
            public bool IsInitialized { get; set; }
            public bool HasConfigurationPolicy => false;
            public AIChatConfigurationPolicy.PolicyPreset ConfigurationPolicyPreset =>
                AIChatConfigurationPolicy.PolicyPreset.Custom;
            public bool IsUserSpeaking { get; set; }
            public bool HasConversationHistory { get; set; }
            public float TimeSinceLastUserActivity { get; set; }
            public float TimeSinceLastAssistantResponse { get; set; }
            public float MicStartupDelaySeconds { get; set; }
            public AIChatResolvedConfiguration ResolvedConfiguration => new AIChatResolvedConfiguration();
            public bool AcceptSends { get; set; } = true;
            public System.Action<string> OnAcceptedSend { get; set; }
            public int SendAttemptCount { get; private set; }
            public int SendCount { get; private set; }
            public string LastPrompt { get; private set; }

            public bool TrySendProactiveMessage(string prompt, bool recordUserMessage)
            {
                SendAttemptCount++;
                LastPrompt = prompt;
                if (!AcceptSends)
                {
                    return false;
                }

                SendCount++;
                OnAcceptedSend?.Invoke(prompt);
                return true;
            }
        }
    }
}
