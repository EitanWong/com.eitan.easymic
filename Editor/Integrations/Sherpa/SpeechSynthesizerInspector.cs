#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using Eitan.EasyMic.Editor.Icons;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEditor;
using UnityEngine;

namespace Eitan.EasyMic.Editor.Integration.SherpaONNXUnity
{
    [CustomEditor(typeof(SpeechSynthesizer))]
    public sealed class SpeechSynthesizerInspector : UnityEditor.Editor
    {
        private void OnEnable()
        {
            EasyMicComponentIconInstaller.ApplyTemporaryIcon((SpeechSynthesizer)target);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Output levelling balances local models before the AEC reference is mixed. Playback Volume adjusts the listening level; peak protection remains active. The -20 dBFS RMS target is not a LUFS measurement.", MessageType.Info);
            var synth = (SpeechSynthesizer)target;
            if (Application.isPlaying && synth.EnableLog)
            {
                EditorGUILayout.LabelField("Input RMS / Output peak", $"{synth.OutputInputRmsDb:F1} / {synth.OutputPeakDb:F1} dBFS");
                EditorGUILayout.LabelField("Applied output gain", $"{synth.OutputGainDb:F1} dB");
                Repaint();
            }
        }
    }
}
#endif
