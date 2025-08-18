using System;
using System.Runtime.InteropServices;

namespace Eitan.EasyMic.Runtime
{

    internal static unsafe partial class Native
    {
        // Platform-specific library naming based on your RID folder structure
#if UNITY_IOS
    private const string LibraryName = "__Internal";  // iOS uses framework linked statically
#elif UNITY_WEBGL
    private const string LibraryName = "__Internal";  // WebGL uses emscripten linking
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const string LibraryName = "miniaudio";   // Windows: miniaudio.dll
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    private const string LibraryName = "miniaudio";   // macOS: libminiaudio.dylib (Unity handles lib prefix)
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
    private const string LibraryName = "miniaudio";   // Linux: libminiaudio.so (Unity handles lib prefix)
#elif UNITY_ANDROID
    private const string LibraryName = "miniaudio";   // Android: libminiaudio.so (Unity handles lib prefix)
#else
    private const string LibraryName = "miniaudio";   // Default fallback
#endif

        // Delegates
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AudioCallback(IntPtr device, IntPtr output, IntPtr input, uint length);

        // Extended callback that carries pUserData for direct instance recovery (preferred when available)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AudioCallbackEx(IntPtr device, IntPtr output, IntPtr input, uint length, IntPtr userData);

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
        public static extern Result DecoderReadPcmFrames(IntPtr decoder, IntPtr framesOut, uint frameCount, out ulong framesRead);

        [DllImport(LibraryName, EntryPoint = "ma_decoder_seek_to_pcm_frame", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderSeekToPcmFrame(IntPtr decoder, ulong frame);

        [DllImport(LibraryName, EntryPoint = "ma_decoder_get_length_in_pcm_frames", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result DecoderGetLengthInPcmFrames(IntPtr decoder, out uint* length);

        #endregion

        #region Context

        [DllImport(LibraryName, EntryPoint = "ma_context_init", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextInit(IntPtr backends, uint backendCount, IntPtr config, IntPtr context);

        [DllImport(LibraryName, EntryPoint = "ma_context_uninit", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ContextUninit(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "ma_context_get_devices", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result ContextGetDevices(IntPtr context, out IntPtr pPlaybackDeviceInfos, out IntPtr pPlaybackDeviceCount, out IntPtr pCaptureDeviceInfos, out IntPtr pCaptureDeviceCount);


        #endregion

        #region Device

        [DllImport(LibraryName, EntryPoint = "sf_get_devices", CallingConvention = CallingConvention.Cdecl)]
        public static extern Result GetDevices(IntPtr context, out IntPtr pPlaybackDevices, out IntPtr pCaptureDevices, out IntPtr playbackDeviceCount, out IntPtr captureDeviceCount);

        // [DllImport(LibraryName, EntryPoint = "ma_context_get_device_info", CallingConvention = CallingConvention.Cdecl)]
        // public static extern Result GetDeviceInfo(IntPtr context, DeviceType deviceType, ma_device_id pDeviceID, out IntPtr pDeviceInfo);

        // [DllImport(LibraryName, EntryPoint = "ma_context_enumerate_devices", CallingConvention = CallingConvention.Cdecl)]
        // public static extern Result EnumerateDevices(IntPtr context, EnumDevicesCallbackProc callback, IntPtr pUserData);

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

        [DllImport(LibraryName, EntryPoint = "sf_allocate_encoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateEncoder();

        [DllImport(LibraryName, EntryPoint = "sf_allocate_decoder", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateDecoder();

        [DllImport(LibraryName, EntryPoint = "sf_allocate_context", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateContext();

        [DllImport(LibraryName, EntryPoint = "sf_allocate_device", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateDevice();

        [DllImport(LibraryName, EntryPoint = "sf_allocate_decoder_config", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateDecoderConfig(SampleFormat format, uint channels, uint sampleRate);

        [DllImport(LibraryName, EntryPoint = "sf_allocate_encoder_config", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateEncoderConfig(EncodingFormat encodingFormat, SampleFormat format, uint channels, uint sampleRate);

        // Newer API variant: sf_allocate_device_config(capability, sampleRate, callback, pSfConfig)
        // This version is used by updated native builds. pSfConfig may be NULL for defaults.
        [DllImport(LibraryName, EntryPoint = "sf_allocate_device_config", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateDeviceConfig(Capability capabilityType, uint sampleRate, AudioCallback dataCallback, IntPtr pSfConfig);

        // Legacy/alternate variants kept for backward compatibility (may not exist in current native build).
        [DllImport(LibraryName, EntryPoint = "sf_allocate_device_config_ex", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr AllocateDeviceConfigEx(DeviceType capabilityType, SampleFormat format, uint channels, uint sampleRate, AudioCallbackEx dataCallback, IntPtr pUserData, IntPtr playbackDevice, IntPtr captureDevice);

        #endregion

        #region Utils

        [DllImport(LibraryName, EntryPoint = "sf_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Free(IntPtr ptr);

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

        /// <summary>
        /// Capability flags for simplified device config helper.
        /// </summary>
        public enum Capability
        {
            Playback = 1,
            Record = 2,
            Mixed = 3,
            Loopback = 4
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
