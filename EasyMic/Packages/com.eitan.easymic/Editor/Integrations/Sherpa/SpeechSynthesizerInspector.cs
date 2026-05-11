#if UNITY_EDITOR && EITAN_SHERPA_ONNX_UNITY_PRESENT
using Eitan.EasyMic.Editor.Icons;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.TTS;
using UnityEditor;

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
        }
    }
}
#endif
