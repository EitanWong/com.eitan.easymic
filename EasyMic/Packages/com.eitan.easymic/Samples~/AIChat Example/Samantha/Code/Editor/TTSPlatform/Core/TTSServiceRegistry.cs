// ============================================================================
// TTSServiceRegistry.cs - 服务注册与管理
// 管理所有TTS服务的注册、获取和生命周期
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TTSPlatform.Core
{
    /// <summary>
    /// TTS服务注册表 - 单例模式管理所有服务
    /// </summary>
    public static class TTSServiceRegistry
    {
        private static readonly Dictionary<string, ITTSService> _services
            = new Dictionary<string, ITTSService>();

        private static readonly Dictionary<string, Func<ITTSService>> _serviceFactories
            = new Dictionary<string, Func<ITTSService>>();

        private static bool _initialized = false;

        /// <summary>已注册的服务ID列表</summary>
        public static IEnumerable<string> RegisteredServiceIds => _services.Keys;

        /// <summary>已注册的服务列表</summary>
        public static IEnumerable<ITTSService> RegisteredServices => _services.Values;

        /// <summary>服务数量</summary>
        public static int ServiceCount => _services.Count;

        /// <summary>
        /// 初始化注册表（自动发现并注册所有服务）
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (_initialized)
            {
                return;
            }


            _initialized = true;

            // 自动发现所有ITTSService实现
            var serviceTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => typeof(ITTSService).IsAssignableFrom(t)
                    && !t.IsInterface
                    && !t.IsAbstract);

            foreach (var type in serviceTypes)
            {
                try
                {
                    var service = (ITTSService)Activator.CreateInstance(type);
                    Register(service);
                    Debug.Log($"[TTSPlatform] 已注册服务: {service.DisplayName}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TTSPlatform] 注册服务失败 {type.Name}: {e.Message}");
                }
            }
        }

        /// <summary>注册服务实例</summary>
        public static void Register(ITTSService service)
        {
            if (service == null)
            {

                throw new ArgumentNullException(nameof(service));
            }


            _services[service.ServiceId] = service;

            // 加载保存的API Key
            var savedKey = EditorPrefs.GetString($"TTS_ApiKey_{service.ServiceId}", "");
            if (!string.IsNullOrEmpty(savedKey))
            {
                service.ApiKey = savedKey;
            }
        }

        /// <summary>注册服务工厂（延迟实例化）</summary>
        public static void RegisterFactory(string serviceId, Func<ITTSService> factory)
        {
            _serviceFactories[serviceId] = factory;
        }

        /// <summary>获取服务</summary>
        public static ITTSService GetService(string serviceId)
        {
            if (_services.TryGetValue(serviceId, out var service))
            {

                return service;
            }

            // 尝试从工厂创建

            if (_serviceFactories.TryGetValue(serviceId, out var factory))
            {
                service = factory();
                Register(service);
                return service;
            }

            return null;
        }

        /// <summary>获取服务（泛型版本）</summary>
        public static T GetService<T>() where T : class, ITTSService
        {
            return _services.Values.OfType<T>().FirstOrDefault();
        }

        /// <summary>保存服务配置</summary>
        public static void SaveServiceConfig(ITTSService service)
        {
            if (service == null)
            {
                return;
            }


            EditorPrefs.SetString($"TTS_ApiKey_{service.ServiceId}", service.ApiKey ?? "");
        }

        /// <summary>移除服务</summary>
        public static bool Unregister(string serviceId)
        {
            return _services.Remove(serviceId);
        }

        /// <summary>清空所有服务</summary>
        public static void Clear()
        {
            _services.Clear();
            _serviceFactories.Clear();
        }
    }
}
