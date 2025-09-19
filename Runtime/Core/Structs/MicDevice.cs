using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{

    /// <summary>
    /// Represents device information including ID, name, default status, and native format capabilities.
    /// </summary>
    public struct MicDevice
    {
        /// <summary>
        /// Raw bytes representing the native <c>ma_device_id</c>. Length is always <see cref="Native.DeviceIdSizeInBytes"/>.
        /// </summary>
        internal byte[] DeviceId;

        /// <summary>
        /// The device label shown to users.
        /// </summary>
        public string Name;

        /// <summary>
        /// True when the operating system exposes this device as the default capture endpoint.
        /// </summary>
        public bool IsDefault;

        /// <summary>
        /// Native miniaudio data formats reported for this device. Each item encodes format, channels, sample rate and flags.
        /// </summary>
        internal Native.NativeDataFormat[] NativeFormats;

        /// <summary>
        /// Indicates whether this device has a valid identifier that can be used to open capture streams.
        /// </summary>
        public bool HasValidId => DeviceId != null && DeviceId.Length == Native.DeviceIdSizeInBytes;

        internal IntPtr AllocateDeviceIdHandle()
        {
            if (!HasValidId)
            {
                return IntPtr.Zero;
            }

            // A zeroed ID instructs miniaudio to use the default device, so keep that behaviour by returning NULL.
            var hasNonZero = DeviceId.Any(b => b != 0);
            if (!hasNonZero)
            {
                return IntPtr.Zero;
            }

            var handle = Marshal.AllocHGlobal(Native.DeviceIdSizeInBytes);
            Marshal.Copy(DeviceId, 0, handle, DeviceId.Length);
            return handle;
        }

        /// <summary>
        ///     Enumerates raw native formats for this device.
        /// </summary>
        private Native.NativeDataFormat[] EnumerateNativeFormats()
        {
            return NativeFormats ?? Array.Empty<Native.NativeDataFormat>();
        }

        /// <summary>
        ///     Indicates whether the native backend reports support for any channel count.
        /// </summary>
        public bool SupportsAllChannels()
        {
            return EnumerateNativeFormats().Any(fmt => fmt.Channels == 0);
        }

        /// <summary>
        ///     Indicates whether the native backend reports support for any sample rate.
        /// </summary>
        public bool SupportsAllSampleRates()
        {
            return EnumerateNativeFormats().Any(fmt => fmt.SampleRate == 0);
        }

        /// <summary>
        ///     Unique channel configurations explicitly reported by the device.
        /// </summary>
        public Channel[] GetSupportedChannels()
        {
            var formats = EnumerateNativeFormats();
            if (formats == null || formats.Length == 0)
            {
                return Array.Empty<Channel>();
            }

            var channelSet = new HashSet<Channel>();
            foreach (var fmt in formats)
            {
                if (fmt.Channels == 0)
                {
                    // 0 means "all channel counts supported"; surface as Mono/Stereo fallback.
                    channelSet.Add(Channel.Mono);
                    channelSet.Add(Channel.Stereo);
                    continue;
                }

                var value = (int)fmt.Channels;
                if (!Enum.IsDefined(typeof(Channel), value))
                {
                    continue;
                }

                channelSet.Add((Channel)value);
            }

            return channelSet.OrderBy(c => (int)c).ToArray();
        }

        /// <summary>
        ///     Heuristically chooses the most sensible channel count for capture.
        /// </summary>
        public Channel GetPreferredChannel(Channel fallback = Channel.Mono)
        {
            var supported = GetSupportedChannels();
            if (supported.Length == 0)
            {
                return fallback;
            }

            if (supported.Contains(Channel.Mono))
            {
                return Channel.Mono;
            }

            if (supported.Contains(Channel.Stereo))
            {
                return Channel.Stereo;
            }

            return supported[0];
        }

        /// <summary>
        ///     Raw sample rates explicitly reported by the device. Values are in Hz.
        /// </summary>
        public int[] GetSupportedSampleRates()
        {
            var formats = EnumerateNativeFormats();
            if (formats == null || formats.Length == 0)
            {
                return Array.Empty<int>();
            }

            var rateSet = new HashSet<int>();
            foreach (var fmt in formats)
            {
                if (fmt.SampleRate == 0)
                {
                    // Tracked by SupportsAllSampleRates(); no need to add 0 to the list.
                    continue;
                }

                rateSet.Add((int)fmt.SampleRate);
            }

            return rateSet.OrderBy(r => r).ToArray();
        }

        /// <summary>
        ///     Supported sample rates mapped to the EasyMic <see cref="SampleRate"/> enum when possible.
        /// </summary>
        public SampleRate[] GetSupportedSampleRateEnums()
        {
            var supported = GetSupportedSampleRates();
            if (supported.Length == 0)
            {
                if (SupportsAllSampleRates())
                {
                    return Enum.GetValues(typeof(SampleRate)).Cast<SampleRate>().ToArray();
                }

                return Array.Empty<SampleRate>();
            }

            var list = new List<SampleRate>();
            foreach (var rate in supported)
            {
                if (!Enum.IsDefined(typeof(SampleRate), rate))
                {
                    continue;
                }

                list.Add((SampleRate)rate);
            }

            return list.Distinct().OrderBy(r => (int)r).ToArray();
        }

        /// <summary>
        ///     Picks the closest supported sample rate to the requested value.
        /// </summary>
        public SampleRate ResolveSampleRate(SampleRate requested)
        {
            var supported = GetSupportedSampleRateEnums();
            if (supported.Length == 0)
            {
                return requested;
            }

            if (supported.Contains(requested))
            {
                return requested;
            }

            SampleRate best = supported[0];
            var bestDelta = Math.Abs((int)best - (int)requested);
            for (int i = 1; i < supported.Length; i++)
            {
                var candidate = supported[i];
                var delta = Math.Abs((int)candidate - (int)requested);
                if (delta < bestDelta)
                {
                    best = candidate;
                    bestDelta = delta;
                }
            }

            return best;
        }

        /// <summary>
        ///     Returns <c>true</c> when this device can open capture streams using the requested channel layout.
        /// </summary>
        public bool SupportsChannel(Channel channel)
        {
            if (SupportsAllChannels())
            {
                return true;
            }

            return GetSupportedChannels().Contains(channel);
        }

        /// <summary>
        ///     Returns <c>true</c> when this device can capture audio at the requested sample rate.
        /// </summary>
        public bool SupportsSampleRate(SampleRate rate)
        {
            if (SupportsAllSampleRates())
            {
                return true;
            }

            return GetSupportedSampleRateEnums().Contains(rate);
        }

        /// <summary>
        ///     Generates a stable identity string used to diff device enumerations.
        /// </summary>
        internal string GetIdentifier()
        {
            if (HasValidId)
            {
                for (int i = 0; i < DeviceId.Length; i++)
                {
                    if (DeviceId[i] != 0)
                    {
                        return Convert.ToBase64String(DeviceId);
                    }
                }
            }

            return $"name::{Name ?? string.Empty}";
        }

        /// <summary>
        ///     Compares the native identifiers of two devices to determine if they represent the same endpoint.
        /// </summary>
        internal bool SameIdentityAs(in MicDevice other)
        {
            if (HasValidId && other.HasValidId)
            {
                if (DeviceId.Length != other.DeviceId.Length)
                {
                    return false;
                }

                for (int i = 0; i < DeviceId.Length; i++)
                {
                    if (DeviceId[i] != other.DeviceId[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }
    }
}
