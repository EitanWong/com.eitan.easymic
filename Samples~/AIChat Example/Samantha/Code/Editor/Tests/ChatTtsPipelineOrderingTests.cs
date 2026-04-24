#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.Threading.Tasks;
using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class ChatTtsPipelineOrderingTests
    {
        [Test]
        public async Task Session_ShouldAllowOnlyOneActiveRunAtATime()
        {
            var session = new TtsPipelineSession();
            var gate = new TaskCompletionSource<bool>();
            int runCount = 0;

            bool firstStarted = session.EnsureStarted((_, __) =>
            {
                runCount++;
                return gate.Task;
            });

            bool secondStarted = session.EnsureStarted((_, __) =>
            {
                runCount++;
                return Task.CompletedTask;
            });

            Assert.IsTrue(firstStarted);
            Assert.IsFalse(secondStarted);
            Assert.AreEqual(1, runCount);

            gate.SetResult(true);
            await gate.Task;

            bool thirdStarted = session.EnsureStarted((_, __) =>
            {
                runCount++;
                return Task.CompletedTask;
            });

            Assert.IsTrue(thirdStarted);
            Assert.AreEqual(2, runCount);
        }
    }
}
#endif
