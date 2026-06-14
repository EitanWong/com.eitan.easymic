using Eitan.EasyMic.Runtime;
using NUnit.Framework;

namespace Eitan.EasyMic.Tests
{
    public class MicDeviceIdentityTests
    {
        [Test]
        public void SameIdentityAs_ComparesDeviceIdBytesByValue()
        {
            var first = CreateDevice("Built-in Microphone", 12);
            var second = CreateDevice("Renamed Microphone", 12);

            Assert.That(first.DeviceId, Is.Not.SameAs(second.DeviceId));
            Assert.That(first.SameIdentityAs(second), Is.True);
        }

        [Test]
        public void SameIdentityAs_ReturnsFalseForDifferentDeviceIdBytes()
        {
            var first = CreateDevice("Mic A", 12);
            var second = CreateDevice("Mic A", 13);

            Assert.That(first.SameIdentityAs(second), Is.False);
        }

        [Test]
        public void GetPreferredChannel_UsesRequestedFallbackWhenSupported()
        {
            var device = CreateDevice("Mic A", 12);
            device.NativeFormats = new[]
            {
                new Native.NativeDataFormat { Channels = 1, SampleRate = 48000 },
                new Native.NativeDataFormat { Channels = 2, SampleRate = 48000 }
            };

            Assert.That(device.GetPreferredChannel(Channel.Mono), Is.EqualTo(Channel.Mono));
            Assert.That(device.GetPreferredChannel(Channel.Stereo), Is.EqualTo(Channel.Stereo));
        }

        [Test]
        public void AllocateDeviceIdHandle_ReturnsNullForDefaultDevice()
        {
            var device = CreateDevice("Default Mic", 12);
            device.IsDefault = true;

            Assert.That(device.AllocateDeviceIdHandle(), Is.EqualTo(System.IntPtr.Zero));
        }

        [Test]
        public void AndroidCaptureLearningPayload_IsScopedToEndpointAndStrategyVersion()
        {
            var builtIn = CreateDevice("Built-in Microphone", 12);
            var usb = CreateDevice("USB Microphone", 42);

            var builtInScope = AndroidCaptureBackendLearning.CreateScope("rom-a", builtIn, new[] { builtIn, usb });
            var usbScope = AndroidCaptureBackendLearning.CreateScope("rom-a", usb, new[] { builtIn, usb });
            var payload = AndroidCaptureBackendLearning.BuildPayload(
                builtInScope,
                AndroidCaptureBackendLearning.CaptureStrategyVersion,
                AndroidCaptureBackendLearnedPreference.OpenSlLowLatency);

            Assert.That(AndroidCaptureBackendLearning.TryParsePayload(
                payload,
                builtInScope,
                AndroidCaptureBackendLearning.CaptureStrategyVersion,
                out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(AndroidCaptureBackendLearnedPreference.OpenSlLowLatency));

            Assert.That(AndroidCaptureBackendLearning.TryParsePayload(
                payload,
                usbScope,
                AndroidCaptureBackendLearning.CaptureStrategyVersion,
                out _), Is.False);
            Assert.That(AndroidCaptureBackendLearning.TryParsePayload(
                payload,
                builtInScope,
                AndroidCaptureBackendLearning.CaptureStrategyVersion + 1,
                out _), Is.False);
        }

        [Test]
        public void AndroidCaptureLearningScope_ChangesWithRomFingerprint()
        {
            var device = CreateDevice("Built-in Microphone", 12);

            var first = AndroidCaptureBackendLearning.CreateScope("rom-a", device, new[] { device });
            var second = AndroidCaptureBackendLearning.CreateScope("rom-b", device, new[] { device });

            Assert.That(first.IsValid, Is.True);
            Assert.That(second.IsValid, Is.True);
            Assert.That(first.PlayerPrefsKey, Is.Not.EqualTo(second.PlayerPrefsKey));
        }

        [Test]
        public void AndroidCaptureLearningEndpointFingerprint_ChangesWithCaptureTopology()
        {
            var defaultMic = CreateDefaultNamedDevice("Default Microphone");
            var usb = CreateDevice("USB Microphone", 42);

            var builtInOnly = AndroidCaptureBackendLearning.BuildEndpointFingerprint(defaultMic, new[] { defaultMic });
            var withExternalInput = AndroidCaptureBackendLearning.BuildEndpointFingerprint(defaultMic, new[] { defaultMic, usb });

            Assert.That(builtInOnly, Is.Not.Empty);
            Assert.That(withExternalInput, Is.Not.Empty);
            Assert.That(builtInOnly, Is.Not.EqualTo(withExternalInput));
        }

        [Test]
        public void AndroidCaptureLearningEndpointFingerprint_IgnoresTopologyForStableNativeIds()
        {
            var selected = CreateDevice("USB Microphone", 42);
            var other = CreateDevice("Conference Microphone", 77);

            var selectedOnly = AndroidCaptureBackendLearning.BuildEndpointFingerprint(selected, new[] { selected });
            var withOtherInput = AndroidCaptureBackendLearning.BuildEndpointFingerprint(selected, new[] { selected, other });

            Assert.That(selectedOnly, Is.Not.Empty);
            Assert.That(withOtherInput, Is.Not.Empty);
            Assert.That(selectedOnly, Is.EqualTo(withOtherInput));
        }

        private static MicDevice CreateDevice(string name, byte marker)
        {
            var id = new byte[Native.DeviceIdSizeInBytes];
            id[0] = marker;
            id[id.Length - 1] = (byte)(marker + 1);
            return new MicDevice
            {
                Name = name,
                DeviceId = id,
                NativeFormats = new[]
                {
                    new Native.NativeDataFormat
                    {
                        Format = Native.SampleFormat.F32,
                        Channels = 1,
                        SampleRate = 48000
                    }
                }
            };
        }

        private static MicDevice CreateDefaultNamedDevice(string name)
        {
            return new MicDevice
            {
                Name = name,
                IsDefault = true,
                DeviceId = new byte[Native.DeviceIdSizeInBytes],
                NativeFormats = new[]
                {
                    new Native.NativeDataFormat
                    {
                        Format = Native.SampleFormat.F32,
                        Channels = 1,
                        SampleRate = 48000
                    }
                }
            };
        }
    }
}
