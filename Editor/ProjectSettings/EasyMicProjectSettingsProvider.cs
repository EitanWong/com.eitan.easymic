#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Eitan.EasyMic.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eitan.EasyMic.Editor.ProjectSettings
{
    internal static class EasyMicProjectSettingsStorage
    {
        internal const string SettingsFilePath = "ProjectSettings/EasyMicSettings.asset";

        public static EasyMicProjectSettings LoadOrCreate()
        {
            var assets = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(SettingsFilePath);
            if (assets != null)
            {
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] is EasyMicProjectSettings settings)
                    {
                        settings.Migrate();
                        return settings;
                    }
                }
            }

            var created = ScriptableObject.CreateInstance<EasyMicProjectSettings>();
            created.ResetToDefaults();
            Save(created);
            return created;
        }

        public static void Save(EasyMicProjectSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.Migrate();
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { settings }, SettingsFilePath, true);
        }

        public static bool Import(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                return false;
            }

            var loaded = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget(sourcePath);
            bool valid = false;
            if (loaded != null)
            {
                for (int i = 0; i < loaded.Length; i++)
                {
                    if (loaded[i] is EasyMicProjectSettings)
                    {
                        valid = true;
                        break;
                    }
                }
            }

            if (!valid)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
            File.Copy(sourcePath, SettingsFilePath, true);
            return true;
        }

        public static void Export(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                return;
            }

            if (!File.Exists(SettingsFilePath))
            {
                Save(LoadOrCreate());
            }

            File.Copy(SettingsFilePath, destinationPath, true);
        }
    }

    internal sealed class EasyMicProjectSettingsProvider : SettingsProvider
    {
        private const int PageWidth = 660;

        private enum Page
        {
            General,
            Platforms,
            Diagnostics,
            Advanced
        }

        private enum PlatformPage
        {
            Windows,
            MacOS,
            Linux,
            Android,
            IOS
        }

        private readonly Page _page;
        private EasyMicProjectSettings _settings;
        private SerializedObject _serializedSettings;
        private VisualElement _root;
        private VisualElement _content;
        private PlatformPage _platformPage = PlatformPage.Windows;

        private EasyMicProjectSettingsProvider(string path, string labelKey, Page page)
            : base(path, SettingsScope.Project)
        {
            _page = page;
            label = EasyMicEditorLocalization.ProjectSettingsText(labelKey);
            keywords = new[]
            {
                "EasyMic", "audio", "microphone", "latency", "buffer", "telemetry",
                "diagnostics", "resampler", "drift", "backend", "Android", "iOS",
                "Windows", "macOS", "Linux", "Burst", "SIMD", "APM"
            };
        }

        [SettingsProvider]
        public static SettingsProvider CreateGeneralProvider()
        {
            return new EasyMicProjectSettingsProvider(
                EasyMicEditorLocalization.ProjectSettingsGeneralPath,
                "settings.title.general",
                Page.General);
        }

        [SettingsProvider]
        public static SettingsProvider CreatePlatformsProvider()
        {
            return new EasyMicProjectSettingsProvider(
                EasyMicEditorLocalization.ProjectSettingsPlatformsPath,
                "settings.title.platforms",
                Page.Platforms);
        }

        [SettingsProvider]
        public static SettingsProvider CreateDiagnosticsProvider()
        {
            return new EasyMicProjectSettingsProvider(
                EasyMicEditorLocalization.ProjectSettingsDiagnosticsPath,
                "settings.title.diagnostics",
                Page.Diagnostics);
        }

        [SettingsProvider]
        public static SettingsProvider CreateAdvancedProvider()
        {
            return new EasyMicProjectSettingsProvider(
                EasyMicEditorLocalization.ProjectSettingsAdvancedPath,
                "settings.title.advanced",
                Page.Advanced);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _settings = EasyMicProjectSettingsStorage.LoadOrCreate();
            _serializedSettings = new SerializedObject(_settings);
            _root = rootElement;
            EasyMicEditorLocalization.ProjectSettingsLanguageChanged += Rebuild;
            Build();
        }

        public override void OnDeactivate()
        {
            EasyMicEditorLocalization.ProjectSettingsLanguageChanged -= Rebuild;
            Save();
            _serializedSettings = null;
            _settings = null;
            _root = null;
            _content = null;
        }

        public override void OnGUI(string searchContext)
        {
        }

        private void Rebuild()
        {
            Save();
            Build();
        }

        private void Build()
        {
            if (_root == null)
            {
                return;
            }

            _serializedSettings.Update();
            _root.Clear();
            _root.style.paddingLeft = 18;
            _root.style.paddingRight = 18;
            _root.style.paddingTop = 14;

            _content = new ScrollView();
            _content.style.maxWidth = PageWidth;
            _content.style.flexGrow = 1;
            _root.Add(_content);

            AddHeader(_content, TitleKeyForPage(), SubtitleKeyForPage());

            switch (_page)
            {
                case Page.Platforms:
                    BuildPlatformsPage(_content);
                    break;
                case Page.Diagnostics:
                    BuildDiagnosticsPage(_content);
                    break;
                case Page.Advanced:
                    BuildAdvancedPage(_content);
                    break;
                default:
                    BuildGeneralPage(_content);
                    break;
            }

            _content.Bind(_serializedSettings);
        }

        private void BuildGeneralPage(VisualElement parent)
        {
            AddRuntimeStatus(parent);
            AddSection(parent, "section.backend",
                "runtime.backendMode",
                "runtime.preferNativeDeviceFormat");
            AddSection(parent, "section.latency",
                "runtime.latencyProfile",
                "runtime.bufferStrategy",
                "runtime.customBufferFrames");
            AddSection(parent, "section.dsp",
                "runtime.enableDspLimiter",
                "runtime.enableDriftCorrection",
                "runtime.resamplerQuality");
            AddSection(parent, "section.threading",
                "runtime.audioThreadPriority",
                "runtime.runInBackground",
                "runtime.autoFallback");
            AddSection(parent, "section.device",
                "runtime.autoRefreshDevices",
                "runtime.deviceRefreshIntervalSeconds",
                "runtime.defaultSampleRate",
                "runtime.defaultChannels",
                "runtime.streamingQueueMilliseconds");
            AddValidation(parent, EasyMicProjectSettingsValidator.ValidateRuntime(_settings));
        }

        private void BuildPlatformsPage(VisualElement parent)
        {
            AddInfo(parent, "help.platforms");
            AddPlatformTabs(parent);
            AddPlatformPanel(parent, _platformPage);
            AddValidation(parent, EasyMicProjectSettingsValidator.ValidatePlatforms(_settings));
        }

        private void BuildDiagnosticsPage(VisualElement parent)
        {
            AddSection(parent, "section.telemetry",
                "runtime.enableTelemetry",
                "runtime.enableRuntimeDiagnostics",
                "runtime.enableLogging",
                "runtime.logLevel");
            AddSection(parent, "section.editorDiagnostics",
                "editor.enableEditorDiagnostics",
                "editor.telemetryRefreshRate",
                "editor.diagnosticsMode");
            AddSection(parent, "section.graph",
                "editor.enableGraphAnimations",
                "editor.followUnityEditorTheme",
                "editor.highContrastGraphs",
                "editor.graphMaximumVisibleNodes");
            AddValidation(parent, EasyMicProjectSettingsValidator.ValidateEditor(_settings));
        }

        private void BuildAdvancedPage(VisualElement parent)
        {
            AddInfo(parent, "help.advanced");
            AddSection(parent, "section.experimental",
                "experimental.enableBurstDsp",
                "experimental.enableSimdKernels",
                "experimental.enableLowLatencyCapturePath",
                "experimental.enableAdvancedTelemetry",
                "experimental.enableFutureDspGraph",
                "editor.developerMode",
                "editor.showAdvancedPipelineMetrics");
            AddPresetActions(parent);
            AddValidation(parent, EasyMicProjectSettingsValidator.ValidateExperimental(_settings));
        }

        private void AddPlatformTabs(VisualElement parent)
        {
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginTop = 8;
            tabs.style.marginBottom = 14;
            tabs.style.borderBottomWidth = 1;
            tabs.style.borderBottomColor = BorderColor();
            parent.Add(tabs);

            AddPlatformTab(tabs, PlatformPage.Windows, "section.platformWindows");
            AddPlatformTab(tabs, PlatformPage.MacOS, "section.platformMacOS");
            AddPlatformTab(tabs, PlatformPage.Linux, "section.platformLinux");
            AddPlatformTab(tabs, PlatformPage.Android, "section.platformAndroid");
            AddPlatformTab(tabs, PlatformPage.IOS, "section.platformIOS");
        }

        private void AddPlatformTab(VisualElement tabs, PlatformPage page, string labelKey)
        {
            bool selected = _platformPage == page;
            var button = new Button(() =>
            {
                _platformPage = page;
                Build();
            })
            {
                text = EasyMicEditorLocalization.ProjectSettingsText(labelKey)
            };
            button.style.height = 28;
            button.style.marginRight = 4;
            button.style.marginBottom = -1;
            button.style.paddingLeft = 12;
            button.style.paddingRight = 12;
            button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            button.style.borderBottomWidth = selected ? 2 : 1;
            button.style.borderBottomColor = selected ? AccentColor() : BorderColor();
            tabs.Add(button);
        }

        private void AddPlatformPanel(VisualElement parent, PlatformPage page)
        {
            string path = PlatformPath(page);
            AddInfo(parent, PlatformStatusKey(page));
            AddSection(parent, "section.platformBackend",
                path + ".backendMode",
                path + ".preferNativeDeviceFormat");
            AddSection(parent, "section.platformLatency",
                path + ".overrideLatencyProfile",
                path + ".latencyProfile",
                path + ".overrideBufferFrames",
                path + ".bufferFrames");
            AddSection(parent, "section.platformDsp",
                path + ".enableDspLimiter",
                path + ".enableDriftCorrection",
                path + ".enableDiagnostics");

            var nativeFields = new List<string>();
            switch (page)
            {
                case PlatformPage.Windows:
                    nativeFields.Add(path + ".useWasapiExclusiveMode");
                    break;
                case PlatformPage.MacOS:
                case PlatformPage.IOS:
                    nativeFields.Add(path + ".useCoreAudioVoiceProcessing");
                    break;
                case PlatformPage.Linux:
                    nativeFields.Add(path + ".enablePulseAudioFallback");
                    break;
                case PlatformPage.Android:
                    nativeFields.Add(path + ".useAAudio");
                    nativeFields.Add(path + ".forceSafeDeviceEnumeration");
                    break;
            }

            if (nativeFields.Count > 0)
            {
                AddSection(parent, "section.platformNative", nativeFields.ToArray());
            }
        }

        private void AddHeader(VisualElement parent, string titleKey, string subtitleKey)
        {
            var title = new Label(EasyMicEditorLocalization.ProjectSettingsText(titleKey));
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 3;
            parent.Add(title);

            var subtitle = new Label(EasyMicEditorLocalization.ProjectSettingsText(subtitleKey));
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.marginBottom = 18;
            subtitle.style.color = SecondaryTextColor();
            parent.Add(subtitle);
        }

        private void AddSection(VisualElement parent, string titleKey, params string[] propertyPaths)
        {
            var section = new VisualElement();
            section.style.marginBottom = 18;
            section.style.paddingBottom = 12;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = BorderColor();

            var title = new Label(EasyMicEditorLocalization.ProjectSettingsText(titleKey));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            section.Add(title);

            for (int i = 0; i < propertyPaths.Length; i++)
            {
                AddProperty(section, propertyPaths[i]);
            }

            parent.Add(section);
        }

        private PropertyField AddProperty(VisualElement parent, string path)
        {
            var property = _serializedSettings.FindProperty(path);
            if (property == null)
            {
                return null;
            }

            var field = new PropertyField(property, LabelFor(path));
            field.tooltip = TooltipFor(path);
            field.style.marginBottom = 4;
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => ApplyAndSave());
            parent.Add(field);
            return field;
        }

        private void AddRuntimeStatus(VisualElement parent)
        {
            bool running = false;
            try
            {
                running = AudioSystem.Instance.IsRunning;
            }
            catch
            {
            }

            AddInfo(parent, running ? "status.runtime.running" : "status.runtime.stopped", running);
        }

        private void AddInfo(VisualElement parent, string key, bool warning = false)
        {
            var row = new Label(EasyMicEditorLocalization.ProjectSettingsText(key));
            row.style.whiteSpace = WhiteSpace.Normal;
            row.style.marginBottom = 14;
            row.style.paddingLeft = 10;
            row.style.paddingRight = 10;
            row.style.paddingTop = 7;
            row.style.paddingBottom = 8;
            row.style.borderLeftWidth = 3;
            row.style.borderLeftColor = warning ? WarningColor() : AccentColor();
            row.style.backgroundColor = PanelBackgroundColor();
            parent.Add(row);
        }

        private void AddPresetActions(VisualElement parent)
        {
            var section = new VisualElement();
            section.style.marginBottom = 18;
            section.style.paddingBottom = 12;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = BorderColor();

            var title = new Label(EasyMicEditorLocalization.ProjectSettingsText("section.presets"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            section.Add(title);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(CreateToolbarButton("settings.import", ImportSettings));
            row.Add(CreateToolbarButton("settings.export", ExportSettings));
            row.Add(CreateToolbarButton("settings.reset", ResetSettings));
            section.Add(row);
            parent.Add(section);
        }

        private void AddValidation(VisualElement parent, IReadOnlyList<EasyMicProjectSettingsIssue> issues)
        {
            if (issues == null)
            {
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                AddInfo(parent, issues[i].LocalizationKey, issues[i].Type == HelpBoxMessageType.Warning);
            }
        }

        private Button CreateToolbarButton(string key, Action action)
        {
            var button = new Button(action)
            {
                text = EasyMicEditorLocalization.ProjectSettingsText(key)
            };
            button.style.marginRight = 6;
            button.style.height = 26;
            return button;
        }

        private void ApplyAndSave()
        {
            _serializedSettings.ApplyModifiedProperties();
            _settings.Migrate();
            EditorUtility.SetDirty(_settings);
            Save();
        }

        private void Save()
        {
            if (_settings != null)
            {
                EasyMicProjectSettingsStorage.Save(_settings);
            }
        }

        private void ResetSettings()
        {
            if (!EditorUtility.DisplayDialog(
                    EasyMicEditorLocalization.ProjectSettingsText("settings.reset.confirm.title"),
                    EasyMicEditorLocalization.ProjectSettingsText("settings.reset.confirm.message"),
                    EasyMicEditorLocalization.ProjectSettingsText("settings.reset"),
                    EasyMicEditorLocalization.Text(EasyMicEditorTextKey.CommonOk)))
            {
                return;
            }

            Undo.RecordObject(_settings, EasyMicEditorLocalization.ProjectSettingsText("settings.reset"));
            _settings.ResetToDefaults();
            ApplyAndSave();
            Build();
        }

        private void ImportSettings()
        {
            string path = EditorUtility.OpenFilePanel(EasyMicEditorLocalization.ProjectSettingsText("settings.import.title"), Directory.GetCurrentDirectory(), "asset");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (!EasyMicProjectSettingsStorage.Import(path))
            {
                EditorUtility.DisplayDialog(
                    EasyMicEditorLocalization.ProjectSettingsText("settings.import.title"),
                    EasyMicEditorLocalization.ProjectSettingsText("settings.import.failed"),
                    EasyMicEditorLocalization.Text(EasyMicEditorTextKey.CommonOk));
                return;
            }

            _settings = EasyMicProjectSettingsStorage.LoadOrCreate();
            _serializedSettings = new SerializedObject(_settings);
            Build();
        }

        private void ExportSettings()
        {
            string path = EditorUtility.SaveFilePanel(EasyMicEditorLocalization.ProjectSettingsText("settings.export.title"), Directory.GetCurrentDirectory(), "EasyMicSettings.asset", "asset");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                Save();
                EasyMicProjectSettingsStorage.Export(path);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    EasyMicEditorLocalization.ProjectSettingsText("settings.export.title"),
                    EasyMicEditorLocalization.ProjectSettingsText("settings.export.failed", ex.Message),
                    EasyMicEditorLocalization.Text(EasyMicEditorTextKey.CommonOk));
            }
        }

        private string TitleKeyForPage()
        {
            switch (_page)
            {
                case Page.Platforms:
                    return "settings.title.platforms";
                case Page.Diagnostics:
                    return "settings.title.diagnostics";
                case Page.Advanced:
                    return "settings.title.advanced";
                default:
                    return "settings.title.general";
            }
        }

        private string SubtitleKeyForPage()
        {
            switch (_page)
            {
                case Page.Platforms:
                    return "settings.subtitle.platforms";
                case Page.Diagnostics:
                    return "settings.subtitle.diagnostics";
                case Page.Advanced:
                    return "settings.subtitle.advanced";
                default:
                    return "settings.subtitle.general";
            }
        }

        private static string PlatformPath(PlatformPage page)
        {
            switch (page)
            {
                case PlatformPage.MacOS:
                    return "macOS";
                case PlatformPage.Linux:
                    return "linux";
                case PlatformPage.Android:
                    return "android";
                case PlatformPage.IOS:
                    return "iOS";
                default:
                    return "windows";
            }
        }

        private static string PlatformStatusKey(PlatformPage page)
        {
            switch (page)
            {
                case PlatformPage.MacOS:
                    return "status.platform.macos";
                case PlatformPage.Linux:
                    return "status.platform.linux";
                case PlatformPage.Android:
                    return "status.platform.android";
                case PlatformPage.IOS:
                    return "status.platform.ios";
                default:
                    return "status.platform.windows";
            }
        }

        private static string LabelFor(string path)
        {
            string key = "field." + path.Substring(path.LastIndexOf('.') + 1);
            return EasyMicEditorLocalization.ProjectSettingsText(key);
        }

        private static string TooltipFor(string path)
        {
            string key = "field." + path.Substring(path.LastIndexOf('.') + 1) + ".tooltip";
            string value = EasyMicEditorLocalization.ProjectSettingsText(key);
            return value == key ? string.Empty : value;
        }

        private static Color BorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.28f, 0.28f, 0.28f, 1f)
                : new Color(0.78f, 0.78f, 0.78f, 1f);
        }

        private static Color AccentColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.30f, 0.56f, 0.88f, 1f)
                : new Color(0.18f, 0.42f, 0.78f, 1f);
        }

        private static Color WarningColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.95f, 0.72f, 0.26f, 1f)
                : new Color(0.82f, 0.58f, 0.08f, 1f);
        }

        private static Color SecondaryTextColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.70f, 0.70f, 0.70f, 1f)
                : new Color(0.36f, 0.36f, 0.36f, 1f);
        }

        private static Color PanelBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.15f, 0.15f, 0.15f, 1f)
                : new Color(0.94f, 0.94f, 0.94f, 1f);
        }
    }

    internal readonly struct EasyMicProjectSettingsIssue
    {
        public readonly string LocalizationKey;
        public readonly HelpBoxMessageType Type;

        public EasyMicProjectSettingsIssue(string localizationKey, HelpBoxMessageType type)
        {
            LocalizationKey = localizationKey;
            Type = type;
        }
    }

    internal static class EasyMicProjectSettingsValidator
    {
        public static IReadOnlyList<EasyMicProjectSettingsIssue> ValidateRuntime(EasyMicProjectSettings settings)
        {
            var issues = new List<EasyMicProjectSettingsIssue>(3);
            if (settings.Runtime.latencyProfile == EasyMicLatencyProfile.UltraLowLatency)
            {
                issues.Add(new EasyMicProjectSettingsIssue("validation.ultraLow", HelpBoxMessageType.Warning));
            }

            if (settings.Runtime.bufferStrategy == EasyMicBufferStrategy.Custom &&
                (settings.Runtime.customBufferFrames < 128 || settings.Runtime.customBufferFrames > 2048))
            {
                issues.Add(new EasyMicProjectSettingsIssue("validation.customBuffer", HelpBoxMessageType.Warning));
            }

            return issues;
        }

        public static IReadOnlyList<EasyMicProjectSettingsIssue> ValidateEditor(EasyMicProjectSettings settings)
        {
            var issues = new List<EasyMicProjectSettingsIssue>(1);
            if (settings.Editor.telemetryRefreshRate > 45f)
            {
                issues.Add(new EasyMicProjectSettingsIssue("validation.telemetryRate", HelpBoxMessageType.Warning));
            }

            return issues;
        }

        public static IReadOnlyList<EasyMicProjectSettingsIssue> ValidatePlatforms(EasyMicProjectSettings settings)
        {
            var issues = new List<EasyMicProjectSettingsIssue>(1);
            if (!settings.Android.forceSafeDeviceEnumeration)
            {
                issues.Add(new EasyMicProjectSettingsIssue("validation.androidEnumeration", HelpBoxMessageType.Warning));
            }

            return issues;
        }

        public static IReadOnlyList<EasyMicProjectSettingsIssue> ValidateExperimental(EasyMicProjectSettings settings)
        {
            var issues = new List<EasyMicProjectSettingsIssue>(1);
            if (settings.Experimental.enableBurstDsp)
            {
                issues.Add(new EasyMicProjectSettingsIssue("validation.burstUnavailable", HelpBoxMessageType.Info));
            }

            return issues;
        }
    }
}
#endif
