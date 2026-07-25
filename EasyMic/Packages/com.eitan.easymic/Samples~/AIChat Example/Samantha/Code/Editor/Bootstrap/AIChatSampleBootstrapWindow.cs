#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    /// <summary>
    /// Dependency-free entry point for the imported AI Chat sample.
    /// </summary>
    internal sealed class AIChatSampleBootstrapWindow : EditorWindow
    {
        private const string RequiredPackageName = "com.eitan.sherpa-onnx-unity";
        private const string RequiredPackageGitUrl = "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm";
        private const string ProviderSetupMenuPath = "Tools/EasyMic/AI Chat/Provider Setup";
        private const string ControllerTypeName =
            "Eitan.EasyMic.Demo.AIChat.Samantha.AIChatController, Eitan.EasyMic.Demo.AIChat.Samantha";
        private const string DefaultRuntimeConfigFileName = "ai_chat_config.json";
        private const int CurrentSchemaVersion = 4;

        private bool _dependencyInstalled;
        private bool _providerConfigured;
        private string _sampleRootPath = string.Empty;
        private string _sampleScenePath = string.Empty;
        private string _readmeAssetPath = string.Empty;
        private Vector2 _scrollPosition;
        private AIChatSetupLanguage _language;

        [MenuItem("Tools/EasyMic/AI Chat/Setup", false, 2000)]
        public static void Open()
        {
            var window = GetWindow<AIChatSampleBootstrapWindow>();
            window.minSize = new Vector2(520f, 480f);
            window.RefreshStatus();
        }

        private void OnEnable()
        {
            RefreshStatus();
        }

        private void OnFocus()
        {
            RefreshStatus();
        }

        private void OnGUI()
        {
            _language = AIChatSetupLocalization.CurrentLanguage;
            UpdateWindowTitle();

            AIChatSetupGui.DrawHero(
                "d_UnityEditor.InspectorWindow",
                Text(AIChatSetupTextKey.SetupHeader),
                Text(AIChatSetupTextKey.SetupDescription),
                Text(AIChatSetupTextKey.Latest));

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                AIChatSetupGui.DrawStatusCard(
                    1,
                    Text(AIChatSetupTextKey.StepImport),
                    string.IsNullOrWhiteSpace(_sampleScenePath)
                        ? Text(AIChatSetupTextKey.SceneNotFoundDetail)
                        : _sampleScenePath,
                    !string.IsNullOrWhiteSpace(_sampleScenePath) && !string.IsNullOrWhiteSpace(_readmeAssetPath));

                AIChatSetupGui.DrawStatusCard(
                    2,
                    Text(AIChatSetupTextKey.StepDependency),
                    Text(_dependencyInstalled
                        ? AIChatSetupTextKey.DependencyReadyDetail
                        : AIChatSetupTextKey.DependencyMissingDetail),
                    _dependencyInstalled);

                bool providerReady = _dependencyInstalled && _providerConfigured;
                AIChatSetupGui.DrawStatusCard(
                    3,
                    Text(AIChatSetupTextKey.StepProvider),
                    Text(!_dependencyInstalled
                        ? AIChatSetupTextKey.DependencyMissingHelp
                        : _providerConfigured
                            ? AIChatSetupTextKey.ProviderConfiguredDetail
                            : AIChatSetupTextKey.ProviderPendingDetail),
                    providerReady);

                AIChatSetupGui.DrawContract(
                    Text(AIChatSetupTextKey.CurrentContract),
                    Text(AIChatSetupTextKey.CurrentContractDetail, CurrentSchemaVersion));

                if (!_dependencyInstalled)
                {
                    using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
                    {
                        AIChatSetupGui.DrawSectionHeader(2, Text(AIChatSetupTextKey.VoiceDependency));
                        if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.CopyGitUrl)))
                        {
                            EditorGUIUtility.systemCopyBuffer = RequiredPackageGitUrl;
                        }

                        if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.CopyManifestEntry)))
                        {
                            EditorGUIUtility.systemCopyBuffer =
                                $"\"{RequiredPackageName}\": \"{RequiredPackageGitUrl}\"";
                        }
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            DrawActionBar();
        }

        private void DrawActionBar()
        {
            GUILayout.Space(4f);
            if (_dependencyInstalled)
            {
                if (AIChatSetupGui.PrimaryButton(Text(AIChatSetupTextKey.OpenProviderSetup)))
                {
                    OpenProviderSetup();
                }
            }
            else if (AIChatSetupGui.PrimaryButton(Text(AIChatSetupTextKey.OpenPackageManager)))
            {
                EditorApplication.ExecuteMenuItem("Window/Package Manager");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.OpenSampleScene)))
                {
                    OpenSampleScene();
                }

                if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.OpenReadme)))
                {
                    OpenReadme();
                }

                if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.Refresh)))
                {
                    RefreshStatus();
                }
            }
        }

        private void RefreshStatus()
        {
            _dependencyInstalled = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                $"Packages/{RequiredPackageName}") != null;
            _sampleRootPath = ResolveImportedSampleRootPath();
            _sampleScenePath = ResolveSampleScenePath(_sampleRootPath);
            _readmeAssetPath = ResolveReadmeAssetPath(_sampleRootPath);
            _providerConfigured = HasCurrentDeviceConfiguration();
            _language = AIChatSetupLocalization.CurrentLanguage;
            UpdateWindowTitle();
            Repaint();
        }

        private void OpenProviderSetup()
        {
            if (EditorApplication.ExecuteMenuItem(ProviderSetupMenuPath))
            {
                return;
            }

            EditorUtility.DisplayDialog(
                Text(AIChatSetupTextKey.ProviderSetupUnavailableTitle),
                Text(AIChatSetupTextKey.ProviderSetupUnavailableMessage),
                Text(AIChatSetupTextKey.Ok));
        }

        private void OpenSampleScene()
        {
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(_sampleScenePath);
            if (scene != null)
            {
                AssetDatabase.OpenAsset(scene);
                return;
            }

            EditorUtility.DisplayDialog(
                Text(AIChatSetupTextKey.SampleSceneMissingTitle),
                Text(AIChatSetupTextKey.SampleSceneMissingMessage),
                Text(AIChatSetupTextKey.Ok));
        }

        private void OpenReadme()
        {
            AIChatSampleReadme readme = AssetDatabase.LoadAssetAtPath<AIChatSampleReadme>(_readmeAssetPath);
            if (readme != null)
            {
                Selection.activeObject = readme;
                EditorGUIUtility.PingObject(readme);
                return;
            }

            EditorUtility.DisplayDialog(
                Text(AIChatSetupTextKey.ReadmeMissingTitle),
                Text(AIChatSetupTextKey.ReadmeMissingMessage),
                Text(AIChatSetupTextKey.Ok));
        }

        private string ResolveImportedSampleRootPath()
        {
            MonoScript script = MonoScript.FromScriptableObject(this);
            string scriptPath = AssetDatabase.GetAssetPath(script);
            const string marker = "/Samantha/Code/Editor/Bootstrap/";
            int markerIndex = scriptPath.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex <= 0)
            {
                return string.Empty;
            }

            string root = scriptPath.Substring(0, markerIndex);
            return root.StartsWith("Assets/", StringComparison.Ordinal) ? root : string.Empty;
        }

        private static string ResolveSampleScenePath(string sampleRootPath)
        {
            string path = sampleRootPath + "/Samantha/Samantha.unity";
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null ? path : string.Empty;
        }

        private static string ResolveReadmeAssetPath(string sampleRootPath)
        {
            string path = sampleRootPath + "/AIChatSampleReadme.asset";
            return AssetDatabase.LoadAssetAtPath<AIChatSampleReadme>(path) != null ? path : string.Empty;
        }

        private static bool HasCurrentDeviceConfiguration()
        {
            string path = Path.Combine(
                Application.persistentDataPath,
                ResolveRuntimeConfigFileName());
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var status = JsonUtility.FromJson<RuntimeConfigStatus>(File.ReadAllText(path));
                return status != null &&
                       status.SchemaVersion == CurrentSchemaVersion &&
                       !string.IsNullOrWhiteSpace(status.ApiKey) &&
                       !string.IsNullOrWhiteSpace(status.ApiBaseUrl) &&
                       !string.IsNullOrWhiteSpace(status.LlmModel);
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveRuntimeConfigFileName()
        {
            Type controllerType = Type.GetType(ControllerTypeName, throwOnError: false);
            if (controllerType == null)
            {
                return DefaultRuntimeConfigFileName;
            }

            UnityEngine.Object[] controllers = Resources.FindObjectsOfTypeAll(controllerType);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (!(controllers[i] is Component controller) ||
                    EditorUtility.IsPersistent(controller) ||
                    !controller.gameObject.scene.IsValid())
                {
                    continue;
                }

                var serializedController = new SerializedObject(controller);
                SerializedProperty config = serializedController.FindProperty("_config");
                SerializedProperty fileName = config?.FindPropertyRelative("RuntimeConfigFileName");
                if (!string.IsNullOrWhiteSpace(fileName?.stringValue))
                {
                    return fileName.stringValue.Trim();
                }
            }

            return DefaultRuntimeConfigFileName;
        }

        [Serializable]
        private sealed class RuntimeConfigStatus
        {
            public int SchemaVersion = 0;
            public string ApiKey = string.Empty;
            public string ApiBaseUrl = string.Empty;
            public string LlmModel = string.Empty;
        }

        private void UpdateWindowTitle()
        {
            titleContent = new GUIContent(Text(AIChatSetupTextKey.SetupWindowTitle));
        }

        private string Text(AIChatSetupTextKey key, params object[] args)
        {
            return AIChatSetupLocalization.Text(key, _language, args);
        }
    }
}

#endif
