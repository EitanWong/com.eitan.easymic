#if UNITY_ANDROID && !UNITY_EDITOR
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

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool s_androidPermissionRequestInFlight;
        private static bool s_androidPermissionDenied;
        private static bool s_androidPermissionProbeActive;
        private static int s_androidPermissionProbeReleaseFrame = -1;
        private static float s_androidPermissionProbeReleaseTime = -1f;
        private static PermissionCallbacks s_androidPermissionCallbacks;
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
                return IsGranted && IsAndroidPermissionProbeReleased();
            }

            if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                s_androidPermissionDenied = false;
                MarkPermissionGranted();
                StopAndroidPermissionProbe();
                return IsAndroidPermissionProbeReleased();
            }

            IsGranted = false;
            StopAndroidPermissionProbe();
            if (!s_androidPermissionDenied)
            {
                RequestPlatformPermission();
            }

            return false;
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

        if (Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            OnPermissionResult(true);
            return;
        }

        if (s_androidPermissionRequestInFlight)
        {
            return;
        }

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            s_androidPermissionRequestInFlight = true;
            StartAndroidPermissionProbe();
            s_androidPermissionCallbacks = new PermissionCallbacks();
            s_androidPermissionCallbacks.PermissionGranted += s => OnPermissionResult(true);
            s_androidPermissionCallbacks.PermissionDenied += s => OnPermissionResult(false);
            s_androidPermissionCallbacks.PermissionDeniedAndDontAskAgain += s => OnPermissionResult(false);
            Permission.RequestUserPermission(Permission.Microphone, s_androidPermissionCallbacks);
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
#if UNITY_ANDROID && !UNITY_EDITOR
            s_androidPermissionRequestInFlight = false;
            s_androidPermissionDenied = !granted;
            s_androidPermissionCallbacks = null;
#endif

            if (granted)
            {
                MarkPermissionGranted();
            }
            else
            {
                IsGranted = false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            StopAndroidPermissionProbe();
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
        internal static string DiagnosticStatus
        {
            get
            {
                bool platformGranted = false;
                if (!EasyMicPlatformSupport.RequiresAndroidMainThread || EasyMicUnityThread.IsMainThread)
                {
                    platformGranted = Permission.HasUserAuthorizedPermission(Permission.Microphone);
                }

                return $"androidPermissionCached={IsGranted}, androidPermissionPlatform={platformGranted}, " +
                       $"androidPermissionRequestInFlight={s_androidPermissionRequestInFlight}, " +
                       $"androidPermissionDenied={s_androidPermissionDenied}, " +
                       $"unityMicrophoneProbeActive={s_androidPermissionProbeActive}, " +
                       $"nativeCaptureReady={IsAndroidPermissionProbeReleased()}";
            }
        }

        private static void StartAndroidPermissionProbe()
        {
            if (s_androidPermissionProbeActive)
            {
                return;
            }

            try
            {
                var sampleRate = UnityEngine.Mathf.Max(8000, UnityEngine.AudioSettings.outputSampleRate);
                var clip = UnityEngine.Microphone.Start(null, true, 1, sampleRate);
                s_androidPermissionProbeActive = clip != null;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("EasyMic: Unity microphone permission probe could not start. " + ex.Message);
                s_androidPermissionProbeActive = false;
            }
        }

        private static void StopAndroidPermissionProbe()
        {
            if (!s_androidPermissionProbeActive)
            {
                return;
            }

            if (EasyMicPlatformSupport.RequiresAndroidMainThread && !EasyMicUnityThread.IsMainThread)
            {
                return;
            }

            try
            {
                UnityEngine.Microphone.End(null);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("EasyMic: Unity microphone permission probe could not be stopped. " + ex.Message);
            }
            finally
            {
                s_androidPermissionProbeActive = false;
                s_androidPermissionProbeReleaseFrame = UnityEngine.Time.frameCount;
                s_androidPermissionProbeReleaseTime = UnityEngine.Time.realtimeSinceStartup;
            }
        }

        private static bool IsAndroidPermissionProbeReleased()
        {
            if (s_androidPermissionProbeActive)
            {
                return false;
            }

            if (s_androidPermissionProbeReleaseFrame < 0)
            {
                return true;
            }

            if (UnityEngine.Time.frameCount <= s_androidPermissionProbeReleaseFrame)
            {
                return false;
            }

            return UnityEngine.Time.realtimeSinceStartup - s_androidPermissionProbeReleaseTime >= 0.05f;
        }
#else
        internal static string DiagnosticStatus => "microphonePermissionNotRequired";
#endif


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
