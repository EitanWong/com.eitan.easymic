#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Services;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public sealed class SherpaONNXUnityIntegrationTests
    {
        [Test]
        public void ChunkReaderEmitsExactChunkSizesAcrossPartialFrames()
        {
            var chunks = new List<float[]>();
            using var ready = new ManualResetEventSlim(false);
            using var reader = new EasyMicSherpaChunkReader(
                16000,
                1,
                4,
                (chunk, _) =>
                {
                    lock (chunks)
                    {
                        chunks.Add(chunk);
                        if (chunks.Count == 2)
                        {
                            ready.Set();
                        }
                    }
                },
                null);

            reader.Initialize(new AudioContext(1, 16000, 4));
            reader.OnAudioPass(new float[] { 1f, 2f, 3f }, new AudioContext(1, 16000, 3));
            reader.OnAudioPass(new float[] { 4f, 5f, 6f, 7f, 8f }, new AudioContext(1, 16000, 5));

            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(1)), "Timed out waiting for reader worker.");
            lock (chunks)
            {
                Assert.AreEqual(2, chunks.Count);
                CollectionAssert.AreEqual(new[] { 1f, 2f, 3f, 4f }, chunks[0]);
                CollectionAssert.AreEqual(new[] { 5f, 6f, 7f, 8f }, chunks[1]);
            }
        }

        [Test]
        public void ChunkReaderDropsMismatchedFormat()
        {
            int mismatchCount = 0;
            int emittedCount = 0;
            using var mismatch = new ManualResetEventSlim(false);
            using var reader = new EasyMicSherpaChunkReader(
                16000,
                1,
                4,
                (_, _) => Interlocked.Increment(ref emittedCount),
                (sampleRate, channels, expectedRate, expectedChannels) =>
                {
                    if (sampleRate == 48000 && channels == 2 && expectedRate == 16000 && expectedChannels == 1)
                    {
                        Interlocked.Increment(ref mismatchCount);
                        mismatch.Set();
                    }
                });

            reader.Initialize(new AudioContext(1, 16000, 4));
            reader.OnAudioPass(new float[] { 1f, 2f, 3f, 4f }, new AudioContext(2, 48000, 4));

            Assert.IsTrue(mismatch.Wait(TimeSpan.FromSeconds(1)), "Timed out waiting for mismatch callback.");
            Assert.AreEqual(1, mismatchCount);
            Assert.AreEqual(0, emittedCount);
        }

        [Test]
        public void InputSourceDropsOldestChunkWhenQueueIsFull()
        {
            var go = new GameObject("EasyMic Sherpa Input Test");
            try
            {
                var source = go.AddComponent<EasyMicSherpaAudioInputSource>();
                for (int i = 0; i < 10; i++)
                {
                    source.EnqueueChunkFromWorker(new[] { (float)i }, 16000);
                }

                Assert.AreEqual(8, source.PendingChunkCount);
                Assert.AreEqual(2, source.DroppedChunkCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void InputSourceTracksChunkReadyListeners()
        {
            var go = new GameObject("EasyMic Sherpa Input Listener Test");
            try
            {
                var source = go.AddComponent<EasyMicSherpaAudioInputSource>();
                void Handler(float[] _, int __) { }
                void OtherHandler(float[] _, int __) { }

                source.ChunkReady += Handler;
                source.ChunkReady += OtherHandler;
                Assert.AreEqual(2, source.ListenerCount);

                source.ChunkReady -= Handler;
                Assert.AreEqual(1, source.ListenerCount);

                source.ChunkReady -= OtherHandler;
                Assert.AreEqual(0, source.ListenerCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void RegistryReusesModulesAndDisposesAtZeroReferences()
        {
            var registry = new SherpaModelServiceRegistry();
            var key = new SherpaModelServiceKey("audio-tagging", "model-a", 16000, 123);
            int factoryCalls = 0;

            var leaseA = registry.Acquire(key, () =>
            {
                factoryCalls++;
                return new DisposableModule();
            });
            var leaseB = registry.Acquire(key, () =>
            {
                factoryCalls++;
                return new DisposableModule();
            });

            Assert.AreSame(leaseA.Module, leaseB.Module);
            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(1, registry.GetSnapshot().Entries.Length);
            Assert.AreEqual(2, registry.GetSnapshot().Entries[0].RefCount);

            leaseA.Dispose();
            Assert.IsFalse(leaseB.Module.Disposed);
            Assert.AreEqual(1, registry.GetSnapshot().Entries[0].RefCount);

            leaseB.Dispose();
            Assert.IsTrue(leaseB.Module.Disposed);
            Assert.AreEqual(0, registry.GetSnapshot().Entries.Length);
        }

        [Test]
        public void RegistryLeaseDisposeIsIdempotent()
        {
            var registry = new SherpaModelServiceRegistry();
            var lease = registry.Acquire(new SherpaModelServiceKey("audio-tagging", "model-a", 16000), () => new DisposableModule());

            lease.Dispose();
            lease.Dispose();

            Assert.AreEqual(0, registry.GetSnapshot().Entries.Length);
        }

        [Test]
        public void RegistrySeparatesDifferentOptions()
        {
            var registry = new SherpaModelServiceRegistry();
            using var leaseA = registry.Acquire(new SherpaModelServiceKey("slid", "model-a", 16000, 1), () => new DisposableModule());
            using var leaseB = registry.Acquire(new SherpaModelServiceKey("slid", "model-a", 16000, 2), () => new DisposableModule());

            Assert.AreNotSame(leaseA.Module, leaseB.Module);
            Assert.AreEqual(2, registry.GetSnapshot().Entries.Length);
        }

        [Test]
        public void RegistrySeparatesDifferentScopes()
        {
            var registry = new SherpaModelServiceRegistry();
            using var leaseA = registry.Acquire(new SherpaModelServiceKey("streaming-asr", "model-a", 16000, 1, "session-a"), () => new DisposableModule());
            using var leaseB = registry.Acquire(new SherpaModelServiceKey("streaming-asr", "model-a", 16000, 1, "session-b"), () => new DisposableModule());

            Assert.AreNotSame(leaseA.Module, leaseB.Module);
            Assert.AreEqual(2, registry.GetSnapshot().Entries.Length);
        }

        [Test]
        public void RegistryReleaseAllShutsDownAndInvalidatesOutstandingLeases()
        {
            var registry = new SherpaModelServiceRegistry();
            var lease = registry.Acquire(new SherpaModelServiceKey("audio-tagging", "model-a", 16000), () => new DisposableModule());
            var module = lease.Module;

            registry.ReleaseAll();

            Assert.IsTrue(module.Disposed);
            Assert.IsTrue(registry.GetSnapshot().IsShutdown);
            Assert.IsFalse(lease.IsValid);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                GC.KeepAlive(lease.Module);
            });
            Assert.Throws<ObjectDisposedException>(() =>
            {
                registry.Acquire(new SherpaModelServiceKey("audio-tagging", "model-a", 16000), () => new DisposableModule());
            });

            lease.Dispose();
        }

        private sealed class DisposableModule : IDisposable
        {
            public bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
#endif
