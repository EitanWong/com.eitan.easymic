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
    }
}
