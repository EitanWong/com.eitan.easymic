#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eitan.EasyMic.Editor.ProjectSettings
{
    internal sealed class SherpaONNXUnityProjectSettingsProvider : SettingsProvider
    {
        private const string PackageName = "com.eitan.sherpa-onnx-unity";
        private const string PackagePath = "Packages/com.eitan.sherpa-onnx-unity/package.json";
        private const string PackageGitUrl = "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm";
        private const string RuntimeAssemblyName = "Eitan.EasyMic.Integration.SherpaONNXUnity";
        private const string EditorAssemblyName = "Eitan.EasyMic.Integration.SherpaONNXUnity.Editor";
        private const string SettingsPath = EasyMicEditorLocalization.ProjectSettingsIntegrationsPath + "/SherpaONNXUnity";
        private const int PageWidth = 660;
        private const int NoProgressId = -1;

        private static AddRequest s_installRequest;
        private static int s_installProgressId = NoProgressId;
        private static double s_installStartedAt;
        private static double s_lastInstallFeedbackUpdate;
        private static SherpaONNXUnityProjectSettingsProvider s_activeProvider;

        private SherpaONNXUnityIntegrationStatus _status;
        private VisualElement _root;
        private VisualElement _content;
        private Label _installStatusLabel;
        private VisualElement _installActivityIndicator;

        static SherpaONNXUnityProjectSettingsProvider()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        private SherpaONNXUnityProjectSettingsProvider()
            : base(SettingsPath, SettingsScope.Project)
        {
            label = T("sherpa.title");
            keywords = new[]
            {
                "EasyMic",
                "Sherpa",
                "SherpaONNXUnity",
                "speech",
                "asr",
                "kws",
                "vad",
                "microphone",
                "integration"
            };
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new SherpaONNXUnityProjectSettingsProvider();
        }

        [SettingsProvider]
        public static SettingsProvider CreateIntegrationsProvider()
        {
            return new SettingsProvider(EasyMicEditorLocalization.ProjectSettingsIntegrationsPath, SettingsScope.Project)
            {
                label = T("settings.title.integrations"),
                keywords = new[] { "EasyMic", "integration", "integrations", "Sherpa", "ONNX" },
                activateHandler = (searchContext, rootElement) =>
                {
                    rootElement.Clear();
                    rootElement.style.paddingLeft = 18;
                    rootElement.style.paddingRight = 18;
                    rootElement.style.paddingTop = 14;
                    AddHeader(
                        rootElement,
                        T("settings.title.integrations"),
                        T("settings.subtitle.integrations"));
                }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _root = rootElement;
            s_activeProvider = this;
            EasyMicEditorLocalization.ProjectSettingsLanguageChanged += Rebuild;
            RefreshStatus();
            EnsureInstallUpdateRegistered();
        }

        public override void OnDeactivate()
        {
            if (s_activeProvider == this)
            {
                s_activeProvider = null;
            }

            _root = null;
            _content = null;
            _installStatusLabel = null;
            _installActivityIndicator = null;
            EasyMicEditorLocalization.ProjectSettingsLanguageChanged -= Rebuild;
        }

        public override void OnGUI(string searchContext)
        {
        }

        private static void EditorUpdate()
        {
            UpdateInstallFeedback();
            PollInstallRequest();
        }

        private void Build()
        {
            if (_root == null)
            {
                return;
            }

            _root.Clear();
            label = T("sherpa.title");
            _root.style.paddingLeft = 18;
            _root.style.paddingRight = 18;
            _root.style.paddingTop = 14;

            _content = new ScrollView();
            _content.style.maxWidth = PageWidth;
            _content.style.flexGrow = 1;
            _root.Add(_content);

            AddHeader(
                _content,
                T("sherpa.title"),
                T("sherpa.subtitle"));
            AddPackageSection(_content);
            AddIntegrationSection(_content);
        }

        private void AddPackageSection(VisualElement parent)
        {
            var section = AddSection(parent, T("sherpa.section.package"));
            AddStatusRow(section, T("sherpa.status"), _status.PackageInstalled ? T("sherpa.installed") : T("sherpa.notInstalled"), _status.PackageInstalled);
            AddValueRow(section, T("sherpa.packageId"), _status.PackageId);

            if (_status.PackageInstalled)
            {
                AddValueRow(section, T("sherpa.version"), _status.Version);
                AddValueRow(section, T("sherpa.source"), _status.Source);
                AddValueRow(section, T("sherpa.path"), _status.ResolvedPath);
            }
            else
            {
                AddValueRow(section, T("sherpa.upmGitUrl"), PackageGitUrl);
            }

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 8;
            if (!_status.PackageInstalled)
            {
                var install = CreateToolbarButton(T("sherpa.install"), StartInstall);
                install.SetEnabled(s_installRequest == null);
                actions.Add(install);
            }

            actions.Add(CreateToolbarButton(T("sherpa.refresh"), RefreshStatus));
            section.Add(actions);

            if (s_installRequest != null)
            {
                AddInstallFeedback(section);
                UpdateInstallFeedback(force: true);
            }
        }

        private void AddIntegrationSection(VisualElement parent)
        {
            var section = AddSection(parent, T("sherpa.section.bridge"));
            AddStatusRow(section, T("sherpa.status"), _status.DefineActive ? T("sherpa.ready") : T("sherpa.inactive"), _status.DefineActive);
            AddStatusRow(section, T("sherpa.runtimeAssembly"), _status.RuntimeAssemblyAvailable ? T("sherpa.available") : T("sherpa.notCompiled"), _status.RuntimeAssemblyAvailable);
            AddStatusRow(section, T("sherpa.editorAssembly"), _status.EditorAssemblyAvailable ? T("sherpa.available") : T("sherpa.notCompiled"), _status.EditorAssemblyAvailable);
            AddStatusRow(section, T("sherpa.define"), _status.DefineActive ? T("sherpa.active") : T("sherpa.inactive"), _status.DefineActive);
            AddValueRow(section, T("sherpa.defineSymbol"), _status.DefineActive ? "EITAN_SHERPA_ONNX_UNITY_PRESENT" : "-");
            AddValueRow(section, T("sherpa.buildTarget"), _status.CurrentBuildTarget);
            AddStatusRow(section, T("sherpa.nativePlugin"), _status.NativePluginLikelySupported ? T("sherpa.supported") : T("sherpa.checkTarget"), _status.NativePluginLikelySupported);

            if (_status.PackageInstalled && !_status.DefineActive)
            {
                AddInfo(parent, T("sherpa.waitCompile"), true);
            }
        }

        private void StartInstall()
        {
            try
            {
                if (s_installRequest != null)
                {
                    return;
                }

                s_installStartedAt = EditorApplication.timeSinceStartup;
                s_lastInstallFeedbackUpdate = 0;
                StartInstallProgress();
                s_installRequest = Client.Add(PackageGitUrl);
                EnsureInstallUpdateRegistered();
                Build();
            }
            catch (Exception ex)
            {
                s_installRequest = null;
                FinishInstallProgress(Progress.Status.Failed);
                StopInstallUpdateIfIdle();
                Build();
                EditorUtility.DisplayDialog(T("sherpa.installFailedTitle"), ex.Message, EasyMicEditorLocalization.Text(EasyMicEditorTextKey.CommonOk));
            }
        }

        private static void PollInstallRequest()
        {
            if (s_installRequest == null || !s_installRequest.IsCompleted)
            {
                return;
            }

            bool failed = s_installRequest.Status >= StatusCode.Failure;
            string errorMessage = s_installRequest.Error != null ? s_installRequest.Error.message : T("sherpa.unknownPackageError");
            s_installRequest = null;

            if (failed)
            {
                FinishInstallProgress(Progress.Status.Failed);
                EditorUtility.DisplayDialog(T("sherpa.installFailedTitle"), errorMessage, EasyMicEditorLocalization.Text(EasyMicEditorTextKey.CommonOk));
            }
            else
            {
                FinishInstallProgress(Progress.Status.Succeeded);
                EditorUtility.DisplayDialog(
                    T("sherpa.installedTitle"),
                    T("sherpa.installedMessage"),
                    EasyMicEditorLocalization.Text(EasyMicEditorTextKey.CommonOk));
            }

            StopInstallUpdateIfIdle();
            s_activeProvider?.RefreshStatus();
        }

        private static void StartInstallProgress()
        {
            FinishInstallProgress(Progress.Status.Canceled);
            s_installProgressId = Progress.Start(
                T("sherpa.progressTitle"),
                T("sherpa.progressStarting"),
                Progress.Options.None,
                NoProgressId);
            Progress.ShowDetails(false);
        }

        private static void UpdateInstallFeedback(bool force = false)
        {
            if (s_installRequest == null || s_installRequest.IsCompleted)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (!force && now - s_lastInstallFeedbackUpdate < 0.2)
            {
                return;
            }

            s_lastInstallFeedbackUpdate = now;
            double elapsed = Math.Max(0, now - s_installStartedAt);
            string message = T("sherpa.progressRunning", elapsed);

            if (s_activeProvider?._installStatusLabel != null)
            {
                s_activeProvider._installStatusLabel.text = message;
            }

            if (s_activeProvider?._installActivityIndicator != null)
            {
                float alpha = 0.45f + 0.25f * Mathf.PingPong((float)elapsed * 1.2f, 1f);
                s_activeProvider._installActivityIndicator.style.opacity = alpha;
            }

            if (s_installProgressId != NoProgressId && Progress.Exists(s_installProgressId))
            {
                Progress.Report(s_installProgressId, 0f, message);
            }
        }

        private static void FinishInstallProgress(Progress.Status status)
        {
            if (s_installProgressId == NoProgressId)
            {
                return;
            }

            if (Progress.Exists(s_installProgressId))
            {
                Progress.Finish(s_installProgressId, status);
            }

            s_installProgressId = NoProgressId;
        }

        private static void EnsureInstallUpdateRegistered()
        {
            EditorApplication.update -= EditorUpdate;
            if (s_installRequest != null)
            {
                EditorApplication.update += EditorUpdate;
            }
        }

        private static void StopInstallUpdateIfIdle()
        {
            if (s_installRequest == null)
            {
                EditorApplication.update -= EditorUpdate;
            }
        }

        private static void BeforeAssemblyReload()
        {
            if (s_installRequest != null)
            {
                FinishInstallProgress(Progress.Status.Succeeded);
                s_installRequest = null;
            }

            StopInstallUpdateIfIdle();
        }

        private void RefreshStatus()
        {
            _status = SherpaONNXUnityIntegrationStatus.Refresh(PackageName, PackagePath, RuntimeAssemblyName, EditorAssemblyName);
            Build();
        }

        private void Rebuild()
        {
            Build();
        }

        private static string T(string key)
        {
            return EasyMicEditorLocalization.ProjectSettingsText(key);
        }

        private static string T(string key, params object[] args)
        {
            return EasyMicEditorLocalization.ProjectSettingsText(key, args);
        }

        private static void AddHeader(VisualElement parent, string titleText, string subtitleText)
        {
            var title = new Label(titleText);
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 3;
            parent.Add(title);

            var subtitle = new Label(subtitleText);
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.marginBottom = 18;
            subtitle.style.color = SecondaryTextColor();
            parent.Add(subtitle);
        }

        private static VisualElement AddSection(VisualElement parent, string titleText)
        {
            var section = new VisualElement();
            section.style.marginBottom = 18;
            section.style.paddingBottom = 12;
            section.style.borderBottomWidth = 1;
            section.style.borderBottomColor = BorderColor();

            var title = new Label(titleText);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            section.Add(title);

            parent.Add(section);
            return section;
        }

        private static void AddStatusRow(VisualElement parent, string labelText, string statusText, bool ok)
        {
            var row = CreateRow(labelText);
            row.Add(CreateBadge(statusText, ok));
            parent.Add(row);
        }

        private static void AddValueRow(VisualElement parent, string labelText, string valueText)
        {
            var row = CreateRow(labelText);
            var value = new Label(string.IsNullOrEmpty(valueText) ? "-" : valueText);
            value.style.flexGrow = 1;
            value.style.whiteSpace = WhiteSpace.Normal;
            value.style.color = SecondaryTextColor();
            row.Add(value);
            parent.Add(row);
        }

        private static VisualElement CreateRow(string labelText)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 5;

            var label = new Label(labelText);
            label.style.width = 150;
            label.style.minWidth = 120;
            label.style.marginRight = 10;
            label.style.color = SecondaryTextColor();
            row.Add(label);
            return row;
        }

        private static Label CreateBadge(string text, bool ok)
        {
            var badge = new Label(string.IsNullOrEmpty(text) ? "-" : text);
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.fontSize = 10;
            badge.style.color = Color.white;
            badge.style.backgroundColor = ok ? OkColor() : WarningColor();
            badge.style.paddingLeft = 8;
            badge.style.paddingRight = 8;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;
            badge.style.minWidth = 84;
            badge.style.maxWidth = 150;
            badge.style.height = 20;
            badge.style.flexShrink = 0;
            return badge;
        }

        private static void AddInfo(VisualElement parent, string text, bool warning)
        {
            var row = new Label(text);
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

        private void AddInstallFeedback(VisualElement parent)
        {
            var container = new VisualElement();
            container.style.marginTop = 10;
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.paddingTop = 7;
            container.style.paddingBottom = 8;
            container.style.borderLeftWidth = 3;
            container.style.borderLeftColor = AccentColor();
            container.style.backgroundColor = PanelBackgroundColor();
            parent.Add(container);

            _installStatusLabel = new Label();
            _installStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            _installStatusLabel.style.marginBottom = 6;
            container.Add(_installStatusLabel);

            _installActivityIndicator = new VisualElement();
            _installActivityIndicator.style.height = 3;
            _installActivityIndicator.style.backgroundColor = AccentColor();
            _installActivityIndicator.style.opacity = 0.55f;
            container.Add(_installActivityIndicator);
        }

        private static Button CreateToolbarButton(string text, Action action)
        {
            var button = new Button(action)
            {
                text = text
            };
            button.style.marginRight = 6;
            button.style.height = 26;
            return button;
        }

        private static Color SecondaryTextColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.74f, 0.74f, 0.74f)
                : new Color(0.32f, 0.32f, 0.32f);
        }

        private static Color BorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.28f, 0.28f, 0.28f)
                : new Color(0.72f, 0.72f, 0.72f);
        }

        private static Color PanelBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.19f, 0.19f, 0.19f)
                : new Color(0.92f, 0.92f, 0.92f);
        }

        private static Color AccentColor()
        {
            return new Color(0.19f, 0.48f, 0.9f);
        }

        private static Color OkColor()
        {
            return new Color(0.18f, 0.55f, 0.25f);
        }

        private static Color WarningColor()
        {
            return new Color(0.78f, 0.52f, 0.12f);
        }

        private static bool IsLoadedAssemblyAvailable(string assemblyName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));
        }

        private readonly struct SherpaONNXUnityIntegrationStatus
        {
            private SherpaONNXUnityIntegrationStatus(
                bool packageInstalled,
                string packageId,
                string version,
                string source,
                string resolvedPath,
                bool runtimeAssemblyAvailable,
                bool editorAssemblyAvailable,
                string currentBuildTarget,
                bool nativePluginLikelySupported)
            {
                PackageInstalled = packageInstalled;
                PackageId = packageId;
                Version = version;
                Source = source;
                ResolvedPath = resolvedPath;
                RuntimeAssemblyAvailable = runtimeAssemblyAvailable;
                EditorAssemblyAvailable = editorAssemblyAvailable;
                DefineActive = runtimeAssemblyAvailable || editorAssemblyAvailable;
                CurrentBuildTarget = currentBuildTarget;
                NativePluginLikelySupported = nativePluginLikelySupported;
            }

            public bool PackageInstalled { get; }
            public string PackageId { get; }
            public string Version { get; }
            public string Source { get; }
            public string ResolvedPath { get; }
            public bool RuntimeAssemblyAvailable { get; }
            public bool EditorAssemblyAvailable { get; }
            public bool DefineActive { get; }
            public string CurrentBuildTarget { get; }
            public bool NativePluginLikelySupported { get; }

            public static SherpaONNXUnityIntegrationStatus Refresh(
                string packageName,
                string packagePath,
                string runtimeAssemblyName,
                string editorAssemblyName)
            {
                UnityEditor.PackageManager.PackageInfo package = null;
                try
                {
                    package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packagePath);
                }
                catch
                {
                    package = null;
                }

                bool runtimeAssembly = IsCompiledOrLoaded(runtimeAssemblyName);
                bool editorAssembly = IsCompiledOrLoaded(editorAssemblyName);
                string buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString();
                bool nativeLikelySupported = IsNativeLikelySupported(EditorUserBuildSettings.activeBuildTarget);

                return new SherpaONNXUnityIntegrationStatus(
                    package != null,
                    package?.packageId ?? packageName,
                    package?.version ?? string.Empty,
                    package != null ? package.source.ToString() : string.Empty,
                    package?.resolvedPath ?? string.Empty,
                    runtimeAssembly,
                    editorAssembly,
                    buildTarget,
                    nativeLikelySupported);
            }

            private static bool IsCompiledOrLoaded(string assemblyName)
            {
                if (IsLoadedAssemblyAvailable(assemblyName))
                {
                    return true;
                }

                try
                {
                    return CompilationPipeline.GetAssemblies()
                        .Any(assembly => string.Equals(assembly.name, assemblyName, StringComparison.Ordinal));
                }
                catch
                {
                    return false;
                }
            }

            private static bool IsNativeLikelySupported(BuildTarget target)
            {
                switch (target)
                {
                    case BuildTarget.Android:
                    case BuildTarget.iOS:
                    case BuildTarget.StandaloneLinux64:
                    case BuildTarget.StandaloneOSX:
                    case BuildTarget.StandaloneWindows:
                    case BuildTarget.StandaloneWindows64:
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
#endif
