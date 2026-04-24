#if UNITY_EDITOR
namespace Eitan.EasyMic.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    internal enum EasyMicEditorLanguage
    {
        English = 0,
        ChineseSimplified = 1
    }

    internal enum EasyMicEditorTextKey
    {
        CommonOk,
        CommonEnableLog,
        CommonOpenScript,
        CommonCopyPath,
        CommonRefreshStatus,
        CommonOpenProjectSettings,
        CommonNoAdditionalDiagnostics,
        CommonProviderScript,
        CommonGenerated,
        CommonUpdated,

        ApmPageTitle,
        ApmActivationStatusHeader,
        ApmConfiguredHintAuto,
        ApmConfiguredHintCustom,
        ApmConfiguredDetailAuto,
        ApmConfiguredDetailCustom,
        ApmUpdateActivationHeader,
        ApmActivateHeader,
        ApmUpdateActivationButton,
        ApmActivateButton,
        ApmSecurityTip,
        ApmTokenImportFromFile,
        ApmTokenManualPaste,
        ApmNoFileSelected,
        ApmChooseFile,
        ApmChooseLicenseTokenFileDialogTitle,
        ApmMissingActivationNotice,
        ApmConfiguredActivationNotice,
        ApmCustomActivationNotice,
        ApmOpenActivationScript,
        ApmHeroActivateSubtitle,
        ApmHeroUpdateSubtitle,
        ApmStatusActiveTitle,
        ApmStatusInactiveTitle,
        ApmStatusFailedTitle,
        ApmStatusUnavailableTitle,
        ApmStatusUnknownTitle,
        ApmBadgeActive,
        ApmBadgeInactive,
        ApmBadgeFailed,
        ApmBadgeUnavailable,
        ApmBadgeUnknown,
        ApmStatusActiveDetail,
        ApmStatusMissingProviderMessage,
        ApmStatusRuntimeUnavailableMessage,
        ApmStatusMissingTokenMessage,
        ApmStatusValidationFailedMessage,
        ApmDialogOpenScriptFailed,
        ApmSaveCanceled,
        ApmLicenseTokenEmpty,
        ApmDialogProviderExists,
        ApmDialogSaveProviderTitle,
        ApmDialogSaveProviderMessage,
        ApmInfoProviderUpdated,
        ApmInfoLicenseAssetSaved,
        ApmInfoTokenStoredStreamingAssets,
        ApmInfoGitIgnoreUpdated,
        ApmInfoGitIgnoreSkipped,
        ApmInfoGitIgnoreAlreadyContains,
        ApmErrorProtectedAssetNotFound,
        ApmErrorProtectedWriteFailed,
        ApmErrorReadTokenFileFailed,
        ApmErrorTokenFileEmpty,
        ApmErrorTokenFilePathEmpty,
        ApmErrorTokenFileMissing,
        ApmErrorCannotResolveProjectRoot,
        ApmProcessingAecTooltip,
        ApmProcessingAnsTooltip,
        ApmProcessingAgcTooltip,

        VoiceMenuCreate,
        VoiceMenuGameObjectName,
        VoiceCaptureHeader,
        VoiceMicrophoneOptionsLabel,
        VoiceDeviceOptionsLabel,
        VoiceAudioProcessingLabel,
        VoiceAsrHeader,
        VoiceAsrConfigLabel,
        VoiceActivePresetLabel,
        VoiceRecognitionModeLabel,
        VoiceRuntimeHeader,
        VoiceInitializedLabel,
        VoiceRecordingLabel,
        VoiceVoiceActiveLabel,
        VoiceSpeakingLabel,
        VoiceTrueLabel,
        VoiceFalseLabel,

        EasyMicMenuCreate,
        EasyMicMenuGameObjectName,
        EasyMicSectionRuntimeControls,
        EasyMicPlayModeNotice,
        EasyMicSectionStatus,
        EasyMicStatusInitialized,
        EasyMicStatusNotInitialized,
        EasyMicStatusRecording,
        EasyMicStatusIdle,
        EasyMicAvailableDevices,
        EasyMicSectionConfiguration,
        EasyMicSectionRecordingControl,
        EasyMicSectionRecordingPreview,
        EasyMicPreviewUnavailableNotice,
        EasyMicLengthLabel,
        EasyMicSampleRateLabel,
        EasyMicChannelsLabel,
        EasyMicPreviewTitle,
        EasyMicSaveRecordingTitle,
        EasyMicSaveRecordingUnavailable,
        EasyMicSaveRecordingFailed,
        EasyMicWaveformHint,
        EasyMicInitialize,
        EasyMicInitializeTooltip,
        EasyMicStart,
        EasyMicStartTooltip,
        EasyMicStop,
        EasyMicStopTooltip,
        EasyMicSave,
        EasyMicSaveTooltip,
        EasyMicPlay,
        EasyMicPause,
        EasyMicLoopEnabled,
        EasyMicLoopDisabled,
        EasyMicGoToStart,
        EasyMicGoToEnd
    }

    internal static partial class EasyMicEditorLocalization
    {
        private static readonly IReadOnlyDictionary<EasyMicEditorTextKey, string> English = new Dictionary<EasyMicEditorTextKey, string>
        {
            { EasyMicEditorTextKey.CommonOk, "OK" },
            { EasyMicEditorTextKey.CommonEnableLog, "Enable Log" },
            { EasyMicEditorTextKey.CommonOpenScript, "Open Script" },
            { EasyMicEditorTextKey.CommonCopyPath, "Copy Path" },
            { EasyMicEditorTextKey.CommonRefreshStatus, "Refresh Status" },
            { EasyMicEditorTextKey.CommonOpenProjectSettings, "Open Project Settings" },
            { EasyMicEditorTextKey.CommonNoAdditionalDiagnostics, "No additional diagnostic details were returned." },
            { EasyMicEditorTextKey.CommonProviderScript, "Provider Script" },
            { EasyMicEditorTextKey.CommonGenerated, "generated" },
            { EasyMicEditorTextKey.CommonUpdated, "updated" },

            { EasyMicEditorTextKey.ApmPageTitle, "EasyMic APM License" },
            { EasyMicEditorTextKey.ApmActivationStatusHeader, "Activation Status" },
            { EasyMicEditorTextKey.ApmConfiguredHintAuto, "Activation is already configured. You can update the activation token below without editing scripts manually." },
            { EasyMicEditorTextKey.ApmConfiguredHintCustom, "A custom activation script is managing this setup. Edit that script directly if you need to change how activation is loaded." },
            { EasyMicEditorTextKey.ApmConfiguredDetailAuto, "The current activation setup is managed automatically by EasyMic." },
            { EasyMicEditorTextKey.ApmConfiguredDetailCustom, "Recommendation: customize token loading logic and apply advanced security protections (for example, encryption or obfuscation) in that script." },
            { EasyMicEditorTextKey.ApmUpdateActivationHeader, "Update Activation" },
            { EasyMicEditorTextKey.ApmActivateHeader, "Activate EasyMic APM" },
            { EasyMicEditorTextKey.ApmUpdateActivationButton, "Update Activation" },
            { EasyMicEditorTextKey.ApmActivateButton, "Activate" },
            { EasyMicEditorTextKey.ApmSecurityTip, "Security tip: keep activation-related generated files out of version control." },
            { EasyMicEditorTextKey.ApmTokenImportFromFile, "Import From File" },
            { EasyMicEditorTextKey.ApmTokenManualPaste, "Manual Paste" },
            { EasyMicEditorTextKey.ApmNoFileSelected, "<No file selected>" },
            { EasyMicEditorTextKey.ApmChooseFile, "Choose File" },
            { EasyMicEditorTextKey.ApmChooseLicenseTokenFileDialogTitle, "Choose License Token File" },
            { EasyMicEditorTextKey.ApmMissingActivationNotice, "EasyMic APM has not been activated yet. Import a token from file or paste it manually, then activate it." },
            { EasyMicEditorTextKey.ApmConfiguredActivationNotice, "Activation is configured. You can refresh it below." },
            { EasyMicEditorTextKey.ApmCustomActivationNotice, "A custom activation script is managing this setup. Open it below if you need to edit the activation flow." },
            { EasyMicEditorTextKey.ApmOpenActivationScript, "Open Activation Script" },
            { EasyMicEditorTextKey.ApmHeroActivateSubtitle, "Unlock AEC, ANS, and AGC for production-grade voice capture." },
            { EasyMicEditorTextKey.ApmHeroUpdateSubtitle, "Refresh your local activation and keep advanced audio processing ready." },
            { EasyMicEditorTextKey.ApmStatusActiveTitle, "Plugin is activated." },
            { EasyMicEditorTextKey.ApmStatusInactiveTitle, "Plugin is not activated yet." },
            { EasyMicEditorTextKey.ApmStatusFailedTitle, "Activation failed." },
            { EasyMicEditorTextKey.ApmStatusUnavailableTitle, "Editor could not verify activation state." },
            { EasyMicEditorTextKey.ApmStatusUnknownTitle, "Unknown activation state." },
            { EasyMicEditorTextKey.ApmBadgeActive, "ACTIVE" },
            { EasyMicEditorTextKey.ApmBadgeInactive, "INACTIVE" },
            { EasyMicEditorTextKey.ApmBadgeFailed, "FAILED" },
            { EasyMicEditorTextKey.ApmBadgeUnavailable, "UNAVAILABLE" },
            { EasyMicEditorTextKey.ApmBadgeUnknown, "UNKNOWN" },
            { EasyMicEditorTextKey.ApmStatusActiveDetail, "AEC / ANS / AGC are available in the Inspector and can now be enabled on supported components." },
            { EasyMicEditorTextKey.ApmStatusMissingProviderMessage, "EasyMic APM license is not configured. Configure your token in Project Settings before enabling AEC/ANS/AGC. Avoid token leakage in source control." },
            { EasyMicEditorTextKey.ApmStatusRuntimeUnavailableMessage, "EasyMic APM runtime is unavailable in the editor, so activation could not be verified. Reimport or recompile the APM package before enabling AEC/ANS/AGC." },
            { EasyMicEditorTextKey.ApmStatusMissingTokenMessage, "EasyMic APM activation token is missing or empty. Please open the activation script and confirm a valid token is being returned." },
            { EasyMicEditorTextKey.ApmStatusValidationFailedMessage, "EasyMic APM activation failed. The current token could not be authorized, so AEC/ANS/AGC remains unavailable in the Inspector." },
            { EasyMicEditorTextKey.ApmDialogOpenScriptFailed, "Unable to open activation script. Check file existence and compile status." },
            { EasyMicEditorTextKey.ApmSaveCanceled, "Save canceled." },
            { EasyMicEditorTextKey.ApmLicenseTokenEmpty, "License token is empty." },
            { EasyMicEditorTextKey.ApmDialogProviderExists, "An activation script already exists at: {0}\n\nPlease update the token manually in that file, or customize token retrieval logic there.\nIf you want to reconfigure from this page, delete that .cs file first, then save again." },
            { EasyMicEditorTextKey.ApmDialogSaveProviderTitle, "Save EasyMic APM Activation Script" },
            { EasyMicEditorTextKey.ApmDialogSaveProviderMessage, "Choose where to save the activation script." },
            { EasyMicEditorTextKey.ApmInfoProviderUpdated, "Activation script {0} at: {1}" },
            { EasyMicEditorTextKey.ApmInfoLicenseAssetSaved, "Protected activation asset saved at: {0}" },
            { EasyMicEditorTextKey.ApmInfoTokenStoredStreamingAssets, "The token is stored as an encrypted StreamingAssets payload for runtime activation." },
            { EasyMicEditorTextKey.ApmInfoGitIgnoreUpdated, ".gitignore updated with protected activation ignore rules." },
            { EasyMicEditorTextKey.ApmInfoGitIgnoreSkipped, ".gitignore not found; skipped auto-ignore." },
            { EasyMicEditorTextKey.ApmInfoGitIgnoreAlreadyContains, ".gitignore already contains protected activation ignore rules." },
            { EasyMicEditorTextKey.ApmErrorProtectedAssetNotFound, "Protected activation asset was not found yet. Save an activation token first to create it." },
            { EasyMicEditorTextKey.ApmErrorProtectedWriteFailed, "Failed to save protected activation asset: {0}" },
            { EasyMicEditorTextKey.ApmErrorReadTokenFileFailed, "Failed to read token file: {0}" },
            { EasyMicEditorTextKey.ApmErrorTokenFileEmpty, "Token file is empty." },
            { EasyMicEditorTextKey.ApmErrorTokenFilePathEmpty, "Token file path is empty." },
            { EasyMicEditorTextKey.ApmErrorTokenFileMissing, "Token file does not exist: {0}" },
            { EasyMicEditorTextKey.ApmErrorCannotResolveProjectRoot, "Cannot resolve project root for .gitignore update: {0}" },
            { EasyMicEditorTextKey.ApmProcessingAecTooltip, "Acoustic Echo Cancellation\nRemoves speaker feedback from the microphone feed for clearer speech." },
            { EasyMicEditorTextKey.ApmProcessingAnsTooltip, "Adaptive Noise Suppression\nDynamically attenuates persistent background noise to keep the voice track clean." },
            { EasyMicEditorTextKey.ApmProcessingAgcTooltip, "Automatic Gain Control\nAutomatically evens out input gain so recordings stay consistently audible." },

            { EasyMicEditorTextKey.VoiceMenuCreate, "Create Voice Microphone" },
            { EasyMicEditorTextKey.VoiceMenuGameObjectName, "Voice Microphone" },
            { EasyMicEditorTextKey.VoiceCaptureHeader, "Capture Settings" },
            { EasyMicEditorTextKey.VoiceMicrophoneOptionsLabel, "Microphone Options" },
            { EasyMicEditorTextKey.VoiceDeviceOptionsLabel, "Device Options" },
            { EasyMicEditorTextKey.VoiceAudioProcessingLabel, "Audio Processing" },
            { EasyMicEditorTextKey.VoiceAsrHeader, "ASR Configuration" },
            { EasyMicEditorTextKey.VoiceAsrConfigLabel, "Configuration" },
            { EasyMicEditorTextKey.VoiceActivePresetLabel, "Active Preset" },
            { EasyMicEditorTextKey.VoiceRecognitionModeLabel, "Recognition Mode" },
            { EasyMicEditorTextKey.VoiceRuntimeHeader, "Runtime Status" },
            { EasyMicEditorTextKey.VoiceInitializedLabel, "Initialized" },
            { EasyMicEditorTextKey.VoiceRecordingLabel, "Recording" },
            { EasyMicEditorTextKey.VoiceVoiceActiveLabel, "Voice Active" },
            { EasyMicEditorTextKey.VoiceSpeakingLabel, "Speaking" },
            { EasyMicEditorTextKey.VoiceTrueLabel, "TRUE" },
            { EasyMicEditorTextKey.VoiceFalseLabel, "FALSE" },

            { EasyMicEditorTextKey.EasyMicMenuCreate, "Create Easy Microphone" },
            { EasyMicEditorTextKey.EasyMicMenuGameObjectName, "Easy Microphone" },
            { EasyMicEditorTextKey.EasyMicSectionRuntimeControls, "Runtime Controls" },
            { EasyMicEditorTextKey.EasyMicPlayModeNotice, "Enter Play Mode to monitor microphone status, control recording, and preview captured clips." },
            { EasyMicEditorTextKey.EasyMicSectionStatus, "Status" },
            { EasyMicEditorTextKey.EasyMicStatusInitialized, "Initialized" },
            { EasyMicEditorTextKey.EasyMicStatusNotInitialized, "Not Initialized" },
            { EasyMicEditorTextKey.EasyMicStatusRecording, "Recording" },
            { EasyMicEditorTextKey.EasyMicStatusIdle, "Idle" },
            { EasyMicEditorTextKey.EasyMicAvailableDevices, "Available Devices" },
            { EasyMicEditorTextKey.EasyMicSectionConfiguration, "Configuration" },
            { EasyMicEditorTextKey.EasyMicSectionRecordingControl, "Recording Control" },
            { EasyMicEditorTextKey.EasyMicSectionRecordingPreview, "Recording Preview" },
            { EasyMicEditorTextKey.EasyMicPreviewUnavailableNotice, "Stop recording to generate a preview clip." },
            { EasyMicEditorTextKey.EasyMicLengthLabel, "Length" },
            { EasyMicEditorTextKey.EasyMicSampleRateLabel, "Sample Rate" },
            { EasyMicEditorTextKey.EasyMicChannelsLabel, "Channels" },
            { EasyMicEditorTextKey.EasyMicPreviewTitle, "Recording Preview" },
            { EasyMicEditorTextKey.EasyMicSaveRecordingTitle, "Save Recording" },
            { EasyMicEditorTextKey.EasyMicSaveRecordingUnavailable, "No recording is available to save." },
            { EasyMicEditorTextKey.EasyMicSaveRecordingFailed, "Failed to save the recording. Check the console for details." },
            { EasyMicEditorTextKey.EasyMicWaveformHint, "Scroll to zoom · Alt+Drag to pan · Click to scrub" },
            { EasyMicEditorTextKey.EasyMicInitialize, "Initialize" },
            { EasyMicEditorTextKey.EasyMicInitializeTooltip, "Initialize the Easy Microphone component" },
            { EasyMicEditorTextKey.EasyMicStart, "Start" },
            { EasyMicEditorTextKey.EasyMicStartTooltip, "Begin recording" },
            { EasyMicEditorTextKey.EasyMicStop, "Stop" },
            { EasyMicEditorTextKey.EasyMicStopTooltip, "Stop recording" },
            { EasyMicEditorTextKey.EasyMicSave, "Save" },
            { EasyMicEditorTextKey.EasyMicSaveTooltip, "Export the last recording to disk" },
            { EasyMicEditorTextKey.EasyMicPlay, "Play" },
            { EasyMicEditorTextKey.EasyMicPause, "Pause" },
            { EasyMicEditorTextKey.EasyMicLoopEnabled, "Loop Enabled" },
            { EasyMicEditorTextKey.EasyMicLoopDisabled, "Loop Disabled" },
            { EasyMicEditorTextKey.EasyMicGoToStart, "Go To Start" },
            { EasyMicEditorTextKey.EasyMicGoToEnd, "Go To End" },
        };

        private static readonly IReadOnlyDictionary<EasyMicEditorTextKey, string> ChineseSimplified = new Dictionary<EasyMicEditorTextKey, string>
        {
            { EasyMicEditorTextKey.CommonOk, "确定" },
            { EasyMicEditorTextKey.CommonEnableLog, "启用日志" },
            { EasyMicEditorTextKey.CommonOpenScript, "打开脚本" },
            { EasyMicEditorTextKey.CommonCopyPath, "复制路径" },
            { EasyMicEditorTextKey.CommonRefreshStatus, "刷新状态" },
            { EasyMicEditorTextKey.CommonOpenProjectSettings, "打开项目设置" },
            { EasyMicEditorTextKey.CommonNoAdditionalDiagnostics, "当前没有额外的诊断信息。" },
            { EasyMicEditorTextKey.CommonProviderScript, "Provider 脚本" },
            { EasyMicEditorTextKey.CommonGenerated, "已生成" },
            { EasyMicEditorTextKey.CommonUpdated, "已更新" },

            { EasyMicEditorTextKey.ApmPageTitle, "EasyMic APM 激活" },
            { EasyMicEditorTextKey.ApmActivationStatusHeader, "激活状态" },
            { EasyMicEditorTextKey.ApmConfiguredHintAuto, "当前已完成激活配置。你可以直接在下方更新激活 Token，无需手动修改脚本。" },
            { EasyMicEditorTextKey.ApmConfiguredHintCustom, "当前使用自定义激活脚本管理配置。如需修改激活加载方式，请直接编辑该脚本。" },
            { EasyMicEditorTextKey.ApmConfiguredDetailAuto, "当前激活配置由 EasyMic 自动管理。" },
            { EasyMicEditorTextKey.ApmConfiguredDetailCustom, "建议：在该脚本中自行定制 Token 加载逻辑，并加入更高级的安全保护措施，例如加密、混淆或服务端下发。" },
            { EasyMicEditorTextKey.ApmUpdateActivationHeader, "更新激活" },
            { EasyMicEditorTextKey.ApmActivateHeader, "激活 EasyMic APM" },
            { EasyMicEditorTextKey.ApmUpdateActivationButton, "更新激活" },
            { EasyMicEditorTextKey.ApmActivateButton, "激活" },
            { EasyMicEditorTextKey.ApmSecurityTip, "安全提示：请确保与激活相关的自动生成文件不要提交到版本控制。" },
            { EasyMicEditorTextKey.ApmTokenImportFromFile, "从文件导入" },
            { EasyMicEditorTextKey.ApmTokenManualPaste, "手动粘贴" },
            { EasyMicEditorTextKey.ApmNoFileSelected, "<尚未选择文件>" },
            { EasyMicEditorTextKey.ApmChooseFile, "选择文件" },
            { EasyMicEditorTextKey.ApmChooseLicenseTokenFileDialogTitle, "选择 License Token 文件" },
            { EasyMicEditorTextKey.ApmMissingActivationNotice, "EasyMic APM 尚未激活。请从文件导入 Token 或手动粘贴，然后完成激活。" },
            { EasyMicEditorTextKey.ApmConfiguredActivationNotice, "当前已配置激活信息。你可以在下方刷新。" },
            { EasyMicEditorTextKey.ApmCustomActivationNotice, "当前由自定义激活脚本管理该配置。如需调整激活流程，请在下方打开它进行编辑。" },
            { EasyMicEditorTextKey.ApmOpenActivationScript, "打开激活脚本" },
            { EasyMicEditorTextKey.ApmHeroActivateSubtitle, "解锁 AEC、ANS、AGC，让语音采集进入可交付状态。" },
            { EasyMicEditorTextKey.ApmHeroUpdateSubtitle, "刷新本地激活配置，持续保持高级音频处理能力可用。" },
            { EasyMicEditorTextKey.ApmStatusActiveTitle, "插件已激活。" },
            { EasyMicEditorTextKey.ApmStatusInactiveTitle, "插件尚未激活。" },
            { EasyMicEditorTextKey.ApmStatusFailedTitle, "激活失败。" },
            { EasyMicEditorTextKey.ApmStatusUnavailableTitle, "编辑器当前无法验证激活状态。" },
            { EasyMicEditorTextKey.ApmStatusUnknownTitle, "未知激活状态。" },
            { EasyMicEditorTextKey.ApmBadgeActive, "已激活" },
            { EasyMicEditorTextKey.ApmBadgeInactive, "未激活" },
            { EasyMicEditorTextKey.ApmBadgeFailed, "失败" },
            { EasyMicEditorTextKey.ApmBadgeUnavailable, "不可用" },
            { EasyMicEditorTextKey.ApmBadgeUnknown, "未知" },
            { EasyMicEditorTextKey.ApmStatusActiveDetail, "AEC / ANS / AGC 已在 Inspector 中可用，现在可以在支持的组件上启用。" },
            { EasyMicEditorTextKey.ApmStatusMissingProviderMessage, "EasyMic APM 尚未配置激活信息。请先在 Project Settings 中配置 Token，然后再启用 AEC / ANS / AGC，并注意避免将 Token 泄漏到版本控制中。" },
            { EasyMicEditorTextKey.ApmStatusRuntimeUnavailableMessage, "编辑器中的 EasyMic APM 运行时当前不可用，因此无法验证激活状态。请重新导入或重新编译 APM 包后再试。" },
            { EasyMicEditorTextKey.ApmStatusMissingTokenMessage, "EasyMic APM 激活 Token 缺失或为空。请打开激活脚本，确认它返回了有效的 Token。" },
            { EasyMicEditorTextKey.ApmStatusValidationFailedMessage, "EasyMic APM 激活失败。当前 Token 未能通过授权，因此 AEC / ANS / AGC 仍无法在 Inspector 中使用。" },
            { EasyMicEditorTextKey.ApmDialogOpenScriptFailed, "无法打开激活脚本。请检查文件是否存在，以及是否存在编译错误。" },
            { EasyMicEditorTextKey.ApmSaveCanceled, "已取消保存。" },
            { EasyMicEditorTextKey.ApmLicenseTokenEmpty, "License Token 为空。" },
            { EasyMicEditorTextKey.ApmDialogProviderExists, "已存在激活脚本：{0}\n\n请直接在该文件中手动更新 Token，或在其中自定义 Token 获取逻辑。\n如果你希望从当前页面重新配置，请先删除该 .cs 文件后再保存。" },
            { EasyMicEditorTextKey.ApmDialogSaveProviderTitle, "保存 EasyMic APM 激活脚本" },
            { EasyMicEditorTextKey.ApmDialogSaveProviderMessage, "请选择激活脚本的保存位置。" },
            { EasyMicEditorTextKey.ApmInfoProviderUpdated, "激活脚本已{0}：{1}" },
            { EasyMicEditorTextKey.ApmInfoLicenseAssetSaved, "受保护的激活资源已保存到：{0}" },
            { EasyMicEditorTextKey.ApmInfoTokenStoredStreamingAssets, "Token 已作为加密后的 StreamingAssets 载荷保存，供运行时激活使用。" },
            { EasyMicEditorTextKey.ApmInfoGitIgnoreUpdated, ".gitignore 已更新受保护激活文件的忽略规则。" },
            { EasyMicEditorTextKey.ApmInfoGitIgnoreSkipped, "未找到 .gitignore，已跳过自动忽略配置。" },
            { EasyMicEditorTextKey.ApmInfoGitIgnoreAlreadyContains, ".gitignore 已包含受保护激活文件的忽略规则。" },
            { EasyMicEditorTextKey.ApmErrorProtectedAssetNotFound, "尚未找到受保护的激活资源。请先保存一次激活 Token 以创建它。" },
            { EasyMicEditorTextKey.ApmErrorProtectedWriteFailed, "保存受保护的激活资源失败：{0}" },
            { EasyMicEditorTextKey.ApmErrorReadTokenFileFailed, "读取 Token 文件失败：{0}" },
            { EasyMicEditorTextKey.ApmErrorTokenFileEmpty, "Token 文件为空。" },
            { EasyMicEditorTextKey.ApmErrorTokenFilePathEmpty, "Token 文件路径为空。" },
            { EasyMicEditorTextKey.ApmErrorTokenFileMissing, "Token 文件不存在：{0}" },
            { EasyMicEditorTextKey.ApmErrorCannotResolveProjectRoot, "无法解析项目根目录用于更新 .gitignore：{0}" },
            { EasyMicEditorTextKey.ApmProcessingAecTooltip, "声学回声消除\n移除扬声器回馈到麦克风的回声，让语音更加清晰。" },
            { EasyMicEditorTextKey.ApmProcessingAnsTooltip, "自适应噪声抑制\n动态削减持续的背景噪声，保持语音干净专注。" },
            { EasyMicEditorTextKey.ApmProcessingAgcTooltip, "自动增益控制\n自动调节输入音量，避免过大或过小的波动。" },

            { EasyMicEditorTextKey.VoiceMenuCreate, "创建 Voice Microphone" },
            { EasyMicEditorTextKey.VoiceMenuGameObjectName, "Voice Microphone" },
            { EasyMicEditorTextKey.VoiceCaptureHeader, "采集设置" },
            { EasyMicEditorTextKey.VoiceMicrophoneOptionsLabel, "麦克风选项" },
            { EasyMicEditorTextKey.VoiceDeviceOptionsLabel, "设备选项" },
            { EasyMicEditorTextKey.VoiceAudioProcessingLabel, "音频处理" },
            { EasyMicEditorTextKey.VoiceAsrHeader, "ASR 配置" },
            { EasyMicEditorTextKey.VoiceAsrConfigLabel, "配置" },
            { EasyMicEditorTextKey.VoiceActivePresetLabel, "当前预设" },
            { EasyMicEditorTextKey.VoiceRecognitionModeLabel, "识别模式" },
            { EasyMicEditorTextKey.VoiceRuntimeHeader, "运行时状态" },
            { EasyMicEditorTextKey.VoiceInitializedLabel, "已初始化" },
            { EasyMicEditorTextKey.VoiceRecordingLabel, "录音中" },
            { EasyMicEditorTextKey.VoiceVoiceActiveLabel, "语音活跃" },
            { EasyMicEditorTextKey.VoiceSpeakingLabel, "说话中" },
            { EasyMicEditorTextKey.VoiceTrueLabel, "是" },
            { EasyMicEditorTextKey.VoiceFalseLabel, "否" },

            { EasyMicEditorTextKey.EasyMicMenuCreate, "创建 Easy Microphone" },
            { EasyMicEditorTextKey.EasyMicMenuGameObjectName, "Easy Microphone" },
            { EasyMicEditorTextKey.EasyMicSectionRuntimeControls, "运行时控制" },
            { EasyMicEditorTextKey.EasyMicPlayModeNotice, "进入 Play Mode 后即可监视麦克风状态、控制录音，并预览录制结果。" },
            { EasyMicEditorTextKey.EasyMicSectionStatus, "状态" },
            { EasyMicEditorTextKey.EasyMicStatusInitialized, "已初始化" },
            { EasyMicEditorTextKey.EasyMicStatusNotInitialized, "未初始化" },
            { EasyMicEditorTextKey.EasyMicStatusRecording, "录音中" },
            { EasyMicEditorTextKey.EasyMicStatusIdle, "空闲" },
            { EasyMicEditorTextKey.EasyMicAvailableDevices, "可用设备数" },
            { EasyMicEditorTextKey.EasyMicSectionConfiguration, "配置" },
            { EasyMicEditorTextKey.EasyMicSectionRecordingControl, "录音控制" },
            { EasyMicEditorTextKey.EasyMicSectionRecordingPreview, "录音预览" },
            { EasyMicEditorTextKey.EasyMicPreviewUnavailableNotice, "请先停止录音以生成预览片段。" },
            { EasyMicEditorTextKey.EasyMicLengthLabel, "时长" },
            { EasyMicEditorTextKey.EasyMicSampleRateLabel, "采样率" },
            { EasyMicEditorTextKey.EasyMicChannelsLabel, "声道数" },
            { EasyMicEditorTextKey.EasyMicPreviewTitle, "录音预览" },
            { EasyMicEditorTextKey.EasyMicSaveRecordingTitle, "保存录音" },
            { EasyMicEditorTextKey.EasyMicSaveRecordingUnavailable, "当前没有可保存的录音。" },
            { EasyMicEditorTextKey.EasyMicSaveRecordingFailed, "保存录音失败。请查看控制台获取详细信息。" },
            { EasyMicEditorTextKey.EasyMicWaveformHint, "滚轮缩放 · Alt+拖拽平移 · 单击拖动定位" },
            { EasyMicEditorTextKey.EasyMicInitialize, "初始化" },
            { EasyMicEditorTextKey.EasyMicInitializeTooltip, "初始化 Easy Microphone 组件" },
            { EasyMicEditorTextKey.EasyMicStart, "开始" },
            { EasyMicEditorTextKey.EasyMicStartTooltip, "开始录音" },
            { EasyMicEditorTextKey.EasyMicStop, "停止" },
            { EasyMicEditorTextKey.EasyMicStopTooltip, "停止录音" },
            { EasyMicEditorTextKey.EasyMicSave, "保存" },
            { EasyMicEditorTextKey.EasyMicSaveTooltip, "将最近一次录音导出到磁盘" },
            { EasyMicEditorTextKey.EasyMicPlay, "播放" },
            { EasyMicEditorTextKey.EasyMicPause, "暂停" },
            { EasyMicEditorTextKey.EasyMicLoopEnabled, "循环已启用" },
            { EasyMicEditorTextKey.EasyMicLoopDisabled, "循环已禁用" },
            { EasyMicEditorTextKey.EasyMicGoToStart, "跳到开头" },
            { EasyMicEditorTextKey.EasyMicGoToEnd, "跳到结尾" },
        };

        public static EasyMicEditorLanguage CurrentLanguage => ResolveCurrentLanguage();
        public static bool IsChineseSimplified => CurrentLanguage == EasyMicEditorLanguage.ChineseSimplified;

        public static string Text(EasyMicEditorTextKey key)
        {
            var table = CurrentLanguage == EasyMicEditorLanguage.ChineseSimplified ? ChineseSimplified : English;
            return table.TryGetValue(key, out string value) ? value : key.ToString();
        }

        public static string Text(EasyMicEditorTextKey key, params object[] args)
        {
            string format = Text(key);
            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }

        public static GUIContent Content(EasyMicEditorTextKey key)
        {
            return new GUIContent(Text(key));
        }

        public static GUIContent Content(EasyMicEditorTextKey textKey, EasyMicEditorTextKey tooltipKey)
        {
            return new GUIContent(Text(textKey), Text(tooltipKey));
        }

        public static GUIContent Content(string text, EasyMicEditorTextKey tooltipKey)
        {
            return new GUIContent(text, Text(tooltipKey));
        }

        private static EasyMicEditorLanguage ResolveCurrentLanguage()
        {
            try
            {
                Type localizationDatabaseType = Type.GetType("UnityEditor.LocalizationDatabase, UnityEditor", throwOnError: false);
                PropertyInfo currentLanguageProperty = localizationDatabaseType?.GetProperty("currentEditorLanguage", BindingFlags.Public | BindingFlags.Static);
                object languageValue = currentLanguageProperty?.GetValue(null, null);
                if (languageValue != null && IsChineseLanguageName(languageValue.ToString()))
                {
                    return EasyMicEditorLanguage.ChineseSimplified;
                }
            }
            catch
            {
            }

            return IsChineseSystemLanguage(Application.systemLanguage)
                ? EasyMicEditorLanguage.ChineseSimplified
                : EasyMicEditorLanguage.English;
        }

        private static bool IsChineseLanguageName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsChineseSystemLanguage(SystemLanguage language)
        {
            return language == SystemLanguage.Chinese ||
                   language == SystemLanguage.ChineseSimplified ||
                   language == SystemLanguage.ChineseTraditional;
        }
    }
}
#endif
