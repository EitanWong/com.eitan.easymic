#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// A self-contained module for handling microphone permissions on different platforms.
    /// </summary>
    public static class PermissionUtils
    {
        private static bool IsGranted;
        // 安卓权限的字符串常量

#if UNITY_ANDROID
        private const string PERMISSION_RECORD_AUDIO = "android.permission.RECORD_AUDIO";
#endif

        /// <summary>
        /// Checks if the microphone permission has been granted.
        /// </summary>
        public static bool HasPermission()
        {
            // 针对桌面平台和编辑器的优化：直接视为已授权，并立即返回。
#if UNITY_STANDALONE || UNITY_EDITOR
            return true;
#elif UNITY_ANDROID
            if (!IsGranted){
                IsGranted = Permission.HasUserAuthorizedPermission(Permission.Microphone);
                if(!IsGranted){
                    RequestPlatformPermission();
                }
            }
#endif
#pragma warning disable CS0162 // Unreachable code detected

            return IsGranted;
#pragma warning restore CS0162 // Unreachable code detected

        }

        private static void RequestPlatformPermission()
        {

#if UNITY_STANDALONE || UNITY_EDITOR
            return;
#elif UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            UnityEngine.Microphone.Start(null,true,1,UnityEngine.AudioSettings.outputSampleRate); // use Unity Microphone API force permission request
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += s => OnPermissionResult(true);
            callbacks.PermissionDenied += s => OnPermissionResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += s => OnPermissionResult(false);
            Permission.RequestUserPermission(Permission.Microphone, callbacks); // not working
        }
#endif
        }

        private static void OnPermissionResult(bool granted)
        {
            IsGranted = granted;
            
#if UNITY_ANDROID && !UNITY_EDITOR
            UnityEngine.Microphone.End(null); // stop Recording for permission request
#endif
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
