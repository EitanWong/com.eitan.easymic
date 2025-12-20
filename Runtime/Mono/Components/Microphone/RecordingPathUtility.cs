using System;
using System.IO;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Utilities
{
    internal static class RecordingPathUtility
    {
        public static string PrepareActiveTempRecordingPath(int instanceId)
        {
            static string ResolveBaseDirectory(string root)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    return null;
                }

                var directory = Path.Combine(root, "EasyMic", "RecordingCache");
                try
                {
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    return directory;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"EasyMicrophone: Failed to prepare temp directory at '{directory}'. {ex.Message}");
                    return null;
                }
            }

            var baseDirectory = ResolveBaseDirectory(Application.temporaryCachePath) ??
                                 ResolveBaseDirectory(Application.persistentDataPath);

            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = Path.Combine(Path.GetTempPath(), "EasyMic", "RecordingCache");
            }

            try
            {
                if (!Directory.Exists(baseDirectory))
                {
                    Directory.CreateDirectory(baseDirectory);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"EasyMicrophone: Using system temp directory due to failure preparing '{baseDirectory}'. {ex.Message}");
                baseDirectory = Path.Combine(Path.GetTempPath(), "EasyMic", "RecordingCache");
                try
                {
                    if (!Directory.Exists(baseDirectory))
                    {
                        Directory.CreateDirectory(baseDirectory);
                    }
                }
                catch (Exception inner)
                {
                    Debug.LogError($"EasyMicrophone: Unable to create temp directory. {inner.Message}");
                }
            }

            string sessionId = Guid.NewGuid().ToString("N");
            return Path.Combine(baseDirectory, $"EasyMic_{instanceId:X8}_{sessionId}_active.wav");
        }

        public static string EnsureWavExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var extension = Path.GetExtension(path);
            return string.IsNullOrEmpty(extension) || !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                ? path + ".wav"
                : path;
        }
    }
}
