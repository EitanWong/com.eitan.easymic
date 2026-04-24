using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    internal static class EasyMicPlatformSupport
    {
        private const string SupportedPlatformsLabel = "Unity Editor, Windows, macOS, Linux, Android, and iOS";

        public static bool IsAndroidPlayer
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsIosPlayer
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static bool RequiresAndroidMainThread => IsAndroidPlayer;

        public static bool UsesMobileVoiceCallAecProfile => IsAndroidPlayer || IsIosPlayer;

        public static bool IsCurrentPlatformSupported(out string reason)
        {
            return IsCurrentPlatformSupported("EasyMic", out reason);
        }

        public static bool IsCurrentPlatformSupported(string productName, out string reason)
        {
#if UNITY_EDITOR
            reason = string.Empty;
            return true;
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX || UNITY_ANDROID || UNITY_IOS
            reason = string.Empty;
            return true;
#else
            reason =
                (string.IsNullOrWhiteSpace(productName) ? "This package" : productName) +
                " is not supported on the current platform. " +
                "Supported platforms: " + SupportedPlatformsLabel + ".";
            return false;
#endif
        }

        public static string GetCurrentPlatformFamilyLabel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "Android";
#elif UNITY_IOS && !UNITY_EDITOR
            return "iOS";
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return "macOS";
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return "Windows";
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            return "Linux";
#else
            return Application.platform.ToString();
#endif
        }
    }
}
