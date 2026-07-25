#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.Threading.Tasks;
using System.Linq;
using NUnit.Framework;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class ChatTtsPipelineOrderingTests
    {
        [Test]
        public void Session_ShouldAllowOnlyOneActiveRunAtATime()
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
            gate.Task.GetAwaiter().GetResult();

            bool thirdStarted = session.EnsureStarted((_, __) =>
            {
                runCount++;
                return Task.CompletedTask;
            });

            Assert.IsTrue(thirdStarted);
            Assert.AreEqual(2, runCount);
        }

        [Test]
        public void Session_ConcurrentStartShouldCreateOnlyOneRun()
        {
            var session = new TtsPipelineSession();
            var gate = new TaskCompletionSource<bool>();
            int runCount = 0;

            Task<bool>[] attempts = Enumerable.Range(0, 32)
                .Select(_ => Task.Run(() => session.EnsureStarted((__, ___) =>
                {
                    System.Threading.Interlocked.Increment(ref runCount);
                    return gate.Task;
                })))
                .ToArray();

            bool[] results = Task.WhenAll(attempts).GetAwaiter().GetResult();

            Assert.AreEqual(1, results.Count(result => result));
            Assert.AreEqual(1, runCount);
            gate.SetResult(true);
            gate.Task.GetAwaiter().GetResult();
        }

        [Test]
        public void Session_CancelSnapshotShouldKeepSessionAndTaskPaired()
        {
            var session = new TtsPipelineSession();
            var gate = new TaskCompletionSource<bool>();
            Assert.IsTrue(session.EnsureStarted((_, __) => gate.Task));

            TtsPipelineSessionSnapshot snapshot = session.CancelAndGetSnapshot();

            Assert.AreEqual(1, snapshot.SessionId);
            Assert.AreSame(gate.Task, snapshot.Task);
            gate.SetResult(true);
        }

        [Test]
        public void Pipeline_ShouldRejectOlderTurnAfterNewTurnIsPublished()
        {
            using var pipeline = new ChatTtsPipeline(() => null);

            Assert.IsTrue(pipeline.BeginTurn(40));
            Assert.IsFalse(pipeline.BeginTurn(39));
            Assert.IsFalse(pipeline.BeginTurn(40));

            pipeline.StopAndWaitAsync().GetAwaiter().GetResult();

            Assert.IsTrue(pipeline.BeginTurn(41));
            Assert.IsFalse(pipeline.Enqueue(40, "stale sentence"));
        }

        [Test]
        public void PlaybackDrain_ShouldRequireEmptyAndStoppedSink()
        {
            Assert.IsFalse(ChatTtsPipeline.IsPlaybackDrainComplete(0d, isPlaying: true));
            Assert.IsFalse(ChatTtsPipeline.IsPlaybackDrainComplete(0.02d, isPlaying: false));
            Assert.IsTrue(ChatTtsPipeline.IsPlaybackDrainComplete(0d, isPlaying: false));
        }
    }
}
#endif
