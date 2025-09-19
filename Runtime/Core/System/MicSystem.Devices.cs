using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Eitan.EasyMic.Runtime
{
    public sealed partial class MicSystem
    {
        public void Refresh()
        {
            RefreshDevicesInternal(false);
        }

        public void Refresh(bool suppressEvents)
        {
            RefreshDevicesInternal(suppressEvents);
        }

        public void EnableAutoRefresh(float intervalSeconds = 1f)
        {
            ThrowIfDisposed();

            if (_deviceWatcher != null)
            {
                _deviceWatcher.Attach(this, intervalSeconds);
                return;
            }

            _deviceWatcher = MicDeviceWatcher.Ensure(this, intervalSeconds);
        }

        public void DisableAutoRefresh()
        {
            if (_deviceWatcher != null)
            {
                _deviceWatcher.Detach(this);
                _deviceWatcher = null;
            }
        }

        private void RefreshDevicesInternal(bool suppressEvents)
        {
            ThrowIfDisposed();

            var previous = Devices ?? Array.Empty<MicDevice>();
            var current = EnumerateDevices();

            Devices = current;
            DeviceCount = current.Length;

            if (suppressEvents)
            {
                return;
            }

            var changeArgs = BuildChangeArgs(previous, current);
            if (!changeArgs.HasChanges)
            {
                return;
            }

            HandleDeviceRemovals(changeArgs.Removed, current);
            HandleDeviceAdditions(changeArgs.Added);
            HandleDeviceAdditions(changeArgs.Updated);
            DevicesChanged?.Invoke(changeArgs);
        }

        private MicDevice[] EnumerateDevices()
        {
            ThrowIfDisposed();

            IntPtr playbackInfos;
            uint playbackCount;
            IntPtr captureInfos;
            uint captureCount;

            var result = Native.ContextGetDevices(
                _context,
                out playbackInfos,
                out playbackCount,
                out captureInfos,
                out captureCount);

            if (result != Native.Result.Success)
            {
                throw new InvalidOperationException($"Unable to enumerate devices. {result}");
            }

            if (captureInfos == IntPtr.Zero || captureCount == 0)
            {
                return Array.Empty<MicDevice>();
            }

            var captureDevices = new MicDevice[(int)captureCount];
            for (int i = 0; i < captureDevices.Length; i++)
            {
                var basicInfo = Native.ReadDeviceInfo(captureInfos, i);

                Native.NativeDeviceInfo detailedInfo;
                var tempIdHandle = Marshal.AllocHGlobal(Native.DeviceIdSizeInBytes);
                try
                {
                    Marshal.Copy(basicInfo.Id, 0, tempIdHandle, basicInfo.Id.Length);
                    var infoResult = Native.ContextGetDeviceInfo(_context, Native.DeviceType.Record, tempIdHandle, out detailedInfo);
                    if (infoResult != Native.Result.Success)
                    {
                        detailedInfo = basicInfo;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(tempIdHandle);
                }

                var name = detailedInfo.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = basicInfo.Name;
                }

                captureDevices[i] = new MicDevice
                {
                    Name = name ?? string.Empty,
                    IsDefault = detailedInfo.IsDefault != 0,
                    DeviceId = basicInfo.Id != null ? (byte[])basicInfo.Id.Clone() : new byte[Native.DeviceIdSizeInBytes],
                    NativeFormats = detailedInfo.GetActiveNativeFormats()
                };
            }

            return captureDevices;
        }

        private MicDevicesChangedEventArgs BuildChangeArgs(MicDevice[] previous, MicDevice[] current)
        {
            previous ??= Array.Empty<MicDevice>();
            current ??= Array.Empty<MicDevice>();

            var previousMap = new Dictionary<string, MicDevice>(previous.Length);
            for (int i = 0; i < previous.Length; i++)
            {
                previousMap[previous[i].GetIdentifier()] = previous[i];
            }

            var currentMap = new Dictionary<string, MicDevice>(current.Length);
            for (int i = 0; i < current.Length; i++)
            {
                currentMap[current[i].GetIdentifier()] = current[i];
            }

            var added = new List<MicDevice>();
            var removed = new List<MicDevice>();
            var updated = new List<MicDevice>();

            foreach (var pair in currentMap)
            {
                if (!previousMap.TryGetValue(pair.Key, out var prev))
                {
                    added.Add(pair.Value);
                    continue;
                }

                if (IsDeviceUpdated(prev, pair.Value))
                {
                    updated.Add(pair.Value);
                }
            }

            foreach (var pair in previousMap)
            {
                if (!currentMap.ContainsKey(pair.Key))
                {
                    removed.Add(pair.Value);
                }
            }

            return new MicDevicesChangedEventArgs(
                previous,
                current,
                added.ToArray(),
                removed.ToArray(),
                updated.ToArray(),
                TryGetDefault(previous),
                TryGetDefault(current));
        }

        private void HandleDeviceRemovals(IReadOnlyList<MicDevice> removedDevices, MicDevice[] currentDevices)
        {
            if (removedDevices == null || removedDevices.Count == 0)
            {
                return;
            }

            currentDevices ??= Array.Empty<MicDevice>();

            lock (_operateLock)
            {
                if (_activeRecordings.Count == 0)
                {
                    return;
                }

                var keysToRemove = new List<int>();

                foreach (var pair in _activeRecordings)
                {
                    var session = pair.Value;
                    if (!IsSessionUsingRemovedDevice(session, removedDevices))
                    {
                        continue;
                    }

                    bool recovered = false;
                    if (TryFindReplacement(session.MicDevice, currentDevices, out var replacement))
                    {
                        var resolvedChannel = replacement.SupportsChannel(session.Channel)
                            ? session.Channel
                            : replacement.GetPreferredChannel(session.Channel);

                        var resolvedRate = replacement.ResolveSampleRate(session.SampleRate);

                        recovered = session.TrySwitchDevice(replacement, resolvedRate, resolvedChannel);
                        if (recovered)
                        {
                            try
                            {
                                Debug.Log($"EasyMic: Recording {pair.Key} rerouted to '{session.MicDevice.Name}' ({resolvedRate}, {resolvedChannel}) after device removal.");
                            }
                            catch
                            {
                                // logging only
                            }
                        }
                    }

                    if (!recovered)
                    {
                        keysToRemove.Add(pair.Key);
                        try
                        {
                            session.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"EasyMic: Error disposing session during device removal fallback. {ex.Message}");
                        }
                        Debug.LogWarning($"EasyMic: Recording {pair.Key} stopped because capture device '{session.MicDevice.Name}' was removed and no fallback device was available.");
                    }
                }

                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    _activeRecordings.Remove(keysToRemove[i]);
                }
            }
        }

        private void HandleDeviceAdditions(IReadOnlyList<MicDevice> addedDevices)
        {
            if (addedDevices == null || addedDevices.Count == 0)
            {
                return;
            }

            lock (_operateLock)
            {
                if (_activeRecordings.Count == 0)
                {
                    return;
                }

                foreach (var session in _activeRecordings.Values)
                {
                    if (!session.IsUsingFallback)
                    {
                        continue;
                    }

                    if (session.TryRestorePreferredDevice(addedDevices))
                    {
                        try
                        {
                            Debug.Log($"EasyMic: Recording restored to preferred device '{session.MicDevice.Name}'.");
                        }
                        catch { }
                    }
                }
            }
        }

        private static bool IsSessionUsingRemovedDevice(RecordingSession session, IReadOnlyList<MicDevice> removedDevices)
        {
            for (int i = 0; i < removedDevices.Count; i++)
            {
                if (removedDevices[i].SameIdentityAs(session.MicDevice))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindReplacement(MicDevice original, MicDevice[] candidates, out MicDevice replacement)
        {
            if (candidates == null || candidates.Length == 0)
            {
                replacement = default;
                return false;
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].SameIdentityAs(original))
                {
                    replacement = candidates[i];
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(original.Name))
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (string.Equals(candidates[i].Name, original.Name, StringComparison.Ordinal))
                    {
                        replacement = candidates[i];
                        return true;
                    }
                }
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i].IsDefault)
                {
                    replacement = candidates[i];
                    return true;
                }
            }

            replacement = candidates[0];
            return true;
        }

        private static MicDevice? TryGetDefault(MicDevice[] devices)
        {
            if (devices == null)
            {
                return null;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].IsDefault)
                {
                    return devices[i];
                }
            }

            return null;
        }

        private static bool IsDeviceUpdated(MicDevice previous, MicDevice current)
        {
            if (!string.Equals(previous.Name, current.Name, StringComparison.Ordinal))
            {
                return true;
            }

            if (previous.IsDefault != current.IsDefault)
            {
                return true;
            }

            return false;
        }
    }
}
