using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{

    internal static unsafe partial class Native
    {
        private static readonly object s_contextAllocationApiLock = new object();

        private const string LibraryName = EasyMicNativeLibraryNames.RuntimeBindingLibraryName;

        internal const int MaxDeviceNameLength = 255;
        internal const int DeviceNameBufferLength = MaxDeviceNameLength + 1;
        internal const int NativeDataFormatsCapacity = 64; // Mirrors miniaudio's inline array capacity.
        internal const int DeviceIdSizeInBytes = 256;      // Matches ma_device_id union size.
        private const int DeviceAllocationBytes = 64 * 1024;
        private const int DecoderAllocationBytes = 16 * 1024;
        private const int EncoderAllocationBytes = 4 * 1024;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate Result BufferProcessingCallback(
            IntPtr pCodecContext,
            IntPtr pBuffer,
            ulong bytesRequested,
            out ulong* bytesTransferred
        );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate Result SeekCallback(IntPtr pDecoder, long byteOffset, SeekPoint origin);

        // [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        // public delegate bool EnumDevicesCallbackProc(IntPtr pContext, DeviceType deviceType, IntPtr pInfo, IntPtr pUserData);

        #region Encoder

        [DllImport(LibraryName, EntryPoint = "ma_encoder_init", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern Result EncoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback, IntPtr pUserData, IntPtr pConfig, IntPtr pEncoder);

        [DllImport(LibraryName, EntryPoint = "ma_encoder_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern void EncoderUninit(IntPtr pEncoder);

        [DllImport(LibraryName, EntryPoint = "ma_encoder_write_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result EncoderWritePcmFrames(IntPtr pEncoder, IntPtr pFramesIn, ulong frameCount, ulong* pFramesWritten);

        #endregion

        #region Decoder

        [DllImport(LibraryName, EntryPoint = "ma_decoder_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback, IntPtr pUserData, IntPtr pConfig, IntPtr pDecoder);

        [DllImport(LibraryName, EntryPoint = "ma_decoder_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderUninit(IntPtr pDecoder);

        [DllImport(LibraryName, EntryPoint = "ma_decoder_read_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderReadPcmFrames(IntPtr decoder, IntPtr framesOut, ulong frameCount, out ulong framesRead);

        [DllImport(LibraryName, EntryPoint = "ma_decoder_seek_to_pcm_frame", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderSeekToPcmFrame(IntPtr decoder, ulong frame);

        [DllImport(LibraryName, EntryPoint = "ma_decoder_get_length_in_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderGetLengthInPcmFrames(IntPtr decoder, out ulong length);

        #endregion

        #region Context

        [DllImport(LibraryName, EntryPoint = "ma_context_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextInit(IntPtr backends, uint backendCount, IntPtr config, IntPtr context);

        [DllImport(LibraryName, EntryPoint = "ma_context_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ContextUninit(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "ma_context_sizeof", CallingConvention = CallingConvention.Cdecl)]
        private static extern UIntPtr ContextSizeOfNative();

        [DllImport(LibraryName, EntryPoint = "ma_context_get_devices", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextGetDevices(
            IntPtr context,
            out IntPtr pPlaybackDeviceInfos,
            out uint playbackDeviceCount,
            out IntPtr pCaptureDeviceInfos,
            out uint captureDeviceCount);

        [DllImport(LibraryName, EntryPoint = "ma_context_get_device_info", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextGetDeviceInfo(
            IntPtr context,
            DeviceType deviceType,
            IntPtr pDeviceID,
            out NativeDeviceInfo deviceInfo);

        #endregion

        #region Device

        [DllImport(LibraryName, EntryPoint = "ma_device_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DeviceInit(IntPtr context, IntPtr config, IntPtr device);

        [DllImport(LibraryName, EntryPoint = "ma_device_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeviceUninit(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "ma_device_start", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DeviceStart(IntPtr device);

        [DllImport(LibraryName, EntryPoint = "ma_device_stop", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DeviceStop(IntPtr device);

        #endregion

        #region Allocations

        [DllImport(LibraryName, EntryPoint = "ma_decoder_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern DecoderConfig DecoderConfigInit(SampleFormat format, uint channels, uint sampleRate);

        [DllImport(LibraryName, EntryPoint = "ma_encoder_config_init", CallingConvention = CallingConvention.Cdecl)]
        private static extern EncoderConfig EncoderConfigInit(EncodingFormat encodingFormat, SampleFormat format, uint channels, uint sampleRate);

        #endregion

        #region Utils

        public static IntPtr AllocateEncoder()
        {
            return AllocateZeroedNativeBuffer(EncoderAllocationBytes);
        }

        public static IntPtr AllocateDecoder()
        {
            return AllocateZeroedNativeBuffer(DecoderAllocationBytes);
        }

        public static IntPtr AllocateDevice()
        {
            return AllocateZeroedNativeBuffer(DeviceAllocationBytes);
        }

        public static IntPtr AllocateDecoderConfig(SampleFormat format, uint channels, uint sampleRate)
        {
            return CopyStructToNative(DecoderConfigInit(format, channels, sampleRate));
        }

        public static IntPtr AllocateEncoderConfig(EncodingFormat encodingFormat, SampleFormat format, uint channels, uint sampleRate)
        {
            return CopyStructToNative(EncoderConfigInit(encodingFormat, format, channels, sampleRate));
        }

        public static void Free(IntPtr ptr)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public static IntPtr AllocateDeviceConfig(
            DeviceType capabilityType,
            SampleFormat format,
            uint channels,
            uint sampleRate,
            IntPtr playbackDevice,
            IntPtr captureDevice,
            AudioCallback dataCallback,
            out bool usesExtendedCallback)
        {
            return AllocateDeviceConfig(
                capabilityType,
                format,
                channels,
                sampleRate,
                playbackDevice,
                captureDevice,
                dataCallback,
                EasyMicLatencyProfile.Balanced,
                IntPtr.Zero,
                out usesExtendedCallback);
        }

        public static IntPtr AllocateDeviceConfig(
            DeviceType capabilityType,
            SampleFormat format,
            uint channels,
            uint sampleRate,
            IntPtr playbackDevice,
            IntPtr captureDevice,
            AudioCallback dataCallback,
            EasyMicLatencyProfile latencyProfile,
            out bool usesExtendedCallback)
        {
            return AllocateDeviceConfig(
                capabilityType,
                format,
                channels,
                sampleRate,
                playbackDevice,
                captureDevice,
                dataCallback,
                latencyProfile,
                IntPtr.Zero,
                out usesExtendedCallback);
        }

        public static IntPtr AllocateDeviceConfig(
            DeviceType capabilityType,
            SampleFormat format,
            uint channels,
            uint sampleRate,
            IntPtr playbackDevice,
            IntPtr captureDevice,
            AudioCallback dataCallback,
            EasyMicLatencyProfile latencyProfile,
            IntPtr userData,
            out bool usesExtendedCallback)
        {
            if (dataCallback == null)
            {
                throw new ArgumentNullException(nameof(dataCallback));
            }

            var config = CreateDeviceConfig(capabilityType, sampleRate);
            config.DataCallback = Marshal.GetFunctionPointerForDelegate(dataCallback);
            config.UserData = userData;

            if (capabilityType == DeviceType.Playback || capabilityType == DeviceType.Mixed || capabilityType == DeviceType.Loopback)
            {
                config.Playback.Format = format;
                config.Playback.Channels = channels;
                config.Playback.DeviceId = playbackDevice;
            }

            if (capabilityType == DeviceType.Record || capabilityType == DeviceType.Mixed || capabilityType == DeviceType.Loopback)
            {
                config.Capture.Format = format;
                config.Capture.Channels = channels;
                config.Capture.DeviceId = captureDevice;
            }

            MiniaudioDeviceConfigPolicy.Apply(ref config, sampleRate, capabilityType, latencyProfile);
            usesExtendedCallback = false;
            return CopyStructToNative(config);
        }

        private static DeviceConfig CreateDeviceConfig(DeviceType capabilityType, uint sampleRate)
        {
            return new DeviceConfig
            {
                DeviceType = capabilityType,
                SampleRate = sampleRate,
                Resampling = new ResamplerConfig
                {
                    Format = SampleFormat.Unknown,
                    Channels = 0,
                    SampleRateIn = 0,
                    SampleRateOut = 0,
                    Algorithm = 0,
                    Linear = new ResamplerLinearConfig
                    {
                        LpfOrder = 4
                    }
                }
            };
        }

        public static IntPtr AllocateContext(out NativeAllocationSource allocationSource)
        {
            lock (s_contextAllocationApiLock)
            {
                ulong size64;
                try
                {
                    size64 = ContextSizeOfNative().ToUInt64();
                }
                catch (EntryPointNotFoundException ex)
                {
                    throw new InvalidOperationException(
                        "The loaded miniaudio plugin is too old for this EasyMic build. " +
                        "It does not export ma_context_sizeof.",
                        ex);
                }

                if (size64 == 0 || size64 > int.MaxValue)
                {
                    allocationSource = NativeAllocationSource.ManagedHGlobal;
                    return IntPtr.Zero;
                }

                IntPtr buffer = Marshal.AllocHGlobal((int)size64);
                new Span<byte>((void*)buffer, (int)size64).Clear();
                allocationSource = NativeAllocationSource.ManagedHGlobal;
                return buffer;
            }
        }

        public static void FreeAllocated(IntPtr ptr, NativeAllocationSource allocationSource)
        {
            if (ptr == IntPtr.Zero)
            {
                return;
            }

            if (allocationSource == NativeAllocationSource.ManagedHGlobal)
            {
                Marshal.FreeHGlobal(ptr);
                return;
            }

            Free(ptr);
        }

        private static IntPtr AllocateZeroedNativeBuffer(int bytes)
        {
            var ptr = Marshal.AllocHGlobal(bytes);
            new Span<byte>((void*)ptr, bytes).Clear();
            return ptr;
        }

        private static IntPtr CopyStructToNative<T>(T value) where T : struct
        {
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                return ptr;
            }
            catch
            {
                Marshal.FreeHGlobal(ptr);
                throw;
            }
        }

        public static NativeDeviceInfo ReadDeviceInfo(IntPtr deviceInfos, int index)
        {
            if (deviceInfos == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(deviceInfos));
            }

            var stride = Marshal.SizeOf<NativeDeviceInfo>();
            var entryPtr = IntPtr.Add(deviceInfos, index * stride);
            return Marshal.PtrToStructure<NativeDeviceInfo>(entryPtr);
        }

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct NativeDeviceInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = DeviceIdSizeInBytes)]
            public byte[] Id; // raw bytes of ma_device_id union; interpret per backend if needed.

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameBufferLength)]
            public string Name;

            public uint IsDefault; // ma_bool32

            public uint NativeDataFormatCount;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = NativeDataFormatsCapacity)]
            public NativeDataFormat[] NativeDataFormats;

            /// <summary>
            /// Returns the populated native formats, trimmed to the reported count.
            /// </summary>
            public NativeDataFormat[] GetActiveNativeFormats()
            {
                if (NativeDataFormatCount == 0 || NativeDataFormats == null || NativeDataFormats.Length == 0)
                {
                    return Array.Empty<NativeDataFormat>();
                }

                var length = (int)Math.Min(NativeDataFormatCount, (uint)NativeDataFormats.Length);
                if (length == NativeDataFormats.Length)
                {
                    return NativeDataFormats;
                }

                var result = new NativeDataFormat[length];
                Array.Copy(NativeDataFormats, result, length);
                return result;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NativeDataFormat
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRate;
            public NativeDataFormatFlags Flags;
        }

        [Flags]
        public enum NativeDataFormatFlags : uint
        {
            None = 0,
            ExclusiveMode = 1u << 1
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AllocationCallbacks
        {
            public IntPtr UserData;
            public IntPtr OnMalloc;
            public IntPtr OnRealloc;
            public IntPtr OnFree;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ResamplerLinearConfig
        {
            public uint LpfOrder;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ResamplerConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRateIn;
            public uint SampleRateOut;
            public int Algorithm;
            public IntPtr BackendVTable;
            public IntPtr BackendUserData;
            public ResamplerLinearConfig Linear;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceSubConfig
        {
            public IntPtr DeviceId;
            public SampleFormat Format;
            public uint Channels;
            public IntPtr ChannelMap;
            public int ChannelMixMode;
            public uint CalculateLFEFromSpatialChannels;
            public int ShareMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeviceConfig
        {
            public DeviceType DeviceType;
            public uint SampleRate;
            public uint PeriodSizeInFrames;
            public uint PeriodSizeInMilliseconds;
            public uint Periods;
            public int PerformanceProfile;
            public byte NoPreSilencedOutputBuffer;
            public byte NoClip;
            public byte NoDisableDenormals;
            public byte NoFixedSizedCallback;
            public IntPtr DataCallback;
            public IntPtr NotificationCallback;
            public IntPtr StopCallback;
            public IntPtr UserData;
            public ResamplerConfig Resampling;
            public DeviceSubConfig Playback;
            public DeviceSubConfig Capture;
            public WasapiDeviceConfig Wasapi;
            public AlsaDeviceConfig Alsa;
            public PulseDeviceConfig Pulse;
            public CoreAudioDeviceConfig CoreAudio;
            public OpenSlDeviceConfig OpenSl;
            public AAudioDeviceConfig AAudio;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WasapiDeviceConfig
        {
            public int Usage;
            public byte NoAutoConvertSrc;
            public byte NoDefaultQualitySrc;
            public byte NoAutoStreamRouting;
            public byte NoHardwareOffloading;
            public uint LoopbackProcessId;
            public byte LoopbackProcessExclude;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AlsaDeviceConfig
        {
            public uint NoMMap;
            public uint NoAutoFormat;
            public uint NoAutoChannels;
            public uint NoAutoResample;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PulseDeviceConfig
        {
            public IntPtr StreamNamePlayback;
            public IntPtr StreamNameCapture;
            public int ChannelMap;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CoreAudioDeviceConfig
        {
            public uint AllowNominalSampleRateChange;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct OpenSlDeviceConfig
        {
            public int StreamType;
            public int RecordingPreset;
            public uint EnableCompatibilityWorkarounds;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AAudioDeviceConfig
        {
            public int Usage;
            public int ContentType;
            public int InputPreset;
            public int AllowedCapturePolicy;
            public uint NoAutoStartAfterReroute;
            public uint EnableCompatibilityWorkarounds;
            public uint AllowSetBufferCapacity;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DecoderConfig
        {
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRate;
            public IntPtr ChannelMap;
            public int ChannelMixMode;
            public int DitherMode;
            public ResamplerConfig Resampling;
            public AllocationCallbacks AllocationCallbacks;
            public EncodingFormat EncodingFormat;
            public uint SeekPointCount;
            public IntPtr CustomBackendVTables;
            public uint CustomBackendCount;
            public IntPtr CustomBackendUserData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EncoderConfig
        {
            public EncodingFormat EncodingFormat;
            public SampleFormat Format;
            public uint Channels;
            public uint SampleRate;
            public AllocationCallbacks AllocationCallbacks;
        }

        #endregion

        #region Enum

        /// <summary>
        ///     Describes the capabilities of a sound device.
        /// </summary>
        [Flags]
        public enum DeviceType
        {
            /// <summary>
            ///     The device is used for audio playback.
            /// </summary>
            Playback = 1,

            /// <summary>
            ///     The device is used for audio capture.
            /// </summary>
            Record = 2,

            /// <summary>
            ///     The device is used for both playback and capture.
            /// </summary>
            Mixed = Playback | Record,

            /// <summary>
            ///     The device is used for loopback recording (capturing the output).
            /// </summary>
            Loopback = 4
        }


        /// <summary>
        /// Describes the result of an operation.
        /// </summary>
        public enum Result
        {
            Success = 0,
            Error = -1,
            InvalidArgs = -2,
            InvalidOperation = -3,
            OutOfMemory = -4,
            OutOfRange = -5,
            AccessDenied = -6,
            DoesNotExist = -7,
            AlreadyExists = -8,
            TooManyOpenFiles = -9,
            InvalidFile = -10,
            TooBig = -11,
            PathTooLong = -12,
            NameTooLong = -13,
            NotDirectory = -14,
            IsDirectory = -15,
            DirectoryNotEmpty = -16,
            AtEnd = -17,
            NoSpace = -18,
            Busy = -19,
            IoError = -20,
            Interrupt = -21,
            Unavailable = -22,
            AlreadyInUse = -23,
            BadAddress = -24,
            BadSeek = -25,
            BadPipe = -26,
            Deadlock = -27,
            TooManyLinks = -28,
            NotImplemented = -29,
            NoMessage = -30,
            BadMessage = -31,
            NoDataAvailable = -32,
            InvalidData = -33,
            Timeout = -34,
            NoNetwork = -35,
            NotUnique = -36,
            NotSocket = -37,
            NoAddress = -38,
            BadProtocol = -39,
            ProtocolUnavailable = -40,
            ProtocolNotSupported = -41,
            ProtocolFamilyNotSupported = -42,
            AddressFamilyNotSupported = -43,
            SocketNotSupported = -44,
            ConnectionReset = -45,
            AlreadyConnected = -46,
            NotConnected = -47,
            ConnectionRefused = -48,
            NoHost = -49,
            InProgress = -50,
            Cancelled = -51,
            MemoryAlreadyMapped = -52,

            // General non-standard errors.
            CrcMismatch = -100,

            // General miniaudio-specific errors.
            FormatNotSupported = -200,
            DeviceTypeNotSupported = -201,
            ShareModeNotSupported = -202,
            NoBackend = -203,
            NoDevice = -204,
            ApiNotFound = -205,
            InvalidDeviceConfig = -206,
            Loop = -207,
            BackendNotEnabled = -208,

            // State errors.
            DeviceNotInitialized = -300,
            DeviceAlreadyInitialized = -301,
            DeviceNotStarted = -302,
            DeviceNotStopped = -303,

            // Operation errors.
            FailedToInitBackend = -400,
            FailedToOpenBackendDevice = -401,
            FailedToStartBackendDevice = -402,
            FailedToStopBackendDevice = -403

        }

        /// <summary>
        /// Enum for sample formats.
        /// </summary>
        /// <remarks>
        /// Currently only contains standard formats.
        /// </remarks>
        public enum SampleFormat
        {
            /// <summary>
            /// Unknown sample format.
            /// </summary>
            Unknown = 0,

            /// <summary>
            /// Unsigned 8-bit format.
            /// </summary>
            U8 = 1,

            /// <summary>
            /// Signed 16-bit format.
            /// </summary>
            S16 = 2,

            /// <summary>
            /// Signed 24-bit format.
            /// </summary>
            S24 = 3,

            /// <summary>
            /// Signed 32-bit format.
            /// </summary>
            S32 = 4,

            /// <summary>
            /// 32-bit floating point format.
            /// </summary>
            F32 = 5
        }
        /// <summary>
        ///     Supported audio encoding formats.
        /// </summary>
        /// <remarks>
        ///     Current backend (miniaudio) supports only Wav for encoding.
        /// </remarks>
        public enum EncodingFormat
        {
            /// <summary>
            /// Unknown encoding format.
            /// </summary>
            Unknown = 0,

            /// <summary>
            /// Waveform Audio File Format.
            /// </summary>
            Wav,

            /// <summary>
            /// Free Lossless Audio Codec.
            /// </summary>
            Flac,

            /// <summary>
            /// MPEG-1 or MPEG-2 Audio Layer III.
            /// </summary>
            Mp3,

            /// <summary>
            /// Ogg Vorbis audio format.
            /// </summary>
            Vorbis
        }

        internal enum NativeAllocationSource
        {
            ManagedHGlobal = 0,
        }

        internal enum SeekPoint
        {
            /// <summary>
            ///     Seek from the beginning of the stream.
            /// </summary>
            FromStart,

            /// <summary>
            ///     Seek from the current position in the stream.
            /// </summary>
            FromCurrent
        }



        #endregion

    }
}
