using System;
using System.Reflection;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Exceptions;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class RecordingControlContractTests
    {
        private MicSystem _system;
        private object _previous;
        private FieldInfo _apiSystem;

        [SetUp]
        public void SetUp()
        {
            _system = new MicSystem(initializeNative: false);
            _apiSystem = typeof(EasyMicAPI).GetField("_micSystem", AudioTestAccess.Static);
            _previous = _apiSystem.GetValue(null);
            _apiSystem.SetValue(null, _system);
        }
        [TearDown]
        public void TearDown() { _system.Dispose(); _apiSystem.SetValue(null, _previous); }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(123456)]
        public void UnknownHandles_DoNotCreateRecordingsOrProcessors(int id)
        {
            var handle = new RecordingHandle(id);
            var blueprint = new AudioWorkerBlueprint(() => throw new AssertionException("Unknown handle must not instantiate workers."));
            Assert.That(EasyMicAPI.IsHandleAlive(handle), Is.False);
            Assert.That(handle.IsAlive, Is.False);
            EasyMicAPI.StopRecording(handle);
            EasyMicAPI.AddProcessor(handle, blueprint);
            EasyMicAPI.RemoveProcessor(handle, blueprint);
            EasyMicAPI.SetRecordingCallbackDiagnostics(handle, true);
            Assert.That(EasyMicAPI.GetProcessor<NativeGainer>(handle, blueprint), Is.Null);
            Assert.That(EasyMicAPI.GetRecordingInfo(handle).IsActive, Is.False);
            Assert.That(EasyMicAPI.GetRecordingPipelineSnapshots(), Is.Empty);
            Assert.That(EasyMicAPI.isWorking, Is.False);
        }

        [Test]
        public void NoCaptureDevice_StartFailsWithoutPublishingHandle()
        {
            Assert.Throws<EasyMicDeviceNotFoundException>(() => _system.StartRecording(default, SampleRate.Hz16000, Channel.Mono));
            Assert.That(_system.HasActiveRecordings, Is.False);
            Assert.That(_system.GetRecordingPipelineSnapshots(), Is.Empty);
            Assert.DoesNotThrow(() => _system.StopAllRecordings());
        }

        [Test]
        public void Dispose_IsIdempotent_AndRejectsDeviceOrRecordingStarts()
        {
            _system.Dispose();
            _system.Dispose();
            Assert.That(_system.IsDisposed, Is.True);
            Assert.Throws<ObjectDisposedException>(() => _system.StartRecording(default, SampleRate.Hz16000, Channel.Mono));
            Assert.Throws<ObjectDisposedException>(() => _system.Refresh());
            Assert.Throws<ObjectDisposedException>(() => _system.EnableAutoRefresh());
            Assert.DoesNotThrow(() => _system.StopAllRecordings());
        }

        [Test]
        public void DeviceDelta_DetectsAddRemoveAndRename_ByStableIdentity()
        {
            var old = Device(1, "old");
            var renamed = Device(1, "renamed");
            var removed = Device(2, "removed");
            var added = Device(3, "added");
            var delta = Delta(new[] { old, removed }, new[] { renamed, added });
            Assert.That(delta.HasChanges, Is.True);
            Assert.That(delta.Added.Length, Is.EqualTo(1));
            Assert.That(delta.Added[0].SameIdentityAs(added), Is.True);
            Assert.That(delta.Removed.Length, Is.EqualTo(1));
            Assert.That(delta.Removed[0].SameIdentityAs(removed), Is.True);
            Assert.That(delta.Updated.Length, Is.EqualTo(1));
            Assert.That(delta.Updated[0].Name, Is.EqualTo("renamed"));
        }

        [Test]
        public void DeviceDelta_ReorderingDoesNotProduceChanges()
        {
            var first = Device(1, "first");
            var second = Device(2, "second");
            Assert.That(Delta(new[] { first, second }, new[] { second, first }).HasChanges, Is.False);
            Assert.That(Delta(null, null).HasChanges, Is.False);
        }

        [Test]
        public void DeviceDelta_DefaultChangeIsReportedWithoutAddOrRemove()
        {
            var first = Device(1, "first"); first.IsDefault = true;
            var second = Device(2, "second");
            var nextFirst = first; nextFirst.IsDefault = false;
            var nextSecond = second; nextSecond.IsDefault = true;
            var delta = Delta(new[] { first, second }, new[] { nextFirst, nextSecond });
            Assert.That(delta.HasChanges, Is.True);
            Assert.That(delta.Added, Is.Empty);
            Assert.That(delta.Removed, Is.Empty);
            Assert.That(delta.PreviousDefault.Value.SameIdentityAs(first), Is.True);
            Assert.That(delta.CurrentDefault.Value.SameIdentityAs(second), Is.True);
        }

        [Test]
        public void DeviceDelta_CapabilityChangeIsReported_ButFormatOrderIsIgnored()
        {
            var first = Device(1, "USB");
            var mono = new Native.NativeDataFormat { Format = Native.SampleFormat.F32, Channels = 1, SampleRate = 16000 };
            var stereo = new Native.NativeDataFormat { Format = Native.SampleFormat.F32, Channels = 2, SampleRate = 48000 };
            first.NativeFormats = new[] { mono, stereo };
            var reordered = first; reordered.NativeFormats = new[] { stereo, mono };
            Assert.That(Delta(new[] { first }, new[] { reordered }).HasChanges, Is.False);
            var changed = first; changed.NativeFormats = new[] { mono };
            var delta = Delta(new[] { first }, new[] { changed });
            Assert.That(delta.HasChanges, Is.True);
            Assert.That(delta.Updated.Length, Is.EqualTo(1));
            Assert.That(delta.Added, Is.Empty);
            Assert.That(delta.Removed, Is.Empty);
        }

        [Test]
        public void DefaultDeviceChangeEvent_ReportsNoChanges()
        {
            Assert.That(default(MicDevicesChangedEventArgs).HasChanges, Is.False);
        }

        [Test]
        public void DeviceSelection_PrefersIdentityThenExactNameThenDefault()
        {
            var first = Device(1, "USB");
            var exact = Device(2, "usb");
            var fallback = Device(3, "default"); fallback.IsDefault = true;
            AudioTestAccess.Set(_system, "_devices", new[] { first, exact, fallback });
            Assert.That(Select(first, "usb").SameIdentityAs(first), Is.True);
            Assert.That(Select(null, "usb").SameIdentityAs(exact), Is.True);
            Assert.That(Select(null, "UsB").SameIdentityAs(first), Is.True);
            Assert.That(Select(null, "missing").SameIdentityAs(fallback), Is.True);
        }

        [TestCase(float.NaN, 1000)]
        [TestCase(float.PositiveInfinity, 1000)]
        [TestCase(-1f, 250)]
        [TestCase(0f, 250)]
        [TestCase(1.5f, 1500)]
        [TestCase(float.MaxValue, int.MaxValue)]
        public void Watcher_ClampsInterval_AndCanDetachRepeatedly(float seconds, int expected)
        {
            // A long initial timer keeps native enumeration out of this control-path test.
            using var watcher = MicDeviceWatcher.Ensure(_system, 30f);
            int interval = (int)AudioTestAccess.Call(watcher, "ClampInterval", seconds);
            Assert.That(interval, Is.EqualTo(expected));
            using var other = new MicSystem(false);
            Assert.Throws<InvalidOperationException>(() => watcher.Attach(other, 1f));
            watcher.Detach(other);
            Assert.That((bool)AudioTestAccess.Get(watcher, "_disposed"), Is.False);
            watcher.Detach(_system);
            watcher.Dispose();
            watcher.Attach(_system, 1f);
            Assert.That(AudioTestAccess.Get(watcher, "_timer"), Is.Null);
        }

        [Test]
        public void DefaultWorkers_AreDeduplicatedAndCopiedFromCaller()
        {
            var previous = EasyMicAPI.DefaultWorkers;
            try
            {
                var a = new AudioWorkerBlueprint(() => new NativeGainer(), "gain");
                var sameKey = new AudioWorkerBlueprint(() => new NativeGainer(), "gain");
                var input = new System.Collections.Generic.List<AudioWorkerBlueprint> { a, sameKey };
                EasyMicAPI.DefaultWorkers = input;
                input.Clear();
                Assert.That(EasyMicAPI.DefaultWorkers.Count, Is.EqualTo(1));
                Assert.That(EasyMicAPI.DefaultWorkers[0], Is.SameAs(a));
                EasyMicAPI.DefaultWorkers = null;
                Assert.That(EasyMicAPI.DefaultWorkers, Is.Null);
            }
            finally { EasyMicAPI.DefaultWorkers = previous; }
        }

        [Test]
        public void ActiveDevice_CannotStartSecondRecordingOnSameEndpoint()
        {
            var handle = ManagedSession();
            Assert.Throws<EasyMicDeviceConflictException>(() =>
                _system.StartRecording(Device(1, "managed test mic"), SampleRate.Hz16000, Channel.Mono));
            Assert.That(handle.IsAlive, Is.True);
            Assert.That(_system.GetRecordingPipelineSnapshots().Length, Is.EqualTo(1));
        }

        [Test]
        public void WorkerInitializationFailure_DisposesRejectedWorkerWithoutPublishingIt()
        {
            var handle = ManagedSession();
            var worker = new ThrowingInitializationProbe();
            var blueprint = new AudioWorkerBlueprint(() => worker);
            Assert.Throws<InvalidOperationException>(() => EasyMicAPI.AddProcessor(handle, blueprint));
            Assert.That(worker.Disposals, Is.EqualTo(1));
            Assert.That(EasyMicAPI.GetProcessor<ThrowingInitializationProbe>(handle, blueprint), Is.Null);
            Assert.That(EasyMicAPI.GetRecordingInfo(handle).ProcessorCount, Is.Zero);
        }

        private sealed class ThrowingInitializationProbe : AudioWriter
        {
            internal int Disposals;
            public override void Initialize(AudioContext state) => throw new InvalidOperationException("initialization failure");
            public override void Dispose() { Disposals++; base.Dispose(); }
            protected override void OnAudioWrite(Span<float> buffer, AudioContext state) { }
        }

        [Test]
        public void ActiveRecording_ReportsStateAndOwnsProcessorsUntilStopped()
        {
            var probe = new LifetimeProbe();
            var blueprint = new AudioWorkerBlueprint(() => probe);
            var handle = ManagedSession(blueprint, blueprint, null);
            Assert.That(EasyMicAPI.IsHandleAlive(handle), Is.True);
            Assert.That(EasyMicAPI.isWorking, Is.True);
            Assert.That(EasyMicAPI.GetRecordingInfo(handle).ProcessorCount, Is.EqualTo(1));
            Assert.That(EasyMicAPI.GetProcessor<LifetimeProbe>(handle, blueprint), Is.SameAs(probe));
            Assert.That(probe.Initializations, Is.EqualTo(1));
            EasyMicAPI.AddProcessor(handle, blueprint);
            Assert.That(probe.Initializations, Is.EqualTo(1));
            EasyMicAPI.StopRecording(handle);
            EasyMicAPI.StopRecording(handle);
            Assert.That(probe.Disposals, Is.EqualTo(1));
            Assert.That(EasyMicAPI.IsHandleAlive(handle), Is.False);
            Assert.That(EasyMicAPI.GetRecordingInfo(handle).IsActive, Is.False);
        }

        [Test]
        public void RemoveProcessor_DisposesExactlyOnce_AndStopAllInvalidatesEveryHandle()
        {
            var first = new LifetimeProbe();
            var second = new LifetimeProbe();
            var blueprint = new AudioWorkerBlueprint(() => first);
            var a = ManagedSession(blueprint);
            var b = ManagedSession(new AudioWorkerBlueprint(() => second));
            EasyMicAPI.RemoveProcessor(a, blueprint);
            EasyMicAPI.RemoveProcessor(a, blueprint);
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(EasyMicAPI.GetProcessor<LifetimeProbe>(a, blueprint), Is.Null);
            Assert.That(EasyMicAPI.GetRecordingInfo(a).ProcessorCount, Is.Zero);
            Assert.That(EasyMicAPI.GetRecordingPipelineSnapshots().Length, Is.EqualTo(2));
            EasyMicAPI.StopAllRecordings();
            EasyMicAPI.StopAllRecordings();
            Assert.That(a.IsAlive || b.IsAlive, Is.False);
            Assert.That(second.Disposals, Is.EqualTo(1));
            Assert.That(first.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void FailingWorkerFactory_DuringCreationDisposesEarlierWorkers()
        {
            var first = new LifetimeProbe();
            var error = Assert.Throws<TargetInvocationException>(() => ManagedSession(
                new AudioWorkerBlueprint(() => first),
                new AudioWorkerBlueprint(() => throw new InvalidOperationException("factory failure"))));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(_system.HasActiveRecordings, Is.False);
        }

        [Test]
        public void RejectedWorker_DoesNotPoisonRegistrationAndCanBeRetried()
        {
            var handle = ManagedSession();
            int attempts = 0;
            var probe = new LifetimeProbe();
            var blueprint = new AudioWorkerBlueprint(() => ++attempts == 1 ? null : probe);
            Assert.Throws<ArgumentNullException>(() => EasyMicAPI.AddProcessor(handle, blueprint));
            Assert.That(EasyMicAPI.GetRecordingInfo(handle).ProcessorCount, Is.Zero);
            EasyMicAPI.AddProcessor(handle, blueprint);
            Assert.That(EasyMicAPI.GetProcessor<LifetimeProbe>(handle, blueprint), Is.SameAs(probe));
            Assert.That(probe.Initializations, Is.EqualTo(1));
        }

        // Exercise real session/control/pipeline ownership, substituting only native activation.
        private RecordingHandle ManagedSession(params AudioWorkerBlueprint[] blueprints)
        {
            var type = typeof(MicSystem).GetNestedType("RecordingSession", BindingFlags.NonPublic);
            var session = Activator.CreateInstance(type, new object[] {
                IntPtr.Zero, Device(1, "managed test mic"), SampleRate.Hz16000, Channel.Mono,
                blueprints, null, false, EasyMicLatencyProfile.Balanced, "test", false, "test",
                Native.AndroidCaptureDeviceConfigProfile.Default, false });
            var recordings = (System.Collections.IDictionary)AudioTestAccess.Get(_system, "_activeRecordings");
            int id = recordings.Count + 1;
            recordings.Add(id, session);
            return new RecordingHandle(id);
        }

        private sealed class LifetimeProbe : AudioWriter
        {
            internal int Initializations, Disposals;
            public override void Initialize(AudioContext state) { Initializations++; base.Initialize(state); }
            public override void Dispose() { Disposals++; base.Dispose(); }
            protected override void OnAudioWrite(Span<float> buffer, AudioContext state) { }
        }

        private MicDevicesChangedEventArgs Delta(MicDevice[] before, MicDevice[] after)
            => (MicDevicesChangedEventArgs)AudioTestAccess.Call(_system, "BuildChangeArgs", before, after);
        private static MicDevice Device(byte id, string name)
        {
            var bytes = new byte[Native.DeviceIdSizeInBytes]; bytes[0] = id;
            return new MicDevice { DeviceId = bytes, Name = name };
        }
        private static MicDevice Select(MicDevice? preferred, string name)
        {
            var args = new object[] { preferred, name, null };
            Assert.That(typeof(EasyMicAPI).GetMethod("TrySelectDevice", AudioTestAccess.Static).Invoke(null, args), Is.True);
            return (MicDevice)args[2];
        }
    }
}
