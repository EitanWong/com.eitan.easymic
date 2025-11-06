#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Eitan.EasyMic;
using Eitan.EasyMic.Runtime;
using Eitan.EasyMic.Runtime.Mono;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{
    [CustomPropertyDrawer(typeof(DeviceOptions))]
    internal sealed class DeviceOptionsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float height = Styles.Padding * 2f;
            height += line; // header
            height += Styles.RowSpacing;
            height += line + Styles.InnerSpacing + Styles.FieldHeight; // device section
            height += Styles.RowSpacing;
            height += line + Styles.InnerSpacing + Styles.FieldHeight; // signal path section
            height += Styles.RowSpacing;
            height += Styles.ButtonHeight; // actions row
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect frameRect = EditorGUI.IndentedRect(position);
            Styles.DrawFrame(frameRect);

            Rect contentRect = new Rect(
                frameRect.x + Styles.Padding,
                frameRect.y + Styles.Padding,
                frameRect.width - Styles.Padding * 2f,
                frameRect.height - Styles.Padding * 2f);

            float line = EditorGUIUtility.singleLineHeight;
            float cursorY = contentRect.y;

            var channelProp = property.FindPropertyRelative("Channel");
            var sampleProp = property.FindPropertyRelative("SampleRate");
            var deviceProp = property.FindPropertyRelative("DeviceName");

            Rect headerRect = new Rect(contentRect.x, cursorY, contentRect.width, line);
            EditorGUI.LabelField(headerRect, label, Styles.SectionHeader);
            cursorY = headerRect.yMax + Styles.RowSpacing;

            if (property.serializedObject.targetObjects.Length != 1)
            {
                Rect helpRect = new Rect(contentRect.x, cursorY, contentRect.width, Styles.FieldHeight);
                EditorGUI.HelpBox(helpRect, "Multi-object editing is not supported.", MessageType.Info);
                EditorGUI.EndProperty();
                return;
            }

            if (!(property.serializedObject.targetObject is EasyMicrophone mic))
            {
                Rect helpRect = new Rect(contentRect.x, cursorY, contentRect.width, Styles.FieldHeight);
                EditorGUI.HelpBox(helpRect, "Target is not an EasyMicrophone instance.", MessageType.Error);
                EditorGUI.EndProperty();
                return;
            }

            DeviceOptions options = mic.DeviceOpts;

            Rect deviceLabelRect = new Rect(contentRect.x, cursorY, contentRect.width, line);
            EditorGUI.LabelField(deviceLabelRect, Styles.DeviceLabelContent, Styles.CaptionLabel);
            cursorY = deviceLabelRect.yMax + Styles.InnerSpacing;

            Rect deviceFieldRect = new Rect(contentRect.x, cursorY, contentRect.width, Styles.FieldHeight);

#if UNITY_2021_2_OR_NEWER
            EasyMicAPI.Refresh();
            MicDevice[] devices = EasyMicAPI.Devices ?? Array.Empty<MicDevice>();
            if (devices.Length == 0)
            {
                EditorGUI.HelpBox(deviceFieldRect, "No microphone devices detected.", MessageType.Warning);
            }
            else
            {
                string[] names = devices.Select(d => string.IsNullOrEmpty(d.Name) ? "Unnamed Device" : d.Name).ToArray();
                int currentIndex = FindDeviceIndex(devices, options.DeviceName);
                currentIndex = Mathf.Clamp(currentIndex < 0 ? 0 : currentIndex, 0, devices.Length - 1);

                EditorGUI.BeginChangeCheck();
                int selectedIndex = EditorGUI.Popup(deviceFieldRect, currentIndex, names);
                if (EditorGUI.EndChangeCheck())
                {
                    MicDevice selected = devices[Mathf.Clamp(selectedIndex, 0, devices.Length - 1)];
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
            EditorGUI.HelpBox(deviceFieldRect, "Device selection requires Unity 2021.2 or newer.", MessageType.Info);
#endif

            cursorY = deviceFieldRect.yMax + Styles.RowSpacing;

            Rect separatorRect = new Rect(contentRect.x, cursorY - Styles.RowSpacing * 0.5f, contentRect.width, 1f);
            EditorGUI.DrawRect(separatorRect, Styles.SeparatorColor);

            Rect signalLabelRect = new Rect(contentRect.x, cursorY, contentRect.width, line);
            EditorGUI.LabelField(signalLabelRect, Styles.SignalPathLabelContent, Styles.CaptionLabel);
            cursorY = signalLabelRect.yMax + Styles.InnerSpacing;

            float columnWidth = (contentRect.width - Styles.ColumnSpacing) * 0.5f;
            Rect channelFieldRect = new Rect(contentRect.x, cursorY, columnWidth, Styles.FieldHeight);
            Rect sampleFieldRect = new Rect(channelFieldRect.xMax + Styles.ColumnSpacing, cursorY, columnWidth, Styles.FieldHeight);

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Styles.FieldLabelWidth;

            EditorGUI.BeginChangeCheck();
            Channel channelValue = (Channel)EditorGUI.EnumPopup(channelFieldRect, Styles.ChannelLabelContent, options.Channel);
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
            SampleRate sampleValue = (SampleRate)EditorGUI.EnumPopup(sampleFieldRect, Styles.SampleRateLabelContent, options.SampleRate);
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

            EditorGUIUtility.labelWidth = previousLabelWidth;

            cursorY = Mathf.Max(channelFieldRect.yMax, sampleFieldRect.yMax) + Styles.RowSpacing;

            Rect actionSeparatorRect = new Rect(contentRect.x, cursorY - Styles.RowSpacing * 0.5f, contentRect.width, 1f);
            EditorGUI.DrawRect(actionSeparatorRect, Styles.SeparatorColor);

            Rect buttonRect = new Rect(contentRect.x, cursorY, Mathf.Min(Styles.ButtonWidth, contentRect.width * 0.6f), Styles.ButtonHeight);
            if (GUI.Button(buttonRect, Styles.UseDefaultContent, Styles.ActionButton))
            {
                Undo.RecordObject(mic, "Reset Microphone Device");
                try
                {
                    DeviceOptions defaults = DeviceOptions.Default;
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

            Rect summaryRect = new Rect(
                buttonRect.xMax + 12f,
                buttonRect.y,
                contentRect.width - (buttonRect.width + 12f),
                Styles.ButtonHeight);
            if (summaryRect.width > 0f)
            {
                GUI.Label(summaryRect, FormatSummary(options), Styles.SummaryLabel);
            }

            EditorGUI.EndProperty();
        }

        private static void ApplyDeviceOptions(EasyMicrophone mic, DeviceOptions options, bool restartRecording)
        {

            mic.ApplyDeviceOptions(options, restartRecording);
        }

        private static string FormatSummary(DeviceOptions options)
        {
            string deviceName = string.IsNullOrEmpty(options.DeviceName) ? "System Default" : options.DeviceName;
            string channel = options.Channel.ToString();
            int sampleRate = (int)options.SampleRate;
            return $"Current: {deviceName}   |   Channel: {channel}   |   {sampleRate} Hz";
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

        private static class Styles
        {
            public static readonly GUIStyle SectionHeader;
            public static readonly GUIStyle CaptionLabel;
            public static readonly GUIStyle ActionButton;
            public static readonly GUIStyle SummaryLabel;

            public static readonly Color FrameBackground;
            public static readonly Color FrameBorder;
            public static readonly Color SeparatorColor;

            public static readonly GUIContent UseDefaultContent;
            public static readonly GUIContent DeviceLabelContent;
            public static readonly GUIContent SignalPathLabelContent;
            public static readonly GUIContent ChannelLabelContent;
            public static readonly GUIContent SampleRateLabelContent;

            public static float Padding => 10f;
            public static float RowSpacing => 10f;
            public static float InnerSpacing => 4f;
            public static float ColumnSpacing => 12f;
            public static float FieldHeight => EditorGUIUtility.singleLineHeight * 2f;
            public static float ButtonHeight => EditorGUIUtility.singleLineHeight + 6f;
            public static float ButtonWidth => 180f;
            public static float FieldLabelWidth => 95f;

            static Styles()
            {
                bool proSkin = EditorGUIUtility.isProSkin;

                FrameBackground = proSkin ? new Color(0.16f, 0.16f, 0.16f, 1f) : new Color(0.94f, 0.94f, 0.94f, 1f);
                FrameBorder = proSkin ? new Color(0.28f, 0.28f, 0.28f, 1f) : new Color(0.78f, 0.78f, 0.78f, 1f);
                SeparatorColor = proSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.08f);

                SectionHeader = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft
                };

                CaptionLabel = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = proSkin ? new Color(0.85f, 0.85f, 0.85f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f) }
                };

                ActionButton = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(12, 12, 4, 4),
                    fixedHeight = ButtonHeight,
                    fontStyle = FontStyle.Bold
                };

                SummaryLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    normal = { textColor = proSkin ? new Color(0.75f, 0.75f, 0.75f, 1f) : new Color(0.35f, 0.35f, 0.35f, 1f) }
                };

                UseDefaultContent = MakeLabeledContent("Refresh", "Use Default Device", "Revert to the system default microphone device", "Use Default Device");
                DeviceLabelContent = MakeLabel("Input Device", "AudioSource Icon");
                SignalPathLabelContent = MakeLabel("Signal Path", "Animation.Record");
                ChannelLabelContent = MakeLabel("Channel", "SceneViewOrtho");
                SampleRateLabelContent = MakeLabel("Sample Rate", "Profiler.Audio");
            }

            public static void DrawFrame(Rect rect)
            {
                EditorGUI.DrawRect(rect, FrameBackground);
                DrawBorder(rect, FrameBorder);
            }

            public static void DrawBorder(Rect rect, Color color)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
            }

            private static GUIContent MakeLabel(string text, string iconName)
            {
                var content = EditorGUIUtility.IconContent(iconName);
                if (content == null || content.image == null)
                {
                    return new GUIContent(text);
                }

                return new GUIContent($" {text}", content.image);
            }

            private static GUIContent MakeLabeledContent(string iconName, string text, string tooltip, string fallbackText)
            {
                var content = EditorGUIUtility.IconContent(iconName);
                if (content == null || content.image == null)
                {
                    return new GUIContent(fallbackText ?? text, tooltip);
                }

                return new GUIContent($" {text}", content.image, tooltip);
            }
        }
    }
}
#endif
