#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.Reflection;
using NUnit.Framework;
using Radishmouse;
using UnityEngine;
using UnityEngine.Serialization;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class AIChatProviderPresetTests
    {
        [Test]
        public void SiliconFlow_ShouldExposeTheSampleDefaults()
        {
            AIChatProviderPreset preset = AIChatProviderPresets.Get(AIChatProviderPresetKind.SiliconFlow);

            Assert.AreEqual("https://api.siliconflow.cn/v1/", preset.ApiBaseUrl);
            Assert.AreEqual("Qwen/Qwen3.5-9B", preset.LlmModel);
            Assert.AreEqual("FunAudioLLM/CosyVoice2-0.5B", preset.TtsModel);
            Assert.AreEqual("FunAudioLLM/CosyVoice2-0.5B:alex", preset.TtsVoice);
            Assert.IsTrue(preset.SupportsRemoteTts);
        }

        [Test]
        public void OpenAI_ShouldExposeCurrentOfficialDefaults()
        {
            AIChatProviderPreset preset = AIChatProviderPresets.OpenAICompatible;

            Assert.AreEqual("https://api.openai.com/v1/", preset.ApiBaseUrl);
            Assert.AreEqual("gpt-5.4", preset.LlmModel);
            Assert.AreEqual("gpt-4o-mini-tts", preset.TtsModel);
            Assert.AreEqual("marin", preset.TtsVoice);
            Assert.IsTrue(preset.SupportsRemoteTts);
        }

        [Test]
        public void NormalizeApiBaseUrl_ShouldTrimAndEnsureTrailingSlash()
        {
            string normalized = AIChatProviderPresets.NormalizeApiBaseUrl("  https://provider.example/api/v1  ");

            Assert.AreEqual("https://provider.example/api/v1/", normalized);
        }

        [TestCase("https://api.siliconflow.cn/v1/", AIChatProviderPresetKind.SiliconFlow)]
        [TestCase("https://tenant.siliconflow.cn/v1/", AIChatProviderPresetKind.SiliconFlow)]
        [TestCase("https://siliconflow.cn.evil.example/v1/", AIChatProviderPresetKind.OpenAICompatible)]
        [TestCase("https://provider.example/v1/", AIChatProviderPresetKind.OpenAICompatible)]
        public void ResolveKind_ShouldUseTheNormalizedProviderHost(
            string baseUrl,
            AIChatProviderPresetKind expected)
        {
            Assert.AreEqual(expected, AIChatProviderPresets.ResolveKind(baseUrl));
        }

        [Test]
        public void ApplyTo_ShouldKeepRemoteTtsConfigurationWhenRequested()
        {
            var config = new AIChatControllerConfig();
            AIChatProviderPreset preset = AIChatProviderPresets.Get(AIChatProviderPresetKind.SiliconFlow);

            preset.ApplyTo(config, useLocalTts: false);

            Assert.AreEqual(preset.ApiBaseUrl, config.ApiBaseUrl);
            Assert.AreEqual(preset.LlmModel, config.LlmModel);
            Assert.IsFalse(config.UseLocalTts);
            Assert.AreEqual(preset.TtsModel, config.TtsModel);
            Assert.AreEqual(preset.TtsVoice, config.TtsVoice);
            Assert.IsTrue(config.UseStreamingTts);
        }

        [Test]
        public void ControllerAndPolicyDefaults_ShouldUseTheSharedOpenAiPreset()
        {
            AIChatProviderPreset preset = AIChatProviderPresets.OpenAICompatible;
            var config = new AIChatControllerConfig();

            Assert.AreEqual(preset.ApiBaseUrl, config.ApiBaseUrl);
            Assert.AreEqual(preset.LlmModel, config.LlmModel);
            Assert.AreEqual(preset.TtsModel, config.TtsModel);
            Assert.AreEqual(preset.TtsVoice, config.TtsVoice);

            var gameObject = new GameObject("AIChatPolicyTest");
            try
            {
                var policy = gameObject.AddComponent<AIChatConfigurationPolicy>();
                config.ApiBaseUrl = "https://custom.example/v1/";
                config.LlmModel = "custom-model";
                policy.ApplyTo(config);

                Assert.AreEqual(preset.ApiBaseUrl, config.ApiBaseUrl);
                Assert.AreEqual(preset.LlmModel, config.LlmModel);
                Assert.AreEqual(preset.TtsModel, config.TtsModel);
                Assert.AreEqual(preset.TtsVoice, config.TtsVoice);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Policy_ShouldNotSerializeCredentials()
        {
            FieldInfo[] fields = typeof(AIChatConfigurationPolicy).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo[] serializedCredentialFields = Array.FindAll(
                fields,
                field => field.GetCustomAttribute<SerializeField>() != null &&
                         field.Name.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0);

            Assert.IsEmpty(serializedCredentialFields);
        }

        [Test]
        public void UiLineRenderer_ShouldNotDeclareFormerSerializedNames()
        {
            FieldInfo field = typeof(UILineRenderer).GetField(
                "pointsInPivotSpace",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotNull(field);
            Assert.IsEmpty(field.GetCustomAttributes(typeof(FormerlySerializedAsAttribute), false));
        }
    }
}
#endif
