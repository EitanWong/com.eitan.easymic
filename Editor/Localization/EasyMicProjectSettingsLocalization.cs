#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;

namespace Eitan.EasyMic.Editor
{
    internal static partial class EasyMicEditorLocalization
    {
        public const string ProjectSettingsRootPath = "Project/Easy Mic";
        public const string ProjectSettingsGeneralPath = ProjectSettingsRootPath + "/General";
        public const string ProjectSettingsPlatformsPath = ProjectSettingsRootPath + "/Platforms";
        public const string ProjectSettingsDiagnosticsPath = ProjectSettingsRootPath + "/Diagnostics";
        public const string ProjectSettingsIntegrationsPath = ProjectSettingsRootPath + "/Integrations";
        public const string ProjectSettingsAdvancedPath = ProjectSettingsRootPath + "/Advanced";

        private static readonly IReadOnlyDictionary<string, string> ProjectSettingsEnglish = new Dictionary<string, string>
        {
            { "settings.title", "Easy Mic" },
            { "settings.title.general", "General" },
            { "settings.title.platforms", "Platforms" },
            { "settings.title.diagnostics", "Diagnostics" },
            { "settings.title.integrations", "Integrations" },
            { "settings.title.advanced", "Advanced" },
            { "settings.subtitle.general", "Project-wide runtime behavior and editor workflow defaults." },
            { "settings.subtitle.platforms", "Platform-specific audio backend and tuning policy." },
            { "settings.subtitle.diagnostics", "Telemetry, diagnostics, and editor visualization preferences." },
            { "settings.subtitle.integrations", "Optional package bridges and third-party integration status." },
            { "settings.subtitle.advanced", "Experimental capabilities and project preset workflow." },
            { "sherpa.title", "SherpaONNXUnity" },
            { "sherpa.subtitle", "Install SherpaONNXUnity from UPM and verify that the EasyMic bridge assemblies are available." },
            { "sherpa.section.package", "Package" },
            { "sherpa.section.bridge", "EasyMic Bridge" },
            { "sherpa.status", "Status" },
            { "sherpa.packageId", "Package ID" },
            { "sherpa.version", "Version" },
            { "sherpa.source", "Source" },
            { "sherpa.path", "Path" },
            { "sherpa.upmGitUrl", "UPM Git URL" },
            { "sherpa.install", "Install SherpaONNXUnity" },
            { "sherpa.refresh", "Refresh" },
            { "sherpa.installed", "Installed" },
            { "sherpa.notInstalled", "Not installed" },
            { "sherpa.ready", "Ready" },
            { "sherpa.inactive", "Inactive" },
            { "sherpa.available", "Available" },
            { "sherpa.notCompiled", "Not compiled" },
            { "sherpa.active", "Active" },
            { "sherpa.runtimeAssembly", "Runtime Assembly" },
            { "sherpa.editorAssembly", "Editor Assembly" },
            { "sherpa.define", "Define" },
            { "sherpa.defineSymbol", "Define Symbol" },
            { "sherpa.buildTarget", "Build Target" },
            { "sherpa.nativePlugin", "Native Plugin" },
            { "sherpa.supported", "Supported" },
            { "sherpa.checkTarget", "Check Target" },
            { "sherpa.waitCompile", "The package is installed, but the EasyMic Sherpa integration assemblies are not compiled yet. Wait for Unity to finish compilation, then press Refresh." },
            { "sherpa.installFailedTitle", "SherpaONNXUnity Install Failed" },
            { "sherpa.installedTitle", "SherpaONNXUnity Installed" },
            { "sherpa.installedMessage", "Unity Package Manager finished installing SherpaONNXUnity. Wait for Unity compilation to complete, then refresh this page." },
            { "sherpa.unknownPackageError", "Unknown Package Manager error." },
            { "sherpa.progressTitle", "EasyMic/SherpaONNXUnity install" },
            { "sherpa.progressStarting", "Package Manager request is starting." },
            { "sherpa.progressRunning", "Package Manager request is running ({0:0}s). Unity does not expose exact download progress for Client.Add." },
            { "settings.reset", "Reset Defaults" },
            { "settings.export", "Export" },
            { "settings.import", "Import" },
            { "settings.reset.confirm.title", "Reset Easy Mic Settings" },
            { "settings.reset.confirm.message", "Reset all Easy Mic project settings to package defaults?" },
            { "settings.import.title", "Import Easy Mic Settings" },
            { "settings.export.title", "Export Easy Mic Settings" },
            { "settings.import.failed", "Unable to import Easy Mic settings. Check that the selected file is a valid Easy Mic settings asset." },
            { "settings.export.failed", "Unable to export Easy Mic settings: {0}" },
            { "section.backend", "Audio Backend" },
            { "section.latency", "Latency and Buffers" },
            { "section.telemetry", "Telemetry" },
            { "section.dsp", "DSP and Resampling" },
            { "section.threading", "Threading and Fallbacks" },
            { "section.device", "Device Defaults" },
            { "section.graph", "Pipeline Graph" },
            { "section.editorDiagnostics", "Editor Diagnostics" },
            { "section.platformBackend", "Backend" },
            { "section.platformLatency", "Latency" },
            { "section.platformDsp", "DSP and Diagnostics" },
            { "section.platformNative", "Native Options" },
            { "section.platformWindows", "Windows" },
            { "section.platformMacOS", "macOS" },
            { "section.platformLinux", "Linux" },
            { "section.platformAndroid", "Android" },
            { "section.platformIOS", "iOS" },
            { "section.experimental", "Preview Capabilities" },
            { "section.presets", "Project Presets" },
            { "field.backendMode", "Backend Mode" },
            { "field.backendMode.tooltip", "Select how EasyMic chooses the realtime audio backend." },
            { "field.latencyProfile", "Latency Profile" },
            { "field.latencyProfile.tooltip", "Policy-level target used by the playback transport and backend allocation." },
            { "field.bufferStrategy", "Buffer Strategy" },
            { "field.customBufferFrames", "Custom Buffer Frames" },
            { "field.preferNativeDeviceFormat", "Prefer Native Device Format" },
            { "field.enableTelemetry", "Runtime Telemetry" },
            { "field.enableRuntimeDiagnostics", "Runtime Diagnostics" },
            { "field.enableLogging", "Logging" },
            { "field.logLevel", "Log Level" },
            { "field.enableDspLimiter", "Output Limiter" },
            { "field.enableDriftCorrection", "Drift Correction" },
            { "field.resamplerQuality", "Resampler Quality" },
            { "field.audioThreadPriority", "Audio Thread Priority" },
            { "field.runInBackground", "Run In Background" },
            { "field.autoFallback", "Automatic Fallback" },
            { "field.autoRefreshDevices", "Auto Refresh Devices" },
            { "field.deviceRefreshIntervalSeconds", "Device Refresh Interval" },
            { "field.defaultSampleRate", "Default Sample Rate" },
            { "field.defaultChannels", "Default Channels" },
            { "field.streamingQueueMilliseconds", "Streaming Queue" },
            { "field.enableEditorDiagnostics", "Editor Diagnostics" },
            { "field.enableGraphAnimations", "Graph Animations" },
            { "field.showAdvancedPipelineMetrics", "Advanced Pipeline Metrics" },
            { "field.followUnityEditorTheme", "Follow Unity Theme" },
            { "field.highContrastGraphs", "High Contrast Graphs" },
            { "field.developerMode", "Developer Mode" },
            { "field.diagnosticsMode", "Diagnostics Mode" },
            { "field.telemetryRefreshRate", "Telemetry Refresh Rate" },
            { "field.graphMaximumVisibleNodes", "Maximum Visible Nodes" },
            { "field.overrideLatencyProfile", "Override Latency Profile" },
            { "field.overrideBufferFrames", "Override Buffer Frames" },
            { "field.bufferFrames", "Buffer Frames" },
            { "field.enableDiagnostics", "Platform Diagnostics" },
            { "field.forceSafeDeviceEnumeration", "Safe Device Enumeration" },
            { "field.useAAudio", "Use AAudio" },
            { "field.useWasapiExclusiveMode", "WASAPI Exclusive Mode" },
            { "field.useCoreAudioVoiceProcessing", "CoreAudio Voice Processing" },
            { "field.enablePulseAudioFallback", "PulseAudio Fallback" },
            { "field.enableBurstDsp", "Burst DSP" },
            { "field.enableSimdKernels", "SIMD Kernels" },
            { "field.enableLowLatencyCapturePath", "Low Latency Capture Path" },
            { "field.enableAdvancedTelemetry", "Advanced Telemetry" },
            { "field.enableFutureDspGraph", "Future DSP Graph" },
            { "status.runtime.running", "AudioSystem is running. Stop playback before applying backend, latency, or device format changes." },
            { "status.runtime.stopped", "Runtime settings are applied the next time EasyMic starts." },
            { "status.platform.windows", "Windows tuning targets desktop realtime playback through miniaudio and WASAPI-compatible device paths." },
            { "status.platform.macos", "macOS tuning targets CoreAudio devices and native format negotiation." },
            { "status.platform.linux", "Linux tuning favors resilient desktop audio fallback behavior across distributions." },
            { "status.platform.android", "Android tuning keeps device enumeration on safe paths by default and can prefer AAudio on supported devices." },
            { "status.platform.ios", "iOS tuning targets CoreAudio capture/playback policy for mobile voice workflows." },
            { "validation.ok", "No validation issues detected." },
            { "validation.customBuffer", "Custom buffer strategies should stay between 64 and 4096 frames. Very small values may underrun on mobile hardware." },
            { "validation.ultraLow", "Ultra-low latency can increase CPU usage and underrun risk. Use it only after device testing." },
            { "validation.telemetryRate", "High editor refresh rates can create unnecessary editor repaint pressure." },
            { "validation.androidEnumeration", "Android device enumeration is safest on Unity/main-thread paths. Keep safe enumeration enabled unless you have tested target devices." },
            { "validation.experimental", "Experimental features are disabled by default and may not be available in the current package build." },
            { "validation.burstUnavailable", "Burst is not currently referenced by the EasyMic runtime assembly. This option is a forward-compatible project preference." },
            { "help.runtime", "These values define EasyMic's project-level runtime policy. Components may still override capture-specific options where appropriate." },
            { "help.platforms", "Platform overrides apply only when explicitly enabled for that platform." },
            { "help.advanced", "Advanced options are project-level preferences for features that may require platform testing before production use." },
        };

