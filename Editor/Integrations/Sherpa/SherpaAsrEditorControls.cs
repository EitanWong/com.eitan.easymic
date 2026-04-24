#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Eitan.EasyMic.Editor;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    internal enum SherpaAsrModelList
    {
        StreamingAsr,
        OfflineAsr,
        Vad,
        Punctuation,
        Keyword
    }

    internal static class SherpaAsrEditorControls
    {
        private const float PickerWidth = 156f;
        private const float MinimumInlineModelFieldWidth = 320f;
        private const float MinimumInlinePropertyFieldWidth = 300f;
        private const float MinimumInlineRangeWidth = 360f;
        private const float PopupButtonWidth = 24f;
        private const float FieldSpacing = 4f;
        private const float TurnRangeMaxDefault = 5f;
        private const float KeywordRemoveButtonWidth = 24f;
        private const float KeywordRowPadding = 4f;
        private const float MinimumInlineKeywordSettingsWidth = 340f;
        private static readonly Dictionary<SherpaAsrModelList, string[]> ModelIdsCache = new Dictionary<SherpaAsrModelList, string[]>();
        private static readonly HashSet<string> ManualModelFields = new HashSet<string>();
        private static readonly Dictionary<string, string> KeywordDrafts = new Dictionary<string, string>();
        private static readonly Dictionary<string, WidthSnapshot> PropertyContentWidths = new Dictionary<string, WidthSnapshot>();

        public static float ModelFieldHeight(float width)
        {
            return width >= MinimumInlineModelFieldWidth
                ? EditorGUIUtility.singleLineHeight
                : EditorGUIUtility.singleLineHeight * 2f + FieldSpacing;
        }

        public static float EstimateNestedContentWidth()
        {
            return Mathf.Max(120f, EditorGUIUtility.currentViewWidth - AsrInspectorStyles.ContentPadding * 4f);
        }

        public static void RememberContentWidth(SerializedProperty property, float width)
        {
            if (property == null)
            {
                return;
            }

            PropertyContentWidths[GetManualFieldKey(property)] = new WidthSnapshot(
                Mathf.Max(0f, width),
                Mathf.Max(0f, EditorGUIUtility.currentViewWidth));
        }

        public static float GetRememberedContentWidth(SerializedProperty property)
        {
            float estimatedWidth = EstimateNestedContentWidth();
            if (property != null &&
                PropertyContentWidths.TryGetValue(GetManualFieldKey(property), out WidthSnapshot snapshot) &&
                snapshot.ContentWidth > 0f)
            {
                float viewDelta = EditorGUIUtility.currentViewWidth - snapshot.InspectorWidth;
                float projectedWidth = snapshot.ContentWidth + viewDelta;
                return Mathf.Max(120f, projectedWidth);
            }

            return estimatedWidth;
        }

        public static void ForgetContentWidth(SerializedProperty property)
        {
            if (property == null)
            {
                return;
            }

            PropertyContentWidths.Remove(GetManualFieldKey(property));
        }

        public static float ResponsivePropertyFieldHeight(float width)
        {
            return width >= MinimumInlinePropertyFieldWidth
                ? EditorGUIUtility.singleLineHeight
                : EditorGUIUtility.singleLineHeight * 2f + FieldSpacing;
        }

        public static void DrawResponsivePropertyField(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (property == null)
            {
                return;
            }

            if (rect.width >= MinimumInlinePropertyFieldWidth)
            {
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight), property, label);
                return;
            }

            Rect labelRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = new Rect(rect.x, labelRect.yMax + FieldSpacing, rect.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(labelRect, label);
            EditorGUI.PropertyField(fieldRect, property, GUIContent.none);
        }

        private struct WidthSnapshot
        {
            public WidthSnapshot(float contentWidth, float inspectorWidth)
            {
                ContentWidth = contentWidth;
                InspectorWidth = inspectorWidth;
            }

            public float ContentWidth { get; }
            public float InspectorWidth { get; }
        }

        public static float TurnRangeHeight(float width)
        {
            return width >= MinimumInlineRangeWidth
                ? EditorGUIUtility.singleLineHeight * 2f + FieldSpacing
                : EditorGUIUtility.singleLineHeight * 3f + FieldSpacing * 2f;
        }

        public static void DrawModelIdField(Rect rect, SerializedProperty property, GUIContent label, SherpaAsrModelList list)
        {
            if (property == null || property.propertyType != SerializedPropertyType.String)
            {
                if (property != null)
                {
                    EditorGUI.PropertyField(rect, property, label);
                }

                return;
            }

            Rect labelRect;
            Rect controlRect;
            if (rect.width >= MinimumInlineModelFieldWidth)
            {
                controlRect = EditorGUI.PrefixLabel(rect, label);
            }
            else
            {
                labelRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(labelRect, label);
                controlRect = new Rect(rect.x, labelRect.yMax + FieldSpacing, rect.width, EditorGUIUtility.singleLineHeight);
            }

            Rect valueRect = new Rect(controlRect.x, controlRect.y, Mathf.Max(0f, controlRect.width - PopupButtonWidth - FieldSpacing), EditorGUIUtility.singleLineHeight);
            Rect buttonRect = new Rect(valueRect.xMax + FieldSpacing, controlRect.y, PopupButtonWidth, EditorGUIUtility.singleLineHeight);

            string key = GetManualFieldKey(property);
            bool manual = ManualModelFields.Contains(key);
            if (manual)
            {
                property.stringValue = EditorGUI.TextField(valueRect, property.stringValue);
                if (GUI.Button(buttonRect, Styles.PickerModeContent, EditorStyles.miniButton))
                {
                    ManualModelFields.Remove(key);
                }

                return;
            }

            DrawModelPicker(valueRect, property, list);
            if (GUI.Button(buttonRect, Styles.ManualModeContent, EditorStyles.miniButton))
            {
                ManualModelFields.Add(key);
            }
        }

        public static void DrawTurnRange(Rect rect, SerializedProperty minProp, SerializedProperty maxProp, GUIContent minLabel, GUIContent maxLabel)
        {
            if (minProp == null || maxProp == null)
            {
                return;
            }

            float minValue = Mathf.Max(0f, minProp.floatValue);
            float maxValue = Mathf.Max(minValue, maxProp.floatValue);
            float rangeMax = Mathf.Max(TurnRangeMaxDefault, Mathf.Ceil(maxValue));
            bool inline = rect.width >= MinimumInlineRangeWidth;

            Rect sliderRect = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            Rect minRect;
            Rect maxRect;
            if (inline)
            {
                float halfWidth = (rect.width - FieldSpacing) * 0.5f;
                minRect = new Rect(rect.x, sliderRect.yMax + FieldSpacing, halfWidth, EditorGUIUtility.singleLineHeight);
                maxRect = new Rect(minRect.xMax + FieldSpacing, sliderRect.yMax + FieldSpacing, halfWidth, EditorGUIUtility.singleLineHeight);
            }
            else
            {
                minRect = new Rect(rect.x, sliderRect.yMax + FieldSpacing, rect.width, EditorGUIUtility.singleLineHeight);
                maxRect = new Rect(rect.x, minRect.yMax + FieldSpacing, rect.width, EditorGUIUtility.singleLineHeight);
            }

            EditorGUI.MinMaxSlider(sliderRect, ref minValue, ref maxValue, 0f, rangeMax);
            minValue = DrawCompactFloatField(minRect, minLabel, minValue);
            maxValue = DrawCompactFloatField(maxRect, maxLabel, maxValue);

            minValue = Mathf.Max(0f, minValue);
            maxValue = Mathf.Max(minValue, maxValue);
            minProp.floatValue = minValue;
            maxProp.floatValue = maxValue;
        }

        public static float CustomKeywordsHeight(SerializedProperty property, float width)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            float height = line;
            if (property == null || !property.isExpanded)
            {
                return height;
            }

            height += spacing + line; // add row
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                height += spacing + KeywordRowHeight(element, width);
            }

            return height;
        }

        public static void DrawCustomKeywords(Rect rect, SerializedProperty property, GUIContent label)
        {
            if (property == null || !property.isArray)
            {
                if (property != null)
                {
                    EditorGUI.PropertyField(rect, property, label, true);
                }

                return;
            }

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            Rect headerRect = new Rect(rect.x, rect.y, rect.width, line);
            property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, label, true);

            if (!property.isExpanded)
            {
                return;
            }

            string draftKey = GetManualFieldKey(property);
            KeywordDrafts.TryGetValue(draftKey, out string draft);
            string addControlName = draftKey + ":AddKeyword";
            Rect addRowRect = new Rect(rect.x, headerRect.yMax + spacing, rect.width, line);
            Rect addFieldRect = new Rect(addRowRect.x, addRowRect.y, Mathf.Max(0f, addRowRect.width - KeywordRemoveButtonWidth - FieldSpacing), line);
            Rect addButtonRect = new Rect(addFieldRect.xMax + FieldSpacing, addRowRect.y, KeywordRemoveButtonWidth, line);
            GUI.SetNextControlName(addControlName);
            string draftValue = SanitizeKeyword(EditorGUI.TextField(addFieldRect, draft ?? string.Empty));
            KeywordDrafts[draftKey] = draftValue;
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(draftValue) || KeywordExists(property, draftValue)))
            {
                if (GUI.Button(addButtonRect, Styles.AddKeywordContent, EditorStyles.miniButton))
                {
                    AddKeyword(property, draftValue);
                    KeywordDrafts[draftKey] = string.Empty;
                    GUI.FocusControl(null);
                    GUI.changed = true;
                }
            }

            float y = addRowRect.yMax + spacing;
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                float rowHeight = KeywordRowHeight(element, rect.width);
                Rect rowRect = new Rect(rect.x, y, rect.width, rowHeight);
                if (DrawKeywordRow(rowRect, property, element, i))
                {
                    break;
                }

                y += rowHeight + spacing;
            }
        }

        private static bool DrawKeywordRow(Rect rect, SerializedProperty arrayProp, SerializedProperty element, int index)
        {
            float line = EditorGUIUtility.singleLineHeight;
            bool hasAdvanced = element.propertyType != SerializedPropertyType.String &&
                               (element.FindPropertyRelative("BoostingScore") != null ||
                                element.FindPropertyRelative("TriggerThreshold") != null);
            DrawKeywordRowBackground(rect);

            float foldoutWidth = hasAdvanced ? 18f : 0f;
            Rect contentRect = new Rect(
                rect.x + KeywordRowPadding,
                rect.y + KeywordRowPadding,
                Mathf.Max(0f, rect.width - KeywordRowPadding * 2f),
                Mathf.Max(0f, rect.height - KeywordRowPadding * 2f));
            Rect foldoutRect = new Rect(contentRect.x, contentRect.y, foldoutWidth, line);
            Rect fieldRect = new Rect(contentRect.x + foldoutWidth, contentRect.y, Mathf.Max(0f, contentRect.width - foldoutWidth - KeywordRemoveButtonWidth - FieldSpacing), line);
            Rect removeRect = new Rect(fieldRect.xMax + FieldSpacing, contentRect.y, KeywordRemoveButtonWidth, line);

            if (hasAdvanced)
            {
                element.isExpanded = EditorGUI.Foldout(foldoutRect, element.isExpanded, GUIContent.none, true);
            }

            string current = GetKeywordValue(element);
            string next = SanitizeKeyword(EditorGUI.TextField(fieldRect, current));
            if (!string.Equals(current, next, StringComparison.Ordinal))
            {
                SetKeywordValue(element, next);
            }

            if (GUI.Button(removeRect, Styles.RemoveKeywordContent, EditorStyles.miniButton))
            {
                arrayProp.DeleteArrayElementAtIndex(index);
                GUI.changed = true;
                return true;
            }

            if (hasAdvanced && element.isExpanded)
            {
                DrawKeywordAdvanced(
                    new Rect(
                        contentRect.x + foldoutWidth,
                        contentRect.y + line + AsrInspectorStyles.ContentSpacing,
                        Mathf.Max(0f, contentRect.width - foldoutWidth),
                        Mathf.Max(0f, contentRect.height - line - AsrInspectorStyles.ContentSpacing)),
                    element);
            }

            return false;
        }

        private static float KeywordRowHeight(SerializedProperty element, float width)
        {
            float line = EditorGUIUtility.singleLineHeight;
            if (element == null ||
                element.propertyType == SerializedPropertyType.String ||
                !element.isExpanded)
            {
                return line + KeywordRowPadding * 2f;
            }

            float settingsWidth = Mathf.Max(0f, width - 18f);
            bool inlineSettings = settingsWidth >= MinimumInlineKeywordSettingsWidth;
            float advancedHeight = inlineSettings
                ? line
                : line * 2f + AsrInspectorStyles.ContentSpacing;
            return KeywordRowPadding * 2f + line + AsrInspectorStyles.ContentSpacing + advancedHeight;
        }

        private static void DrawKeywordRowBackground(Rect rect)
        {
            bool pro = EditorGUIUtility.isProSkin;
            Color fill = pro
                ? new Color(1f, 1f, 1f, 0.035f)
                : new Color(1f, 1f, 1f, 0.72f);
            Color border = pro
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(0f, 0f, 0f, 0.12f);

            EditorGUI.DrawRect(rect, fill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);
        }

        private static void DrawKeywordAdvanced(Rect rect, SerializedProperty element)
        {
            SerializedProperty boostProp = element.FindPropertyRelative("BoostingScore");
            SerializedProperty thresholdProp = element.FindPropertyRelative("TriggerThreshold");
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = AsrInspectorStyles.ContentSpacing;
            bool twoColumns = rect.width >= MinimumInlineKeywordSettingsWidth;
            if (twoColumns)
            {
                float halfWidth = (rect.width - spacing) * 0.5f;
                Rect boostRect = new Rect(rect.x, rect.y, halfWidth, line);
                Rect thresholdRect = new Rect(boostRect.xMax + spacing, rect.y, halfWidth, line);
                if (boostProp != null)
                {
                    boostProp.floatValue = Mathf.Max(0.0001f, DrawCompactFloatField(boostRect, Styles.BoostIconLabel, boostProp.floatValue));
                }

                if (thresholdProp != null)
                {
                    thresholdProp.floatValue = Mathf.Clamp01(DrawCompactFloatField(thresholdRect, Styles.ThresholdIconLabel, thresholdProp.floatValue));
                }

                return;
            }

            if (boostProp != null)
            {
                Rect boostRect = new Rect(rect.x, rect.y, rect.width, line);
                boostProp.floatValue = Mathf.Max(0.0001f, DrawCompactFloatField(boostRect, Styles.BoostIconLabel, boostProp.floatValue));
            }

            if (thresholdProp != null)
            {
                Rect thresholdRect = new Rect(rect.x, rect.y + line + spacing, rect.width, line);
                thresholdProp.floatValue = Mathf.Clamp01(DrawCompactFloatField(thresholdRect, Styles.ThresholdIconLabel, thresholdProp.floatValue));
            }
        }

        private static Dictionary<string, Vector2> CaptureKeywordSettings(SerializedProperty property)
        {
            var settings = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                if (element.propertyType == SerializedPropertyType.String)
                {
                    continue;
                }

                string keyword = element.FindPropertyRelative("Keyword")?.stringValue;
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                float boost = element.FindPropertyRelative("BoostingScore")?.floatValue ?? 2f;
                float threshold = element.FindPropertyRelative("TriggerThreshold")?.floatValue ?? 0.1f;
                settings[keyword] = new Vector2(boost, threshold);
            }

            return settings;
        }

        private static void WriteKeywordElement(SerializedProperty element, string keyword, Dictionary<string, Vector2> existing)
        {
            if (element.propertyType == SerializedPropertyType.String)
            {
                element.stringValue = keyword;
                return;
            }

            SerializedProperty keywordProp = element.FindPropertyRelative("Keyword");
            SerializedProperty boostProp = element.FindPropertyRelative("BoostingScore");
            SerializedProperty thresholdProp = element.FindPropertyRelative("TriggerThreshold");
            if (keywordProp != null)
            {
                keywordProp.stringValue = keyword;
            }

            Vector2 values = existing.TryGetValue(keyword, out Vector2 savedValues)
                ? savedValues
                : new Vector2(2f, 0.1f);

            if (boostProp != null)
            {
                boostProp.floatValue = values.x;
            }

            if (thresholdProp != null)
            {
                thresholdProp.floatValue = values.y;
            }
        }

        private static void AddKeyword(SerializedProperty property, string keyword)
        {
            keyword = SanitizeKeyword(keyword);
            if (string.IsNullOrWhiteSpace(keyword) || KeywordExists(property, keyword))
            {
                return;
            }

            var existing = CaptureKeywordSettings(property);
            int index = property.arraySize;
            property.arraySize++;
            WriteKeywordElement(property.GetArrayElementAtIndex(index), keyword, existing);
        }

        private static bool KeywordExists(SerializedProperty property, string keyword)
        {
            keyword = SanitizeKeyword(keyword);
            for (int i = 0; i < property.arraySize; i++)
            {
                if (string.Equals(GetKeywordValue(property.GetArrayElementAtIndex(i)), keyword, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetKeywordValue(SerializedProperty element)
        {
            return element.propertyType == SerializedPropertyType.String
                ? element.stringValue
                : element.FindPropertyRelative("Keyword")?.stringValue ?? string.Empty;
        }

        private static void SetKeywordValue(SerializedProperty element, string keyword)
        {
            if (element.propertyType == SerializedPropertyType.String)
            {
                element.stringValue = keyword;
                return;
            }

            SerializedProperty keywordProp = element.FindPropertyRelative("Keyword");
            if (keywordProp != null)
            {
                keywordProp.stringValue = keyword;
            }
        }

        private static string SanitizeKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return string.Empty;
            }

            var sanitized = new char[keyword.Length];
            int count = 0;
            for (int i = 0; i < keyword.Length; i++)
            {
                char value = keyword[i];
                if (!char.IsControl(value))
                {
                    sanitized[count++] = value;
                }
            }

            return new string(sanitized, 0, count).Trim();
        }

        private static float DrawCompactFloatField(Rect rect, GUIContent label, float value)
        {
            float labelWidth = label.image != null
                ? Mathf.Min(20f, rect.width * 0.28f)
                : label.text != null && label.text.Length <= 2
                ? Mathf.Min(18f, rect.width * 0.3f)
                : Mathf.Min(86f, rect.width * 0.46f);
            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + FieldSpacing, rect.y, Mathf.Max(0f, rect.width - labelWidth - FieldSpacing), rect.height);
            EditorGUI.LabelField(labelRect, label);
            return EditorGUI.FloatField(fieldRect, value);
        }

        private static void DrawModelPicker(Rect rect, SerializedProperty property, SherpaAsrModelList list)
        {
            string[] modelIds = GetModelIds(list);
            int selected = IndexOf(modelIds, property.stringValue);
            int next = EditorGUI.Popup(rect, selected < 0 ? 0 : selected + 1, BuildOptions(modelIds));
            if (next > 0 && next - 1 < modelIds.Length)
            {
                property.stringValue = modelIds[next - 1];
            }
        }

        private static GUIContent[] BuildOptions(string[] modelIds)
        {
            var options = new GUIContent[modelIds.Length + 1];
            options[0] = new GUIContent("-");
            for (int i = 0; i < modelIds.Length; i++)
            {
                options[i + 1] = new GUIContent(modelIds[i]);
            }

            return options;
        }

        private static string[] GetModelIds(SherpaAsrModelList list)
        {
            if (ModelIdsCache.TryGetValue(list, out string[] cached))
            {
                return cached;
            }

            string[] ids;
            switch (list)
            {
                case SherpaAsrModelList.StreamingAsr:
                    ids = GetModelIdsFromTable("ASR_MODELS_METADATA_TABLES")
                        .Where(IsOnlineModel)
                        .ToArray();
                    break;
                case SherpaAsrModelList.OfflineAsr:
                    ids = GetModelIdsFromTable("ASR_MODELS_METADATA_TABLES")
                        .Where(id => !IsOnlineModel(id))
                        .ToArray();
                    break;
                case SherpaAsrModelList.Vad:
                    ids = GetModelIdsFromTable("VAD_MODELS_METADATA_TABLES");
                    break;
                case SherpaAsrModelList.Punctuation:
                    ids = GetModelIdsFromTable("PUNCTUATION_MODELS_METADATA_TABLES");
                    break;
                case SherpaAsrModelList.Keyword:
                    ids = GetModelIdsFromTable("KWS_MODELS_METADATA_TABLES");
                    break;
                default:
                    ids = Array.Empty<string>();
                    break;
            }

            ids = ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            ModelIdsCache[list] = ids;
            return ids;
        }

        private static string[] GetModelIdsFromTable(string tableName)
        {
            Type modelsType = FindType("Eitan.SherpaONNXUnity.Runtime.Constants.SherpaONNXConstants+Models");
            FieldInfo field = modelsType?.GetField(tableName, BindingFlags.Public | BindingFlags.Static);
            if (!(field?.GetValue(null) is System.Collections.IEnumerable values))
            {
                return Array.Empty<string>();
            }

            var ids = new List<string>();
            foreach (object metadata in values)
            {
                string id = GetStringField(metadata, "modelId");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids.ToArray();
        }

        private static bool IsOnlineModel(string modelId)
        {
            Type apiType = FindType("SherpaONNXUnityAPI");
            MethodInfo method = apiType?.GetMethod("IsOnlineModel", BindingFlags.Public | BindingFlags.Static);
            if (method != null)
            {
                try
                {
                    return method.Invoke(null, new object[] { modelId }) is bool result && result;
                }
                catch
                {
                }
            }

            return !string.IsNullOrWhiteSpace(modelId) &&
                   modelId.IndexOf("streaming", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetStringField(object instance, string fieldName)
        {
            if (instance == null)
            {
                return string.Empty;
            }

            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            return field?.GetValue(instance) as string ?? string.Empty;
        }

        private static Type FindType(string fullName)
        {
            Type type = Type.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static int IndexOf(string[] values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetManualFieldKey(SerializedProperty property)
        {
            int targetId = property.serializedObject.targetObject != null
                ? property.serializedObject.targetObject.GetInstanceID()
                : 0;
            return targetId.ToString() + ":" + property.propertyPath;
        }

        private static class Styles
        {
            private const int KeywordIconSize = 16;
            private static Texture2D _boostIcon;
            private static Texture2D _thresholdIcon;
            private static bool _iconsBuiltForProSkin;

            public static readonly GUIContent ManualModeContent = CreateIconContent("editicon.sml", "E");
            public static readonly GUIContent PickerModeContent = CreateIconContent("_Popup", "-");
            public static readonly GUIContent AddKeywordContent = CreateIconContent("Toolbar Plus", "+");
            public static readonly GUIContent RemoveKeywordContent = CreateIconContent("Toolbar Minus", "-");
            public static GUIContent BoostIconLabel => new GUIContent(BoostIcon, EasyMicEditorLocalization.SherpaAsrText(SherpaAsrEditorTextKey.KeywordScoreLabel));
            public static GUIContent ThresholdIconLabel => new GUIContent(ThresholdIcon, EasyMicEditorLocalization.SherpaAsrText(SherpaAsrEditorTextKey.KeywordThresholdLabel));

            private static Texture2D BoostIcon
            {
                get
                {
                    EnsureKeywordIcons();
                    return _boostIcon;
                }
            }

            private static Texture2D ThresholdIcon
            {
                get
                {
                    EnsureKeywordIcons();
                    return _thresholdIcon;
                }
            }

            private static GUIContent CreateIconContent(string iconName, string fallbackText)
            {
                return CreateIconContent(iconName, fallbackText, string.Empty);
            }

            private static GUIContent CreateIconContent(string iconName, string fallbackText, string tooltip)
            {
                GUIContent content = EditorGUIUtility.IconContent(iconName);
                if (content == null || content.image == null)
                {
                    return new GUIContent(fallbackText, tooltip);
                }

                return new GUIContent(content.image, tooltip);
            }

            private static void EnsureKeywordIcons()
            {
                bool proSkin = EditorGUIUtility.isProSkin;
                if (_boostIcon != null && _thresholdIcon != null && _iconsBuiltForProSkin == proSkin)
                {
                    return;
                }

                _iconsBuiltForProSkin = proSkin;
                Color32 color = proSkin
                    ? new Color32(210, 210, 210, 255)
                    : new Color32(72, 72, 72, 255);
                _boostIcon = CreateKeywordIcon(color, DrawBoostIcon);
                _thresholdIcon = CreateKeywordIcon(color, DrawThresholdIcon);
            }

            private static Texture2D CreateKeywordIcon(Color32 color, Action<Color32[], Color32> draw)
            {
                var pixels = new Color32[KeywordIconSize * KeywordIconSize];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                }

                draw(pixels, color);
                var texture = new Texture2D(KeywordIconSize, KeywordIconSize, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
                texture.SetPixels32(pixels);
                texture.Apply();
                return texture;
            }

            private static void DrawBoostIcon(Color32[] pixels, Color32 color)
            {
                DrawLine(pixels, 6, 3, 9, 3, color);
                DrawPoint(pixels, 5, 4, color);
                DrawPoint(pixels, 10, 4, color);
                DrawLine(pixels, 5, 5, 10, 5, color);
                FillRect(pixels, 4, 7, 11, 12, color);
                DrawPoint(pixels, 3, 8, color);
                DrawPoint(pixels, 12, 8, color);
                DrawPoint(pixels, 3, 9, color);
                DrawPoint(pixels, 12, 9, color);
                DrawPoint(pixels, 3, 10, color);
                DrawPoint(pixels, 12, 10, color);
            }

            private static void DrawThresholdIcon(Color32[] pixels, Color32 color)
            {
                DrawLine(pixels, 2, 5, 13, 5, color);
                DrawLine(pixels, 2, 10, 13, 10, new Color32(color.r, color.g, color.b, 130));
                DrawPoint(pixels, 5, 5, color);
                DrawPoint(pixels, 5, 6, color);
                DrawPoint(pixels, 5, 7, color);
                DrawPoint(pixels, 10, 3, color);
                DrawPoint(pixels, 10, 4, color);
                DrawPoint(pixels, 10, 5, color);
            }

            private static void DrawLine(Color32[] pixels, int x0, int y0, int x1, int y1, Color32 color)
            {
                int dx = Mathf.Abs(x1 - x0);
                int sx = x0 < x1 ? 1 : -1;
                int dy = -Mathf.Abs(y1 - y0);
                int sy = y0 < y1 ? 1 : -1;
                int error = dx + dy;

                while (true)
                {
                    DrawPoint(pixels, x0, y0, color);
                    if (x0 == x1 && y0 == y1)
                    {
                        break;
                    }

                    int e2 = error * 2;
                    if (e2 >= dy)
                    {
                        error += dy;
                        x0 += sx;
                    }

                    if (e2 <= dx)
                    {
                        error += dx;
                        y0 += sy;
                    }
                }
            }

            private static void FillRect(Color32[] pixels, int minX, int minY, int maxX, int maxY, Color32 color)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        DrawPoint(pixels, x, y, color);
                    }
                }
            }

            private static void DrawPoint(Color32[] pixels, int x, int y, Color32 color)
            {
                if (x < 0 || x >= KeywordIconSize || y < 0 || y >= KeywordIconSize)
                {
                    return;
                }

                pixels[y * KeywordIconSize + x] = color;
            }
        }
    }
}
#endif
