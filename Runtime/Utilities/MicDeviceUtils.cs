using SoundIO;
using System.Linq; // 1. 引入 System.Linq 命名空间

namespace Eitan.EasyMic.Runtime
{
    public static class MicDeviceUtils
    {

       private static Context soundIOContext;

        public static Channel[] MapLayoutToChannel(ChannelLayout layout)
        {
            // 2. 使用 LINQ 将多行代码简化为一行
            return layout.Channels.ToArray().Select(channelLayout => (Channel)channelLayout).ToArray();
        }
        /// <summary>
        /// Thew channel layout of the microphone input device can only be obtained on Windows, Linux, and macOS. The other platforms cannot read it at present and return mono by default.
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns> <summary>
        public static Channel GetDeviceChannel(this MicDevice device)
        {
#if UNITY_EDITOR || UNITY_STANDALONE || PLATFORM_STANDALONE
                if (soundIOContext == null)
                {
                    soundIOContext = Context.Create();
                }

                soundIOContext.Connect();
                soundIOContext.FlushEvents();
                var count = soundIOContext.InputDeviceCount;

                for (int i = 0; i < count; i++)
                {
                    var soundIONativeDevice = soundIOContext.GetInputDevice(i);
                    if (device.Name == soundIONativeDevice.Name)
                    {
                        var channelCount = soundIONativeDevice.CurrentLayout.ChannelCount;
                        if (channelCount > 0)
                        {
                            var channelLayout = soundIONativeDevice.CurrentLayout.Channels[channelCount - 1];
                            return (Channel)channelLayout;
                        }
                    }
                }
                
                soundIOContext.Disconnect();

#endif
            return Channel.Mono;
        }
    }
}