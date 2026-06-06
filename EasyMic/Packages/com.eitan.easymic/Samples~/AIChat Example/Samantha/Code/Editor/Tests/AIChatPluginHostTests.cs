using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class AIChatPluginHostTests
    {
        [Test]
        public void Tick_WhenPluginThrows_ShouldDisableFaultedPluginAndContinueOthers()
        {
            var root = new GameObject("AIChatPluginHostTests");
            try
            {
                var faulty = root.AddComponent<TestPlugin>();
                faulty.ThrowOnTick = true;
                var healthy = root.AddComponent<TestPlugin>();

                var host = new AIChatPluginHost(null, new MonoBehaviour[] { faulty, healthy });

                bool previousIgnore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    host.Tick(0.016f);
                    host.Tick(0.016f);
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnore;
                }

                Assert.AreEqual(1, faulty.TickCount);
                Assert.AreEqual(1, faulty.ShutdownCount);
                Assert.AreEqual(2, healthy.TickCount);
                Assert.AreEqual(1, healthy.InitializeCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NotifyLifecycle_WhenListenerThrows_ShouldDisableFaultedListenerAndContinueOthers()
        {
            var root = new GameObject("AIChatPluginHostTests");
            try
            {
                var faulty = root.AddComponent<TestPlugin>();
                faulty.ThrowOnChatActivated = true;
                var healthy = root.AddComponent<TestPlugin>();

                var host = new AIChatPluginHost(null, new MonoBehaviour[] { faulty, healthy });
                host.Tick(0.016f);

                bool previousIgnore = LogAssert.ignoreFailingMessages;
                LogAssert.ignoreFailingMessages = true;
                try
                {
                    host.NotifyChatActivated();
                    host.NotifyChatActivated();
                }
                finally
                {
                    LogAssert.ignoreFailingMessages = previousIgnore;
                }

                Assert.AreEqual(1, faulty.ChatActivatedCount);
                Assert.AreEqual(1, faulty.ShutdownCount);
                Assert.AreEqual(2, healthy.ChatActivatedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RefreshPlugins_ShouldShutdownPreviousActivePlugins()
        {
            var root = new GameObject("AIChatPluginHostTests");
            try
            {
                var previous = root.AddComponent<TestPlugin>();
                var next = root.AddComponent<TestPlugin>();

                var host = new AIChatPluginHost(null, new MonoBehaviour[] { previous });
                host.Tick(0.016f);
                host.RefreshPlugins(new MonoBehaviour[] { next });
                host.NotifyChatActivated();
                host.Tick(0.016f);

                Assert.AreEqual(1, previous.ShutdownCount);
                Assert.AreEqual(0, previous.ChatActivatedCount);
                Assert.AreEqual(1, next.ChatActivatedCount);
                Assert.AreEqual(1, next.TickCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class TestPlugin : MonoBehaviour, IAIChatPlugin, IAIChatLifecycleListener
        {
            public bool ThrowOnTick;
            public bool ThrowOnChatActivated;
            public int InitializeCount;
            public int TickCount;
            public int ShutdownCount;
            public int ChatActivatedCount;

            public bool IsEnabled => true;

            public void Initialize(IAIChatPluginContext context)
            {
                InitializeCount++;
            }

            public void Tick(float deltaTime)
            {
                TickCount++;
                if (ThrowOnTick)
                {
                    throw new InvalidOperationException("tick failed");
                }
            }

            public void Shutdown()
            {
                ShutdownCount++;
            }

            public void OnChatActivated()
            {
                ChatActivatedCount++;
                if (ThrowOnChatActivated)
                {
                    throw new InvalidOperationException("chat activated failed");
                }
            }

            public void OnConversationStarted(bool isProactive) { }
            public void OnUserMessageSubmitted(string message, bool isProactive) { }
            public void OnAssistantRequestStarted(string prompt, bool isProactive) { }
            public void OnAssistantResponseFinished(string response, bool success, string errorMessage) { }
            public void OnIdleStateChanged(bool isIdle) { }
        }
    }
}
