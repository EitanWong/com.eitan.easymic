using System;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Exceptions;

namespace Eitan.EasyMic.Runtime.Mono
{
    [Serializable]
    public struct DeviceOptions
    {
        public string DeviceName;
        public Channel Channel;
        public SampleRate SampleRate;

        public DeviceOptions(string deviceName, Channel channel, SampleRate sampleRate)
        {
            DeviceName = deviceName;
            Channel = channel;
            SampleRate = sampleRate;
        }

        public DeviceOptions(MicDevice device, Channel channel, SampleRate sampleRate)
        {
            DeviceName = device.Name;
            Channel = channel;
            SampleRate = sampleRate;
        }

        public bool HasDeviceName => !string.IsNullOrEmpty(DeviceName);

        public static DeviceOptions Default
        {
            get
            {
                if (!EasyMicAPI.IsAvailable)
                {
                    return new DeviceOptions(string.Empty, Channel.Mono, SampleRate.Hz16000);
                }

                var defaultDevice = EasyMicAPI.Default;
                if (!defaultDevice.HasValidId)
                {
                    throw new EasyMicDeviceNotFoundException("No default microphone devices found !");
                }

                return new DeviceOptions(defaultDevice.Name, defaultDevice.GetPreferredChannel(), defaultDevice.GetPreferredSampleRate());
            }
        }
    }
}
