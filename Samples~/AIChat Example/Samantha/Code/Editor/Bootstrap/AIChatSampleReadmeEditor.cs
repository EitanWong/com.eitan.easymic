#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CustomEditor(typeof(AIChatSampleReadme))]
    internal sealed class AIChatSampleReadmeEditor : UnityEditor.Editor
    {
        private const string SetupMenuPath = "Tools/EasyMic/AI Chat/Setup";
        private const string ProviderSetupMenuPath = "Tools/EasyMic/AI Chat/Provider Setup";

        private AIChatSetupLanguage _language;

        protected override void OnHeaderGUI()
        {
            _language = AIChatSetupLocalization.CurrentLanguage;
            AIChatSampleReadme.Document document = Readme.GetDocument(_language);
            AIChatSetupGui.DrawHero(
                "d_UnityEditor.InspectorWindow",
                document?.Title ?? string.Empty,
                document?.Summary ?? string.Empty,
                Text(AIChatSetupTextKey.Latest));
        }

        public override void OnInspectorGUI()
        {
            _language = AIChatSetupLocalization.CurrentLanguage;
            AIChatSampleReadme.Document document = Readme.GetDocument(_language);

            AIChatSetupGui.DrawContract(
                Text(AIChatSetupTextKey.CurrentContract),
                Text(AIChatSetupTextKey.CurrentContractDetail, AIChatRuntimeConfigSchemaVersion));

            if (document != null)
            {
                AIChatSampleReadme.Section[] sections = document.Sections;
                for (int i = 0; i < sections.Length; i++)
                {
                    DrawSection(i + 1, sections[i]);
                }
            }

            GUILayout.Space(4f);
            if (AIChatSetupGui.PrimaryButton(Text(AIChatSetupTextKey.OpenProviderSetup)))
            {
                if (!EditorApplication.ExecuteMenuItem(ProviderSetupMenuPath))
                {
                    EditorApplication.ExecuteMenuItem(SetupMenuPath);
                }
            }

            if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.OpenSampleScene)))
            {
                OpenSampleScene();
            }

            if (AIChatSetupGui.SecondaryButton(Text(AIChatSetupTextKey.FullDocumentation)))
            {
                OpenMarkdownDocument();
            }
        }

        private const int AIChatRuntimeConfigSchemaVersion = 4;

        private AIChatSampleReadme Readme => (AIChatSampleReadme)target;

        private void DrawSection(int index, AIChatSampleReadme.Section section)
        {
            if (section == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(AIChatSetupGui.CardStyle))
            {
                AIChatSetupGui.DrawSectionHeader(index, section.Heading);
                GUILayout.Label(section.Body, AIChatSetupGui.BodyStyle);

                if (!string.IsNullOrWhiteSpace(section.LinkLabel) &&
                    !string.IsNullOrWhiteSpace(section.Url) &&
                    GUILayout.Button(section.LinkLabel, EditorStyles.linkLabel))
                {
                    Application.OpenURL(section.Url);
                }
            }
        }

        private void OpenSampleScene()
        {
            string scenePath = SampleRootPath + "/Samantha/Samantha.unity";
            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (scene != null)
            {
                AssetDatabase.OpenAsset(scene);
            }
        }

        private void OpenMarkdownDocument()
        {
            string fileName = _language == AIChatSetupLanguage.ChineseSimplified
                ? "README_zh-CN.md"
                : "README.md";
            UnityEngine.Object document = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                SampleRootPath + "/" + fileName);
            if (document == null)
            {
                return;
            }

            Selection.activeObject = document;
            EditorGUIUtility.PingObject(document);
            AssetDatabase.OpenAsset(document);
        }

        private string SampleRootPath
        {
            get
            {
                string assetPath = AssetDatabase.GetAssetPath(Readme);
                return (Path.GetDirectoryName(assetPath) ?? string.Empty).Replace('\\', '/');
            }
        }

        private string Text(AIChatSetupTextKey key, params object[] args)
        {
            return AIChatSetupLocalization.Text(key, _language, args);
        }
    }
}

#endif
