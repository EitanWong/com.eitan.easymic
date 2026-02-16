#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Samples.SherpaONNXUnity.ASR
{
    [InitializeOnLoad]
    internal static class SherpaONNXAsrDependencyGuidance
    {
        private const string PackageName = "com.eitan.sherpa-onnx-unity";
        private const string PackageGitUrl = "https://github.com/EitanWong/com.eitan.sherpa-onnx-unity.git#upm";
        private const string SessionIgnoreKey = "EasyMic.SherpaONNX.ASR.DependencyReminder.SessionIgnored";
        private const string LogPrefix = "[EasyMic ASR Sample]";

        static SherpaONNXAsrDependencyGuidance()
        {
            EditorApplication.delayCall += CheckDependencyAndPrompt;
        }

        private static void CheckDependencyAndPrompt()
        {
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (IsDependencyInstalled())
            {
                return;
            }

            Debug.LogWarning($"{LogPrefix} Missing dependency: {PackageName}");

            if (SessionState.GetBool(SessionIgnoreKey, false))
            {
                return;
            }

            int action = EditorUtility.DisplayDialogComplex(
                "SherpaONNX ASR Example Dependency Missing",
                "This sample requires com.eitan.sherpa-onnx-unity.\n\n" +
                "Install it to enable ASR components and scripts.",
                "Open Package Manager",
                "Copy Git URL",
                "Ignore This Session");

            switch (action)
            {
                case 0:
                    OpenPackageManagerSafely();
                    break;
                case 1:
                    EditorGUIUtility.systemCopyBuffer = PackageGitUrl;
                    Debug.Log($"{LogPrefix} Copied package git URL: {PackageGitUrl}");
                    break;
                default:
                    SessionState.SetBool(SessionIgnoreKey, true);
                    break;
            }
        }

        private static bool IsDependencyInstalled()
        {
            try
            {
                return UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{PackageName}") != null;
            }
            catch
            {
                return false;
            }
        }

        private static void OpenPackageManagerSafely()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    EditorApplication.ExecuteMenuItem("Window/Package Manager");
                }
                catch (Exception ex)
                {
                    EditorGUIUtility.systemCopyBuffer = PackageGitUrl;
                    Debug.LogWarning(
                        $"{LogPrefix} Failed to open Package Manager ({ex.GetType().Name}: {ex.Message}). " +
                        $"Git URL copied to clipboard: {PackageGitUrl}");
                }
            };
        }
    }
}
#endif
