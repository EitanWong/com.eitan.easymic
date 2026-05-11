#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Editor
{
    internal enum SherpaAsrEditorTextKey
    {
        PresetFallbackLabel,
        UnknownLabel,
        KeywordOnBadge,
        KeywordOffBadge,
        KeywordEnabledBadge,
        KeywordDisabledBadge,
        SectionBasics,
        PresetIdLabel,
        DisplayNameLabel,
        SectionRecognition,
        ModeLabel,
        StreamingModelLabel,
        OfflineModelLabel,
        VadModelLabel,
        SectionPunctuation,
        EnablePunctuationLabel,
        PunctuationModelLabel,
        SectionKeywordOptions,
        SectionTurnDetection,
        KeywordModelIdLabel,
        CustomKeywordsLabel,
        KeywordScoreLabel,
        KeywordThresholdLabel,
        ContinuousConversationLabel,
        ConversationTimeoutLabel,
        TriggerSoundLabel,
        TriggerClipLabel,
        MinSilenceLabel,
        MaxSilenceLabel,
        ActivePresetLabel,
        PresetsLabel,
        AddPresetLabel,
        DuplicatePresetLabel,
        RemovePresetLabel
    }

    internal enum SherpaInputSourceEditorTextKey
    {
        SectionCapture,
        SectionLifecycle,
        SectionBackpressure,
        SectionRuntime,
        SectionEvents,
        SectionSetupActions,
        PreferredDeviceName,
        OutputSampleRate,
        Channel,
        LatencyProfile,
        ChunkDuration,
        AutoStartOnEnable,
        StopOnDisable,
        MaxPendingChunks,
        MaxChunksPerUpdate,
        OnChunkReady,
        OnCaptureStateChanged,
        Capturing,
        Listeners,
        PendingChunks,
        DroppedChunks,
        FormatMismatches,
        LastFormatMismatch,
        NoFormatMismatch,
        PlayModeNotice,
        StartCapture,
        ForceStop,
        AddSherpaComponent,
        CreateAudioTagging,
        CreateRealtimeAsr,
        CreateKeywordSpotter,
        CreateVad,
        CreateOfflineAsrFromVad,
        DefaultDevice,
        UnnamedDevice,
        NoDevicesDetected,
        BackendUnavailable,
        StateOn,
        StateOff
    }

    internal static partial class EasyMicEditorLocalization
    {
        private static readonly IReadOnlyDictionary<SherpaAsrEditorTextKey, string> SherpaAsrEnglish =
            new Dictionary<SherpaAsrEditorTextKey, string>
            {
                { SherpaAsrEditorTextKey.PresetFallbackLabel, "ASR Preset" },
                { SherpaAsrEditorTextKey.UnknownLabel, "Unknown" },
                { SherpaAsrEditorTextKey.KeywordOnBadge, "Keyword ON" },
                { SherpaAsrEditorTextKey.KeywordOffBadge, "Keyword OFF" },
                { SherpaAsrEditorTextKey.KeywordEnabledBadge, "Enabled" },
                { SherpaAsrEditorTextKey.KeywordDisabledBadge, "Disabled" },
                { SherpaAsrEditorTextKey.SectionBasics, "Basics" },
                { SherpaAsrEditorTextKey.PresetIdLabel, "Preset ID" },
                { SherpaAsrEditorTextKey.DisplayNameLabel, "Display Name" },
                { SherpaAsrEditorTextKey.SectionRecognition, "Recognition" },
                { SherpaAsrEditorTextKey.ModeLabel, "Mode" },
                { SherpaAsrEditorTextKey.StreamingModelLabel, "Streaming Model ID" },
                { SherpaAsrEditorTextKey.OfflineModelLabel, "Offline Model ID" },
                { SherpaAsrEditorTextKey.VadModelLabel, "VAD Model ID" },
                { SherpaAsrEditorTextKey.SectionPunctuation, "Punctuation" },
                { SherpaAsrEditorTextKey.EnablePunctuationLabel, "Enable Punctuation" },
                { SherpaAsrEditorTextKey.PunctuationModelLabel, "Punctuation Model ID" },
                { SherpaAsrEditorTextKey.SectionKeywordOptions, "Keyword Options" },
                { SherpaAsrEditorTextKey.SectionTurnDetection, "Turn Detection" },
                { SherpaAsrEditorTextKey.KeywordModelIdLabel, "Model ID" },
                { SherpaAsrEditorTextKey.CustomKeywordsLabel, "Custom Keywords" },
                { SherpaAsrEditorTextKey.KeywordScoreLabel, "Score Boost" },
                { SherpaAsrEditorTextKey.KeywordThresholdLabel, "Trigger Threshold" },
                { SherpaAsrEditorTextKey.ContinuousConversationLabel, "Keep Conversation" },
                { SherpaAsrEditorTextKey.ConversationTimeoutLabel, "Timeout (s)" },
                { SherpaAsrEditorTextKey.TriggerSoundLabel, "Trigger Sound" },
                { SherpaAsrEditorTextKey.TriggerClipLabel, "Audio Clip" },
                { SherpaAsrEditorTextKey.MinSilenceLabel, "Min Silence" },
                { SherpaAsrEditorTextKey.MaxSilenceLabel, "Max Silence" },
                { SherpaAsrEditorTextKey.ActivePresetLabel, "Active Preset" },
                { SherpaAsrEditorTextKey.PresetsLabel, "Presets" },
                { SherpaAsrEditorTextKey.AddPresetLabel, "Add Preset" },
                { SherpaAsrEditorTextKey.DuplicatePresetLabel, "Duplicate Preset" },
                { SherpaAsrEditorTextKey.RemovePresetLabel, "Remove Preset" },
            };

        private static readonly IReadOnlyDictionary<SherpaAsrEditorTextKey, string> SherpaAsrChineseSimplified =
            new Dictionary<SherpaAsrEditorTextKey, string>
            {
                { SherpaAsrEditorTextKey.PresetFallbackLabel, "ASR 预设" },
                { SherpaAsrEditorTextKey.UnknownLabel, "未知" },
                { SherpaAsrEditorTextKey.KeywordOnBadge, "关键词开" },
                { SherpaAsrEditorTextKey.KeywordOffBadge, "关键词关" },
                { SherpaAsrEditorTextKey.KeywordEnabledBadge, "启用" },
                { SherpaAsrEditorTextKey.KeywordDisabledBadge, "禁用" },
                { SherpaAsrEditorTextKey.SectionBasics, "基础信息" },
                { SherpaAsrEditorTextKey.PresetIdLabel, "预设 ID" },
                { SherpaAsrEditorTextKey.DisplayNameLabel, "显示名称" },
                { SherpaAsrEditorTextKey.SectionRecognition, "识别设置" },
                { SherpaAsrEditorTextKey.ModeLabel, "识别模式" },
                { SherpaAsrEditorTextKey.StreamingModelLabel, "实时模型 ID" },
                { SherpaAsrEditorTextKey.OfflineModelLabel, "离线模型 ID" },
                { SherpaAsrEditorTextKey.VadModelLabel, "VAD 模型 ID" },
                { SherpaAsrEditorTextKey.SectionPunctuation, "标点服务" },
                { SherpaAsrEditorTextKey.EnablePunctuationLabel, "启用标点" },
                { SherpaAsrEditorTextKey.PunctuationModelLabel, "标点模型 ID" },
                { SherpaAsrEditorTextKey.SectionKeywordOptions, "关键词配置" },
                { SherpaAsrEditorTextKey.SectionTurnDetection, "轮次检测" },
                { SherpaAsrEditorTextKey.KeywordModelIdLabel, "模型 ID" },
                { SherpaAsrEditorTextKey.CustomKeywordsLabel, "自定义关键词" },
                { SherpaAsrEditorTextKey.KeywordScoreLabel, "加权得分" },
                { SherpaAsrEditorTextKey.KeywordThresholdLabel, "触发阈值" },
                { SherpaAsrEditorTextKey.ContinuousConversationLabel, "连续对话" },
                { SherpaAsrEditorTextKey.ConversationTimeoutLabel, "超时(秒)" },
                { SherpaAsrEditorTextKey.TriggerSoundLabel, "触发音效" },
                { SherpaAsrEditorTextKey.TriggerClipLabel, "音效剪辑" },
                { SherpaAsrEditorTextKey.MinSilenceLabel, "最短静音" },
                { SherpaAsrEditorTextKey.MaxSilenceLabel, "最长静音" },
                { SherpaAsrEditorTextKey.ActivePresetLabel, "当前预设" },
                { SherpaAsrEditorTextKey.PresetsLabel, "预设" },
                { SherpaAsrEditorTextKey.AddPresetLabel, "添加预设" },
                { SherpaAsrEditorTextKey.DuplicatePresetLabel, "复制预设" },
                { SherpaAsrEditorTextKey.RemovePresetLabel, "移除预设" },
            };

        public static string SherpaAsrText(SherpaAsrEditorTextKey key)
        {
            var table = IsChineseSimplified ? SherpaAsrChineseSimplified : SherpaAsrEnglish;
            return table.TryGetValue(key, out string value) ? value : key.ToString();
        }

        public static GUIContent SherpaAsrContent(SherpaAsrEditorTextKey key)
        {
            return new GUIContent(SherpaAsrText(key));
        }

        public static string SherpaAsrRecognitionModeLabel(string enumName)
        {
            if (!IsChineseSimplified)
            {
                return enumName;
            }

            switch (enumName)
            {
                case "Streaming":
                    return "实时识别";
                case "OfflineWithVad":
                    return "离线识别 + VAD";
                case "Hybrid":
                    return "混合识别";
                case "KeywordSpottingOnly":
                    return "仅关键词";
                default:
                    return enumName;
            }
        }

        public static string[] SherpaAsrRecognitionModeLabels(string[] enumNames)
        {
            if (enumNames == null)
            {
                return System.Array.Empty<string>();
            }

            var labels = new string[enumNames.Length];
            for (int i = 0; i < enumNames.Length; i++)
            {
                labels[i] = SherpaAsrRecognitionModeLabel(enumNames[i]);
            }

            return labels;
        }

        private static readonly IReadOnlyDictionary<SherpaInputSourceEditorTextKey, string> SherpaInputSourceEnglish =
            new Dictionary<SherpaInputSourceEditorTextKey, string>
            {
                { SherpaInputSourceEditorTextKey.SectionCapture, "Capture Format" },
                { SherpaInputSourceEditorTextKey.SectionLifecycle, "Lifecycle" },
                { SherpaInputSourceEditorTextKey.SectionBackpressure, "Delivery Limits" },
                { SherpaInputSourceEditorTextKey.SectionRuntime, "Runtime Diagnostics" },
                { SherpaInputSourceEditorTextKey.SectionEvents, "Events" },
                { SherpaInputSourceEditorTextKey.SectionSetupActions, "Sherpa Components" },
                { SherpaInputSourceEditorTextKey.PreferredDeviceName, "Preferred Device" },
                { SherpaInputSourceEditorTextKey.OutputSampleRate, "Output Sample Rate" },
                { SherpaInputSourceEditorTextKey.Channel, "Capture Channel" },
                { SherpaInputSourceEditorTextKey.LatencyProfile, "Latency Profile" },
                { SherpaInputSourceEditorTextKey.ChunkDuration, "Chunk Duration (s)" },
                { SherpaInputSourceEditorTextKey.AutoStartOnEnable, "Auto Start On Enable" },
                { SherpaInputSourceEditorTextKey.StopOnDisable, "Stop On Disable" },
                { SherpaInputSourceEditorTextKey.MaxPendingChunks, "Max Pending Chunks" },
                { SherpaInputSourceEditorTextKey.MaxChunksPerUpdate, "Max Chunks Per Update" },
                { SherpaInputSourceEditorTextKey.OnChunkReady, "On Chunk Ready" },
                { SherpaInputSourceEditorTextKey.OnCaptureStateChanged, "On Capture State Changed" },
                { SherpaInputSourceEditorTextKey.Capturing, "Capturing" },
                { SherpaInputSourceEditorTextKey.Listeners, "Listeners" },
                { SherpaInputSourceEditorTextKey.PendingChunks, "Pending Chunks" },
                { SherpaInputSourceEditorTextKey.DroppedChunks, "Dropped Chunks" },
                { SherpaInputSourceEditorTextKey.FormatMismatches, "Format Mismatches" },
                { SherpaInputSourceEditorTextKey.LastFormatMismatch, "Last Format Mismatch" },
                { SherpaInputSourceEditorTextKey.NoFormatMismatch, "None" },
                { SherpaInputSourceEditorTextKey.PlayModeNotice, "Enter Play Mode to start capture and inspect live queue diagnostics." },
                { SherpaInputSourceEditorTextKey.StartCapture, "Start Capture" },
                { SherpaInputSourceEditorTextKey.ForceStop, "Force Stop" },
                { SherpaInputSourceEditorTextKey.AddSherpaComponent, "Add Sherpa Component" },
                { SherpaInputSourceEditorTextKey.CreateAudioTagging, "Audio Tagging" },
                { SherpaInputSourceEditorTextKey.CreateRealtimeAsr, "Realtime ASR" },
                { SherpaInputSourceEditorTextKey.CreateKeywordSpotter, "Keyword Spotter" },
                { SherpaInputSourceEditorTextKey.CreateVad, "Voice Activity" },
                { SherpaInputSourceEditorTextKey.CreateOfflineAsrFromVad, "Offline ASR + VAD" },
                { SherpaInputSourceEditorTextKey.DefaultDevice, "Default Device" },
                { SherpaInputSourceEditorTextKey.UnnamedDevice, "Unnamed Device" },
                { SherpaInputSourceEditorTextKey.NoDevicesDetected, "No microphone devices detected." },
                { SherpaInputSourceEditorTextKey.BackendUnavailable, "EasyMic native backend is unavailable." },
                { SherpaInputSourceEditorTextKey.StateOn, "ON" },
                { SherpaInputSourceEditorTextKey.StateOff, "OFF" },
            };

        private static readonly IReadOnlyDictionary<SherpaInputSourceEditorTextKey, string> SherpaInputSourceChineseSimplified =
            new Dictionary<SherpaInputSourceEditorTextKey, string>
            {
                { SherpaInputSourceEditorTextKey.SectionCapture, "采集格式" },
                { SherpaInputSourceEditorTextKey.SectionLifecycle, "生命周期" },
                { SherpaInputSourceEditorTextKey.SectionBackpressure, "投递限制" },
                { SherpaInputSourceEditorTextKey.SectionRuntime, "运行时诊断" },
                { SherpaInputSourceEditorTextKey.SectionEvents, "事件" },
                { SherpaInputSourceEditorTextKey.SectionSetupActions, "Sherpa 组件" },
                { SherpaInputSourceEditorTextKey.PreferredDeviceName, "首选设备" },
                { SherpaInputSourceEditorTextKey.OutputSampleRate, "输出采样率" },
                { SherpaInputSourceEditorTextKey.Channel, "采集声道" },
                { SherpaInputSourceEditorTextKey.LatencyProfile, "延迟配置" },
                { SherpaInputSourceEditorTextKey.ChunkDuration, "分块时长(秒)" },
                { SherpaInputSourceEditorTextKey.AutoStartOnEnable, "启用时自动开始" },
                { SherpaInputSourceEditorTextKey.StopOnDisable, "禁用时停止" },
                { SherpaInputSourceEditorTextKey.MaxPendingChunks, "最大待投递块数" },
                { SherpaInputSourceEditorTextKey.MaxChunksPerUpdate, "每帧最大投递块数" },
                { SherpaInputSourceEditorTextKey.OnChunkReady, "音频块就绪事件" },
                { SherpaInputSourceEditorTextKey.OnCaptureStateChanged, "采集状态变更事件" },
                { SherpaInputSourceEditorTextKey.Capturing, "采集中" },
                { SherpaInputSourceEditorTextKey.Listeners, "监听者" },
                { SherpaInputSourceEditorTextKey.PendingChunks, "待投递块数" },
                { SherpaInputSourceEditorTextKey.DroppedChunks, "已丢弃块数" },
                { SherpaInputSourceEditorTextKey.FormatMismatches, "格式不匹配次数" },
                { SherpaInputSourceEditorTextKey.LastFormatMismatch, "最近格式不匹配" },
                { SherpaInputSourceEditorTextKey.NoFormatMismatch, "无" },
                { SherpaInputSourceEditorTextKey.PlayModeNotice, "进入 Play Mode 后可启动采集，并查看实时队列诊断。" },
                { SherpaInputSourceEditorTextKey.StartCapture, "开始采集" },
                { SherpaInputSourceEditorTextKey.ForceStop, "强制停止" },
                { SherpaInputSourceEditorTextKey.AddSherpaComponent, "添加 Sherpa 组件" },
                { SherpaInputSourceEditorTextKey.CreateAudioTagging, "音频标签" },
                { SherpaInputSourceEditorTextKey.CreateRealtimeAsr, "实时 ASR" },
                { SherpaInputSourceEditorTextKey.CreateKeywordSpotter, "关键词检测" },
                { SherpaInputSourceEditorTextKey.CreateVad, "语音活动" },
                { SherpaInputSourceEditorTextKey.CreateOfflineAsrFromVad, "离线 ASR + VAD" },
                { SherpaInputSourceEditorTextKey.DefaultDevice, "默认设备" },
                { SherpaInputSourceEditorTextKey.UnnamedDevice, "未命名设备" },
                { SherpaInputSourceEditorTextKey.NoDevicesDetected, "未检测到麦克风设备。" },
                { SherpaInputSourceEditorTextKey.BackendUnavailable, "EasyMic 原生后端当前不可用。" },
                { SherpaInputSourceEditorTextKey.StateOn, "开启" },
                { SherpaInputSourceEditorTextKey.StateOff, "关闭" },
            };

        public static string SherpaInputSourceText(SherpaInputSourceEditorTextKey key)
        {
            var table = IsChineseSimplified ? SherpaInputSourceChineseSimplified : SherpaInputSourceEnglish;
            return table.TryGetValue(key, out string value) ? value : key.ToString();
        }

        public static string SherpaInputSourceText(SherpaInputSourceEditorTextKey key, params object[] args)
        {
            string format = SherpaInputSourceText(key);
            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }

        public static GUIContent SherpaInputSourceContent(SherpaInputSourceEditorTextKey key)
        {
            return new GUIContent(SherpaInputSourceText(key));
        }
    }
}
#endif
