#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using Eitan.EasyMic.Editor.Icons;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using Eitan.Sherpa.Onnx.Unity.Mono.Inputs;
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    internal static class SherpaSceneRecipeBuilder
    {
        [MenuItem("GameObject/SherpaONNX/Audio/EasyMic Audio Input Source", false, 10)]
        public static void CreateEasyMicAudioInputSource()
        {
            var go = new GameObject("EasyMic Sherpa Audio Input");
            Undo.RegisterCreatedObjectUndo(go, "Create EasyMic Sherpa Audio Input");
            var input = Undo.AddComponent<EasyMicSherpaAudioInputSource>(go);
            ApplyMappedComponentIcon(input);
            SelectAndPing(go);
        }

        [MenuItem("GameObject/SherpaONNX/Speech Recognition/Realtime Recognizer With EasyMic Input", false, 30)]
        public static void CreateRealtimeRecognizerWithEasyMicInput()
        {
            CreateStreamingRecipe<RealtimeSpeechRecognizerComponent>(
                "EasyMic Realtime Speech Recognition",
                "Create EasyMic Realtime Speech Recognition");
        }

        [MenuItem("GameObject/SherpaONNX/Speech Recognition/Offline Recognizer With EasyMic VAD", false, 31)]
        public static void CreateOfflineRecognizerWithEasyMicVad()
        {
            var go = new GameObject("EasyMic Offline Speech Recognition");
            Undo.RegisterCreatedObjectUndo(go, "Create EasyMic Offline Speech Recognition");
            var input = Undo.AddComponent<EasyMicSherpaAudioInputSource>(go);
            var vad = Undo.AddComponent<VoiceActivityDetectionComponent>(go);
            var recognizer = Undo.AddComponent<OfflineSpeechRecognizerComponent>(go);

            ApplyMappedComponentIcon(input);
            ApplySherpaComponentIcon(vad);
            ApplySherpaComponentIcon(recognizer, applyToGameObject: true);
            BindStreamingComponent(vad, input);
            BindVoiceActivitySource(recognizer, vad);
            SelectAndPing(go);
        }

        [MenuItem("GameObject/SherpaONNX/Keyword Spotting/Keyword Spotter With EasyMic Input", false, 30)]
        public static void CreateKeywordSpotterWithEasyMicInput()
        {
            CreateStreamingRecipe<KeywordSpottingComponent>(
                "EasyMic Keyword Spotting",
                "Create EasyMic Keyword Spotting");
        }

        [MenuItem("GameObject/SherpaONNX/Voice/Voice Activity Detector With EasyMic Input", false, 40)]
        public static void CreateVoiceActivityDetectorWithEasyMicInput()
        {
            CreateStreamingRecipe<VoiceActivityDetectionComponent>(
                "EasyMic Voice Activity Detection",
                "Create EasyMic Voice Activity Detection");
        }

        [MenuItem("GameObject/SherpaONNX/Text/Audio Tagging With EasyMic Input", false, 50)]
        public static void CreateAudioTaggingWithEasyMicInput()
        {
            CreateStreamingRecipe<AudioTaggingComponent>(
                "EasyMic Audio Tagging",
                "Create EasyMic Audio Tagging");
        }

        [MenuItem("GameObject/SherpaONNX/Speech Synthesis/EasyMic Playback Speech Synthesizer", false, 100)]
        public static void CreateEasyMicPlaybackSpeechSynthesizer()
        {
            var go = new GameObject("EasyMic Playback Speech Synthesizer");
            Undo.RegisterCreatedObjectUndo(go, "Create EasyMic Playback Speech Synthesizer");
            var synthesizer = Undo.AddComponent<SpeechSynthesizer>(go);
            ApplyMappedComponentIcon(synthesizer);
            SelectAndPing(go);
        }

        public static TComponent AddOrBindStreamingComponent<TComponent>(EasyMicSherpaAudioInputSource inputSource)
            where TComponent : MonoBehaviour
        {
            if (inputSource == null)
            {
                return null;
            }

            var component = inputSource.GetComponent<TComponent>();
            if (component == null)
            {
                component = Undo.AddComponent<TComponent>(inputSource.gameObject);
            }

            BindStreamingComponent(component, inputSource);
            ApplySherpaComponentIcon(component);
            Selection.activeObject = component;
            return component;
        }

        public static AudioTaggingComponent AddOrBindAudioTagging(EasyMicSherpaAudioInputSource inputSource)
        {
            return AddOrBindStreamingComponent<AudioTaggingComponent>(inputSource);
        }

        public static OfflineSpeechRecognizerComponent AddOrBindOfflineRecognizerWithVad(EasyMicSherpaAudioInputSource inputSource)
        {
            if (inputSource == null)
            {
                return null;
            }

            var vad = AddOrBindStreamingComponent<VoiceActivityDetectionComponent>(inputSource);
            var recognizer = inputSource.GetComponent<OfflineSpeechRecognizerComponent>();
            if (recognizer == null)
            {
                recognizer = Undo.AddComponent<OfflineSpeechRecognizerComponent>(inputSource.gameObject);
            }

            BindVoiceActivitySource(recognizer, vad);
            ApplySherpaComponentIcon(recognizer);
            Selection.activeObject = recognizer;
            return recognizer;
        }

        private static void CreateStreamingRecipe<TComponent>(string gameObjectName, string undoName)
            where TComponent : MonoBehaviour
        {
            var go = new GameObject(gameObjectName);
            Undo.RegisterCreatedObjectUndo(go, undoName);
            var input = Undo.AddComponent<EasyMicSherpaAudioInputSource>(go);
            var component = Undo.AddComponent<TComponent>(go);
            ApplyMappedComponentIcon(input);
            ApplySherpaComponentIcon(component, applyToGameObject: true);
            BindStreamingComponent(component, input);
            SelectAndPing(go);
        }

        private static bool BindStreamingComponent(MonoBehaviour component, SherpaAudioInputSource inputSource)
        {
            if (component == null || inputSource == null)
            {
                return false;
            }

            var method = component.GetType().GetMethod("BindInput", new[] { typeof(SherpaAudioInputSource) });
            if (method == null)
            {
                return false;
            }

            Undo.RecordObject(component, "Bind EasyMic Sherpa input");
            method.Invoke(component, new object[] { inputSource });
            EditorUtility.SetDirty(component);
            return true;
        }

        private static void BindVoiceActivitySource(OfflineSpeechRecognizerComponent recognizer, VoiceActivityDetectionComponent vad)
        {
            if (recognizer == null || vad == null)
            {
                return;
            }

            var serializedObject = new SerializedObject(recognizer);
            var property = serializedObject.FindProperty("voiceActivitySource");
            if (property != null)
            {
                property.objectReferenceValue = vad;
                serializedObject.ApplyModifiedProperties();
            }

            Undo.RecordObject(recognizer, "Bind Sherpa voice activity source");
            recognizer.BindVoiceActivitySource(vad);
            EditorUtility.SetDirty(recognizer);
        }

        private static void SelectAndPing(GameObject go)
        {
            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        private static void ApplyMappedComponentIcon(Component component)
        {
            EasyMicComponentIconInstaller.ApplyTemporaryIcon(component);
        }

        private static void ApplySherpaComponentIcon(Component component, bool applyToGameObject = false)
        {
            if (component == null || !TryGetSherpaComponentIconId(component.GetType(), out EasyMicIconId iconId))
            {
                return;
            }

            EasyMicComponentIconInstaller.ApplyTemporaryIcon(component, iconId);
            if (applyToGameObject)
            {
                EasyMicComponentIconInstaller.ApplyTemporaryIcon(component.gameObject, iconId);
            }
        }

        private static bool TryGetSherpaComponentIconId(Type type, out EasyMicIconId iconId)
        {
            if (type == typeof(RealtimeSpeechRecognizerComponent))
            {
                iconId = EasyMicIconId.SherpaRealtimeSpeechRecognition;
                return true;
            }

            if (type == typeof(OfflineSpeechRecognizerComponent))
            {
                iconId = EasyMicIconId.SherpaOfflineSpeechRecognition;
                return true;
            }

            if (type == typeof(KeywordSpottingComponent))
            {
                iconId = EasyMicIconId.SherpaKeywordSpotting;
                return true;
            }

            if (type == typeof(VoiceActivityDetectionComponent))
            {
                iconId = EasyMicIconId.SherpaVoiceActivity;
                return true;
            }

            if (type == typeof(AudioTaggingComponent))
            {
                iconId = EasyMicIconId.SherpaAudioTagging;
                return true;
            }

            iconId = EasyMicIconId.EasyMic;
            return false;
        }
    }
}
#endif
