#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Threading.Tasks;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal readonly struct AIChatRuntimeConfigurationResult
    {
        private AIChatRuntimeConfigurationResult(bool succeeded, string errorMessage)
        {
            Succeeded = succeeded;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Succeeded { get; }
        public string ErrorMessage { get; }

        public static AIChatRuntimeConfigurationResult Success()
            => new AIChatRuntimeConfigurationResult(true, string.Empty);

        public static AIChatRuntimeConfigurationResult Failure(string errorMessage)
            => new AIChatRuntimeConfigurationResult(false, errorMessage);
    }

    internal static class AIChatRuntimeConfigurationFlow
    {
        public static void ApplyStartupLayers(
            AIChatConfigurationPolicy policy,
            IAIChatRuntimeConfigStore store,
            string path,
            AIChatControllerConfig controllerConfig,
            bool loadRuntimeConfig)
        {
            if (controllerConfig == null)
            {
                throw new ArgumentNullException(nameof(controllerConfig));
            }

            string injectedApiKey = controllerConfig.ResolveApiKey();
            policy?.ApplyTo(controllerConfig);
            if (!loadRuntimeConfig)
            {
                return;
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            AIChatRuntimeConfig runtimeConfig = store.LoadOrCreate(path, controllerConfig, out _);
            store.Apply(runtimeConfig, controllerConfig);
            if (!string.IsNullOrWhiteSpace(injectedApiKey))
            {
                controllerConfig.SetApiKeyOverride(injectedApiKey);
            }
        }

        public static bool TrySaveAndApply(
            IAIChatRuntimeConfigStore store,
            string path,
            AIChatRuntimeConfig runtimeConfig,
            AIChatControllerConfig controllerConfig,
            Action refreshRuntime,
            out string errorMessage)
        {
            if (store == null)
            {
                errorMessage = "Runtime configuration store is missing.";
                return false;
            }

            if (!store.TrySave(path, runtimeConfig, out errorMessage))
            {
                return false;
            }

            if (controllerConfig != null)
            {
                try
                {
                    store.Apply(runtimeConfig, controllerConfig);
                    refreshRuntime?.Invoke();
                }
                catch (Exception ex)
                {
                    errorMessage = $"Runtime settings were saved, but applying them failed: {ex.Message}";
                    return false;
                }
            }

            return true;
        }

        public static async Task<AIChatRuntimeConfigurationResult> SaveAndApplyAsync(
            IAIChatRuntimeConfigStore store,
            string path,
            AIChatRuntimeConfig runtimeConfig,
            AIChatControllerConfig controllerConfig,
            Func<Task> refreshRuntime)
        {
            if (store == null)
            {
                return AIChatRuntimeConfigurationResult.Failure(
                    "Runtime configuration store is missing.");
            }

            if (!store.TrySave(path, runtimeConfig, out string saveError))
            {
                return AIChatRuntimeConfigurationResult.Failure(saveError);
            }

            if (controllerConfig == null)
            {
                return AIChatRuntimeConfigurationResult.Success();
            }

            try
            {
                store.Apply(runtimeConfig, controllerConfig);
                if (refreshRuntime != null)
                {
                    await refreshRuntime();
                }

                return AIChatRuntimeConfigurationResult.Success();
            }
            catch (Exception ex)
            {
                return AIChatRuntimeConfigurationResult.Failure(
                    $"Runtime settings were saved, but applying them failed: {ex.Message}");
            }
        }
    }
}
#endif
