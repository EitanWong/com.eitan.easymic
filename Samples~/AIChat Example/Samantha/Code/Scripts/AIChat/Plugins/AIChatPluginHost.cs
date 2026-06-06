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
        private readonly HashSet<IAIChatPlugin> _faultedPlugins = new HashSet<IAIChatPlugin>();

        public AIChatPluginHost(IAIChatPluginContext context, IEnumerable<MonoBehaviour> pluginBehaviours)
        {
            _context = context;
            RefreshPlugins(pluginBehaviours);
        }

        public void RefreshPlugins(IEnumerable<MonoBehaviour> pluginBehaviours)
        {
            if (_activePlugins.Count > 0)
            {
                var activePlugins = new List<IAIChatPlugin>(_activePlugins);
                for (int i = 0; i < activePlugins.Count; i++)
                {
                    TryShutdownPlugin(activePlugins[i]);
                }

                _activePlugins.Clear();
            }

            _plugins.Clear();
            _faultedPlugins.Clear();

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

                if (_faultedPlugins.Contains(plugin))
                {
                    continue;
                }

                if (!TryGetPluginEnabled(plugin, out bool enabled))
                {
                    continue;
                }

                bool isActive = _activePlugins.Contains(plugin);

                if (enabled && !isActive)
                {
                    if (!TryInitializePlugin(plugin))
                    {
                        continue;
                    }

                    _activePlugins.Add(plugin);
                    isActive = true;
                }
                else if (!enabled && isActive)
                {
                    TryShutdownPlugin(plugin);
                    _activePlugins.Remove(plugin);
                    isActive = false;
                }

                if (enabled && isActive)
                {
                    TryTickPlugin(plugin, deltaTime);
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
            var activePlugins = new List<IAIChatPlugin>(_activePlugins);
            for (int i = 0; i < activePlugins.Count; i++)
            {
                TryShutdownPlugin(activePlugins[i]);
            }

            _activePlugins.Clear();
            _faultedPlugins.Clear();
        }

        private void DispatchLifecycle(Action<IAIChatLifecycleListener> dispatch)
        {
            if (dispatch == null)
            {
                return;
            }

            var activePlugins = new List<IAIChatPlugin>(_activePlugins);
            for (int i = 0; i < activePlugins.Count; i++)
            {
                var plugin = activePlugins[i];
                if (plugin == null || _faultedPlugins.Contains(plugin))
                {
                    continue;
                }

                if (plugin is IAIChatLifecycleListener listener)
                {
                    try
                    {
                        dispatch(listener);
                    }
                    catch (Exception ex)
                    {
                        MarkPluginFaulted(plugin, "lifecycle callback", ex);
                    }
                }
            }
        }

        private bool TryGetPluginEnabled(IAIChatPlugin plugin, out bool enabled)
        {
            enabled = false;
            try
            {
                enabled = plugin.IsEnabled;
                return true;
            }
            catch (Exception ex)
            {
                MarkPluginFaulted(plugin, "IsEnabled", ex);
                return false;
            }
        }

        private bool TryInitializePlugin(IAIChatPlugin plugin)
        {
            try
            {
                plugin.Initialize(_context);
                return true;
            }
            catch (Exception ex)
            {
                MarkPluginFaulted(plugin, "Initialize", ex);
                return false;
            }
        }

        private void TryTickPlugin(IAIChatPlugin plugin, float deltaTime)
        {
            try
            {
                plugin.Tick(deltaTime);
            }
            catch (Exception ex)
            {
                MarkPluginFaulted(plugin, "Tick", ex);
            }
        }

        private void TryShutdownPlugin(IAIChatPlugin plugin)
        {
            if (plugin == null)
            {
                return;
            }

            try
            {
                plugin.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIChatPluginHost] Plugin {GetPluginName(plugin)} failed during Shutdown: {ex.Message}");
            }
        }

        private void MarkPluginFaulted(IAIChatPlugin plugin, string operation, Exception ex)
        {
            if (plugin == null)
            {
                return;
            }

            _faultedPlugins.Add(plugin);

            if (_activePlugins.Remove(plugin))
            {
                TryShutdownPlugin(plugin);
            }

            Debug.LogError($"[AIChatPluginHost] Plugin {GetPluginName(plugin)} failed during {operation} and was disabled: {ex}");
        }

        private static string GetPluginName(IAIChatPlugin plugin)
        {
            return plugin != null ? plugin.GetType().Name : "<null>";
        }
    }
}
