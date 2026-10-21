using System;
using System.Reflection;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono.Components;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class PlaybackOutputSelectionTests
    {
        private static readonly FieldInfo InstanceField = typeof(AudioSystem).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo RunningField = typeof(AudioSystem).GetField("_running", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo EnsureSessionMethod = typeof(PlaybackAudioSourceBehaviour).GetMethod("EnsureSession", BindingFlags.Instance | BindingFlags.NonPublic);

        private AudioSystem _previousSystem;
        private AudioSystem _system;
        private GameObject _owner;
        private PlaybackAudioSourceBehaviour _playback;

        [SetUp]
        public void SetUp()
        {
            // Isolate configuration tests from the editor's audio device. No test
            // opens a native context, starts a transport, or plays real audio.
            _previousSystem = (AudioSystem)InstanceField.GetValue(null);
            _system = (AudioSystem)Activator.CreateInstance(typeof(AudioSystem), nonPublic: true);
            InstanceField.SetValue(null, _system);
            _owner = new GameObject("Playback output selection test");
            _owner.SetActive(false);
            _playback = _owner.AddComponent<PlaybackAudioSourceBehaviour>();
        }

        [TearDown]
        public void TearDown()
        {
            RunningField.SetValue(_system, false);
            UnityEngine.Object.DestroyImmediate(_owner);
            _system.Dispose();
            InstanceField.SetValue(null, _previousSystem);
        }

        [Test]
        public void DefaultComponent_PreservesOutputConfiguredThroughApi()
        {
            AudioPlayback.SetOutputDevice("api-output");

            Assert.That(_playback.OverrideOutputDevice, Is.False);
            EnsureSession(_playback);

            Assert.That(_system.OutputDeviceId, Is.EqualTo("api-output"));
            Assert.That(_system.IsRunning, Is.False);
        }

        [Test]
        public void ExplicitOutput_IsAppliedBeforeSourceCreation()
        {
            _playback.OutputDeviceId = "selected-speaker";
            Assert.That(_playback.OverrideOutputDevice, Is.True);
            Assert.That(_system.OutputDeviceId, Is.Empty, "Inactive components only store their selection.");

            EnsureSession(_playback);

            Assert.That(_system.OutputDeviceId, Is.EqualTo("selected-speaker"));
            Assert.That(_playback.Source, Is.Null);
            Assert.That(_system.IsRunning, Is.False);
        }

        [Test]
        public void NullOutput_ExplicitlySelectsSystemDefault()
        {
            AudioPlayback.SetOutputDevice("api-output");
            _playback.OutputDeviceId = null;

            EnsureSession(_playback);

            Assert.That(_playback.OverrideOutputDevice, Is.True);
            Assert.That(_playback.OutputDeviceId, Is.Empty);
            Assert.That(_system.OutputDeviceId, Is.Empty);
        }

        [Test]
        public void MatchingComponents_CanJoinRunningOutput()
        {
            _playback.OutputDeviceId = "shared-speaker";
            EnsureSession(_playback);
            RunningField.SetValue(_system, true);
            var second = _owner.AddComponent<PlaybackAudioSourceBehaviour>();
            second.OutputDeviceId = "shared-speaker";

            Assert.DoesNotThrow(() => EnsureSession(second));
            Assert.That(_system.IsRunning, Is.True);
            Assert.That(_system.OutputDeviceId, Is.EqualTo("shared-speaker"));
        }

        [Test]
        public void ConflictingComponent_RejectsSelectionWithoutStoppingSharedOutput()
        {
            AudioPlayback.SetOutputDevice("active-speaker");
            RunningField.SetValue(_system, true);
            _playback.OutputDeviceId = "different-speaker";

            var error = Assert.Throws<TargetInvocationException>(() => EnsureSession(_playback));

            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(_system.OutputDeviceId, Is.EqualTo("active-speaker"));
            Assert.That(_system.IsRunning, Is.True);
            Assert.That(_playback.Source, Is.Null);
        }

        [Test]
        public void DisablingOverride_PreservesCurrentGlobalSelection()
        {
            _playback.OutputDeviceId = "component-speaker";
            EnsureSession(_playback);
            _playback.OverrideOutputDevice = false;
            AudioPlayback.SetOutputDevice("new-global-speaker");

            EnsureSession(_playback);

            Assert.That(_system.OutputDeviceId, Is.EqualTo("new-global-speaker"));
        }

        private static void EnsureSession(PlaybackAudioSourceBehaviour playback)
        {
            Assert.That(EnsureSessionMethod.Invoke(playback, null), Is.True);
        }
    }
}