        private static readonly IReadOnlyDictionary<string, string> ProjectSettingsChineseSimplified = new Dictionary<string, string>
        {
            { "settings.title", "Easy Mic" },
            { "settings.title.general", "通用" },
            { "settings.title.platforms", "平台" },
            { "settings.title.diagnostics", "诊断" },
            { "settings.title.integrations", "集成" },
            { "settings.title.advanced", "高级" },
            { "settings.subtitle.general", "项目级运行时行为与编辑器工作流默认值。" },
            { "settings.subtitle.platforms", "平台特定的音频后端与调优策略。" },
            { "settings.subtitle.diagnostics", "遥测、诊断与编辑器可视化偏好。" },
            { "settings.subtitle.integrations", "可选包桥接与第三方集成状态。" },
            { "settings.subtitle.advanced", "实验能力与项目预设工作流。" },
            { "sherpa.title", "SherpaONNXUnity" },
            { "sherpa.subtitle", "从 UPM 安装 SherpaONNXUnity，并验证 EasyMic 桥接程序集是否可用。" },
            { "sherpa.section.package", "Package" },
            { "sherpa.section.bridge", "EasyMic 桥接" },
            { "sherpa.status", "状态" },
            { "sherpa.packageId", "Package ID" },
            { "sherpa.version", "版本" },
            { "sherpa.source", "来源" },
            { "sherpa.path", "路径" },
            { "sherpa.upmGitUrl", "UPM Git URL" },
            { "sherpa.install", "安装 SherpaONNXUnity" },
            { "sherpa.refresh", "刷新" },
            { "sherpa.installed", "已安装" },
            { "sherpa.notInstalled", "未安装" },
            { "sherpa.ready", "就绪" },
            { "sherpa.inactive", "未激活" },
            { "sherpa.available", "可用" },
            { "sherpa.notCompiled", "未编译" },
            { "sherpa.active", "已激活" },
            { "sherpa.runtimeAssembly", "运行时程序集" },
            { "sherpa.editorAssembly", "编辑器程序集" },
            { "sherpa.define", "Define" },
            { "sherpa.defineSymbol", "Define 符号" },
            { "sherpa.buildTarget", "构建目标" },
            { "sherpa.nativePlugin", "原生插件" },
            { "sherpa.supported", "支持" },
            { "sherpa.checkTarget", "检查目标平台" },
            { "sherpa.waitCompile", "Package 已安装，但 EasyMic Sherpa 集成程序集尚未完成编译。请等待 Unity 编译完成后点击刷新。" },
            { "sherpa.installFailedTitle", "SherpaONNXUnity 安装失败" },
            { "sherpa.installedTitle", "SherpaONNXUnity 已安装" },
            { "sherpa.installedMessage", "Unity Package Manager 已完成 SherpaONNXUnity 安装。请等待 Unity 编译完成后刷新此页面。" },
            { "sherpa.unknownPackageError", "未知 Package Manager 错误。" },
            { "sherpa.progressTitle", "EasyMic/SherpaONNXUnity 安装" },
            { "sherpa.progressStarting", "Package Manager 请求正在启动。" },
            { "sherpa.progressRunning", "Package Manager 请求正在运行（{0:0}秒）。Unity 没有为 Client.Add 暴露精确下载进度。" },
            { "settings.reset", "恢复默认" },
            { "settings.export", "导出" },
            { "settings.import", "导入" },
            { "settings.reset.confirm.title", "重置 EasyMic 设置" },
            { "settings.reset.confirm.message", "将所有 EasyMic 项目设置恢复为包默认值？" },
            { "settings.import.title", "导入 EasyMic 设置" },
            { "settings.export.title", "导出 EasyMic 设置" },
            { "settings.import.failed", "无法导入 EasyMic 设置。请确认所选文件是有效的 EasyMic 设置资源。" },
            { "settings.export.failed", "无法导出 EasyMic 设置：{0}" },
            { "section.backend", "音频后端" },
            { "section.latency", "延迟与缓冲" },
            { "section.telemetry", "遥测" },
            { "section.dsp", "DSP 与重采样" },
            { "section.threading", "线程与回退" },
            { "section.device", "设备默认值" },
            { "section.graph", "管线图" },
            { "section.editorDiagnostics", "编辑器诊断" },
            { "section.platformBackend", "后端" },
            { "section.platformLatency", "延迟" },
            { "section.platformDsp", "DSP 与诊断" },
            { "section.platformNative", "原生选项" },
            { "section.platformWindows", "Windows" },
            { "section.platformMacOS", "macOS" },
            { "section.platformLinux", "Linux" },
            { "section.platformAndroid", "Android" },
            { "section.platformIOS", "iOS" },
            { "section.experimental", "预览能力" },
            { "section.presets", "项目预设" },
            { "field.backendMode", "后端模式" },
            { "field.backendMode.tooltip", "选择 EasyMic 如何决定实时音频后端。" },
            { "field.latencyProfile", "延迟配置" },
            { "field.latencyProfile.tooltip", "播放传输与后端分配使用的策略级延迟目标。" },
            { "field.bufferStrategy", "缓冲策略" },
            { "field.customBufferFrames", "自定义缓冲帧数" },
            { "field.preferNativeDeviceFormat", "优先使用设备原生格式" },
            { "field.enableTelemetry", "运行时遥测" },
            { "field.enableRuntimeDiagnostics", "运行时诊断" },
            { "field.enableLogging", "日志" },
            { "field.logLevel", "日志级别" },
            { "field.enableDspLimiter", "输出限制器" },
            { "field.enableDriftCorrection", "漂移校正" },
            { "field.resamplerQuality", "重采样质量" },
            { "field.audioThreadPriority", "音频线程优先级" },
            { "field.runInBackground", "后台运行" },
            { "field.autoFallback", "自动回退" },
            { "field.autoRefreshDevices", "自动刷新设备" },
            { "field.deviceRefreshIntervalSeconds", "设备刷新间隔" },
            { "field.defaultSampleRate", "默认采样率" },
            { "field.defaultChannels", "默认声道数" },
            { "field.streamingQueueMilliseconds", "流式队列" },
            { "field.enableEditorDiagnostics", "编辑器诊断" },
            { "field.enableGraphAnimations", "管线图动画" },
            { "field.showAdvancedPipelineMetrics", "高级管线指标" },
            { "field.followUnityEditorTheme", "跟随 Unity 主题" },
            { "field.highContrastGraphs", "高对比度管线图" },
            { "field.developerMode", "开发者模式" },
            { "field.diagnosticsMode", "诊断模式" },
            { "field.telemetryRefreshRate", "遥测刷新率" },
            { "field.graphMaximumVisibleNodes", "最大可见节点数" },
            { "field.overrideLatencyProfile", "覆盖延迟配置" },
            { "field.overrideBufferFrames", "覆盖缓冲帧数" },
            { "field.bufferFrames", "缓冲帧数" },
            { "field.enableDiagnostics", "平台诊断" },
            { "field.forceSafeDeviceEnumeration", "安全设备枚举" },
            { "field.useAAudio", "使用 AAudio" },
            { "field.useWasapiExclusiveMode", "WASAPI 独占模式" },
            { "field.useCoreAudioVoiceProcessing", "CoreAudio 语音处理" },
            { "field.enablePulseAudioFallback", "PulseAudio 回退" },
            { "field.enableBurstDsp", "Burst DSP" },
            { "field.enableSimdKernels", "SIMD 内核" },
            { "field.enableLowLatencyCapturePath", "低延迟采集路径" },
            { "field.enableAdvancedTelemetry", "高级遥测" },
            { "field.enableFutureDspGraph", "未来 DSP 图" },
            { "status.runtime.running", "AudioSystem 正在运行。请先停止播放，再应用后端、延迟或设备格式变更。" },
            { "status.runtime.stopped", "运行时设置会在 EasyMic 下次启动时生效。" },
            { "status.platform.windows", "Windows 调优面向通过 miniaudio 与 WASAPI 兼容设备路径进行的桌面实时播放。" },
            { "status.platform.macos", "macOS 调优面向 CoreAudio 设备与原生格式协商。" },
            { "status.platform.linux", "Linux 调优优先保证跨发行版桌面音频回退行为的稳定性。" },
            { "status.platform.android", "Android 调优默认保持安全设备枚举，并可在支持设备上优先使用 AAudio。" },
            { "status.platform.ios", "iOS 调优面向移动语音工作流中的 CoreAudio 采集/播放策略。" },
            { "validation.ok", "未检测到验证问题。" },
            { "validation.customBuffer", "自定义缓冲策略建议保持在 64 到 4096 帧之间。过小的值可能在移动硬件上欠载。" },
            { "validation.ultraLow", "超低延迟会增加 CPU 使用率和欠载风险。请仅在完成设备测试后使用。" },
            { "validation.telemetryRate", "过高的编辑器刷新率会造成不必要的编辑器重绘压力。" },
            { "validation.androidEnumeration", "Android 设备枚举最好保持在 Unity/主线程路径上。除非已充分测试目标设备，否则建议启用安全枚举。" },
            { "validation.experimental", "实验功能默认关闭，且当前包构建中可能不可用。" },
            { "validation.burstUnavailable", "当前 EasyMic 运行时程序集尚未引用 Burst。该选项是面向未来的项目偏好。" },
            { "help.runtime", "这些值定义 EasyMic 的项目级运行时策略。必要时，组件仍可覆盖采集相关选项。" },
            { "help.platforms", "平台覆盖仅在对应平台显式启用时生效。" },
            { "help.advanced", "高级选项是项目级偏好，部分功能在生产使用前需要完成平台测试。" },
        };

        public static event Action ProjectSettingsLanguageChanged;

        private static EasyMicEditorLanguage s_lastProjectSettingsLanguage = CurrentLanguage;

        static EasyMicEditorLocalization()
        {
            EditorApplication.update += PollProjectSettingsLanguage;
        }

        public static string ProjectSettingsText(string key)
        {
            var table = CurrentLanguage == EasyMicEditorLanguage.ChineseSimplified
                ? ProjectSettingsChineseSimplified
                : ProjectSettingsEnglish;
            if (table.TryGetValue(key, out string value))
            {
                return value;
            }

            if (ProjectSettingsEnglish.TryGetValue(key, out string fallback))
            {
                return fallback;
            }

            return key;
        }

        public static string ProjectSettingsText(string key, params object[] args)
        {
            string format = ProjectSettingsText(key);
            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }

        private static void PollProjectSettingsLanguage()
        {
            var current = CurrentLanguage;
            if (current == s_lastProjectSettingsLanguage)
            {
                return;
            }

            s_lastProjectSettingsLanguage = current;
            ProjectSettingsLanguageChanged?.Invoke();
        }
    }
}
#endif
