#if UNITY_2021_3_OR_NEWER
using System;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    public enum EasyMicAudioBackendMode
    {
        Auto = 0,
        Miniaudio = 1,
        UnityFallback = 2
    }

    public enum EasyMicBufferStrategy
    {
        Automatic = 0,
        LowLatency = 1,
        Balanced = 2,
        StableStreaming = 3,
        Custom = 4
    }

    public enum EasyMicResamplerQuality
    {
        Fast = 0,
        Balanced = 1,
        HighQuality = 2
    }

    public enum EasyMicThreadPriorityMode
    {
        PlatformDefault = 0,
        Normal = 1,
        AboveNormal = 2,
        TimeCritical = 3
    }

    public enum EasyMicLogLevel
    {
        ErrorsOnly = 0,
        Warnings = 1,
        Info = 2,
        Verbose = 3
    }

    [Serializable]
    public sealed class EasyMicRuntimeGlobalSettings
    {
        public EasyMicAudioBackendMode backendMode = EasyMicAudioBackendMode.Auto;
        public EasyMicLatencyProfile latencyProfile = EasyMicLatencyProfile.LowLatency;
        public EasyMicBufferStrategy bufferStrategy = EasyMicBufferStrategy.Automatic;
        public int customBufferFrames = 256;
        public bool preferNativeDeviceFormat = true;
        public bool enableTelemetry = true;
        public bool enableRuntimeDiagnostics = true;
        public bool enableLogging = true;
        public EasyMicLogLevel logLevel = EasyMicLogLevel.Warnings;
        public bool enableDspLimiter = true;
        public bool enableDriftCorrection = true;
        public EasyMicResamplerQuality resamplerQuality = EasyMicResamplerQuality.Balanced;
        public EasyMicThreadPriorityMode audioThreadPriority = EasyMicThreadPriorityMode.PlatformDefault;
        public bool runInBackground = true;
        public bool autoFallback = true;
        public bool autoRefreshDevices = true;
        public float deviceRefreshIntervalSeconds = 2f;
        public int defaultSampleRate = 48000;
        public int defaultChannels = 2;
        public int streamingQueueMilliseconds = 250;
    }

    [Serializable]
    public sealed class EasyMicEditorToolingSettings
    {
        public bool enableEditorDiagnostics = true;
        public bool enableGraphAnimations = true;
        public bool showAdvancedPipelineMetrics;
        public bool followUnityEditorTheme = true;
        public bool highContrastGraphs;
        public bool developerMode;
        public bool diagnosticsMode;
        public float telemetryRefreshRate = 30f;
        public int graphMaximumVisibleNodes = 128;
    }

    [Serializable]
    public sealed class EasyMicPlatformTuningSettings
    {
        public EasyMicAudioBackendMode backendMode = EasyMicAudioBackendMode.Auto;
        public bool overrideLatencyProfile;
        public EasyMicLatencyProfile latencyProfile = EasyMicLatencyProfile.LowLatency;
        public bool overrideBufferFrames;
        public int bufferFrames = 256;
        public bool enableDspLimiter = true;
        public bool enableDiagnostics;
        public bool preferNativeDeviceFormat = true;
        public bool enableDriftCorrection = true;
        public bool forceSafeDeviceEnumeration;
        public bool useAAudio = true;
        public bool useWasapiExclusiveMode;
        public bool useCoreAudioVoiceProcessing;
        public bool enablePulseAudioFallback = true;
    }

    [Serializable]
    public sealed class EasyMicExperimentalSettings
    {
        public bool enableBurstDsp;
        public bool enableSimdKernels;
        public bool enableLowLatencyCapturePath;
        public bool enableAdvancedTelemetry;
        public bool enableFutureDspGraph;
    }

    [Serializable]
    public sealed class EasyMicLocalizationSettings
    {
        public bool followUnityEditorLanguage = true;
        public string languageOverride = string.Empty;
        public bool showMissingLocalizationKeys;
    }

    public sealed class EasyMicProjectSettings : ScriptableObject
    {
        public const int CurrentVersion = 1;

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private EasyMicRuntimeGlobalSettings runtime = new EasyMicRuntimeGlobalSettings();
        [SerializeField] private EasyMicEditorToolingSettings editor = new EasyMicEditorToolingSettings();
        [SerializeField] private EasyMicPlatformTuningSettings windows = new EasyMicPlatformTuningSettings();
        [SerializeField] private EasyMicPlatformTuningSettings macOS = new EasyMicPlatformTuningSettings();
        [SerializeField] private EasyMicPlatformTuningSettings linux = new EasyMicPlatformTuningSettings();
        [SerializeField] private EasyMicPlatformTuningSettings android = new EasyMicPlatformTuningSettings { forceSafeDeviceEnumeration = true };
        [SerializeField] private EasyMicPlatformTuningSettings iOS = new EasyMicPlatformTuningSettings { preferNativeDeviceFormat = true };
        [SerializeField] private EasyMicExperimentalSettings experimental = new EasyMicExperimentalSettings();
        [SerializeField] private EasyMicLocalizationSettings localization = new EasyMicLocalizationSettings();

        public int Version => version;
        public EasyMicRuntimeGlobalSettings Runtime => runtime;
        public EasyMicEditorToolingSettings Editor => editor;
        public EasyMicPlatformTuningSettings Windows => windows;
        public EasyMicPlatformTuningSettings MacOS => macOS;
        public EasyMicPlatformTuningSettings Linux => linux;
        public EasyMicPlatformTuningSettings Android => android;
        public EasyMicPlatformTuningSettings IOS => iOS;
        public EasyMicExperimentalSettings Experimental => experimental;
        public EasyMicLocalizationSettings Localization => localization;

        public void ResetToDefaults()
        {
            version = CurrentVersion;
            runtime = new EasyMicRuntimeGlobalSettings();
            editor = new EasyMicEditorToolingSettings();
            windows = new EasyMicPlatformTuningSettings();
            macOS = new EasyMicPlatformTuningSettings();
            linux = new EasyMicPlatformTuningSettings();
            android = new EasyMicPlatformTuningSettings { forceSafeDeviceEnumeration = true };
            iOS = new EasyMicPlatformTuningSettings { preferNativeDeviceFormat = true };
            experimental = new EasyMicExperimentalSettings();
            localization = new EasyMicLocalizationSettings();
        }

        public void Migrate()
        {
            if (version < 1)
            {
                version = CurrentVersion;
            }

            runtime ??= new EasyMicRuntimeGlobalSettings();
            editor ??= new EasyMicEditorToolingSettings();
            windows ??= new EasyMicPlatformTuningSettings();
            macOS ??= new EasyMicPlatformTuningSettings();
            linux ??= new EasyMicPlatformTuningSettings();
            android ??= new EasyMicPlatformTuningSettings { forceSafeDeviceEnumeration = true };
            iOS ??= new EasyMicPlatformTuningSettings { preferNativeDeviceFormat = true };
            experimental ??= new EasyMicExperimentalSettings();
            localization ??= new EasyMicLocalizationSettings();
        }

        private void OnValidate()
        {
            Migrate();
            runtime.customBufferFrames = Mathf.Clamp(runtime.customBufferFrames, 64, 4096);
            runtime.defaultSampleRate = Mathf.Clamp(runtime.defaultSampleRate, 8000, 192000);
            runtime.defaultChannels = Mathf.Clamp(runtime.defaultChannels, 1, 8);
            runtime.deviceRefreshIntervalSeconds = Mathf.Clamp(runtime.deviceRefreshIntervalSeconds, 0.25f, 30f);
            runtime.streamingQueueMilliseconds = Mathf.Clamp(runtime.streamingQueueMilliseconds, 20, 5000);
            editor.telemetryRefreshRate = Mathf.Clamp(editor.telemetryRefreshRate, 1f, 60f);
            editor.graphMaximumVisibleNodes = Mathf.Clamp(editor.graphMaximumVisibleNodes, 16, 2048);
            ClampPlatform(windows);
            ClampPlatform(macOS);
            ClampPlatform(linux);
            ClampPlatform(android);
            ClampPlatform(iOS);
        }

        private static void ClampPlatform(EasyMicPlatformTuningSettings platform)
        {
            if (platform == null)
            {
                return;
            }

            platform.bufferFrames = Mathf.Clamp(platform.bufferFrames, 64, 4096);
        }
    }
}
#endif
