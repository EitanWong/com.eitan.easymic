using System;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        private bool TryAdvanceAndroidCaptureBackendFallback(NativeDeviceActivationException activationFailure)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            ThrowIfDisposed();
            if (_activeRecordings.Count != 0)
            {
                return false;
            }

            if (!_contextIsOpenSlOnly &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.Default &&
                !_androidCaptureAttempt.OpenSlBackend)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.AAudioCompatibility;
                Log(
                    "EasyMic: Android AAudio low-latency capture activation failed; " +
                    "retrying with compatible AAudio capture profile. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                return true;
            }

            if (!_contextIsOpenSlOnly &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.AAudioCompatibility)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.AAudioUltraSafe;
                Log(
                    "EasyMic: Android compatible AAudio capture activation failed; " +
                    "retrying with ultra-safe AAudio capture profile. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                return true;
            }

            if (!_contextIsOpenSlOnly &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.AAudioUltraSafe)
            {
                Log(
                    "EasyMic: Android ultra-safe AAudio capture activation failed; " +
                    "retrying capture with OpenSL ES low-latency backend. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                ReplaceContext(new[] { Native.Backend.OpenSl }, usingAndroidOpenSlFallback: true);
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.OpenSlLowLatency;
                RefreshDevicesAfterAndroidBackendSwitch();
                return true;
            }

            if (_androidCaptureAttempt.OpenSlBackend &&
                _androidCaptureAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.Default)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.OpenSlSafe;
                Log(
                    "EasyMic: Android OpenSL ES low-latency capture activation failed; " +
                    "retrying with safe OpenSL ES capture profile. " +
                    BuildAndroidFallbackReason(activationFailure),
                    LogLevel.Warning);
                return true;
            }

            return false;
#else
            _ = activationFailure;
            return false;
#endif
        }

        private void PrepareAndroidCaptureAttemptForDevice(
            MicDevice requestedDevice,
            ref MicDevice chosen,
            ref SampleRate sampleRate,
            ref Channel channel,
            ref AndroidCaptureBackendLearningScope learningScope)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EasyMicRuntimeSettings.Current.Android.useAAudio)
            {
                return;
            }

            if (_contextIsOpenSlOnly && _usingAndroidOpenSlFallback)
            {
                learningScope = AndroidCaptureBackendLearning.CreateScope(chosen, Devices);
                var openSlPreference = AndroidCaptureBackendLearning.LoadPreference(learningScope);
                if (openSlPreference != AndroidCaptureBackendLearnedPreference.Unknown)
                {
                    ApplyLearnedAndroidCapturePreference(openSlPreference);
                    return;
                }

                if (!_androidAAudioContextUnavailable &&
                    _activeRecordings.Count == 0 &&
                    TryRestoreAndroidAAudioContextForProbe())
                {
                    chosen = ResolveDevice(requestedDevice);
                    ResolveFormatForDevice(chosen, ref sampleRate, ref channel);
                    learningScope = AndroidCaptureBackendLearning.CreateScope(chosen, Devices);
                }
            }

            learningScope = AndroidCaptureBackendLearning.CreateScope(chosen, Devices);
            var preference = AndroidCaptureBackendLearning.LoadPreference(learningScope);
            if (preference == AndroidCaptureBackendLearnedPreference.Unknown)
            {
                return;
            }

            if (!_contextIsOpenSlOnly && AndroidCaptureBackendLearning.RequiresOpenSlContext(preference))
            {
                if (_activeRecordings.Count != 0)
                {
                    Log(
                        "EasyMic: Android capture has a learned OpenSL ES fallback for this endpoint, " +
                        "but the active AAudio context cannot be replaced while another recording is running.",
                        LogLevel.Warning);
                    return;
                }

                Log(
                    "EasyMic: Android capture is starting with the learned " +
                    AndroidCaptureBackendLearning.GetPreferenceLabel(preference) +
                    " profile for this endpoint.",
                    LogLevel.Info);
                ReplaceContext(new[] { Native.Backend.OpenSl }, usingAndroidOpenSlFallback: true);
                ApplyLearnedAndroidCapturePreference(preference);
                RefreshDevicesAfterAndroidBackendSwitch();
                chosen = ResolveDevice(requestedDevice);
                ResolveFormatForDevice(chosen, ref sampleRate, ref channel);
                return;
            }

            ApplyLearnedAndroidCapturePreference(preference);
#else
            _ = requestedDevice;
            _ = chosen;
            _ = sampleRate;
            _ = channel;
            _ = learningScope;
