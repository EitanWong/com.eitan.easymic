using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Eitan.EasyMic.Runtime
{
    internal enum AndroidCaptureBackendLearnedPreference
    {
        Unknown = 0,
        AAudioCompatibility = 1,
        AAudioUltraSafe = 2,
        OpenSlLowLatency = 3,
        OpenSlSafe = 4
    }

    internal readonly struct AndroidCaptureBackendLearningScope
    {
        internal static AndroidCaptureBackendLearningScope Invalid => default;

        internal AndroidCaptureBackendLearningScope(string playerPrefsKey, string romHash, string endpointHash)
        {
            PlayerPrefsKey = playerPrefsKey ?? string.Empty;
            RomHash = romHash ?? string.Empty;
            EndpointHash = endpointHash ?? string.Empty;
        }

        internal string PlayerPrefsKey { get; }
        internal string RomHash { get; }
        internal string EndpointHash { get; }

        internal bool IsValid =>
            !string.IsNullOrEmpty(PlayerPrefsKey) &&
            !string.IsNullOrEmpty(RomHash) &&
            !string.IsNullOrEmpty(EndpointHash);

        internal bool SameStorageKeyAs(AndroidCaptureBackendLearningScope other)
        {
            return IsValid &&
                   other.IsValid &&
                   string.Equals(PlayerPrefsKey, other.PlayerPrefsKey, StringComparison.Ordinal);
        }
    }

    internal static class AndroidCaptureBackendLearning
    {
        internal const int CaptureStrategyVersion = 3;
        private const string PayloadPrefix = "emcap2";
        private const string PlayerPrefsKeyPrefix = "Eitan.EasyMic.AndroidCaptureBackendLearning";

        internal static bool RequiresOpenSlContext(AndroidCaptureBackendLearnedPreference preference)
        {
            return preference == AndroidCaptureBackendLearnedPreference.OpenSlLowLatency ||
                   preference == AndroidCaptureBackendLearnedPreference.OpenSlSafe;
        }

        internal static AndroidCaptureBackendLearningScope CreateScope(
            MicDevice device,
            IReadOnlyList<MicDevice> captureTopology)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!CanUseUnityPersistence())
            {
                return AndroidCaptureBackendLearningScope.Invalid;
            }

            string romFingerprint = GetCurrentAndroidRomFingerprint();
            if (string.IsNullOrEmpty(romFingerprint))
            {
                return AndroidCaptureBackendLearningScope.Invalid;
            }

            return CreateScope(romFingerprint, device, captureTopology);
#else
            _ = device;
            _ = captureTopology;
            return AndroidCaptureBackendLearningScope.Invalid;
#endif
        }

        internal static AndroidCaptureBackendLearningScope CreateScope(
            string romFingerprint,
            MicDevice device,
            IReadOnlyList<MicDevice> captureTopology)
        {
            if (string.IsNullOrEmpty(romFingerprint) || !device.HasValidId)
            {
                return AndroidCaptureBackendLearningScope.Invalid;
            }

            string endpointFingerprint = BuildEndpointFingerprint(device, captureTopology);
            if (string.IsNullOrEmpty(endpointFingerprint))
            {
                return AndroidCaptureBackendLearningScope.Invalid;
            }

            string romHash = ComputeStableHash(romFingerprint);
            string endpointHash = ComputeStableHash(endpointFingerprint);
            string key = BuildPlayerPrefsKey(romHash, endpointHash, CaptureStrategyVersion);
            return new AndroidCaptureBackendLearningScope(key, romHash, endpointHash);
        }

        internal static AndroidCaptureBackendLearnedPreference LoadPreference(AndroidCaptureBackendLearningScope scope)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!scope.IsValid || !CanUseUnityPersistence())
            {
                return AndroidCaptureBackendLearnedPreference.Unknown;
            }

            string payload = UnityEngine.PlayerPrefs.GetString(scope.PlayerPrefsKey, string.Empty);
            return TryParsePayload(payload, scope, CaptureStrategyVersion, out var preference)
                ? preference
                : AndroidCaptureBackendLearnedPreference.Unknown;
#else
            _ = scope;
            return AndroidCaptureBackendLearnedPreference.Unknown;
