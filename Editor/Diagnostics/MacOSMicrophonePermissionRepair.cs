#if UNITY_EDITOR_OSX
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Eitan.EasyMic.Editor.Diagnostics
{
    [InitializeOnLoad]
    internal static class MacOSMicrophonePermissionRepair
    {
        private const string UsageKey = "NSMicrophoneUsageDescription";
        private const string UsageDescription = "Unity Editor needs microphone access for projects that record audio.";
        private const string PromptPrefKey = "Eitan.EasyMic.MacOSMicrophonePermissionRepair.PromptedEditorPath";

        static MacOSMicrophonePermissionRepair()
        {
            EditorApplication.delayCall += PromptIfCurrentEditorNeedsRepair;
        }

        [MenuItem("Window/EasyMic/macOS Microphone Permission Repair")]
        public static void OpenRepairDialog()
        {
            var status = GetStatus();
            if (!status.IsMacEditor)
            {
                EditorUtility.DisplayDialog("EasyMic", "This repair is only needed in the macOS Unity Editor.", "OK");
                return;
            }

            if (!status.NeedsInfoPlistRepair)
            {
                bool resetOnly = EditorUtility.DisplayDialog(
                    "EasyMic macOS Microphone Permission",
                    "This Unity Editor already has NSMicrophoneUsageDescription. Reset microphone permission anyway so macOS can prompt again?",
                    "Reset Permission",
                    "Cancel");

                if (resetOnly)
                {
                    ResetTcc(status.BundleIdentifier);
                    EditorUtility.DisplayDialog("EasyMic", "Microphone permission was reset. Restart Unity Editor, then allow microphone access when prompted.", "OK");
                }

                return;
            }

            bool repair = EditorUtility.DisplayDialog(
                "EasyMic macOS Microphone Permission",
                "This Unity Editor app is missing NSMicrophoneUsageDescription. macOS can start CoreAudio capture but deliver silent buffers.\n\n" +
                "EasyMic can add the missing plist entry, ad-hoc re-sign Unity.app, and reset microphone permission for this Editor bundle. " +
                "You will need to restart Unity Editor and allow microphone access when macOS prompts.",
                "Repair Unity Editor",
                "Cancel");

            if (repair)
            {
                RepairCurrentEditor(status, showSuccessDialog: true);
            }
        }

        private static void PromptIfCurrentEditorNeedsRepair()
        {
            var status = GetStatus();
            if (!status.IsMacEditor || !status.NeedsInfoPlistRepair)
            {
                return;
            }

            if (EditorPrefs.GetString(PromptPrefKey, string.Empty) == status.EditorAppPath)
            {
                return;
            }

            EditorPrefs.SetString(PromptPrefKey, status.EditorAppPath);
            bool repair = EditorUtility.DisplayDialog(
                "EasyMic macOS Microphone Permission",
                "EasyMic detected that this Unity Editor app is missing NSMicrophoneUsageDescription. On macOS this can cause native microphone capture to return silence even when Unity reports permission as granted.\n\n" +
                "Repair this Editor now?",
                "Repair",
                "Later");

            if (repair)
            {
                RepairCurrentEditor(status, showSuccessDialog: true);
            }
        }

        private static EditorMicPermissionStatus GetStatus()
        {
            string editorAppPath = EditorApplication.applicationPath;
            if (string.IsNullOrEmpty(editorAppPath) || !editorAppPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return new EditorMicPermissionStatus(false, editorAppPath, string.Empty, string.Empty, false);
            }

            string infoPlistPath = Path.Combine(editorAppPath, "Contents", "Info.plist");
            string bundleId = ReadPlistValue(infoPlistPath, "CFBundleIdentifier");
            bool hasUsageDescription = !string.IsNullOrWhiteSpace(ReadPlistValue(infoPlistPath, UsageKey));

            return new EditorMicPermissionStatus(
                true,
                editorAppPath,
                infoPlistPath,
                bundleId,
                !hasUsageDescription);
        }

        private static void RepairCurrentEditor(EditorMicPermissionStatus status, bool showSuccessDialog)
        {
            try
            {
                if (string.IsNullOrEmpty(status.InfoPlistPath) || !File.Exists(status.InfoPlistPath))
                {
                    throw new FileNotFoundException("Unity Editor Info.plist was not found.", status.InfoPlistPath);
                }

                if (!CanWrite(status.InfoPlistPath))
                {
                    throw new UnauthorizedAccessException("Unity Editor Info.plist is not writable. Run Unity from a writable install location or repair it manually with administrator privileges.");
                }

                EditorUtility.DisplayProgressBar("EasyMic", "Updating Unity Editor Info.plist...", 0.2f);
                string backupPath = status.InfoPlistPath + ".easymic-backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(status.InfoPlistPath, backupPath, overwrite: false);
                }

                AddOrSetPlistValue(status.InfoPlistPath, UsageKey, UsageDescription);

                EditorUtility.DisplayProgressBar("EasyMic", "Re-signing Unity Editor...", 0.55f);
                RunProcess("/usr/bin/codesign", "--force", "--deep", "--sign", "-", status.EditorAppPath);

                EditorUtility.DisplayProgressBar("EasyMic", "Resetting microphone permission...", 0.85f);
                ResetTcc(status.BundleIdentifier);

                if (showSuccessDialog)
                {
                    EditorUtility.DisplayDialog(
                        "EasyMic",
                        "Unity Editor was repaired for macOS microphone capture.\n\nRestart Unity Editor, then allow microphone access when macOS prompts.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("EasyMic macOS microphone permission repair failed: " + ex.Message);
                EditorUtility.DisplayDialog("EasyMic", "Failed to repair Unity Editor microphone permission:\n\n" + ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ResetTcc(string bundleIdentifier)
        {
            if (string.IsNullOrWhiteSpace(bundleIdentifier))
            {
                return;
            }

            RunProcess("/usr/bin/tccutil", "reset", "Microphone", bundleIdentifier);
        }

        private static string ReadPlistValue(string plistPath, string key)
        {
            if (string.IsNullOrEmpty(plistPath) || !File.Exists(plistPath))
            {
                return string.Empty;
            }

            var result = RunProcess("/usr/libexec/PlistBuddy", new[] { "-c", $"Print :{key}", plistPath }, allowFailure: true);
            return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
        }

        private static void AddOrSetPlistValue(string plistPath, string key, string value)
        {
            var add = RunProcess("/usr/libexec/PlistBuddy", new[] { "-c", $"Add :{key} string {value}", plistPath }, allowFailure: true);
            if (add.ExitCode != 0)
            {
                RunProcess("/usr/libexec/PlistBuddy", "-c", $"Set :{key} {value}", plistPath);
            }

            RunProcess("/usr/bin/plutil", "-lint", plistPath);
        }

        private static bool CanWrite(string path)
        {
            try
            {
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static ProcessResult RunProcess(string fileName, params string[] args)
        {
            return RunProcess(fileName, args, allowFailure: false);
        }

        private static ProcessResult RunProcess(string fileName, string[] args, bool allowFailure)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start process: " + fileName);
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                var result = new ProcessResult(process.ExitCode, stdout, stderr);
                if (!allowFailure && result.ExitCode != 0)
                {
                    throw new InvalidOperationException($"{Path.GetFileName(fileName)} failed with exit code {result.ExitCode}: {result.StdErr}{result.StdOut}");
                }

                return result;
            }
        }

        private readonly struct EditorMicPermissionStatus
        {
            public readonly bool IsMacEditor;
            public readonly string EditorAppPath;
            public readonly string InfoPlistPath;
            public readonly string BundleIdentifier;
            public readonly bool NeedsInfoPlistRepair;

            public EditorMicPermissionStatus(bool isMacEditor, string editorAppPath, string infoPlistPath, string bundleIdentifier, bool needsInfoPlistRepair)
            {
                IsMacEditor = isMacEditor;
                EditorAppPath = editorAppPath;
                InfoPlistPath = infoPlistPath;
                BundleIdentifier = bundleIdentifier;
                NeedsInfoPlistRepair = needsInfoPlistRepair;
            }
        }

        private readonly struct ProcessResult
        {
            public readonly int ExitCode;
            public readonly string StdOut;
            public readonly string StdErr;

            public ProcessResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut ?? string.Empty;
                StdErr = stdErr ?? string.Empty;
            }
        }
    }
}
#endif
