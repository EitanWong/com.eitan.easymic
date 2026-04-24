namespace Eitan.EasyMic.Runtime
{
    internal static class EasyMicNativeLibraryNames
    {
        public const string BaseName = "miniaudio";
        public const string InternalLinkLibraryName = "__Internal";
        public const string MacOsBindingLibraryName = "libminiaudio";

#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        public const string RuntimeBindingLibraryName = InternalLinkLibraryName;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        public const string RuntimeBindingLibraryName = MacOsBindingLibraryName;
#else
        public const string RuntimeBindingLibraryName = BaseName;
#endif
    }
}
