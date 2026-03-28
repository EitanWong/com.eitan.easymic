#if EASYMIC_APM_INTEGRATION
namespace Eitan.EasyMic.Editor.Inspectors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    internal sealed class EasyMicApmProjectSettingsProvider : SettingsProvider
    {
        private const float TokenTextAreaHeight = 120f;
        private const float VerticalSpacing = 6f;
        private const float ActionButtonHeight = 22f;
        private const string TokenInputModeSessionKey = "Eitan.EasyMic.Apm.TokenInputMode";

        private string _tokenInput;
        private string _tokenImportPath;
        private TokenInputMode _tokenInputMode;
        private bool _stateLoaded;

        private enum TokenInputMode
        {
            ImportFile = 0,
            ManualPaste = 1
        }

        private EasyMicApmProjectSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new EasyMicApmProjectSettingsProvider(EasyMicApmLicenseEditorUtility.ProjectSettingsPath, SettingsScope.Project)
            {
                keywords = new HashSet<string>
                {
                    "EasyMic",
                    "APM",
                    "License",
                    "Token",
                    "AEC",
                    "ANS",
                    "AGC"
                }
            };
        }

        public override void OnGUI(string searchContext)
        {
            EnsureStateLoaded();

            bool hasTokenSource = EasyMicApmLicenseEditorUtility.HasConfiguredTokenSource();
            bool hasGeneratedProvider = EasyMicApmLicenseEditorUtility.HasGeneratedProviderFile();
            bool hasAnyProviderScript = EasyMicApmLicenseEditorUtility.HasAnyProviderScript();
            bool hasExistingProvider = hasGeneratedProvider || hasTokenSource || hasAnyProviderScript;

            DrawHeader();
            EditorGUILayout.Space(4f);

            if (hasExistingProvider)
            {
                string providerAssetPath = EasyMicApmLicenseEditorUtility.GetCurrentProviderAssetPathOrDefault();
                DrawExistingProviderNotice();
                EditorGUILayout.Space(8f);

                using (new EditorGUILayout.VerticalScope(Styles.Card))
                {
                    EditorGUILayout.LabelField(
                        "Provider script detected. Edit this script directly to manage token loading and protection logic.",
                        Styles.MutedHint);
                    EditorGUILayout.Space(4f);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("Provider Script", providerAssetPath, Styles.ReadonlyPathField);
                    }

                    EditorGUILayout.Space(6f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Open Script", GUILayout.Height(ActionButtonHeight)))
                        {
                            if (!EasyMicApmLicenseEditorUtility.TryOpenGeneratedProviderScript())
                            {
                                EditorUtility.DisplayDialog("EasyMic APM", "Unable to open provider script. Check file existence and compile status.", "OK");
                            }
                        }

                        if (GUILayout.Button("Copy Path", GUILayout.Height(ActionButtonHeight)))
                        {
                            EditorGUIUtility.systemCopyBuffer = providerAssetPath;
                        }
                    }

                    EditorGUILayout.Space(6f);
                    EditorGUILayout.LabelField(
                        "Recommendation: customize token loading logic and apply advanced security protections (for example, encryption/obfuscation) in that script.",
                        Styles.MutedHint);
                }

                return;
            }

            if (!hasExistingProvider)
            {
                DrawMissingProviderNotice();
                EditorGUILayout.Space(6f);
            }

            using (new EditorGUILayout.VerticalScope(Styles.Card))
            {
                DrawTokenModeSelector();
                EditorGUILayout.Space(VerticalSpacing);
                DrawTokenInputSection();

                EditorGUILayout.Space(10f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save License Provider", GUILayout.Height(ActionButtonHeight)))
                    {
                        if (EasyMicApmLicenseEditorUtility.TryCreateOrUpdateProviderScript(_tokenInput, out string info))
                        {
                            _tokenInput = string.Empty;
                            EditorUtility.DisplayDialog("EasyMic APM", info, "OK");
                        }
                        else if (!string.Equals(info, "Save canceled.", StringComparison.Ordinal))
                        {
                            EditorUtility.DisplayDialog("EasyMic APM", info, "OK");
                        }
                    }

                }

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "Security tip: default provider stores plaintext token. " +
                    "Prefer private repositories or custom token retrieval/encryption in generated code.",
                    Styles.MutedHint);
            }
        }

        private void EnsureStateLoaded()
        {
            if (_stateLoaded)
            {
                return;
            }

            if (_tokenInput == null)
            {
                _tokenInput = string.Empty;
            }

            if (_tokenImportPath == null)
            {
                _tokenImportPath = string.Empty;
            }

            _tokenInputMode = (TokenInputMode)SessionState.GetInt(TokenInputModeSessionKey, (int)TokenInputMode.ImportFile);
            if (!Enum.IsDefined(typeof(TokenInputMode), _tokenInputMode))
            {
                _tokenInputMode = TokenInputMode.ImportFile;
            }

            _stateLoaded = true;
        }

        private void DrawTokenModeSelector()
        {
            int selected = GUILayout.Toolbar((int)_tokenInputMode, new[] { "Import From File", "Manual Paste" });
            if (selected != (int)_tokenInputMode)
            {
                _tokenInputMode = (TokenInputMode)selected;
                SessionState.SetInt(TokenInputModeSessionKey, (int)_tokenInputMode);
            }
        }

        private void DrawTokenInputSection()
        {
            if (_tokenInputMode == TokenInputMode.ImportFile)
            {
                DrawFileImportMode();
                return;
            }

            DrawManualPasteMode();
        }

        private void DrawFileImportMode()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(
                        string.IsNullOrWhiteSpace(_tokenImportPath) ? "<No file selected>" : _tokenImportPath,
                        Styles.PathField);
                }

                if (GUILayout.Button("Choose File", GUILayout.Width(98f), GUILayout.Height(ActionButtonHeight)))
                {
                    string startDir = string.Empty;
                    if (!string.IsNullOrWhiteSpace(_tokenImportPath))
                    {
                        try { startDir = Path.GetDirectoryName(_tokenImportPath) ?? string.Empty; } catch { }
                    }

                    string selectedFile = EditorUtility.OpenFilePanel("Choose License Token File", startDir, string.Empty);
                    if (!string.IsNullOrWhiteSpace(selectedFile))
                    {
                        _tokenImportPath = selectedFile;
                        if (TryImportTokenFromFile(_tokenImportPath, out string importedToken, out string importError))
                        {
                            _tokenInput = importedToken;
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("EasyMic APM", importError, "OK");
                        }
                    }
                }
            }

        }

        private void DrawManualPasteMode()
        {
            _tokenInput = EditorGUILayout.TextArea(_tokenInput ?? string.Empty, Styles.TokenTextArea, GUILayout.MinHeight(TokenTextAreaHeight));
        }

        private static void DrawHeader()
        {
            EditorGUILayout.LabelField("EasyMic APM License", Styles.HeaderLabel);
        }

        private static void DrawMissingProviderNotice()
        {
            using (new EditorGUILayout.HorizontalScope(Styles.NoticeBox))
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.warnicon"), GUILayout.Width(18f), GUILayout.Height(18f));
                EditorGUILayout.LabelField("No generated license provider found. Import token from file or paste manually, then generate one.", Styles.NoticeText);
            }
        }

        private static void DrawExistingProviderNotice()
        {
            using (new EditorGUILayout.HorizontalScope(Styles.NoticeBox))
            {
                GUILayout.Label(EditorGUIUtility.IconContent("console.infoicon"), GUILayout.Width(18f), GUILayout.Height(18f));
                EditorGUILayout.LabelField("License provider script detected. Open the script below and edit it directly.", Styles.NoticeText);
            }
        }

        private static class Styles
        {
            public static readonly GUIStyle HeaderLabel;
            public static readonly GUIStyle Card;
            public static readonly GUIStyle NoticeBox;
            public static readonly GUIStyle NoticeText;
            public static readonly GUIStyle PathField;
            public static readonly GUIStyle ReadonlyPathField;
            public static readonly GUIStyle TokenTextArea;
            public static readonly GUIStyle MutedHint;

            static Styles()
            {
                bool pro = EditorGUIUtility.isProSkin;

                HeaderLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13
                };

                Card = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 10, 10)
                };

                NoticeBox = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 8, 8)
                };

                NoticeText = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                {
                    richText = false,
                    normal = { textColor = pro ? new Color(0.88f, 0.88f, 0.88f, 1f) : new Color(0.16f, 0.16f, 0.16f, 1f) }
                };

                PathField = new GUIStyle(EditorStyles.textField);
                ReadonlyPathField = new GUIStyle(EditorStyles.textField);
                TokenTextArea = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true
                };

                MutedHint = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    normal = { textColor = pro ? new Color(0.68f, 0.68f, 0.68f, 1f) : new Color(0.38f, 0.38f, 0.38f, 1f) }
                };
            }
        }

        private static bool TryImportTokenFromFile(string filePath, out string token, out string error)
        {
            token = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "Token file path is empty.";
                return false;
            }

            if (!File.Exists(filePath))
            {
                error = "Token file does not exist: " + filePath;
                return false;
            }

            try
            {
                token = File.ReadAllText(filePath).Trim();
            }
            catch (Exception ex)
            {
                error = "Failed to read token file: " + ex.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Token file is empty.";
                return false;
            }

            return true;
        }
    }
}
#endif
