#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    internal class AIChatSetupWizardWindow : EditorWindow
    {
        private const string WindowTitle = "AI Chat Setup";

        private enum WizardStep
        {
            Intro = 0,
            Configuration = 1
        }

        private static readonly string[] StepTitles = { "Overview", "Configuration" };

        private AIChatController _controller;
        private SerializedObject _serializedController;
        private WizardStep _currentStep;
        private Vector2 _scroll;
        private GUIContent _headerIcon;

        internal static void ShowWindow(AIChatController controller, bool focusWindow)
        {
            var window = GetWindow<AIChatSetupWizardWindow>(utility: true, title: WindowTitle);
            window.Initialize(controller);
            if (focusWindow)
            {
                window.Focus();
            }
        }

        private void OnEnable()
        {
            minSize = new Vector2(420f, 360f);
            _headerIcon = EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow");
            Initialize(_controller);
        }

        internal void Initialize(AIChatController controller)
        {
            _controller = controller != null ? controller : FindControllerInOpenScenes();
            _serializedController = _controller != null ? new SerializedObject(_controller) : null;
            _currentStep = WizardStep.Intro;
            titleContent = new GUIContent(WindowTitle, _headerIcon?.image);
        }

        private void OnGUI()
        {
            EnsureSerializedObject();

            DrawHeader();
            DrawStepIndicator();

            if (_controller == null || _serializedController == null)
            {
                DrawMissingControllerView();
                return;
            }

            _serializedController.Update();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;
                DrawCurrentStep();
            }

            DrawNavigation();
            _serializedController.ApplyModifiedProperties();
        }

        private void EnsureSerializedObject()
        {
            if (_controller == null)
            {
                _serializedController = null;
                return;
            }

            if (_serializedController == null)
            {
                _serializedController = new SerializedObject(_controller);
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label(_headerIcon?.image, GUILayout.Width(32f), GUILayout.Height(32f));
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("Samantha AI Chat", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Guided setup for the interactive companion scene.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private void DrawStepIndicator()
        {
            Rect rect = GUILayoutUtility.GetRect(20f, 20f);
            float progress = (int)_currentStep / Mathf.Max(1f, StepTitles.Length - 1f);
            EditorGUI.ProgressBar(rect, progress, $"Step {(int)_currentStep + 1}/{StepTitles.Length}: {StepTitles[(int)_currentStep]}");
            EditorGUILayout.Space(6f);
        }

        private void DrawCurrentStep()
        {
            switch (_currentStep)
            {
                case WizardStep.Intro:
                    DrawIntroPage();
                    break;
                case WizardStep.Configuration:
                    DrawConfigurationPage();
                    break;
            }
        }

        private void DrawIntroPage()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Scene Overview", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "This demo blends live ASR, OpenAI Responses, and Samantha's speech synthesis. Follow the next step to configure the API access and speech output settings.",
                    EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Target Controller", EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(_controller, typeof(AIChatController), true);
                    if (GUILayout.Button(EditorGUIUtility.IconContent("d_scenevis_visible_hover"), GUILayout.Width(32f), GUILayout.Height(18f)))
                    {
                        if (_controller != null)
                        {
                            EditorGUIUtility.PingObject(_controller.gameObject);
                            Selection.activeObject = _controller.gameObject;
                        }
                    }
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Checklist", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("1. Microphone is already wired to the scene and requires no action here.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("2. Prepare your OpenAI-compatible API key.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("3. Decide whether the assistant should speak locally or via the remote TTS service.", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawConfigurationPage()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("LLM Endpoint", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(Find("apiBaseUrl"), new GUIContent("API Base URL"));
                EditorGUILayout.PropertyField(Find("apiKey"), new GUIContent("API Key"));
                if (string.IsNullOrWhiteSpace(Find("apiKey")?.stringValue))
                {
                    EditorGUILayout.HelpBox("API Key is required to avoid 401 Unauthorized responses.", MessageType.Warning);
                }
                EditorGUILayout.PropertyField(Find("llmModel"), new GUIContent("Model"));
                EditorGUILayout.PropertyField(Find("llmTemperature"), new GUIContent("Temperature"));
                EditorGUILayout.PropertyField(Find("systemPrompt"), new GUIContent("System Prompt"));
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Speech", EditorStyles.boldLabel);
                var useLocalProp = Find("useLocalTts");
                EditorGUILayout.PropertyField(useLocalProp, new GUIContent("Use Local Synth"));

                if (useLocalProp.boolValue)
                {
                    EditorGUILayout.PropertyField(Find("speechSynthesizer"), new GUIContent("Speech Synthesizer"));
                    if (Find("speechSynthesizer")?.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Assign a SpeechSynthesizer component to enable offline playback.", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.PropertyField(Find("ttsModel"), new GUIContent("Remote TTS Model"));
                    EditorGUILayout.PropertyField(Find("ttsVoice"), new GUIContent("Remote TTS Voice"));
                }

                EditorGUILayout.PropertyField(Find("interruptAssistantOnUserSpeech"), new GUIContent("Interrupt On User Speech"));
                EditorGUILayout.PropertyField(Find("logStreamingChunks"), new GUIContent("Verbose Streaming Log"));
            }
        }

        private void DrawNavigation()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_currentStep == WizardStep.Intro))
                {
                    if (GUILayout.Button("Back", GUILayout.Width(100f)))
                    {
                        _currentStep = WizardStep.Intro;
                    }
                }

                GUILayout.FlexibleSpace();

                if (_currentStep == WizardStep.Configuration)
                {
                    using (new EditorGUI.DisabledScope(!IsConfigurationValid()))
                    {
                        if (GUILayout.Button("Finish Setup", GUILayout.Width(140f)))
                        {
                            Close();
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button("Next", GUILayout.Width(120f)))
                    {
                        _currentStep = WizardStep.Configuration;
                    }
                }
            }
        }

        private void DrawMissingControllerView()
        {
            EditorGUILayout.HelpBox("No AIChatController was found in the open scenes.", MessageType.Warning);
            if (GUILayout.Button("Rescan Scenes"))
            {
                Initialize(null);
            }
        }

        private SerializedProperty Find(string property)
        {
            return _serializedController?.FindProperty(property);
        }

        private bool IsConfigurationValid()
        {
            if (_serializedController == null)
            {
                return false;
            }

            bool hasKey = !string.IsNullOrWhiteSpace(Find("apiKey")?.stringValue);
            bool hasBase = !string.IsNullOrWhiteSpace(Find("apiBaseUrl")?.stringValue);
            bool useLocal = Find("useLocalTts")?.boolValue ?? false;
            bool speechOk = useLocal ? Find("speechSynthesizer")?.objectReferenceValue != null :
                !string.IsNullOrWhiteSpace(Find("ttsModel")?.stringValue) && !string.IsNullOrWhiteSpace(Find("ttsVoice")?.stringValue);

            return hasKey && hasBase && speechOk;
        }

        private AIChatController FindControllerInOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var controller = FindController(SceneManager.GetSceneAt(i));
                if (controller != null)
                {
                    return controller;
                }
            }

            return Object.FindObjectOfType<AIChatController>();
        }

        private static AIChatController FindController(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                var controller = root.GetComponentInChildren<AIChatController>(true);
                if (controller != null)
                {
                    return controller;
                }
            }

            return null;
        }
    }

    [InitializeOnLoad]
    internal static class AIChatSetupWizardAutoPopup
    {
        static AIChatSetupWizardAutoPopup()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (!scene.IsValid() || Application.isPlaying)
            {
                return;
            }

            var controller = FindController(scene);
            if (controller == null)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying)
                {
                    return;
                }

                ShowWizardUnique(controller);
            };
        }

        private static void ShowWizardUnique(AIChatController controller)
        {
            var existing = Resources.FindObjectsOfTypeAll<AIChatSetupWizardWindow>().FirstOrDefault();
            if (existing != null)
            {
                existing.Initialize(controller);
                existing.Focus();
                return;
            }

            AIChatSetupWizardWindow.ShowWindow(controller, focusWindow: true);
        }

        private static AIChatController FindController(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                var controller = root.GetComponentInChildren<AIChatController>(true);
                if (controller != null)
                {
                    return controller;
                }
            }

            return null;
        }
    }
}
#endif
