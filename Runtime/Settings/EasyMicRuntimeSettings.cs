#if UNITY_2021_3_OR_NEWER
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    public static class EasyMicRuntimeSettings
    {
        private const string RuntimeResourceName = "EasyMicProjectSettings";
        private static EasyMicProjectSettings s_settings;
        private static EasyMicProjectSettings s_defaults;

        public static EasyMicProjectSettings Current
        {
            get
            {
                if (s_settings != null)
                {
                    return s_settings;
                }

                s_settings = Resources.Load<EasyMicProjectSettings>(RuntimeResourceName);
                if (s_settings != null)
                {
                    s_settings.Migrate();
                    return s_settings;
                }

                if (s_defaults == null)
                {
                    s_defaults = ScriptableObject.CreateInstance<EasyMicProjectSettings>();
                    s_defaults.hideFlags = HideFlags.HideAndDontSave;
                    s_defaults.ResetToDefaults();
                }

                return s_defaults;
            }
        }

        public static EasyMicRuntimeGlobalSettings Runtime => Current.Runtime;

        public static void OverrideForSession(EasyMicProjectSettings settings)
        {
            s_settings = settings;
            s_settings?.Migrate();
        }

        public static void ClearSessionOverride()
        {
            s_settings = null;
        }

        public static void ApplyTo(AudioSystem audioSystem)
        {
            if (audioSystem == null)
            {
                return;
            }

            var runtime = Runtime;
            if (audioSystem.IsRunning)
            {
                audioSystem.MeteringEnabled = runtime.enableTelemetry || runtime.enableRuntimeDiagnostics;
                return;
            }

            audioSystem.LatencyProfile = runtime.latencyProfile;
            audioSystem.MeteringEnabled = runtime.enableTelemetry || runtime.enableRuntimeDiagnostics;

            if (runtime.preferNativeDeviceFormat)
            {
                audioSystem.PreferNativeFormat();
            }
            else
            {
                audioSystem.Configure((uint)runtime.defaultSampleRate, (uint)runtime.defaultChannels);
            }
        }
    }
}
#endif
