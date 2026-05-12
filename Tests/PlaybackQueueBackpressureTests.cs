using NUnit.Framework;

namespace Eitan.EasyMic.Runtime.Tests
{
    public class PlaybackQueueBackpressureTests
    {
        [Test]
        public void Enqueue_WhenQueueIsFull_ReturnsPartialInsteadOfBlocking()
        {
            using var source = new PlaybackAudioSource(1, 48000, queueSeconds: 0.01f, attachTo: null);
            var samples = new float[source.FreeSamples + 1024];

            int written = source.Enqueue(samples);

            Assert.That(written, Is.GreaterThan(0));
            Assert.That(written, Is.LessThan(samples.Length));
            Assert.That(source.FreeSamples, Is.EqualTo(0));
        }

        [Test]
        public void TryEnqueue_WhenQueueCannotFitAllSamples_ReturnsFalseAndReportsWrittenCount()
        {
            using var source = new PlaybackAudioSource(1, 48000, queueSeconds: 0.01f, attachTo: null);
            var samples = new float[source.FreeSamples + 256];

            bool success = source.TryEnqueue(samples, out int written);

            Assert.That(success, Is.False);
            Assert.That(written, Is.GreaterThan(0));
            Assert.That(written, Is.LessThan(samples.Length));
        }
    }
}
