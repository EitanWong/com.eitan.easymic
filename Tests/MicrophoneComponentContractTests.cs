using System;
using System.IO;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono;
using Eitan.EasyMic.Runtime.Mono.Utilities;
using Eitan.EasyMic.Runtime.Mono.Recording;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class MicrophoneComponentContractTests
    {
        private GameObject _owner;
        private EasyMicrophone _mic;
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _owner = new GameObject("Inactive microphone test");
            _owner.SetActive(false); // Keep permission requests and native activation out of unit tests.
            _mic = _owner.AddComponent<EasyMicrophone>();
            _directory = Path.Combine(Path.GetTempPath(), "EasyMicComponentTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_owner);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void InactiveComponent_CanStageDeviceAndDspOptionsWithoutRecording()
        {
            _mic.ApplyDeviceOptions(new DeviceOptions("test mic", Channel.Stereo, SampleRate.Hz48000));
            _mic.ApplyMicrophoneOptions(new MicrophoneOptions(false, false));
            _mic.AecEnabled = true; _mic.AnsEnabled = false; _mic.AgcEnabled = true; _mic.DirectionalEnabled = true;
            Assert.That(_mic.DeviceOpts.DeviceName, Is.EqualTo("test mic"));
            Assert.That(_mic.DeviceOpts.Channel, Is.EqualTo(Channel.Stereo));
            Assert.That(_mic.MicrophoneOpts.recordOnAwake, Is.False);
            Assert.That(_mic.AudioProcessingOpts.EnableAEC, Is.True);
            Assert.That(_mic.AudioProcessingOpts.EnableANS, Is.False);
            Assert.That(_mic.AudioProcessingOpts.EnableAGC, Is.True);
            Assert.That(_mic.AudioProcessingOpts.EnableDirectional, Is.True);
            Assert.That(_mic.IsRecording, Is.False);
            Assert.That(_mic.Initialized, Is.False);
            Assert.That(_mic.HasRecordedClip, Is.False);
            Assert.That(_mic.CurrentRecordingInfo.IsActive, Is.False);
        }

        [TestCase(-90f, 270f)]
        [TestCase(360f, 0f)]
        [TestCase(810f, 90f)]
        public void DirectionalTarget_WrapsAngles(float input, float expected)
        {
            _mic.ConfigureDirectionalPickup(MicrophoneDirectionalMode.Fixed, input);
            Assert.That(_mic.FixedTargetDegrees, Is.EqualTo(expected));
            Assert.That(_mic.DirectionalMode, Is.EqualTo(MicrophoneDirectionalMode.Fixed));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void DirectionalTarget_RejectsNonfiniteAnglesAndUnknownModes(float angle)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _mic.ConfigureDirectionalPickup(MicrophoneDirectionalMode.Fixed, angle));
            Assert.Throws<ArgumentOutOfRangeException>(() => _mic.ConfigureDirectionalPickup((MicrophoneDirectionalMode)999, 90f));
        }

        [Test]
        public void SaveWithoutRecording_ReturnsFalseWithoutCreatingDestination()
        {
            string destination = Path.Combine(_directory, "missing.wav");
            Assert.That(_mic.TrySaveLatestRecording(destination), Is.False);
            Assert.That(File.Exists(destination), Is.False);
            Assert.That(_mic.LastSavedPath, Is.Null);
        }

        [Test]
        public void SaveRecording_CreatesNestedDestinationAndPreservesOriginalBytes()
        {
            string source = TempRecording();
            string destination = Path.Combine(_directory, "nested", "recording");
            Assert.That(_mic.TrySaveLatestRecording(destination), Is.True);
            CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(destination + ".wav"));
            Assert.That(_mic.LastSavedPath, Is.EqualTo(destination + ".wav"));
            Assert.That(File.Exists(source), Is.True);
        }

        [Test]
        public void SaveRecording_RespectsOverwriteAndCanSaveToItsOwnPath()
        {
            string source = TempRecording();
            string destination = Path.Combine(_directory, "existing.WAV");
            File.WriteAllBytes(destination, new byte[] { 1, 2, 3 });
            Assert.That(_mic.TrySaveLatestRecording(destination, false), Is.False);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
            Assert.That(_mic.TrySaveLatestRecording(destination, true), Is.True);
            CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(destination));
            Assert.That(_mic.TrySaveLatestRecording(source), Is.True);
            Assert.That(_mic.LastSavedPath, Is.EqualTo(source));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("invalid\0name")]
        public void SaveInvalidDestination_ReturnsFalseAndPreservesPreviousSavedPath(string destination)
        {
            TempRecording();
            AudioTestAccess.Set(_mic, "_latestRecordingPath", "previous.wav");
            Assert.That(_mic.TrySaveLatestRecording(destination), Is.False);
            Assert.That(_mic.LastSavedPath, Is.EqualTo("previous.wav"));
        }

        [TestCase("recording", "recording.wav")]
        [TestCase("recording.WAV", "recording.WAV")]
        [TestCase("recording.mp3", "recording.mp3.wav")]
        public void WavPathExtension_IsCaseInsensitiveAndDoesNotDiscardExistingSuffix(string input, string expected)
        {
            Assert.That(RecordingPathUtility.EnsureWavExtension(input), Is.EqualTo(expected));
        }

        private string TempRecording()
        {
            string path = Path.Combine(_directory, "capture.wav");
            using (var writer = new StreamingWavWriter(path))
            {
                writer.OnSamples(new[] { 0.25f, -0.25f, 0.5f, -0.5f }, 16000, 1);
                writer.FinalizeRecording();
            }
            AudioTestAccess.Set(_mic, "_latestRecordingTempPath", path);
            return path;
        }
    }
}
