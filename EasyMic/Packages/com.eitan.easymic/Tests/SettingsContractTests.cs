using System;
using System.Reflection;
using Eitan.EasyMic.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Eitan.EasyMic.Tests
{
    public class SettingsContractTests
    {
        private EasyMicProjectSettings _settings;
        private object _previous;
        private FieldInfo _override;
        [SetUp]
        public void SetUp()
        {
            _override = typeof(EasyMicRuntimeSettings).GetField("s_settings", AudioTestAccess.Static);
            _previous = _override.GetValue(null);
            _settings = ScriptableObject.CreateInstance<EasyMicProjectSettings>();
        }
        [TearDown]
        public void TearDown()
        {
            _override.SetValue(null, _previous);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void Migration_RepairsMissingSectionsWithoutReplacingExistingValues()
        {
            _settings.Runtime.defaultSampleRate = 44100;
            var runtime = _settings.Runtime;
            AudioTestAccess.Set(_settings, "version", 0);
            foreach (string field in new[] { "editor", "windows", "macOS", "linux", "android", "iOS", "experimental", "localization" })
                AudioTestAccess.Set(_settings, field, null);
            _settings.Migrate(); _settings.Migrate();
            Assert.That(_settings.Version, Is.EqualTo(EasyMicProjectSettings.CurrentVersion));
            Assert.That(_settings.Runtime, Is.SameAs(runtime));
            Assert.That(_settings.Runtime.defaultSampleRate, Is.EqualTo(44100));
            Assert.That(_settings.Editor, Is.Not.Null);
            Assert.That(_settings.Windows, Is.Not.Null);
            Assert.That(_settings.MacOS, Is.Not.Null);
            Assert.That(_settings.Linux, Is.Not.Null);
            Assert.That(_settings.Android.forceSafeDeviceEnumeration, Is.True);
            Assert.That(_settings.IOS.preferNativeDeviceFormat, Is.True);
            Assert.That(_settings.Experimental, Is.Not.Null);
            Assert.That(_settings.Localization, Is.Not.Null);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Validation_RecoversFiniteDefaultsForInvalidRefreshRates(float value)
        {
            _settings.Runtime.deviceRefreshIntervalSeconds = value;
            _settings.Editor.telemetryRefreshRate = value;
            AudioTestAccess.Call(_settings, "OnValidate");
            Assert.That(_settings.Runtime.deviceRefreshIntervalSeconds, Is.EqualTo(2f));
            Assert.That(_settings.Editor.telemetryRefreshRate, Is.EqualTo(30f));
        }

        [Test]
        public void Reset_ReplacesModifiedSectionsAndKeepsPlatformsIndependent()
        {
            _settings.Runtime.defaultChannels = 7;
            _settings.Android.bufferFrames = 1024;
            _settings.ResetToDefaults();
            Assert.That(_settings.Runtime.defaultChannels, Is.EqualTo(2));
            Assert.That(_settings.Runtime.defaultSampleRate, Is.EqualTo(48000));
            Assert.That(_settings.Android.bufferFrames, Is.EqualTo(256));
            Assert.That(_settings.Android, Is.Not.SameAs(_settings.Windows));
            _settings.Android.bufferFrames = 512;
            Assert.That(_settings.Windows.bufferFrames, Is.EqualTo(256));
        }

        [TestCase(-1, 64, 8000, 1, 20)]
        [TestCase(int.MaxValue, 4096, 192000, 8, 5000)]
        public void Validation_ClampsRuntimeAndPlatformLimits(int input, int buffer, int rate, int channels, int queue)
        {
            _settings.Runtime.customBufferFrames = input;
            _settings.Runtime.defaultSampleRate = input;
            _settings.Runtime.defaultChannels = input;
            _settings.Runtime.streamingQueueMilliseconds = input;
            foreach (var platform in new[] { _settings.Windows, _settings.MacOS, _settings.Linux, _settings.Android, _settings.IOS }) platform.bufferFrames = input;
            AudioTestAccess.Call(_settings, "OnValidate");
            Assert.That(_settings.Runtime.customBufferFrames, Is.EqualTo(buffer));
            Assert.That(_settings.Runtime.defaultSampleRate, Is.EqualTo(rate));
            Assert.That(_settings.Runtime.defaultChannels, Is.EqualTo(channels));
            Assert.That(_settings.Runtime.streamingQueueMilliseconds, Is.EqualTo(queue));
            foreach (var platform in new[] { _settings.Windows, _settings.MacOS, _settings.Linux, _settings.Android, _settings.IOS }) Assert.That(platform.bufferFrames, Is.EqualTo(buffer));
        }

        [Test]
        public void Override_MigratesAndReturnsExactInstance_AndClearRemovesOverride()
        {
            AudioTestAccess.Set(_settings, "runtime", null);
            EasyMicRuntimeSettings.OverrideForSession(_settings);
            Assert.That(EasyMicRuntimeSettings.Current, Is.SameAs(_settings));
            Assert.That(EasyMicRuntimeSettings.Runtime, Is.Not.Null);
            EasyMicRuntimeSettings.ClearSessionOverride();
            Assert.That(_override.GetValue(null), Is.Null);
            Assert.DoesNotThrow(() => EasyMicRuntimeSettings.ApplyTo(null));
        }

        [Test]
        public void ApplyToStoppedSystem_UsesConfiguredFormatAndTelemetry()
        {
            EasyMicRuntimeSettings.OverrideForSession(_settings);
            _settings.Runtime.preferNativeDeviceFormat = false;
            _settings.Runtime.defaultSampleRate = 44100;
            _settings.Runtime.defaultChannels = 1;
            _settings.Runtime.enableTelemetry = false;
            _settings.Runtime.enableRuntimeDiagnostics = false;
            var system = (AudioSystem)Activator.CreateInstance(typeof(AudioSystem), true);
            try
            {
                EasyMicRuntimeSettings.ApplyTo(system);
                Assert.That(system.SampleRate, Is.EqualTo(44100));
                Assert.That(system.Channels, Is.EqualTo(1));
                Assert.That(system.MeteringEnabled, Is.False);
                _settings.Runtime.preferNativeDeviceFormat = true;
                EasyMicRuntimeSettings.ApplyTo(system);
                Assert.That(system.SampleRate, Is.Zero);
                Assert.That(system.Channels, Is.Zero);
            }
            finally { system.Dispose(); }
        }

        [Test]
        public void ApplyToRunningSystem_UpdatesMetersWhilePreservingActiveFormatAndLatency()
        {
            using var system = new ManagedPlaybackTestSystem();
            var profile = system.System.LatencyProfile;
            _settings.Runtime.latencyProfile = EasyMicLatencyProfile.SafeStreaming;
            _settings.Runtime.defaultSampleRate = 16000;
            _settings.Runtime.defaultChannels = 1;
            _settings.Runtime.enableTelemetry = false;
            _settings.Runtime.enableRuntimeDiagnostics = true;
            EasyMicRuntimeSettings.OverrideForSession(_settings);
            EasyMicRuntimeSettings.ApplyTo(system.System);
            Assert.That(system.System.MeteringEnabled, Is.True);
            Assert.That(system.System.SampleRate, Is.EqualTo(48000));
            Assert.That(system.System.Channels, Is.EqualTo(2));
            Assert.That(system.System.LatencyProfile, Is.EqualTo(profile));
        }
    }
}
