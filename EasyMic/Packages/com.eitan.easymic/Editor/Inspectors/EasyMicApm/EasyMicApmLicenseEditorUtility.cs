#if EASYMIC_APM_INTEGRATION
namespace Eitan.EasyMic.Editor.Inspectors
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;

    internal static class EasyMicApmLicenseEditorUtility
    {
        public const string ProjectSettingsPath = "Project/Easy Mic/APM License";
        public const string GeneratedProviderAssetPath = "Assets/EasyMic/Scripts/EasyMicApmLicenseToken.cs";
        private const string GeneratedProviderPathSessionKey = "Eitan.EasyMic.Apm.GeneratedProviderAssetPath";
        private const string ProviderClassName = "EasyMicApmProjectLicenseProvider";
        private const string RuntimeAssemblyQualifiedType = "Eitan.EasyMic.Runtime.Apm.EasyMicApmLicenseRuntime, Eitan.EasyMic.Apm";
        private const string RuntimeHasConfiguredMethod = "HasConfiguredTokenSource";
        private const string RuntimeResetMethod = "ResetAuthorizationState";

        public static bool HasConfiguredTokenSource()
        {
            var runtimeType = Type.GetType(RuntimeAssemblyQualifiedType, throwOnError: false);
            if (runtimeType == null)
            {
                return false;
            }

            var method = runtimeType.GetMethod(RuntimeHasConfiguredMethod, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return false;
            }

            try
            {
                var result = method.Invoke(null, null);
                return result is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        public static bool HasGeneratedProviderFile()
        {
            string assetPath = GetCurrentProviderAssetPath();
            return !string.IsNullOrEmpty(assetPath) && File.Exists(GetAbsolutePath(assetPath));
        }

        public static bool HasAnyProviderScript()
        {
            return TryGetAnyProviderAssetPath(out _);
        }

        public static void OpenProjectSettingsPage()
        {
            SettingsService.OpenProjectSettings(ProjectSettingsPath);
        }

        public static bool TryOpenGeneratedProviderScript()
        {
            if (!TryGetAnyProviderAssetPath(out string assetPath) || string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            var asset = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            if (asset == null)
            {
                return false;
            }

            AssetDatabase.OpenAsset(asset);
            return true;
        }

        public static string GetCurrentProviderAssetPathOrDefault()
        {
            string assetPath = string.Empty;
            if (!TryGetAnyProviderAssetPath(out assetPath))
            {
                assetPath = GetCurrentProviderAssetPath();
            }

            return string.IsNullOrEmpty(assetPath) ? GeneratedProviderAssetPath : assetPath;
        }

        public static bool TryCreateOrUpdateProviderScript(string plainToken, out string info)
        {
            info = string.Empty;
            if (string.IsNullOrWhiteSpace(plainToken))
            {
                info = "License token is empty.";
                return false;
            }

            if (TryGetAnyProviderAssetPath(out string existingAssetPath))
            {
                info = "A license provider class already exists at: " + existingAssetPath + "\n\n" +
                       "Please update token manually in that file, or customize token retrieval logic there.\n" +
                       "If you want to reconfigure from this page, delete that .cs file first, then save again.";
                return false;
            }

            string selectedAssetPath = ShowSaveProviderPathDialog();
            if (string.IsNullOrEmpty(selectedAssetPath))
            {
                info = "Save canceled.";
                return false;
            }

            string source = BuildProviderSource(plainToken.Trim());
            string absolutePath = GetAbsolutePath(selectedAssetPath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(absolutePath, source, new UTF8Encoding(false));

            // Force script import first so runtime provider is recompiled.
            AssetDatabase.ImportAsset(selectedAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SessionState.SetString(GeneratedProviderPathSessionKey, selectedAssetPath);

            TryResetRuntimeAuthorizationCache();
            bool ignored = TryEnsureGitIgnoreRule(selectedAssetPath, out string gitIgnoreInfo);

            if (ignored)
            {
                info = "License provider script generated at: " + selectedAssetPath + "\n" +
                       "Default mode stores plaintext token; avoid leakage.\n" + gitIgnoreInfo;
            }
            else
            {
                info = "License provider script generated at: " + selectedAssetPath + "\n" +
                       "Default mode stores plaintext token; avoid leakage.\n" + gitIgnoreInfo;
            }

            return true;
        }

        private static string BuildProviderSource(string plainToken)
        {
            // This template is intentionally small and editable.
            // Users can replace constant assignment with cloud fetching or additional hardening.
            string escapedToken = EscapeForVerbatimCSharpLiteral(plainToken);

            var sb = new StringBuilder(1024);
            sb.AppendLine("namespace Eitan.EasyMic.Generated.Licensing");
            sb.AppendLine("{");
            sb.AppendLine("    using Eitan.EasyMic.Runtime.Apm;");
            sb.AppendLine("    using UnityEngine.Scripting;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// EasyMic APM license token provider.");
            sb.AppendLine("    /// IMPORTANT: this default template stores token in plaintext.");
            sb.AppendLine("    /// To reduce leakage risk, customize this file to fetch from server,");
            sb.AppendLine("    /// split key material, or perform your own encryption/decryption.");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    [Preserve]");
            sb.AppendLine("    internal sealed class EasyMicApmProjectLicenseProvider : IEasyMicApmLicenseTokenProvider");
            sb.AppendLine("    {");
            sb.AppendLine("        public int Priority => 0;");
            sb.AppendLine();
            sb.AppendLine("        private const string PlainLicenseToken = @\"" + escapedToken + "\";");
            sb.AppendLine();
            sb.AppendLine("        public bool TryGetLicenseToken(out string token)");
            sb.AppendLine("        {");
            sb.AppendLine("            // If you encrypt token yourself, decrypt it here and return final plaintext token.");
            sb.AppendLine("            token = PlainLicenseToken;");
            sb.AppendLine("            return !string.IsNullOrWhiteSpace(token);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void TryResetRuntimeAuthorizationCache()
        {
            var runtimeType = Type.GetType(RuntimeAssemblyQualifiedType, throwOnError: false);
            if (runtimeType == null)
            {
                return;
            }

            var resetMethod = runtimeType.GetMethod(RuntimeResetMethod, BindingFlags.NonPublic | BindingFlags.Static);
            if (resetMethod == null)
            {
                return;
            }

            try
            {
                resetMethod.Invoke(null, null);
            }
            catch
            {
                // ignored
            }
        }

        private static bool TryEnsureGitIgnoreRule(string assetPath, out string info)
        {
            info = ".gitignore not found; skipped auto-ignore.";

            string projectRoot;
            try
            {
                projectRoot = Directory.GetParent(Application.dataPath).FullName;
            }
            catch (Exception ex)
            {
                info = "Cannot resolve project root for .gitignore update: " + ex.Message;
                return false;
            }

            string gitIgnorePath = Path.Combine(projectRoot, ".gitignore");
            if (!File.Exists(gitIgnorePath))
            {
                return false;
            }

            string normalizedAssetPath = assetPath.Replace("\\", "/");
            string ignoreLine = "/" + normalizedAssetPath;
            string ignoreMetaLine = ignoreLine + ".meta";

            string[] existingLines = File.ReadAllLines(gitIgnorePath);
            bool hasTokenRule = false;
            bool hasMetaRule = false;

            for (int i = 0; i < existingLines.Length; i++)
            {
                string trimmed = existingLines[i].Trim();
                if (trimmed == ignoreLine)
                {
                    hasTokenRule = true;
                }
                else if (trimmed == ignoreMetaLine)
                {
                    hasMetaRule = true;
                }
            }

            if (hasTokenRule && hasMetaRule)
            {
                info = ".gitignore already contains token ignore rules.";
                return true;
            }

            using (var writer = File.AppendText(gitIgnorePath))
            {
                if (!hasTokenRule)
                {
                    writer.WriteLine(ignoreLine);
                }

                if (!hasMetaRule)
                {
                    writer.WriteLine(ignoreMetaLine);
                }
            }

            AssetDatabase.Refresh();
            info = ".gitignore updated with token ignore rules.";
            return true;
        }

        private static string GetCurrentProviderAssetPath()
        {
            if (TryGetAnyProviderAssetPath(out string existingAssetPath))
            {
                return existingAssetPath;
            }

            string cached = SessionState.GetString(GeneratedProviderPathSessionKey, GeneratedProviderAssetPath);
            if (string.IsNullOrWhiteSpace(cached))
            {
                return GeneratedProviderAssetPath;
            }

            return NormalizeAssetPath(cached);
        }

        private static string ShowSaveProviderPathDialog()
        {
            string suggestedPath = GetCurrentProviderAssetPathOrDefault();
            string suggestedFolder = Path.GetDirectoryName(suggestedPath);
            string suggestedName = Path.GetFileNameWithoutExtension(suggestedPath);

            if (string.IsNullOrWhiteSpace(suggestedFolder))
            {
                suggestedFolder = "Assets";
            }

            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                suggestedName = "EasyMicApmLicenseToken.g";
            }

            string savePath = EditorUtility.SaveFilePanelInProject(
                "Save EasyMic APM License Provider",
                suggestedName,
                "cs",
                "Choose where to save the license provider script.",
                suggestedFolder);

            return NormalizeAssetPath(savePath);
        }

        private static bool TryFindExistingProviderScript(out string assetPath)
        {
            assetPath = string.Empty;
            string[] guids = AssetDatabase.FindAssets(ProviderClassName + " t:Script");
            if (guids == null || guids.Length == 0)
            {
                return false;
            }

            Array.Sort(guids, StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(candidatePath) || !candidatePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath = GetAbsolutePath(candidatePath);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(fullPath);
                }
                catch
                {
                    continue;
                }

                if (content.IndexOf("class " + ProviderClassName, StringComparison.Ordinal) >= 0)
                {
                    assetPath = NormalizeAssetPath(candidatePath);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetAnyProviderAssetPath(out string assetPath)
        {
            assetPath = string.Empty;

            if (TryFindExistingProviderScript(out string exactPath))
            {
                assetPath = exactPath;
                return true;
            }

            string[] guids = AssetDatabase.FindAssets("t:Script", new[] { "Assets" });
            if (guids == null || guids.Length == 0)
            {
                return false;
            }

            Array.Sort(guids, StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(candidatePath) || !candidatePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath = GetAbsolutePath(candidatePath);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(fullPath);
                }
                catch
                {
                    continue;
                }

                if (content.IndexOf("IEasyMicApmLicenseTokenProvider", StringComparison.Ordinal) >= 0 &&
                    content.IndexOf("class ", StringComparison.Ordinal) >= 0)
                {
                    assetPath = NormalizeAssetPath(candidatePath);
                    return true;
                }
            }

            return false;
        }

        private static string GetAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            return assetPath.Replace("\\", "/");
        }

        private static string EscapeForVerbatimCSharpLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\"", "\"\"");
        }
    }
}
#endif
