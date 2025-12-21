using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public sealed class AIChatPluginHost
    {
        private readonly IAIChatPluginContext _context;
        private readonly List<IAIChatPlugin> _plugins = new List<IAIChatPlugin>();
        private readonly HashSet<IAIChatPlugin> _activePlugins = new HashSet<IAIChatPlugin>();

        public AIChatPluginHost(IAIChatPluginContext context, IEnumerable<MonoBehaviour> pluginBehaviours)
        {
            _context = context;
            RefreshPlugins(pluginBehaviours);
        }

        public void RefreshPlugins(IEnumerable<MonoBehaviour> pluginBehaviours)
        {
            _plugins.Clear();

            if (pluginBehaviours == null)
            {
                return;
            }

            foreach (var behaviour in pluginBehaviours)
            {
                if (behaviour == null)
                {
                    continue;
                }

                if (behaviour is IAIChatPlugin plugin)
                {
                    _plugins.Add(plugin);
                }
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                var plugin = _plugins[i];
                if (plugin == null)
                {
                    continue;
                }

                bool enabled = plugin.IsEnabled;
                bool isActive = _activePlugins.Contains(plugin);

                if (enabled && !isActive)
                {
                    plugin.Initialize(_context);
                    _activePlugins.Add(plugin);
                }
                else if (!enabled && isActive)
                {
                    plugin.Shutdown();
                    _activePlugins.Remove(plugin);
                }

                if (enabled)
                {
                    plugin.Tick(deltaTime);
                }
            }
        }

        public void NotifyChatActivated()
        {
            DispatchLifecycle(listener => listener.OnChatActivated());
        }

        public void NotifyConversationStarted(bool isProactive)
        {
            DispatchLifecycle(listener => listener.OnConversationStarted(isProactive));
        }

        public void NotifyUserMessageSubmitted(string message, bool isProactive)
        {
            DispatchLifecycle(listener => listener.OnUserMessageSubmitted(message, isProactive));
        }

        public void NotifyAssistantRequestStarted(string prompt, bool isProactive)
        {
            DispatchLifecycle(listener => listener.OnAssistantRequestStarted(prompt, isProactive));
        }

        public void NotifyAssistantResponseFinished(string response, bool success, string errorMessage)
        {
            DispatchLifecycle(listener => listener.OnAssistantResponseFinished(response, success, errorMessage));
        }

        public void NotifyIdleStateChanged(bool isIdle)
        {
            DispatchLifecycle(listener => listener.OnIdleStateChanged(isIdle));
        }

        public void Shutdown()
        {
            foreach (var plugin in _activePlugins)
            {
                plugin.Shutdown();
            }

            _activePlugins.Clear();
        }

        private void DispatchLifecycle(Action<IAIChatLifecycleListener> dispatch)
        {
            if (dispatch == null)
            {
                return;
            }

            foreach (var plugin in _activePlugins)
            {
                if (plugin is IAIChatLifecycleListener listener)
                {
                    dispatch(listener);
                }
            }
        }
    }
}
