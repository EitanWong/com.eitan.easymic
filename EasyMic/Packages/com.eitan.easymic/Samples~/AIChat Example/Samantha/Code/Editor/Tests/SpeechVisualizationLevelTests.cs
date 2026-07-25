#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class SpeechVisualizationLevelTests
    {
        [Test]
        public void Push_ShouldReportAudibleSignalLevel()
        {
            var level = new SpeechVisualizationLevel();
            var samples = new float[480];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.12f;
            }

            level.Push(samples, samples.Length, channels: 1, sampleRate: 16000);

            Assert.Greater(level.GetRecentLevel(1f), 0.05f);
        }

        [Test]
        public void Push_SilenceShouldReleaseVisualLevel()
        {
            var level = new SpeechVisualizationLevel();
            var voice = new float[480];
            var silence = new float[480];
            for (int i = 0; i < voice.Length; i++)
            {
                voice[i] = 0.18f;
            }

            level.Push(voice, voice.Length, channels: 1, sampleRate: 16000);
            float peak = level.GetRecentLevel(1f);

            for (int i = 0; i < 24; i++)
            {
                level.Push(silence, silence.Length, channels: 1, sampleRate: 16000);
            }

            Assert.Less(level.GetRecentLevel(1f), peak);
        }

        [Test]
        public void Reset_ShouldClearCurrentLevel()
        {
            var level = new SpeechVisualizationLevel();
            var samples = new float[480];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = 0.12f;
            }

            level.Push(samples, samples.Length, channels: 1, sampleRate: 16000);
            level.Reset();

            Assert.AreEqual(0f, level.GetRecentLevel(1f));
        }
    }
}
#endif
