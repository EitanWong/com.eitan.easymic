#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class AIChatSetupLocalizationTests
    {
        [TestCase("zh-Hans", "", SystemLanguage.English, AIChatSetupLanguage.ChineseSimplified)]
        [TestCase("ChineseSimplified", "", SystemLanguage.English, AIChatSetupLanguage.ChineseSimplified)]
        [TestCase("en", "Chinese", SystemLanguage.ChineseSimplified, AIChatSetupLanguage.English)]
        [TestCase("", "zh_CN", SystemLanguage.English, AIChatSetupLanguage.ChineseSimplified)]
        [TestCase("", "", SystemLanguage.ChineseSimplified, AIChatSetupLanguage.ChineseSimplified)]
        [TestCase("", "", SystemLanguage.Japanese, AIChatSetupLanguage.English)]
        [TestCase("en-US", "", SystemLanguage.ChineseSimplified, AIChatSetupLanguage.English)]
        [TestCase("fuzhion", "", SystemLanguage.English, AIChatSetupLanguage.English)]
        [TestCase("ChineseFake", "", SystemLanguage.English, AIChatSetupLanguage.English)]
        public void ResolveLanguage_ShouldPreferEditorLocaleAndFallbackPredictably(
            string selectedEditorLanguage,
            string currentEditorLanguage,
            SystemLanguage systemLanguage,
            AIChatSetupLanguage expected)
        {
            AIChatSetupLanguage actual = AIChatSetupLocalization.ResolveLanguage(
                selectedEditorLanguage,
                currentEditorLanguage,
                systemLanguage);

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void TextTables_ShouldCoverEveryKeyInBothLanguages()
        {
            foreach (AIChatSetupTextKey key in Enum.GetValues(typeof(AIChatSetupTextKey)))
            {
                AssertLocalized(key, AIChatSetupLanguage.English);
                AssertLocalized(key, AIChatSetupLanguage.ChineseSimplified);
            }
        }

        private static void AssertLocalized(AIChatSetupTextKey key, AIChatSetupLanguage language)
        {
            string value = AIChatSetupLocalization.Text(key, language);

            Assert.IsTrue(AIChatSetupLocalization.HasText(key, language), $"Missing {language} text for {key}.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(value), $"Missing {language} text for {key}.");
        }
    }
}

#endif