#endif
        }

        private bool TryRestoreAndroidAAudioContextForProbe()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EasyMicRuntimeSettings.Current.Android.useAAudio ||
                !_contextIsOpenSlOnly ||
                !_usingAndroidOpenSlFallback ||
                _activeRecordings.Count != 0)
            {
                return false;
            }

            try
            {
                ReplaceContext(new[] { Native.Backend.AAudio }, usingAndroidOpenSlFallback: false);
                _androidAAudioContextUnavailable = false;
                RefreshDevicesAfterAndroidBackendSwitch();
                Log(
                    "EasyMic: Android capture is probing AAudio again for an unlearned capture endpoint.",
                    LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                _androidAAudioContextUnavailable = true;
                Log(
                    "EasyMic: Android capture could not restore AAudio probing; " +
                    "continuing with the current OpenSL ES backend. " + ex.Message,
                    LogLevel.Warning);
                return false;
            }
#else
            return false;
#endif
        }

        private void RefreshDevicesAfterAndroidBackendSwitch()
        {
            try
            {
                RefreshDevicesInternal(true);
            }
            catch (Exception ex)
            {
                Devices = Array.Empty<MicDevice>();
                DeviceCount = 0;
                Log(
                    "EasyMic: Android backend fallback could not refresh capture devices; " +
                    "continuing with the platform default capture endpoint. " + ex.Message,
                    LogLevel.Warning);
            }
        }

        private void ApplyLearnedAndroidCapturePreference(AndroidCaptureBackendLearnedPreference preference)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!_contextIsOpenSlOnly && preference == AndroidCaptureBackendLearnedPreference.AAudioCompatibility)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.AAudioCompatibility;
                Log("EasyMic: Android capture is starting with the learned AAudio compatibility profile.", LogLevel.Info);
                return;
            }

            if (!_contextIsOpenSlOnly && preference == AndroidCaptureBackendLearnedPreference.AAudioUltraSafe)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.AAudioUltraSafe;
                Log("EasyMic: Android capture is starting with the learned AAudio ultra-safe profile.", LogLevel.Info);
                return;
            }

            if (_contextIsOpenSlOnly && preference == AndroidCaptureBackendLearnedPreference.OpenSlLowLatency)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.OpenSlLowLatency;
                Log("EasyMic: Android capture is starting with the learned OpenSL ES low-latency profile.", LogLevel.Info);
                return;
            }

            if (_contextIsOpenSlOnly && preference == AndroidCaptureBackendLearnedPreference.OpenSlSafe)
            {
                _androidCaptureAttempt = AndroidCaptureBackendAttempt.OpenSlSafe;
                Log("EasyMic: Android capture is starting with the learned safe OpenSL ES profile.", LogLevel.Info);
            }
#else
            _ = preference;
#endif
        }

        private void RememberAndroidCapturePreference(
            AndroidCaptureBackendLearningScope scope,
            AndroidCaptureBackendLearnedPreference preference)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EasyMicRuntimeSettings.Current.Android.useAAudio)
            {
                return;
            }

            if (AndroidCaptureBackendLearning.RememberPreference(scope, preference))
            {
                Log(
                    "EasyMic: Android capture learned a stable fallback profile for this endpoint: " +
                    AndroidCaptureBackendLearning.GetPreferenceLabel(preference) + ".",
                    LogLevel.Warning);
            }
#else
            _ = scope;
            _ = preference;
#endif
        }

        private void RememberAndroidCaptureSuccess(
            AndroidCaptureBackendLearningScope learningScope,
            MicDevice successfulDevice,
            AndroidCaptureBackendAttempt successfulAttempt,
            bool hadLearnableAAudioActivationFailure)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!EasyMicRuntimeSettings.Current.Android.useAAudio)
            {
                return;
            }

            if (!successfulAttempt.OpenSlBackend)
            {
                switch (successfulAttempt.ConfigProfile)
                {
                    case Native.AndroidCaptureDeviceConfigProfile.AAudioCompatibility:
                        if (hadLearnableAAudioActivationFailure)
                        {
                            RememberAndroidCapturePreference(
                                learningScope,
                                AndroidCaptureBackendLearnedPreference.AAudioCompatibility);
                        }
                        break;
                    case Native.AndroidCaptureDeviceConfigProfile.AAudioUltraSafe:
                        if (hadLearnableAAudioActivationFailure)
                        {
                            RememberAndroidCapturePreference(
                                learningScope,
                                AndroidCaptureBackendLearnedPreference.AAudioUltraSafe);
                        }
                        break;
                    default:
                        if (AndroidCaptureBackendLearning.ClearPreference(learningScope))
                        {
                            Log(
                                "EasyMic: Android capture cleared a learned fallback because AAudio low-latency succeeded for this endpoint.",
                                LogLevel.Info);
                        }
                        break;
                }

                return;
            }

            if (!hadLearnableAAudioActivationFailure)
            {
                return;
            }

            var preference = successfulAttempt.ConfigProfile == Native.AndroidCaptureDeviceConfigProfile.OpenSlSafe
                ? AndroidCaptureBackendLearnedPreference.OpenSlSafe
                : AndroidCaptureBackendLearnedPreference.OpenSlLowLatency;

            RememberAndroidCapturePreference(learningScope, preference);

            var openSlScope = AndroidCaptureBackendLearning.CreateScope(successfulDevice, Devices);
            if (openSlScope.IsValid && !openSlScope.SameStorageKeyAs(learningScope))
            {
                RememberAndroidCapturePreference(openSlScope, preference);
            }
#else
            _ = learningScope;
            _ = successfulDevice;
            _ = successfulAttempt;
            _ = hadLearnableAAudioActivationFailure;
#endif
        }

        private static AndroidCaptureBackendAttempt GetInitialAndroidCaptureAttempt(bool contextIsOpenSlOnly)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (contextIsOpenSlOnly || !EasyMicRuntimeSettings.Current.Android.useAAudio)
            {
                return AndroidCaptureBackendAttempt.OpenSlLowLatency;
            }

            return AndroidCaptureBackendAttempt.AAudioLowLatency;
#else
            _ = contextIsOpenSlOnly;
            return AndroidCaptureBackendAttempt.Default;
#endif
        }

        private static string BuildAndroidFallbackReason(NativeDeviceActivationException activationFailure)
        {
            return activationFailure == null
                ? string.Empty
                : "Previous failure: " + activationFailure.Message;
        }
    }
}