#endif
        }

        internal static bool RememberPreference(
            AndroidCaptureBackendLearningScope scope,
            AndroidCaptureBackendLearnedPreference preference)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!scope.IsValid || !CanUseUnityPersistence())
            {
                return false;
            }

            if (preference == AndroidCaptureBackendLearnedPreference.Unknown)
            {
                return ClearPreference(scope);
            }

            string payload = BuildPayload(scope, CaptureStrategyVersion, preference);
            if (string.Equals(UnityEngine.PlayerPrefs.GetString(scope.PlayerPrefsKey, string.Empty), payload, StringComparison.Ordinal))
            {
                return false;
            }

            UnityEngine.PlayerPrefs.SetString(scope.PlayerPrefsKey, payload);
            UnityEngine.PlayerPrefs.Save();
            return true;
#else
            _ = scope;
            _ = preference;
            return false;
#endif
        }

        internal static bool ClearPreference(AndroidCaptureBackendLearningScope scope)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!scope.IsValid || !CanUseUnityPersistence() || !UnityEngine.PlayerPrefs.HasKey(scope.PlayerPrefsKey))
            {
                return false;
            }

            UnityEngine.PlayerPrefs.DeleteKey(scope.PlayerPrefsKey);
            UnityEngine.PlayerPrefs.Save();
            return true;
#else
            _ = scope;
            return false;
