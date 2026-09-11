using System.Linq;
using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class StreamingSentenceAssemblerTests
    {
        [Test]
        public void Append_ShouldSplitBySentenceEnd()
        {
            var assembler = new StreamingSentenceAssembler();
            var first = assembler.Append("Hello world.", forceFlush: false).ToList();
            var second = assembler.Append(" How are you?", forceFlush: false).ToList();

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual("Hello world.", first[0]);
            Assert.AreEqual(1, second.Count);
            Assert.AreEqual("How are you?", second[0]);
        }

        [Test]
        public void ForceFlush_ShouldEmitTrailingBuffer()
        {
            var assembler = new StreamingSentenceAssembler();
            _ = assembler.Append("Trailing sentence without end", forceFlush: false).ToList();
            var flushed = assembler.Append(string.Empty, forceFlush: true).ToList();

            Assert.AreEqual(1, flushed.Count);
            Assert.AreEqual("Trailing sentence without end", flushed[0]);
        }

        [Test]
        public void Append_ShouldEmitFirstSpeechPhraseAtLowLatencyBoundary()
        {
            var assembler = new StreamingSentenceAssembler();

            var first = assembler.Append("我明白你的意思，我们现在就开始处理，", forceFlush: false).ToList();

            Assert.AreEqual(1, first.Count);
            Assert.AreEqual("我明白你的意思，我们现在就开始处理，", first[0]);
        }

        [Test]
        public void Reset_ShouldDiscardInterruptedPartialPhrase()
        {
            var assembler = new StreamingSentenceAssembler();
            _ = assembler.Append("This response belongs to the interrupted turn", forceFlush: false).ToList();

            assembler.Reset();
            var current = assembler.Append("Current turn.", forceFlush: false).ToList();

            Assert.AreEqual(1, current.Count);
            Assert.AreEqual("Current turn.", current[0]);
        }

        [Test]
        public void Append_ShouldBoundUnpunctuatedCjkSpeech()
        {
            var assembler = new StreamingSentenceAssembler();
            string content = new string('语', 52);

            var emitted = assembler.Append(content, forceFlush: false).ToList();

            Assert.AreEqual(1, emitted.Count);
            Assert.AreEqual(48, emitted[0].Length);
            Assert.AreEqual(4, assembler.BufferLength);
        }

        [Test]
        public void Append_ShouldEmitFirstClauseForLowLatencyLocalTts()
        {
            var assembler = new StreamingSentenceAssembler { PreferShortPhrases = true };

            var emitted = assembler.Append("这是一个用于实时语音合成的短语，后面还有内容", forceFlush: false).ToList();

            CollectionAssert.AreEqual(new[] { "这是一个用于实时语音合成的短语，" }, emitted);
        }
    }
}
