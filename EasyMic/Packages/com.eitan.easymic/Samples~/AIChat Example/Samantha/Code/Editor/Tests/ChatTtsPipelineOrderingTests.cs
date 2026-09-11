#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha.Tests
{
    public class ChatTtsPipelineOrderingTests
    {
        [Test]
        public void LocalPlaybackStart_ShouldForwardAudioStartAndDetachCleanly()
        {
            var owner = new GameObject("Local TTS callbacks test");
            owner.SetActive(false);
            try
            {
                var synth = owner.AddComponent<SpeechSynthesizer>();
                synth.MaxParallelSynthesis = 3;
                using var pipeline = new ChatTtsPipeline(() => null);
                pipeline.BeginTurn(1);
                var starts = new List<(long, string)>();
                pipeline.OnSentenceStarted += (turnId, sentence) => starts.Add((turnId, sentence));
                InvokeLocalCallbackBinding(pipeline, "AttachLocalSynthCallbacks", synth);
                InvokeLocalCallbackBinding(pipeline, "AttachLocalSynthCallbacks", synth);
                Assert.AreEqual(1, synth.MaxParallelSynthesis);

                RaiseSynthEvent(synth, "OnTTSStateChanged", true);
                RaiseSynthEvent(synth, "OnSentenceStarted", "hello");
                Assert.IsEmpty(starts, "Synthesis may take longer than the echo guard window.");

                RaiseSynthEvent(synth, "OnSentencePlaybackStarted", "hello");
                CollectionAssert.AreEqual(new[] { (1L, "hello") }, starts);

                InvokeLocalCallbackBinding(pipeline, "DetachLocalSynthCallbacks");
                Assert.AreEqual(3, synth.MaxParallelSynthesis);
                RaiseSynthEvent(synth, "OnSentencePlaybackStarted", "detached");
                Assert.AreEqual(1, starts.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LocalPlaybackStart_ShouldDiscardDeferredEventAfterTurnChange(bool stop)
        {
            var owner = new GameObject("Local TTS stale callback test");
            owner.SetActive(false);
            try
            {
                var synth = owner.AddComponent<SpeechSynthesizer>();
                using var pipeline = new ChatTtsPipeline(() => null);
                var callbacks = new Queue<Action>();
                typeof(ChatTtsPipeline).GetField("_mainThreadDispatcher", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(pipeline, (Action<Action>)(action => callbacks.Enqueue(action)));
                pipeline.BeginTurn(1);
                int starts = 0;
                pipeline.OnSentenceStarted += (_, __) => starts++;
                InvokeLocalCallbackBinding(pipeline, "AttachLocalSynthCallbacks", synth);
                RaiseSynthEvent(synth, "OnSentencePlaybackStarted", "old turn");
                Assert.AreEqual(1, callbacks.Count);

                if (stop)
                {
                    pipeline.Stop();
                }
                else
                {
                    pipeline.BeginTurn(2);
                }

                while (callbacks.Count > 0) callbacks.Dequeue()();
                Assert.AreEqual(0, starts);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static void InvokeLocalCallbackBinding(ChatTtsPipeline pipeline, string method, params object[] args)
        {
            typeof(ChatTtsPipeline).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(pipeline, args);
        }

        private static void RaiseSynthEvent<T>(SpeechSynthesizer synth, string name, T value)
        {
            var callback = (Action<T>)typeof(SpeechSynthesizer)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(synth);
            callback?.Invoke(value);
        }

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