#endif
        }

        internal static string GetPreferenceLabel(AndroidCaptureBackendLearnedPreference preference)
        {
            switch (preference)
            {
                case AndroidCaptureBackendLearnedPreference.AAudioCompatibility:
                    return "AAudio compatibility";
                case AndroidCaptureBackendLearnedPreference.AAudioUltraSafe:
                    return "AAudio ultra-safe";
                case AndroidCaptureBackendLearnedPreference.OpenSlLowLatency:
                    return "OpenSL ES low-latency";
                case AndroidCaptureBackendLearnedPreference.OpenSlSafe:
                    return "OpenSL ES safe";
                default:
                    return "unknown";
            }
        }

        internal static string BuildEndpointFingerprint(
            MicDevice device,
            IReadOnlyList<MicDevice> captureTopology)
        {
            if (!device.HasValidId)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(256);
            builder.Append("selected:{");
            AppendDeviceFingerprint(builder, device);
            builder.Append('}');

            if (ShouldIncludeCaptureTopology(device))
            {
                string topology = BuildCaptureTopologyFingerprint(captureTopology);
                if (!string.IsNullOrEmpty(topology))
                {
                    builder.Append("|topology:{");
                    builder.Append(topology);
                    builder.Append('}');
                }
            }

            return builder.ToString();
        }

        internal static string BuildPlayerPrefsKey(string romHash, string endpointHash, int strategyVersion)
        {
            if (string.IsNullOrEmpty(romHash) || string.IsNullOrEmpty(endpointHash))
            {
                return string.Empty;
            }

            return PlayerPrefsKeyPrefix + "." +
                   Math.Max(0, strategyVersion).ToString(CultureInfo.InvariantCulture) + "." +
                   romHash + "." +
                   endpointHash;
        }

        internal static string BuildPayload(
            AndroidCaptureBackendLearningScope scope,
            int strategyVersion,
            AndroidCaptureBackendLearnedPreference preference)
        {
            if (!scope.IsValid || preference == AndroidCaptureBackendLearnedPreference.Unknown)
            {
                return string.Empty;
            }

            return PayloadPrefix + "|" +
                   Math.Max(0, strategyVersion).ToString(CultureInfo.InvariantCulture) + "|" +
                   scope.RomHash + "|" +
                   scope.EndpointHash + "|" +
                   ((int)preference).ToString(CultureInfo.InvariantCulture);
        }

        internal static bool TryParsePayload(
            string payload,
            AndroidCaptureBackendLearningScope scope,
            int strategyVersion,
            out AndroidCaptureBackendLearnedPreference preference)
        {
            preference = AndroidCaptureBackendLearnedPreference.Unknown;
            if (string.IsNullOrEmpty(payload) || !scope.IsValid)
            {
                return false;
            }

            string[] parts = payload.Split('|');
            if (parts.Length != 5 ||
                !string.Equals(parts[0], PayloadPrefix, StringComparison.Ordinal) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int storedStrategyVersion) ||
                storedStrategyVersion != strategyVersion ||
                !string.Equals(parts[2], scope.RomHash, StringComparison.Ordinal) ||
                !string.Equals(parts[3], scope.EndpointHash, StringComparison.Ordinal) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int preferenceValue))
            {
                return false;
            }

            switch ((AndroidCaptureBackendLearnedPreference)preferenceValue)
            {
                case AndroidCaptureBackendLearnedPreference.AAudioCompatibility:
                case AndroidCaptureBackendLearnedPreference.AAudioUltraSafe:
                case AndroidCaptureBackendLearnedPreference.OpenSlLowLatency:
                case AndroidCaptureBackendLearnedPreference.OpenSlSafe:
                    preference = (AndroidCaptureBackendLearnedPreference)preferenceValue;
                    return true;
                default:
                    return false;
            }
        }

        private static string BuildCaptureTopologyFingerprint(IReadOnlyList<MicDevice> captureTopology)
        {
            if (captureTopology == null || captureTopology.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[captureTopology.Count];
            for (int i = 0; i < captureTopology.Count; i++)
            {
                var builder = new StringBuilder(128);
                AppendDeviceFingerprint(builder, captureTopology[i]);
                parts[i] = builder.ToString();
            }

            Array.Sort(parts, StringComparer.Ordinal);
            return string.Join(";", parts);
        }

        private static void AppendDeviceFingerprint(StringBuilder builder, MicDevice device)
        {
            builder.Append("name=");
            builder.Append(Normalize(device.Name));
            builder.Append(",default=");
            builder.Append(device.IsDefault ? "1" : "0");
            builder.Append(",id=");
            builder.Append(ComputeStableHash(device.DeviceId));
            builder.Append(",formats=");
            builder.Append(BuildNativeFormatsFingerprint(device.NativeFormats));
        }

        private static bool ShouldIncludeCaptureTopology(MicDevice device)
        {
            return device.IsDefault || !HasNonZeroDeviceId(device);
        }

        private static bool HasNonZeroDeviceId(MicDevice device)
        {
            if (!device.HasValidId)
            {
                return false;
            }

            for (int i = 0; i < device.DeviceId.Length; i++)
            {
                if (device.DeviceId[i] != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildNativeFormatsFingerprint(Native.NativeDataFormat[] formats)
        {
            if (formats == null || formats.Length == 0)
            {
                return "none";
            }

            var parts = new string[formats.Length];
            for (int i = 0; i < formats.Length; i++)
            {
                var fmt = formats[i];
                parts[i] =
                    ((int)fmt.Format).ToString(CultureInfo.InvariantCulture) + ":" +
                    fmt.Channels.ToString(CultureInfo.InvariantCulture) + ":" +
                    fmt.SampleRate.ToString(CultureInfo.InvariantCulture) + ":" +
                    ((uint)fmt.Flags).ToString(CultureInfo.InvariantCulture);
            }

            Array.Sort(parts, StringComparer.Ordinal);
            return string.Join(",", parts);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        internal static string ComputeStableHash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "0000000000000000";
            }

            unchecked
            {
                ulong hash = 1469598103934665603UL;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private static string ComputeStableHash(byte[] value)
        {
            if (value == null || value.Length == 0)
            {
                return "0000000000000000";
            }

            unchecked
            {
                ulong hash = 1469598103934665603UL;
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static bool CanUseUnityPersistence()
        {
            return !EasyMicPlatformSupport.RequiresAndroidMainThread || EasyMicUnityThread.IsMainThread;
        }

        private static string GetCurrentAndroidRomFingerprint()
        {
            try
            {
                using (var build = new UnityEngine.AndroidJavaClass("android.os.Build"))
                using (var version = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return string.Join("|", new[]
                    {
                        GetStaticString(build, "FINGERPRINT"),
                        GetStaticString(build, "MANUFACTURER"),
                        GetStaticString(build, "BRAND"),
                        GetStaticString(build, "MODEL"),
                        GetStaticString(build, "DEVICE"),
                        GetStaticString(build, "PRODUCT"),
                        GetStaticString(build, "HARDWARE"),
                        GetStaticIntString(version, "SDK_INT"),
                        GetStaticString(version, "INCREMENTAL")
                    });
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetStaticString(UnityEngine.AndroidJavaClass javaClass, string fieldName)
        {
            try
            {
                return javaClass.GetStatic<string>(fieldName) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetStaticIntString(UnityEngine.AndroidJavaClass javaClass, string fieldName)
        {
            try
            {
                return javaClass.GetStatic<int>(fieldName).ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }
#endif
    }
}
