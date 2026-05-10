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

        private static MicDevice CreateDevice(string name, byte marker)
        {
            var id = new byte[Native.DeviceIdSizeInBytes];
            id[0] = marker;
            id[id.Length - 1] = (byte)(marker + 1);
            return new MicDevice
            {
                Name = name,
                DeviceId = id
            };
        }
    }
}
