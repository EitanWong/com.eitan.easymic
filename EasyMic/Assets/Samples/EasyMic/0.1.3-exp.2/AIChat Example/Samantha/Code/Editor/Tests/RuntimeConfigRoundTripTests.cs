#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System;
using System.IO;
using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class RuntimeConfigRoundTripTests
    {
        [Test]
        public void SaveAndLoad_ShouldRoundTripAllFields()
        {
            var store = new JsonAIChatRuntimeConfigStore();
            string path = Path.Combine(Path.GetTempPath(), $"ai_chat_runtime_{Guid.NewGuid():N}.json");

            try
            {
                var input = new AIChatRuntimeConfig
                {
                    ApiKey = "test-key",
                    ApiBaseUrl = "https://example.com/v1/",
                    LlmModel = "gpt-4.1-mini",
                    LlmTemperature = 0.3f,
                    TtsModel = "tts-1",
                    TtsVoice = "alloy",
                    UseLocalTts = 1,
                    AsrRecognitionModeIndex = 2,
                    AsrStreamingModelId = "stream-model",
                    AsrOfflineModelId = "offline-model",
                    AsrVadModelId = "silero-vad-v5",
                    AsrEnablePunctuation = 1,
                    AsrPunctuationModelId = "punct-model",
                    LocalTtsModelId = "local-model",
                    LocalTtsVoiceId = 2,
                    LocalTtsSpeed = 1.2f,
                    LocalTtsSampleRate = 22050
                };

                bool saveOk = store.TrySave(path, input, out string saveError);
                Assert.IsTrue(saveOk, saveError);

                bool loadOk = store.TryLoad(path, out var output);
                Assert.IsTrue(loadOk);
                Assert.NotNull(output);
                Assert.AreEqual(input.ApiKey, output.ApiKey);
                Assert.AreEqual(input.ApiBaseUrl, output.ApiBaseUrl);
                Assert.AreEqual(input.LlmModel, output.LlmModel);
                Assert.AreEqual(input.LlmTemperature, output.LlmTemperature);
                Assert.AreEqual(input.TtsModel, output.TtsModel);
                Assert.AreEqual(input.TtsVoice, output.TtsVoice);
                Assert.AreEqual(input.UseLocalTts, output.UseLocalTts);
                Assert.AreEqual(input.AsrRecognitionModeIndex, output.AsrRecognitionModeIndex);
                Assert.AreEqual(input.AsrStreamingModelId, output.AsrStreamingModelId);
                Assert.AreEqual(input.AsrOfflineModelId, output.AsrOfflineModelId);
                Assert.AreEqual(input.AsrVadModelId, output.AsrVadModelId);
                Assert.AreEqual(input.AsrEnablePunctuation, output.AsrEnablePunctuation);
                Assert.AreEqual(input.AsrPunctuationModelId, output.AsrPunctuationModelId);
                Assert.AreEqual(input.LocalTtsModelId, output.LocalTtsModelId);
                Assert.AreEqual(input.LocalTtsVoiceId, output.LocalTtsVoiceId);
                Assert.AreEqual(input.LocalTtsSpeed, output.LocalTtsSpeed);
                Assert.AreEqual(input.LocalTtsSampleRate, output.LocalTtsSampleRate);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
#endif
