#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic;
using Eitan.EasyMic.Runtime;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(EasyMicrophone.DeviceOptions))]
    internal sealed class DeviceOptionsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            return line * 5f + spacing * 4f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var headerRect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.LabelField(headerRect, label, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            var deviceRect = new Rect(position.x, headerRect.yMax + spacing, position.width, line);
            var channelRect = new Rect(position.x, deviceRect.yMax + spacing, position.width, line);
            var sampleRect = new Rect(position.x, channelRect.yMax + spacing, position.width, line);
            var defaultRect = new Rect(position.x, sampleRect.yMax + spacing, position.width, line);

            var channelProp = property.FindPropertyRelative("Channel");
            var sampleProp = property.FindPropertyRelative("SampleRate");
            var deviceProp = property.FindPropertyRelative("DeviceName");

            if (property.serializedObject.targetObjects.Length != 1)
            {
                EditorGUI.HelpBox(deviceRect, "Multi-object editing is not supported.", MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            if (!(property.serializedObject.targetObject is EasyMicrophone mic))
            {
                EditorGUI.HelpBox(deviceRect, "Target is not an EasyMicrophone instance.", MessageType.Error);
                EditorGUI.EndProperty();
                return;
            }

            var options = mic.DeviceOpts;

#if UNITY_2021_2_OR_NEWER
            EasyMicAPI.Refresh();
            var devices = EasyMicAPI.Devices ?? Array.Empty<MicDevice>();
            if (devices.Length == 0)
            {
                EditorGUI.HelpBox(deviceRect, "No microphone devices detected.", MessageType.Warning);
            }
            else
            {
                var names = devices.Select(d => string.IsNullOrEmpty(d.Name) ? "Unnamed Device" : d.Name).ToArray();
                int currentIndex = FindDeviceIndex(devices, options.DeviceName);
                currentIndex = Mathf.Clamp(currentIndex < 0 ? 0 : currentIndex, 0, devices.Length - 1);

                EditorGUI.BeginChangeCheck();
                int selectedIndex = EditorGUI.Popup(deviceRect, "Device", currentIndex, names);
                if (EditorGUI.EndChangeCheck())
                {
                    var selected = devices[Mathf.Clamp(selectedIndex, 0, devices.Length - 1)];
                    Undo.RecordObject(mic, "Change Microphone Device");
                    options.DeviceName = selected.Name;
                    options.Channel = selected.GetPreferredChannel(options.Channel);
                    options.SampleRate = selected.GetPreferredSampleRate(options.SampleRate);
                    ApplyDeviceOptions(mic, options, restartRecording: Application.isPlaying);
                    if (deviceProp != null)
                    {
                        deviceProp.stringValue = options.DeviceName;
                    }
                    if (channelProp != null)
                    {
                        channelProp.intValue = (int)options.Channel;
                    }

                    if (sampleProp != null)
                    {
                        sampleProp.intValue = (int)options.SampleRate;
                    }

                    EditorUtility.SetDirty(mic);
                    property.serializedObject.Update();
                }
            }
#else
            EditorGUI.HelpBox(deviceRect, "Device selection requires Unity 2021.2 or newer.", MessageType.Info);
#endif

            EditorGUI.BeginChangeCheck();
            var channelValue = (Channel)EditorGUI.EnumPopup(channelRect, "Channel", options.Channel);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(mic, "Change Microphone Channel");
                options.Channel = channelValue;
                if (channelProp != null)
                {
                    channelProp.intValue = (int)channelValue;
                }
                ApplyDeviceOptions(mic, options, restartRecording: Application.isPlaying);
                EditorUtility.SetDirty(mic);
            }

            EditorGUI.BeginChangeCheck();
            var sampleValue = (SampleRate)EditorGUI.EnumPopup(sampleRect, "Sample Rate", options.SampleRate);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(mic, "Change Microphone Sample Rate");
                options.SampleRate = sampleValue;
                if (sampleProp != null)
                {
                    sampleProp.intValue = (int)sampleValue;
                }
                ApplyDeviceOptions(mic, options, restartRecording: Application.isPlaying);
                EditorUtility.SetDirty(mic);
            }

            if (GUI.Button(defaultRect, "Use Default Device"))
            {
                Undo.RecordObject(mic, "Reset Microphone Device");
                try
                {
                    var defaults = EasyMicrophone.DeviceOptions.Default;
                    options = defaults;
                    ApplyDeviceOptions(mic, options, restartRecording: Application.isPlaying);
                    if (deviceProp != null)
                    {
                        deviceProp.stringValue = options.DeviceName;
                    }

                    if (channelProp != null)
                    {
                        channelProp.intValue = (int)options.Channel;
                    }

                    if (sampleProp != null)
                    {
                        sampleProp.intValue = (int)options.SampleRate;
                    }

                    EditorUtility.SetDirty(mic);
                    property.serializedObject.Update();
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog("EasyMic", $"Failed to apply default device: {ex.Message}", "OK");
                }
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static void ApplyDeviceOptions(EasyMicrophone mic, EasyMicrophone.DeviceOptions options, bool restartRecording)
        {

            mic.ApplyDeviceOptions(options, restartRecording);
        }

        private static int FindDeviceIndex(IReadOnlyList<MicDevice> devices, string selected)
        {
            if (devices == null || devices.Count == 0)
            {
                return -1;
            }

            if (string.IsNullOrEmpty(selected))
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    if (string.IsNullOrEmpty(devices[i].Name))
                    {
                        return i;
                    }
                }

                return -1;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Name, selected, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Name, selected, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
#endif
