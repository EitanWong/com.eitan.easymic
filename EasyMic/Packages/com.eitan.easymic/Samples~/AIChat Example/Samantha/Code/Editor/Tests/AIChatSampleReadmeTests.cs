#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using NUnit.Framework;
using UnityEditor;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class AIChatSampleReadmeTests
    {
        [Test]
        public void ImportedReadmeAssets_ShouldContainCompleteLocalizedDocuments()
        {
            string[] guids = AssetDatabase.FindAssets("t:AIChatSampleReadme");
            Assert.IsNotEmpty(guids, "The imported AI Chat sample must include its README asset.");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var readme = AssetDatabase.LoadAssetAtPath<AIChatSampleReadme>(path);
                Assert.NotNull(readme, path);
                AssertDocument(readme.GetDocument(AIChatSetupLanguage.English), path, "English");
                AssertDocument(readme.GetDocument(AIChatSetupLanguage.ChineseSimplified), path, "ChineseSimplified");
            }
        }

        private static void AssertDocument(
            AIChatSampleReadme.Document document,
            string path,
            string language)
        {
            Assert.NotNull(document, $"{path} is missing {language} content.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(document.Title));
            Assert.IsFalse(string.IsNullOrWhiteSpace(document.Summary));
            Assert.That(document.Sections, Has.Length.GreaterThanOrEqualTo(4));

            foreach (AIChatSampleReadme.Section section in document.Sections)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(section.Heading));
                Assert.IsFalse(string.IsNullOrWhiteSpace(section.Body));
            }
        }
    }
}

#endif
