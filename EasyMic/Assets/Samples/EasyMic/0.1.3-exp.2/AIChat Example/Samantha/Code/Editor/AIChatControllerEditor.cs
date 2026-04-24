#if EITAN_SHERPA_ONNX_UNITY_PRESENT

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AIChatController))]
    internal class AIChatControllerEditor : UnityEditor.Editor
    {
        private SerializedProperty _configProp;
        private bool _runtimeSnapshotFoldout = true;

        private void OnEnable()
        {
            _configProp = serializedObject.FindProperty("_config");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawInspectorHeader();
            DrawQuickActions();
            EditorGUILayout.Space(4f);

            if (_configProp != null)
            {
                EditorGUILayout.PropertyField(_configProp, new GUIContent("Chat Configuration"), true);
            }

            EditorGUILayout.Space();
            DrawConfigDiagnostics();
            DrawRuntimeSnapshot();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInspectorHeader()
        {
            var icon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow");
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(icon.image, GUILayout.Width(24f), GUILayout.Height(24f));
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("AI Chat Controller", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Live ASR + LLM + TTS orchestration.", EditorStyles.wordWrappedMiniLabel);
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                var controller = target as AIChatController;
                if (controller != null)
                {
                    var script = MonoScript.FromMonoBehaviour(controller);
                    EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
                }
            }
        }

        private void DrawQuickActions()
        {
            using (AIChatEditorHeaderDrawer.BeginTitledHelpBox("Quick Actions"))
            {
                if (serializedObject.isEditingMultipleObjects)
                {
                    EditorGUILayout.HelpBox("Quick actions are disabled while multi-object editing is active.", MessageType.Info);
                    return;
                }

                var controller = target as AIChatController;
                if (controller == null)
                {
                    return;
                }

                string runtimePath = controller.RuntimeConfigPath;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Ping Controller"))
                    {
                        EditorGUIUtility.PingObject(controller.gameObject);
                        Selection.activeObject = controller.gameObject;
                    }

                    if (GUILayout.Button("Copy Runtime Path"))
                    {
                        EditorGUIUtility.systemCopyBuffer = runtimePath ?? string.Empty;
                    }

                    if (GUILayout.Button("Reveal Runtime File"))
                    {
                        RevealRuntimeConfigPath(runtimePath);
                    }
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Runtime Config Path", runtimePath);
                }
            }
        }

        private void DrawConfigDiagnostics()
        {
            using (AIChatEditorHeaderDrawer.BeginTitledHelpBox("Configuration Validation"))
            {
                if (serializedObject.isEditingMultipleObjects || _configProp == null)
                {
                    EditorGUILayout.HelpBox("Validation details are shown for single-object editing only.", MessageType.Info);
                    return;
                }

                var controller = target as AIChatController;
                if (controller != null)
                {
                    var fixedOverride = controller.GetComponent<AIChatConfigurationPolicy>();
                    if (fixedOverride != null && fixedOverride.EnabledOverride)
                    {
                        EditorGUILayout.HelpBox($"Configuration policy is active. Preset: {fixedOverride.Preset}. Matching runtime config values will be overridden at startup.", MessageType.Info);
                    }
                }

                bool hasIssues = false;

                if (GetConfigObject(nameof(AIChatControllerConfig.Microphone)) == null)
                {
                    hasIssues = true;
                    EditorGUILayout.HelpBox("Assign a VoiceMicrophone component or the controller cannot capture input.", MessageType.Error);
                }

                bool? useLocalTts = GetConfigBool(nameof(AIChatControllerConfig.UseLocalTts));
                if (useLocalTts == true && GetConfigObject(nameof(AIChatControllerConfig.SpeechSynthesizer)) == null)
                {
                    hasIssues = true;
                    EditorGUILayout.HelpBox("Local TTS is enabled but no SpeechSynthesizer is assigned.", MessageType.Warning);
                }

                if (useLocalTts == false)
                {
                    string remoteModel = GetConfigString(nameof(AIChatControllerConfig.TtsModel));
                    string remoteVoice = GetConfigString(nameof(AIChatControllerConfig.TtsVoice));
                    if (string.IsNullOrWhiteSpace(remoteModel) || string.IsNullOrWhiteSpace(remoteVoice))
                    {
                        hasIssues = true;
                        EditorGUILayout.HelpBox("Remote playback requires both Model and Voice names.", MessageType.Warning);
                    }
                }

                string apiBase = GetConfigString(nameof(AIChatControllerConfig.ApiBaseUrl));
                if (string.IsNullOrWhiteSpace(apiBase))
                {
                    hasIssues = true;
                    EditorGUILayout.HelpBox("API Base URL is empty. LLM and remote TTS calls will fail.", MessageType.Error);
                }

                int? historyTurns = GetConfigInt(nameof(AIChatControllerConfig.MaxHistoryTurns));
                if (historyTurns.HasValue && historyTurns.Value == 0)
                {
                    EditorGUILayout.HelpBox("Conversation memory is disabled (Max History Turns = 0).", MessageType.Info);
                }

                bool? loadRuntimeConfig = GetConfigBool(nameof(AIChatControllerConfig.LoadRuntimeConfigOnAwake));
                string runtimeFileName = GetConfigString(nameof(AIChatControllerConfig.RuntimeConfigFileName));
                if (loadRuntimeConfig == true && string.IsNullOrWhiteSpace(runtimeFileName))
                {
                    EditorGUILayout.HelpBox("Runtime config file name is empty; default 'ai_chat_config.json' will be used.", MessageType.Info);
                }

                if (!hasIssues)
                {
                    EditorGUILayout.HelpBox("No blocking configuration issues detected.", MessageType.Info);
                }
            }
        }

        private void DrawRuntimeSnapshot()
        {
            _runtimeSnapshotFoldout = EditorGUILayout.Foldout(_runtimeSnapshotFoldout, "Runtime Snapshot", true);
            if (!_runtimeSnapshotFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (serializedObject.isEditingMultipleObjects)
                {
                    EditorGUILayout.HelpBox("Runtime snapshot is unavailable during multi-object editing.", MessageType.Info);
                    return;
                }

                var controller = target as AIChatController;
                if (controller == null)
                {
                    return;
                }

                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Enter Play Mode to inspect live runtime state and metrics.", MessageType.Info);
                    return;
                }

                DrawRuntimeField("Initialized", BoolToStatus(controller.IsInitialized));
                DrawRuntimeField("Chat Active", BoolToStatus(controller.IsChatActive));
                DrawRuntimeField("Idle", BoolToStatus(controller.IsIdle));
                DrawRuntimeField("User Speaking", BoolToStatus(controller.IsUserSpeaking));
                DrawRuntimeField("Assistant Speaking", BoolToStatus(controller.IsAssistantSpeaking));
                DrawRuntimeField("Has History", BoolToStatus(controller.HasConversationHistory));
                DrawRuntimeField("Loading Progress", controller.LastLoadingProgress.ToString("P0"));
                DrawRuntimeField("Since Last User Activity", $"{controller.TimeSinceLastUserActivity:F1}s");
                DrawRuntimeField("Since Last Assistant Response", $"{controller.TimeSinceLastAssistantResponse:F1}s");

                var metrics = controller.GetMetrics();
                DrawRuntimeField("Total Requests", metrics.TotalRequests.ToString());
                DrawRuntimeField("Failed Requests", metrics.FailedRequests.ToString());
                DrawRuntimeField("Average Latency", $"{metrics.AverageResponseLatencyMs:F0} ms");
                DrawRuntimeField("Network Quality", metrics.NetworkQuality.Quality);
                DrawRuntimeField("Network Latency", $"{metrics.NetworkQuality.AverageLatencyMs:F0} ms");
                DrawRuntimeField("Network Jitter", $"{metrics.NetworkQuality.JitterMs:F0} ms");

                if (!string.IsNullOrWhiteSpace(controller.LastErrorMessage))
                {
                    EditorGUILayout.HelpBox($"Last Error: {controller.LastErrorMessage}", MessageType.Warning);
                }
            }
        }

        private static void DrawRuntimeField(string label, string value)
        {
            EditorGUILayout.LabelField(label, string.IsNullOrWhiteSpace(value) ? "-" : value);
        }

        private static string BoolToStatus(bool value)
        {
            return value ? "Yes" : "No";
        }

        private static void RevealRuntimeConfigPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                EditorUtility.RevealInFinder(directory);
            }
        }

        private SerializedProperty FindConfigProperty(string relativePath)
            => _configProp?.FindPropertyRelative(relativePath);

        private bool? GetConfigBool(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }
            return prop.boolValue;
        }

        private int? GetConfigInt(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }
            return prop.intValue;
        }

        private string GetConfigString(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }
            return prop.stringValue;
        }

        private UnityEngine.Object GetConfigObject(string relativePath)
        {
            var prop = FindConfigProperty(relativePath);
            if (prop == null || prop.hasMultipleDifferentValues)
            {
                return null;
            }

            return prop.objectReferenceValue;
        }
    }

    internal static class AIChatEditorHeaderDrawer
    {
        internal static EditorGUILayout.VerticalScope BeginTitledHelpBox(string title)
        {
            var scope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            return scope;
        }

        internal static Rect DrawTitledHelpBox(
            Rect root,
            float currentY,
            float height,
            string title,
            float padding,
            out Rect contentRect)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            var boxRect = new Rect(root.x, currentY, root.width, height);
            GUI.Box(boxRect, GUIContent.none, EditorStyles.helpBox);

            var headerRect = new Rect(
                boxRect.x + padding,
                boxRect.y + padding,
                boxRect.width - (padding * 2f),
                line);

            EditorGUI.LabelField(headerRect, title, EditorStyles.boldLabel);

            contentRect = new Rect(
                boxRect.x + padding,
                headerRect.yMax + spacing,
                boxRect.width - (padding * 2f),
                boxRect.height - line - spacing - (padding * 2f));

            return boxRect;
        }
    }
}
#endif
