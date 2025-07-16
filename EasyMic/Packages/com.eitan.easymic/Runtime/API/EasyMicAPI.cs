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
            var defaultDevice = MicSys.Devices.FirstOrDefault(x => x.IsDefault);
            var channel=defaultDevice.GetDeviceChannel();
            return StartRecording(defaultDevice, sampleRate, channel);
        }

        public static RecordingHandle StartRecording(string name, SampleRate sampleRate = SampleRate.Hz16000, Channel channel = Channel.Mono)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            var matchedDevice = MicSys.Devices.FirstOrDefault(x => x.Name == name);
            return StartRecording(matchedDevice, sampleRate, channel);
        }

        public static RecordingHandle StartRecording(MicDevice device, SampleRate sampleRate = SampleRate.Hz16000, Channel channel = Channel.Mono)
        {
            if (!PermissionUtils.HasPermission())
            {
                Debug.LogError("Cannot start recording. Microphone permission not granted. Call EasyMic.RequestPermission() first.");
                return default;
            }
            return MicSys.StartRecording(device, sampleRate, channel);
        }
        
        // Stop, AddProcessor等方法不需要权限检查，因为它们是基于一个已经成功创建的handle
        public static void StopRecording(RecordingHandle handle) => MicSys.StopRecording(handle);
        public static void StopAllRecordings() => MicSys.StopAllRecordings();
        public static void AddProcessor(RecordingHandle handle, IAudioWorker processor) => MicSys.AddProcessor(handle, processor);
        public static void RemoveProcessor(RecordingHandle handle, IAudioWorker processor) => MicSys.RemoveProcessor(handle, processor);
        public static RecordingInfo GetRecordingInfo(RecordingHandle handle) => MicSys.GetRecordingInfo(handle);
        
        public static void Cleanup()
        {
            if (_micSystem != null)
            {
                _micSystem.Dispose();
                _micSystem = null;
            }
        }
    }
}