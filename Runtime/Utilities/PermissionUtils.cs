#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Eitan.EasyMic.Runtime
{
    using Eitan.EasyMic;

    /// <summary>
    /// A self-contained module for handling microphone permissions on different platforms.
    /// </summary>
    public static class PermissionUtils
    {
        private static bool IsGranted;
        private static bool s_iOSAuthorizationRequested;
        private static bool s_macosAuthorizationRequested;
        // 安卓权限的字符串常量

#if UNITY_ANDROID
        private const string PERMISSION_RECORD_AUDIO = "android.permission.RECORD_AUDIO";
#endif

        /// <summary>
        /// Checks if the microphone permission has been granted.
        /// </summary>
        public static bool HasPermission()
        {
            // Platforms with OS-level microphone privacy must be granted before native capture starts.
#if UNITY_ANDROID && !UNITY_EDITOR
            EasyMicUnityThread.TryCaptureFromCurrentThread();
            if (EasyMicPlatformSupport.RequiresAndroidMainThread && !EasyMicUnityThread.IsMainThread)
            {
                // Android permission APIs must run on Unity main thread.
                return IsGranted;
            }

            if (!IsGranted)
            {
                if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    MarkPermissionGranted();
                }
                else
                {
                    RequestPlatformPermission();
                }
            }

            return IsGranted;
#elif UNITY_IOS && !UNITY_EDITOR
            if (UnityEngine.Application.HasUserAuthorization(UnityEngine.UserAuthorization.Microphone))
            {
                MarkPermissionGranted();
                return true;
            }

            RequestPlatformPermission();
            return false;
#elif (UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX)
            if (UnityEngine.Application.HasUserAuthorization(UnityEngine.UserAuthorization.Microphone))
            {
                MarkPermissionGranted();
                return true;
            }

            RequestPlatformPermission();
            return false;
#elif UNITY_STANDALONE || UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private static void RequestPlatformPermission()
        {

#if UNITY_ANDROID && !UNITY_EDITOR
        if (EasyMicPlatformSupport.RequiresAndroidMainThread && !EasyMicUnityThread.IsMainThread)
        {
            return;
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            UnityEngine.Microphone.Start(null,true,1,UnityEngine.AudioSettings.outputSampleRate); // use Unity Microphone API force permission request
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += s => OnPermissionResult(true);
            callbacks.PermissionDenied += s => OnPermissionResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += s => OnPermissionResult(false);
            Permission.RequestUserPermission(Permission.Microphone, callbacks); // not working
        }
#elif UNITY_IOS && !UNITY_EDITOR
        if (s_iOSAuthorizationRequested)
        {
            return;
        }

        s_iOSAuthorizationRequested = true;
        UnityEngine.Application.RequestUserAuthorization(UnityEngine.UserAuthorization.Microphone);
#elif (UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX)
        if (s_macosAuthorizationRequested)
        {
            return;
        }

        s_macosAuthorizationRequested = true;
        UnityEngine.Application.RequestUserAuthorization(UnityEngine.UserAuthorization.Microphone);
#elif UNITY_STANDALONE || UNITY_EDITOR
            return;
#endif
        }

        private static void OnPermissionResult(bool granted)
        {
            if (granted)
            {
                MarkPermissionGranted();
            }
            else
            {
                IsGranted = false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EasyMicPlatformSupport.RequiresAndroidMainThread || EasyMicUnityThread.IsMainThread)
            {
                UnityEngine.Microphone.End(null); // stop Recording for permission request
            }
#endif
        }

        private static void MarkPermissionGranted()
        {
            if (IsGranted)
            {
                return;
            }

            IsGranted = true;
            EasyMicAPI.Cleanup();
        }


#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Opens the application's settings page on the device.
    /// </summary>
    private static void OpenAppSettings()
    {
        try
        {
            using (var unityClass = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var currentActivity = unityClass.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity"))
            using (var packageName = new UnityEngine.AndroidJavaObject("java.lang.String", UnityEngine.Application.identifier))
            using (var intentClass = new UnityEngine.AndroidJavaClass("android.content.Intent"))
            using (var settingsAction = new UnityEngine.AndroidJavaObject("java.lang.String", "android.settings.APPLICATION_DETAILS_SETTINGS"))
            using (var uriClass = new UnityEngine.AndroidJavaClass("android.net.Uri"))
            using (var uri = uriClass.CallStatic<UnityEngine.AndroidJavaObject>("fromParts", "package", packageName, null))
            {
                using (var intent = new UnityEngine.AndroidJavaObject("android.content.Intent", settingsAction, uri))
                {
                    intent.Call<UnityEngine.AndroidJavaObject>("addCategory", intentClass.GetStatic<UnityEngine.AndroidJavaObject>("CATEGORY_DEFAULT"));
                    intent.Call<UnityEngine.AndroidJavaObject>("setFlags", intentClass.GetStatic<int>("FLAG_ACTIVITY_NEW_TASK"));
                    currentActivity.Call("startActivity", intent);
                }
            }
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("Failed to open app settings: " + ex.Message);
        }
    } 
#endif

    }
}
