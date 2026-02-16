#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [InitializeOnLoad]
    internal static class AIChatDependencyGuidance
    {
        internal const string RequiredPackageName = "com.eitan.sherpa-onnx-unity";
        internal const string RequiredPackageGitUrl = "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm";

        private const string SessionIgnoreKey = "EasyMic.AIChat.DependencyReminder.SessionIgnored";
        private const string LogPrefix = "[EasyMic AIChat]";

        static AIChatDependencyGuidance()
        {
            EditorApplication.delayCall += CheckDependencyAndPrompt;
        }

        private static void CheckDependencyAndPrompt()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (!HasSampleContent())
            {
                return;
            }

            bool installed = IsDependencyInstalled();
            if (installed)
            {
                Debug.Log($"{LogPrefix} Dependency check passed: {RequiredPackageName} is installed.");
                return;
            }

            Debug.LogWarning($"{LogPrefix} Missing dependency: {RequiredPackageName}");

            if (SessionState.GetBool(SessionIgnoreKey, false))
            {
                return;
            }

            int action = EditorUtility.DisplayDialogComplex(
                "EasyMic AI Chat Dependency Missing",
                "The AIChat sample needs com.eitan.sherpa-onnx-unity.\n\n" +
                "Without it, AIChat runtime scripts/components are intentionally excluded. " +
                "Install the package to unlock the sample.",
                "Open Package Manager",
                "Copy Git URL",
                "Ignore This Session");

            switch (action)
            {
                case 0:
                    OpenPackageManagerSafely();
                    break;
                case 1:
                    EditorGUIUtility.systemCopyBuffer = RequiredPackageGitUrl;
                    Debug.Log($"{LogPrefix} Copied package git URL: {RequiredPackageGitUrl}");
                    break;
                default:
                    SessionState.SetBool(SessionIgnoreKey, true);
                    break;
            }
        }

        internal static bool HasSampleContent()
        {
            string samplesRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Samples");
            if (!Directory.Exists(samplesRoot))
            {
                return false;
            }

            try
            {
                return Directory
                    .GetDirectories(samplesRoot, "*", SearchOption.AllDirectories)
                    .Any(path =>
                    {
                        string name = Path.GetFileName(path);
                        return string.Equals(name, "AIChat Example", System.StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, "AIChat", System.StringComparison.OrdinalIgnoreCase);
                    });
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsDependencyInstalled()
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{RequiredPackageName}") != null;
            }
            catch
            {
                return Directory.Exists(Path.Combine("Packages", RequiredPackageName));
            }
        }

        private static void OpenPackageManagerSafely()
        {
            // Unity Package Manager UI can throw internal null refs if opened at an unstable editor timing.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    EditorApplication.ExecuteMenuItem("Window/Package Manager");
                }
                catch (Exception ex)
                {
                    EditorGUIUtility.systemCopyBuffer = RequiredPackageGitUrl;
                    Debug.LogWarning(
                        $"{LogPrefix} Failed to open Package Manager automatically ({ex.GetType().Name}: {ex.Message}). " +
                        $"Git URL copied to clipboard: {RequiredPackageGitUrl}");

                    EditorUtility.DisplayDialog(
                        "Package Manager Open Failed",
                        "Unity failed to open Package Manager in the current editor state.\n\n" +
                        "The required package git URL has been copied to your clipboard. " +
                        "Please open Package Manager manually from Window > Package Manager, then add from git URL.",
                        "OK");
                }
            };
        }
    }
}
#endif
