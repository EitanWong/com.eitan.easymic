// EasyMic.cs (最终的“手动请求权限”模式)
using System;
using System.Linq;
using Eitan.EasyMic.Runtime;
using UnityEngine;

namespace Eitan.EasyMic
{
    public sealed class EasyMicAPI
    {
        private static MicSystem _micSystem;
        private static readonly object _lock = new object();
        private static System.Collections.Generic.List<AudioWorkerBlueprint> _defaultWorkers;

        private static MicSystem MicSys
        {
            get
            {
                if (_micSystem == null)
                {
                    lock (_lock)
                    {
                        if (_micSystem == null)
                            _micSystem = new MicSystem();
                    }
                }
                return _micSystem;
            }
        }

        public static MicDevice[] Devices
        {
            get
            {
                if (!PermissionUtils.HasPermission())
                {
                    Debug.LogWarning("Cannot get devices. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                    return Array.Empty<MicDevice>();
                }

                return MicSys.Devices;
            }
        }

        public static bool IsRecording => MicSys.HasActiveRecordings;

        public static void Refresh()
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogWarning("Cannot refresh. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return;
            }
            MicSys.Refresh();
        }

        public static RecordingHandle StartRecording(SampleRate sampleRate = SampleRate.Hz16000)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            // 设备兜底选择：优先默认设备，其次首个可用设备
            if (!TrySelectValidDevice(default, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }
            var channel = chosen.GetDeviceChannel();
            return StartRecording(chosen, sampleRate, channel, _defaultWorkers);
        }

        public static RecordingHandle StartRecording(string name, SampleRate sampleRate = SampleRate.Hz16000, Channel channel = Channel.Mono)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            var matchedDevice = MicSys.Devices.FirstOrDefault(x => x.Name == name);
            if (!TrySelectValidDevice(matchedDevice, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }
            // 若调用者未显式指定非默认声道，尝试读取设备声道布局
            var channelToUse = channel != Channel.Mono ? channel : chosen.GetDeviceChannel();
            return StartRecording(chosen, sampleRate, channelToUse, _defaultWorkers);
        }

        public static RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate = SampleRate.Hz16000, Channel channel = Channel.Mono)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            if (!TrySelectValidDevice(device, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }
            return MicSys.StartRecording(chosen, sampleRate, channel, _defaultWorkers);
        }

        // Overloads that accept worker blueprints
        public static RecordingHandle StartRecording(SampleRate sampleRate, System.Collections.Generic.IEnumerable<AudioWorkerBlueprint> workers)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            if (!TrySelectValidDevice(default, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }
            var channel = chosen.GetDeviceChannel();
            return MicSys.StartRecording(chosen, sampleRate, channel, workers);
        }

        public static RecordingHandle StartRecording(string name, SampleRate sampleRate, Channel channel, System.Collections.Generic.IEnumerable<AudioWorkerBlueprint> workers)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            var matchedDevice = MicSys.Devices.FirstOrDefault(x => x.Name == name);
            if (!TrySelectValidDevice(matchedDevice, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }
            var channelToUse = channel != Channel.Mono ? channel : chosen.GetDeviceChannel();
            return MicSys.StartRecording(chosen, sampleRate, channelToUse, workers);
        }

        public static RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate, Channel channel, System.Collections.Generic.IEnumerable<AudioWorkerBlueprint> workers)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            if (!TrySelectValidDevice(device, out var chosen))
            {
                Debug.LogError("EasyMic: No valid capture device available.");
                return default;
            }
            return MicSys.StartRecording(chosen, sampleRate, channel, workers);
        }

        /// <summary>
        /// Optional global default worker blueprints used by StartRecording overloads without explicit workers.
        /// Set once at app init to standardize usage across your app.
        /// </summary>
        public static System.Collections.Generic.List<AudioWorkerBlueprint> DefaultWorkers
        {
            get => _defaultWorkers;
            set => _defaultWorkers = value;
        }
        
        // Stop, Add/Remove 等方法不需要权限检查，因为它们是基于一个已经成功创建的 handle
        public static void StopRecording(RecordingHandle handle) => MicSys.StopRecording(handle);
        public static void StopAllRecordings() => MicSys.StopAllRecordings();
        public static void AddProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint) => MicSys.AddProcessor(handle, blueprint);
        public static void RemoveProcessor(RecordingHandle handle, AudioWorkerBlueprint blueprint) => MicSys.RemoveProcessor(handle, blueprint);
        public static RecordingInfo GetRecordingInfo(RecordingHandle handle) => MicSys.GetRecordingInfo(handle);

        public static T GetProcessor<T>(RecordingHandle handle, AudioWorkerBlueprint blueprint) where T : class, IAudioWorker
            => MicSys.GetProcessor<T>(handle, blueprint);

        public static void Cleanup()
        {
            if (_micSystem != null)
            {
                _micSystem.Dispose();
                _micSystem = null;
            }
        }

        // 设备选择兜底：优先使用传入设备；否则选择默认设备；再否则选择第一个设备
        private static bool TrySelectValidDevice(MicDevice preferred, out MicDevice chosen)
        {
            chosen = preferred;
            if (chosen.Id != IntPtr.Zero) return true;

            var devices = MicSys.Devices ?? Array.Empty<MicDevice>();
            var dflt = devices.FirstOrDefault(x => x.IsDefault);
            if (dflt.Id != IntPtr.Zero) { chosen = dflt; return true; }
            if (devices.Length > 0) { chosen = devices[0]; return true; }
            chosen = default;
            return false;
        }
    }
}
