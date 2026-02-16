using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class SiliconFlowExpressiveTtsInputPluginTests
    {
        [Test]
        public void FormatInput_ShouldInjectInstructionMarkerAndAutoMarkers()
        {
            var profile = new SiliconFlowExpressiveTtsInputPlugin.RuntimeProfile
            {
                Enabled = true,
                RequireSiliconFlowHost = true,
                RequireCosyVoiceModel = true,
                KeepExistingPromptWhenMarkerPresent = true,
                AppendPitchMarkers = true,
                Instruction = "Please read with happy emotion.",
                PitchMarkers = string.Empty
            };

            string formatted = SiliconFlowExpressiveTtsInputPlugin.FormatInput(
                "I'm so happy, Spring Festival is coming!",
                profile);

            Assert.That(formatted, Does.StartWith("Please read with happy emotion. <|endofprompt|>"));
            Assert.That(formatted, Does.Contain("[breath]"));
            Assert.That(formatted, Does.Contain("[laughter]"));
        }

        [Test]
        public void FormatInput_ShouldKeepExistingMarkerWhenConfigured()
        {
            var profile = new SiliconFlowExpressiveTtsInputPlugin.RuntimeProfile
            {
                Enabled = true,
                KeepExistingPromptWhenMarkerPresent = true,
                AppendPitchMarkers = false,
                Instruction = "Use a sad emotion"
            };

            const string input = "Use an excited tone <|endofprompt|> Hello there!";
            string formatted = SiliconFlowExpressiveTtsInputPlugin.FormatInput(input, profile);

            Assert.That(formatted, Does.StartWith("Use an excited tone <|endofprompt|> Hello there!"));
            Assert.AreEqual(1, CountOccurrences(formatted, SiliconFlowExpressiveTtsInputPlugin.EndOfPromptMarker));
        }

        [Test]
        public void ShouldApply_ShouldRequireSiliconFlowHostAndCosyVoiceWhenEnabled()
        {
            var profile = new SiliconFlowExpressiveTtsInputPlugin.RuntimeProfile
            {
                Enabled = true,
                RequireSiliconFlowHost = true,
                RequireCosyVoiceModel = true
            };

            Assert.IsFalse(SiliconFlowExpressiveTtsInputPlugin.ShouldApply(
                "https://api.openai.com/v1/",
                "FunAudioLLM/CosyVoice2-0.5B",
                profile));

            Assert.IsFalse(SiliconFlowExpressiveTtsInputPlugin.ShouldApply(
                "https://api.siliconflow.cn/v1/",
                "fnlp/MOSS-TTSD-v0.5",
                profile));

            Assert.IsTrue(SiliconFlowExpressiveTtsInputPlugin.ShouldApply(
                "https://api.siliconflow.cn/v1/",
                "FunAudioLLM/CosyVoice2-0.5B",
                profile));
        }

        [Test]
        public void NormalizeInstruction_ShouldTrimQuotesAndCompressWhitespace()
        {
            string normalized = SiliconFlowExpressiveTtsInputPlugin.NormalizeInstruction(
                "  \"Speak   warmly   and  a bit slower.\"  ",
                64);

            Assert.AreEqual("Speak warmly and a bit slower.", normalized);
        }

        [Test]
        public void AutoInsertExpressiveMarkers_ShouldInsertBreathAtPauseAndLaughterForPositiveTone()
        {
            string result = SiliconFlowExpressiveTtsInputPlugin.AutoInsertExpressiveMarkers(
                "Today is really happy, Spring Festival is coming!");

            Assert.That(result, Does.Contain("[breath]"));
            Assert.That(result, Does.Contain("[laughter]"));
        }

        private static int CountOccurrences(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token))
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            while (index >= 0)
            {
                index = value.IndexOf(token, index, System.StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                count++;
                index += token.Length;
            }

            return count;
        }
    }
}
