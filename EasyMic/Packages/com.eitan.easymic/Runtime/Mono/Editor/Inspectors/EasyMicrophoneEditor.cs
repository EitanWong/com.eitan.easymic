#if UNITY_EDITOR

using UnityEditor;

namespace Eitan.EasyMic.Runtime.Mono.Editor
{

    [CustomEditor(typeof(EasyMicrophone))]
    public class EasyMicrophoneEditor : UnityEditor.Editor
    {

        [MenuItem("GameObject/Audio/Input/Easy Microphone", false, -1)]
        public static void AddPlaybackAudioSource()
        {
            var go = new UnityEngine.GameObject("Easy Microphone");
            go.AddComponent<EasyMicrophone>();
            Undo.RegisterCreatedObjectUndo(go, "Create Easy Microphone");

            // Select the newly created GameObject and start rename
            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Edit/Rename");
        }
    }
}
#endif