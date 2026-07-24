#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public enum AIChatSetupLanguage
    {
        English = 0,
        ChineseSimplified = 1
    }

    public enum AIChatSetupTextKey
    {
        SetupWindowTitle,
        ProviderWindowTitle,
        SetupHeader,
        SetupDescription,
        SampleScene,
        VoiceDependency,
        Ready,
        NeedsAttention,
        SceneNotFoundDetail,
        DependencyReadyDetail,
        DependencyMissingDetail,
        DependencyMissingHelp,
        OpenPackageManager,
        CopyGitUrl,
        CopyManifestEntry,
        DependencyReadyHelp,
        OpenProviderSetup,
        ProviderSetupUnavailableTitle,
        ProviderSetupUnavailableMessage,
        Ok,
        OpenSampleScene,
        OpenReadme,
        Refresh,
        SampleSceneMissingTitle,
        SampleSceneMissingMessage,
        ReadmeMissingTitle,
        ReadmeMissingMessage,
        ProviderHeader,
        ProviderDescription,
        ControllerMissingHelp,
        TargetController,
        UseCurrentSelection,
        FindSceneController,
        RevealRuntimeConfig,
        Provider,
        SiliconFlow,
        OpenAICompatibleCustom,
        ApplyPresetDefaults,
        ApiBaseUrl,
        LlmModel,
        UseLocalTts,
        LocalTtsHelp,
        RemoteTtsModel,
        RemoteTtsVoice,
        StreamRemoteTts,
        ApiKey,
        ApiKeyStorageHelp,
        ClearSavedApiKey,
        ProviderReady,
        ProviderReadyKeyMissing,
        PlayModeEditHelp,
        ApplyToScene,
        ApplyAndSave,
        MissingControllerConfig,
        ProviderApplied,
        RuntimeSaveFailed,
        RuntimeSaved,
        ClearApiKeyTitle,
        ClearApiKeyMessage,
        Clear,
        Cancel,
        ApiKeyCleared,
        ApiKeyClearFailed,
        RuntimeConfigNotCreated,
        ValidationNoController,
        ValidationInvalidBaseUrl,
        ValidationMissingLlmModel,
        ValidationMissingLocalSynthesizer,
        ValidationMissingRemoteTtsModel,
        ValidationMissingRemoteTtsVoice,
        ValidationMissingMicrophone,
        ValidationMissingApiKey,
        Latest,
        CurrentContract,
        CurrentContractDetail,
        StepImport,
        StepDependency,
        StepProvider,
        ProviderConfiguredDetail,
        ProviderPendingDetail,
        UnsavedChanges,
        TargetSection,
        ProviderSection,
        VoiceSection,
        CredentialsSection,
        ValidationSection,
        RemoteTts,
        LocalTts,
        DeviceKeyStored,
        DeviceKeyMissing,
        DiscardChangesTitle,
        DiscardChangesMessage,
        DiscardChanges,
        KeepEditing,
        PolicyHeader,
        PolicyDescription,
        EnablePolicy,
        Preset,
        Active,
        Inactive,
        Overrides,
        ChatSection,
        SpeechSection,
        ExperienceSection,
        AsrSection,
        LocalTtsSection,
        LlmTemperature,
        MaxHistoryTurns,
        EnableTtsDiagnostics,
        InterruptAssistant,
        MicStartupDelay,
        AsrTurnDelay,
        AsrRecognitionMode,
        AsrStreamingModel,
        AsrOfflineModel,
        AsrVadModel,
        EnablePunctuation,
        PunctuationModel,
        LocalTtsModel,
        LocalTtsVoiceId,
        LocalTtsSpeed,
        LocalTtsSampleRate,
        ClearOverrides,
        ScenePolicyPreview,
        DeviceConfigAfterPolicy,
        Custom,
        OpenAI,
        LocalOnly,
        FullDocumentation,
        ConfigureProviderUndo,
        ConfigureProviderPolicyUndo,
        ClearPolicyOverridesUndo
    }

    public static class AIChatSetupLocalization
    {
        private const string UnityEditorLocalePrefKey = "Editor.kEditorLocale";

        private static readonly IReadOnlyDictionary<AIChatSetupTextKey, string> English =
            new Dictionary<AIChatSetupTextKey, string>
            {
                { AIChatSetupTextKey.SetupWindowTitle, "AI Chat Setup" },
                { AIChatSetupTextKey.ProviderWindowTitle, "AI Chat Provider" },
                { AIChatSetupTextKey.SetupHeader, "EasyMic AI Chat Setup" },
                { AIChatSetupTextKey.SetupDescription, "Import check, speech dependency, sample scene, documentation, and Provider configuration." },
                { AIChatSetupTextKey.SampleScene, "Sample Scene" },
                { AIChatSetupTextKey.VoiceDependency, "Speech Dependency" },
                { AIChatSetupTextKey.Ready, "Ready" },
                { AIChatSetupTextKey.NeedsAttention, "Needs Attention" },
                { AIChatSetupTextKey.SceneNotFoundDetail, "Samantha.unity was not found under this imported sample." },
                { AIChatSetupTextKey.DependencyReadyDetail, "com.eitan.sherpa-onnx-unity is installed." },
                { AIChatSetupTextKey.DependencyMissingDetail, "com.eitan.sherpa-onnx-unity must be installed." },
                { AIChatSetupTextKey.DependencyMissingHelp, "Install the speech dependency to compile ASR, local TTS, and Provider Setup." },
                { AIChatSetupTextKey.OpenPackageManager, "Open Package Manager" },
                { AIChatSetupTextKey.CopyGitUrl, "Copy Git URL" },
                { AIChatSetupTextKey.CopyManifestEntry, "Copy manifest entry" },
                { AIChatSetupTextKey.DependencyReadyHelp, "The dependency is ready. Configure SiliconFlow or another OpenAI-compatible Provider next." },
                { AIChatSetupTextKey.OpenProviderSetup, "Open Provider Setup" },
                { AIChatSetupTextKey.ProviderSetupUnavailableTitle, "Provider Setup Is Not Ready" },
                { AIChatSetupTextKey.ProviderSetupUnavailableMessage, "Unity may still be compiling scripts. Wait for compilation to finish, then reopen this window." },
                { AIChatSetupTextKey.Ok, "OK" },
                { AIChatSetupTextKey.OpenSampleScene, "Open Sample Scene" },
                { AIChatSetupTextKey.OpenReadme, "Open README" },
                { AIChatSetupTextKey.Refresh, "Refresh" },
                { AIChatSetupTextKey.SampleSceneMissingTitle, "Sample Scene Not Found" },
                { AIChatSetupTextKey.SampleSceneMissingMessage, "Import AIChat Example into Assets, then refresh this window." },
                { AIChatSetupTextKey.ReadmeMissingTitle, "README Not Found" },
                { AIChatSetupTextKey.ReadmeMissingMessage, "Reimport AIChat Example from Package Manager, then try again." },
                { AIChatSetupTextKey.ProviderHeader, "AI Chat Provider Setup" },
                { AIChatSetupTextKey.ProviderDescription, "Writes Provider defaults to the scene Policy and saves final device settings to schema-v4 runtime JSON. The API key stays on this device." },
                { AIChatSetupTextKey.ControllerMissingHelp, "Open Samantha.unity or assign an AIChatController below." },
                { AIChatSetupTextKey.TargetController, "Target Controller" },
                { AIChatSetupTextKey.UseCurrentSelection, "Use Selection" },
                { AIChatSetupTextKey.FindSceneController, "Find in Scene" },
                { AIChatSetupTextKey.RevealRuntimeConfig, "Reveal Runtime Config" },
                { AIChatSetupTextKey.Provider, "Provider" },
                { AIChatSetupTextKey.SiliconFlow, "SiliconFlow" },
                { AIChatSetupTextKey.OpenAICompatibleCustom, "OpenAI Compatible / Custom" },
                { AIChatSetupTextKey.ApplyPresetDefaults, "Apply Preset Defaults" },
                { AIChatSetupTextKey.ApiBaseUrl, "API Base URL" },
                { AIChatSetupTextKey.LlmModel, "LLM Model" },
                { AIChatSetupTextKey.UseLocalTts, "Use Local TTS" },
                { AIChatSetupTextKey.LocalTtsHelp, "Local TTS replaces speech output only. The LLM still uses this Provider's base URL and API key." },
                { AIChatSetupTextKey.RemoteTtsModel, "Remote TTS Model" },
                { AIChatSetupTextKey.RemoteTtsVoice, "Remote TTS Voice" },
                { AIChatSetupTextKey.StreamRemoteTts, "Stream Remote TTS" },
                { AIChatSetupTextKey.ApiKey, "API Key" },
                { AIChatSetupTextKey.ApiKeyStorageHelp, "Saved as plain text on this device at:\n{0}\nIt is never serialized into the scene or Configuration Policy by this tool." },
                { AIChatSetupTextKey.ClearSavedApiKey, "Clear Saved API Key" },
                { AIChatSetupTextKey.ProviderReady, "Provider settings are ready to apply. Other advanced policy overrides will be preserved." },
                { AIChatSetupTextKey.ProviderReadyKeyMissing, "Provider settings can be applied to the scene. Enter an API key before saving the runnable device configuration." },
                { AIChatSetupTextKey.PlayModeEditHelp, "Exit Play Mode before changing scene Provider settings." },
                { AIChatSetupTextKey.ApplyToScene, "Apply to Scene" },
                { AIChatSetupTextKey.ApplyAndSave, "Apply Scene + Save Device Config" },
                { AIChatSetupTextKey.MissingControllerConfig, "The AIChatController _config serialized field was not found." },
                { AIChatSetupTextKey.ProviderApplied, "Provider settings were applied. Save the scene with Cmd/Ctrl+S." },
                { AIChatSetupTextKey.RuntimeSaveFailed, "Failed to save the device runtime configuration: {0}" },
                { AIChatSetupTextKey.RuntimeSaved, "Device runtime configuration saved: {0}" },
                { AIChatSetupTextKey.ClearApiKeyTitle, "Clear API Key" },
                { AIChatSetupTextKey.ClearApiKeyMessage, "Remove the API key from this device's runtime configuration?" },
                { AIChatSetupTextKey.Clear, "Clear" },
                { AIChatSetupTextKey.Cancel, "Cancel" },
                { AIChatSetupTextKey.ApiKeyCleared, "The saved API key was cleared." },
                { AIChatSetupTextKey.ApiKeyClearFailed, "Failed to clear the API key: {0}" },
                { AIChatSetupTextKey.RuntimeConfigNotCreated, "The runtime configuration does not exist yet. Use Apply Scene + Save Device Config to create it." },
                { AIChatSetupTextKey.ValidationNoController, "Select an AIChatController." },
                { AIChatSetupTextKey.ValidationInvalidBaseUrl, "Use an HTTPS API Base URL without embedded credentials, query parameters, or fragments. HTTP is allowed only for localhost development." },
                { AIChatSetupTextKey.ValidationMissingLlmModel, "Enter an LLM model." },
                { AIChatSetupTextKey.ValidationMissingLocalSynthesizer, "Local TTS requires a SpeechSynthesizer component." },
                { AIChatSetupTextKey.ValidationMissingRemoteTtsModel, "Enter a remote TTS model." },
                { AIChatSetupTextKey.ValidationMissingRemoteTtsVoice, "Enter a remote TTS voice." },
                { AIChatSetupTextKey.ValidationMissingMicrophone, "No VoiceMicrophone was found on the target, its children, or its parents." },
                { AIChatSetupTextKey.ValidationMissingApiKey, "Enter an API key before saving the device configuration." },
                { AIChatSetupTextKey.Latest, "CURRENT" },
                { AIChatSetupTextKey.CurrentContract, "Current configuration contract" },
                { AIChatSetupTextKey.CurrentContractDetail, "Schema v{0} · OpenAI Responses API · Provider-specific Chat Completions" },
                { AIChatSetupTextKey.StepImport, "Imported Sample" },
                { AIChatSetupTextKey.StepDependency, "Speech Runtime" },
                { AIChatSetupTextKey.StepProvider, "Provider Configuration" },
                { AIChatSetupTextKey.ProviderConfiguredDetail, "A current device configuration with Provider values and an API key is saved." },
                { AIChatSetupTextKey.ProviderPendingDetail, "Open Provider Setup, validate the fields, then save the device configuration." },
                { AIChatSetupTextKey.UnsavedChanges, "The form has unsaved changes. Use Apply Scene + Save Device Config." },
                { AIChatSetupTextKey.TargetSection, "Target" },
                { AIChatSetupTextKey.ProviderSection, "Provider and Model" },
                { AIChatSetupTextKey.VoiceSection, "Speech Output" },
                { AIChatSetupTextKey.CredentialsSection, "Device Credential" },
                { AIChatSetupTextKey.ValidationSection, "Validation" },
                { AIChatSetupTextKey.RemoteTts, "Remote TTS" },
                { AIChatSetupTextKey.LocalTts, "Local TTS" },
                { AIChatSetupTextKey.DeviceKeyStored, "A device API key is stored." },
                { AIChatSetupTextKey.DeviceKeyMissing, "No device API key is stored." },
                { AIChatSetupTextKey.DiscardChangesTitle, "Discard Provider Changes?" },
                { AIChatSetupTextKey.DiscardChangesMessage, "Switching the target Controller reloads its Provider values and discards the current form changes." },
                { AIChatSetupTextKey.DiscardChanges, "Discard Changes" },
                { AIChatSetupTextKey.KeepEditing, "Keep Editing" },
                { AIChatSetupTextKey.PolicyHeader, "AI Chat Advanced Policy" },
                { AIChatSetupTextKey.PolicyDescription, "Advanced scene overrides. Use Provider Setup for the normal setup flow." },
                { AIChatSetupTextKey.EnablePolicy, "Enable Policy" },
                { AIChatSetupTextKey.Preset, "Preset" },
                { AIChatSetupTextKey.Active, "Active" },
                { AIChatSetupTextKey.Inactive, "Inactive" },
                { AIChatSetupTextKey.Overrides, "Overrides" },
                { AIChatSetupTextKey.ChatSection, "Chat and Model" },
                { AIChatSetupTextKey.SpeechSection, "Remote Speech" },
                { AIChatSetupTextKey.ExperienceSection, "Conversation Experience" },
                { AIChatSetupTextKey.AsrSection, "Speech Recognition" },
                { AIChatSetupTextKey.LocalTtsSection, "Local Speech Synthesis" },
                { AIChatSetupTextKey.LlmTemperature, "LLM Temperature" },
                { AIChatSetupTextKey.MaxHistoryTurns, "Max History Turns" },
                { AIChatSetupTextKey.EnableTtsDiagnostics, "Enable TTS Diagnostics" },
                { AIChatSetupTextKey.InterruptAssistant, "Interrupt Assistant on User Speech" },
                { AIChatSetupTextKey.MicStartupDelay, "Microphone Startup Delay" },
                { AIChatSetupTextKey.AsrTurnDelay, "Turn Detection Delay" },
                { AIChatSetupTextKey.AsrRecognitionMode, "Recognition Mode" },
                { AIChatSetupTextKey.AsrStreamingModel, "Streaming ASR Model" },
                { AIChatSetupTextKey.AsrOfflineModel, "Offline ASR Model" },
                { AIChatSetupTextKey.AsrVadModel, "VAD Model" },
                { AIChatSetupTextKey.EnablePunctuation, "Enable Punctuation" },
                { AIChatSetupTextKey.PunctuationModel, "Punctuation Model" },
                { AIChatSetupTextKey.LocalTtsModel, "Local TTS Model" },
                { AIChatSetupTextKey.LocalTtsVoiceId, "Local Voice ID" },
                { AIChatSetupTextKey.LocalTtsSpeed, "Local TTS Speed" },
                { AIChatSetupTextKey.LocalTtsSampleRate, "Local TTS Sample Rate" },
                { AIChatSetupTextKey.ClearOverrides, "Clear All Overrides" },
                { AIChatSetupTextKey.ScenePolicyPreview, "Scene Policy Preview" },
                { AIChatSetupTextKey.DeviceConfigAfterPolicy, "Current-schema device settings are applied after this scene Policy at startup." },
                { AIChatSetupTextKey.Custom, "Custom" },
                { AIChatSetupTextKey.OpenAI, "OpenAI" },
                { AIChatSetupTextKey.LocalOnly, "Local Only" },
                { AIChatSetupTextKey.FullDocumentation, "Full Documentation" },
                { AIChatSetupTextKey.ConfigureProviderUndo, "Configure AI Chat Provider" },
                { AIChatSetupTextKey.ConfigureProviderPolicyUndo, "Configure AI Chat Provider Policy" },
                { AIChatSetupTextKey.ClearPolicyOverridesUndo, "Clear AI Chat Policy Overrides" }
            };

        private static readonly IReadOnlyDictionary<AIChatSetupTextKey, string> ChineseSimplified =
            new Dictionary<AIChatSetupTextKey, string>
            {
                { AIChatSetupTextKey.SetupWindowTitle, "AI 对话设置" },
                { AIChatSetupTextKey.ProviderWindowTitle, "AI 对话 Provider" },
                { AIChatSetupTextKey.SetupHeader, "EasyMic AI 语音对话设置" },
                { AIChatSetupTextKey.SetupDescription, "用于检查导入状态、语音依赖、案例场景、文档和 Provider 配置。" },
                { AIChatSetupTextKey.SampleScene, "案例场景" },
                { AIChatSetupTextKey.VoiceDependency, "语音依赖" },
                { AIChatSetupTextKey.Ready, "已就绪" },
                { AIChatSetupTextKey.NeedsAttention, "需要处理" },
                { AIChatSetupTextKey.SceneNotFoundDetail, "未在当前导入的案例目录中找到 Samantha.unity。" },
                { AIChatSetupTextKey.DependencyReadyDetail, "已安装 com.eitan.sherpa-onnx-unity。" },
                { AIChatSetupTextKey.DependencyMissingDetail, "需要安装 com.eitan.sherpa-onnx-unity。" },
                { AIChatSetupTextKey.DependencyMissingHelp, "安装语音依赖后，ASR、本地 TTS 和 Provider 设置工具才会编译。" },
                { AIChatSetupTextKey.OpenPackageManager, "打开 Package Manager" },
                { AIChatSetupTextKey.CopyGitUrl, "复制 Git URL" },
                { AIChatSetupTextKey.CopyManifestEntry, "复制 manifest 条目" },
                { AIChatSetupTextKey.DependencyReadyHelp, "依赖已就绪。下一步可配置 SiliconFlow 或其他兼容 OpenAI API 的 Provider。" },
                { AIChatSetupTextKey.OpenProviderSetup, "打开 Provider 设置" },
                { AIChatSetupTextKey.ProviderSetupUnavailableTitle, "Provider 设置尚未就绪" },
                { AIChatSetupTextKey.ProviderSetupUnavailableMessage, "Unity 可能仍在编译脚本。请等待编译完成后重新打开此窗口。" },
                { AIChatSetupTextKey.Ok, "确定" },
                { AIChatSetupTextKey.OpenSampleScene, "打开案例场景" },
                { AIChatSetupTextKey.OpenReadme, "打开 README" },
                { AIChatSetupTextKey.Refresh, "重新检查" },
                { AIChatSetupTextKey.SampleSceneMissingTitle, "未找到案例场景" },
                { AIChatSetupTextKey.SampleSceneMissingMessage, "请先将 AIChat Example 导入 Assets，然后刷新此窗口。" },
                { AIChatSetupTextKey.ReadmeMissingTitle, "未找到 README" },
                { AIChatSetupTextKey.ReadmeMissingMessage, "请从 Package Manager 重新导入 AIChat Example 后重试。" },
                { AIChatSetupTextKey.ProviderHeader, "AI 对话 Provider 设置" },
                { AIChatSetupTextKey.ProviderDescription, "将 Provider 默认值写入场景 Policy，并把最终设备设置保存到 schema v4 运行时 JSON；API Key 只保留在当前设备。" },
                { AIChatSetupTextKey.ControllerMissingHelp, "请打开 Samantha.unity，或在下方指定一个 AIChatController。" },
                { AIChatSetupTextKey.TargetController, "目标 Controller" },
                { AIChatSetupTextKey.UseCurrentSelection, "使用当前选择" },
                { AIChatSetupTextKey.FindSceneController, "查找场景对象" },
                { AIChatSetupTextKey.RevealRuntimeConfig, "显示运行时配置" },
                { AIChatSetupTextKey.Provider, "Provider" },
                { AIChatSetupTextKey.SiliconFlow, "SiliconFlow" },
                { AIChatSetupTextKey.OpenAICompatibleCustom, "OpenAI 兼容 / 自定义" },
                { AIChatSetupTextKey.ApplyPresetDefaults, "应用预设默认值" },
                { AIChatSetupTextKey.ApiBaseUrl, "API Base URL" },
                { AIChatSetupTextKey.LlmModel, "LLM 模型" },
                { AIChatSetupTextKey.UseLocalTts, "使用本地 TTS" },
                { AIChatSetupTextKey.LocalTtsHelp, "本地 TTS 只替换语音输出；LLM 仍使用当前 Provider 的 Base URL 和 API Key。" },
                { AIChatSetupTextKey.RemoteTtsModel, "远程 TTS 模型" },
                { AIChatSetupTextKey.RemoteTtsVoice, "远程 TTS 音色" },
                { AIChatSetupTextKey.StreamRemoteTts, "流式播放远程 TTS" },
                { AIChatSetupTextKey.ApiKey, "API Key" },
                { AIChatSetupTextKey.ApiKeyStorageHelp, "以明文保存在当前设备：\n{0}\n本工具不会把它序列化到场景或 Configuration Policy。" },
                { AIChatSetupTextKey.ClearSavedApiKey, "清除已保存的 API Key" },
                { AIChatSetupTextKey.ProviderReady, "Provider 设置可以写入；Policy 中其他高级覆盖会保留。" },
                { AIChatSetupTextKey.ProviderReadyKeyMissing, "Provider 设置可以应用到场景。保存可运行的设备配置前，请先填写 API Key。" },
                { AIChatSetupTextKey.PlayModeEditHelp, "请退出 Play Mode 后再修改场景 Provider 设置。" },
                { AIChatSetupTextKey.ApplyToScene, "应用到场景" },
                { AIChatSetupTextKey.ApplyAndSave, "应用场景并保存设备配置" },
                { AIChatSetupTextKey.MissingControllerConfig, "未找到 AIChatController 的 _config 序列化字段。" },
                { AIChatSetupTextKey.ProviderApplied, "Provider 设置已应用。请使用 Cmd/Ctrl+S 保存场景。" },
                { AIChatSetupTextKey.RuntimeSaveFailed, "保存设备运行时配置失败：{0}" },
                { AIChatSetupTextKey.RuntimeSaved, "设备运行时配置已保存：{0}" },
                { AIChatSetupTextKey.ClearApiKeyTitle, "清除 API Key" },
                { AIChatSetupTextKey.ClearApiKeyMessage, "是否从当前设备的运行时配置中删除 API Key？" },
                { AIChatSetupTextKey.Clear, "清除" },
                { AIChatSetupTextKey.Cancel, "取消" },
                { AIChatSetupTextKey.ApiKeyCleared, "已清除保存的 API Key。" },
                { AIChatSetupTextKey.ApiKeyClearFailed, "清除 API Key 失败：{0}" },
                { AIChatSetupTextKey.RuntimeConfigNotCreated, "运行时配置尚未创建。点击“应用场景并保存设备配置”即可创建。" },
                { AIChatSetupTextKey.ValidationNoController, "请选择 AIChatController。" },
                { AIChatSetupTextKey.ValidationInvalidBaseUrl, "请使用不含内嵌凭证、查询参数或片段的 HTTPS API Base URL；只有 localhost 开发地址可以使用 HTTP。" },
                { AIChatSetupTextKey.ValidationMissingLlmModel, "请填写 LLM 模型。" },
                { AIChatSetupTextKey.ValidationMissingLocalSynthesizer, "本地 TTS 需要 SpeechSynthesizer 组件。" },
                { AIChatSetupTextKey.ValidationMissingRemoteTtsModel, "请填写远程 TTS 模型。" },
                { AIChatSetupTextKey.ValidationMissingRemoteTtsVoice, "请填写远程 TTS 音色。" },
                { AIChatSetupTextKey.ValidationMissingMicrophone, "目标对象、子对象或父对象中未找到 VoiceMicrophone。" },
                { AIChatSetupTextKey.ValidationMissingApiKey, "保存设备配置前请填写 API Key。" },
                { AIChatSetupTextKey.Latest, "当前版本" },
                { AIChatSetupTextKey.CurrentContract, "当前配置契约" },
                { AIChatSetupTextKey.CurrentContractDetail, "Schema v{0} · OpenAI Responses API · Provider 专用 Chat Completions" },
                { AIChatSetupTextKey.StepImport, "案例已导入" },
                { AIChatSetupTextKey.StepDependency, "语音运行时" },
                { AIChatSetupTextKey.StepProvider, "Provider 配置" },
                { AIChatSetupTextKey.ProviderConfiguredDetail, "当前设备已保存包含 Provider 参数和 API Key 的最新配置。" },
                { AIChatSetupTextKey.ProviderPendingDetail, "打开 Provider 设置，完成校验后保存设备配置。" },
                { AIChatSetupTextKey.UnsavedChanges, "表单存在尚未保存的修改，请点击“应用场景并保存设备配置”。" },
                { AIChatSetupTextKey.TargetSection, "目标对象" },
                { AIChatSetupTextKey.ProviderSection, "Provider 与模型" },
                { AIChatSetupTextKey.VoiceSection, "语音输出" },
                { AIChatSetupTextKey.CredentialsSection, "设备凭证" },
                { AIChatSetupTextKey.ValidationSection, "配置校验" },
                { AIChatSetupTextKey.RemoteTts, "远程 TTS" },
                { AIChatSetupTextKey.LocalTts, "本地 TTS" },
                { AIChatSetupTextKey.DeviceKeyStored, "当前设备已保存 API Key。" },
                { AIChatSetupTextKey.DeviceKeyMissing, "当前设备尚未保存 API Key。" },
                { AIChatSetupTextKey.DiscardChangesTitle, "放弃 Provider 修改？" },
                { AIChatSetupTextKey.DiscardChangesMessage, "切换目标 Controller 会重新载入它的 Provider 参数，并丢弃当前表单修改。" },
                { AIChatSetupTextKey.DiscardChanges, "放弃修改" },
                { AIChatSetupTextKey.KeepEditing, "继续编辑" },
                { AIChatSetupTextKey.PolicyHeader, "AI 对话高级 Policy" },
                { AIChatSetupTextKey.PolicyDescription, "用于场景级高级覆盖；常规配置请使用 Provider 设置工具。" },
                { AIChatSetupTextKey.EnablePolicy, "启用 Policy" },
                { AIChatSetupTextKey.Preset, "预设" },
                { AIChatSetupTextKey.Active, "已启用" },
                { AIChatSetupTextKey.Inactive, "未启用" },
                { AIChatSetupTextKey.Overrides, "覆盖项" },
                { AIChatSetupTextKey.ChatSection, "对话与模型" },
                { AIChatSetupTextKey.SpeechSection, "远程语音" },
                { AIChatSetupTextKey.ExperienceSection, "对话体验" },
                { AIChatSetupTextKey.AsrSection, "语音识别" },
                { AIChatSetupTextKey.LocalTtsSection, "本地语音合成" },
                { AIChatSetupTextKey.LlmTemperature, "LLM 温度" },
                { AIChatSetupTextKey.MaxHistoryTurns, "最大历史轮数" },
                { AIChatSetupTextKey.EnableTtsDiagnostics, "启用 TTS 诊断" },
                { AIChatSetupTextKey.InterruptAssistant, "用户说话时打断助手" },
                { AIChatSetupTextKey.MicStartupDelay, "麦克风启动延迟" },
                { AIChatSetupTextKey.AsrTurnDelay, "轮次检测延迟" },
                { AIChatSetupTextKey.AsrRecognitionMode, "识别模式" },
                { AIChatSetupTextKey.AsrStreamingModel, "流式 ASR 模型" },
                { AIChatSetupTextKey.AsrOfflineModel, "离线 ASR 模型" },
                { AIChatSetupTextKey.AsrVadModel, "VAD 模型" },
                { AIChatSetupTextKey.EnablePunctuation, "启用标点" },
                { AIChatSetupTextKey.PunctuationModel, "标点模型" },
                { AIChatSetupTextKey.LocalTtsModel, "本地 TTS 模型" },
                { AIChatSetupTextKey.LocalTtsVoiceId, "本地音色 ID" },
                { AIChatSetupTextKey.LocalTtsSpeed, "本地 TTS 语速" },
                { AIChatSetupTextKey.LocalTtsSampleRate, "本地 TTS 采样率" },
                { AIChatSetupTextKey.ClearOverrides, "清除全部覆盖" },
                { AIChatSetupTextKey.ScenePolicyPreview, "场景 Policy 预览" },
                { AIChatSetupTextKey.DeviceConfigAfterPolicy, "启动时会在此场景 Policy 之后应用当前 schema 的设备设置。" },
                { AIChatSetupTextKey.Custom, "自定义" },
                { AIChatSetupTextKey.OpenAI, "OpenAI" },
                { AIChatSetupTextKey.LocalOnly, "仅本地语音" },
                { AIChatSetupTextKey.FullDocumentation, "完整文档" },
                { AIChatSetupTextKey.ConfigureProviderUndo, "配置 AI 对话 Provider" },
                { AIChatSetupTextKey.ConfigureProviderPolicyUndo, "配置 AI 对话 Provider Policy" },
                { AIChatSetupTextKey.ClearPolicyOverridesUndo, "清除 AI 对话 Policy 覆盖" }
            };

        public static AIChatSetupLanguage CurrentLanguage
        {
            get
            {
                string selectedLanguage = EditorPrefs.GetString(UnityEditorLocalePrefKey, string.Empty);
                TryGetCurrentEditorLanguageName(out string currentLanguage);
                return ResolveLanguage(selectedLanguage, currentLanguage, Application.systemLanguage);
            }
        }

        public static bool IsChineseSimplified => CurrentLanguage == AIChatSetupLanguage.ChineseSimplified;

        public static string Text(AIChatSetupTextKey key)
        {
            return Text(key, CurrentLanguage);
        }

        public static string Text(AIChatSetupTextKey key, AIChatSetupLanguage language)
        {
            IReadOnlyDictionary<AIChatSetupTextKey, string> table =
                language == AIChatSetupLanguage.ChineseSimplified ? ChineseSimplified : English;
            if (table.TryGetValue(key, out string value))
            {
                return value;
            }

            return English.TryGetValue(key, out string fallback) ? fallback : key.ToString();
        }

        public static bool HasText(AIChatSetupTextKey key, AIChatSetupLanguage language)
        {
            IReadOnlyDictionary<AIChatSetupTextKey, string> table =
                language == AIChatSetupLanguage.ChineseSimplified ? ChineseSimplified : English;
            return table.ContainsKey(key);
        }

        public static string Text(AIChatSetupTextKey key, params object[] args)
        {
            string format = Text(key);
            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }

        public static string Text(
            AIChatSetupTextKey key,
            AIChatSetupLanguage language,
            params object[] args)
        {
            string format = Text(key, language);
            return args == null || args.Length == 0 ? format : string.Format(format, args);
        }

        public static AIChatSetupLanguage ResolveLanguage(
            string selectedEditorLanguage,
            string currentEditorLanguage,
            SystemLanguage systemLanguage)
        {
            if (IsChineseLanguageName(selectedEditorLanguage))
            {
                return AIChatSetupLanguage.ChineseSimplified;
            }

            if (IsEnglishLanguageName(selectedEditorLanguage))
            {
                return AIChatSetupLanguage.English;
            }

            if (IsChineseLanguageName(currentEditorLanguage))
            {
                return AIChatSetupLanguage.ChineseSimplified;
            }

            if (IsEnglishLanguageName(currentEditorLanguage))
            {
                return AIChatSetupLanguage.English;
            }

            return systemLanguage == SystemLanguage.Chinese ||
                   systemLanguage == SystemLanguage.ChineseSimplified ||
                   systemLanguage == SystemLanguage.ChineseTraditional
                ? AIChatSetupLanguage.ChineseSimplified
                : AIChatSetupLanguage.English;
        }

        private static bool TryGetCurrentEditorLanguageName(out string languageName)
        {
            languageName = string.Empty;
            try
            {
                Type localizationDatabaseType =
                    Type.GetType("UnityEditor.LocalizationDatabase, UnityEditor", false) ??
                    typeof(EditorApplication).Assembly.GetType("UnityEditor.LocalizationDatabase", false);
                PropertyInfo currentLanguageProperty = localizationDatabaseType?.GetProperty(
                    "currentEditorLanguage",
                    BindingFlags.Public | BindingFlags.Static);
                object languageValue = currentLanguageProperty?.GetValue(null, null);
                languageName = languageValue?.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(languageName);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsChineseLanguageName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().Replace('_', '-');
            return normalized.Equals("Chinese", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("ChineseSimplified", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("ChineseTraditional", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("Chinese-", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ||
                   normalized.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.IndexOf("简体", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsEnglishLanguageName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().Replace('_', '-');
            return normalized.Equals("English", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("English-", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
        }
    }
}

#endif
