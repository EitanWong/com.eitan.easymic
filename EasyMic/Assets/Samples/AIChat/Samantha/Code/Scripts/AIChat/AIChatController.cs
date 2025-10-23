using Eitan.EasyMic.Runtime.Mono;
using UnityEngine;

namespace Eitan.EasyMic.Demo.AIChat.Samantha
{
    public class AIChatController : MonoBehaviour
    {
        [SerializeField] private VoiceMicrophone microphone;
        [SerializeField] private SpeechSynthesizer speechSynthesizer;
        [SerializeField] private AIChatAnimator animator;
        #region MonoBehaviour

        private void Awake()
        {
            if (microphone != null)
            {
                //register voice microphone event callback. when component init
                microphone.OnMicrophoneInitialized += OnMicrophoneInitializedHandler;
                microphone.OnASRTranscriptionStreaming += OnASRTranscriptionStreamingHandler;
                microphone.OnASRTranscriptionSubmit += OnASRTranscriptionSubmitHandler;
                microphone.OnSpeakingChanged += OnSpeakingChangedHandler;
                microphone.OnLoadingProgressFeedback += OnMicrophoneLoadingProgressFeedbackHandler;
                if (!microphone.MicrophoneOpts.recordOnAwake)
                {
                    microphone.Init();
                }
            }

            if (speechSynthesizer != null)
            {
                speechSynthesizer.OnLoadingProgressFeedback += OnSpeechSynthesizerProgressFeedbackHandler;
            }
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (speechSynthesizer)
                {
                    speechSynthesizer.EnqueueSentence("测试一下语音转文字。");
                }
            }
        }

        private void OnDestroy()
        {

            if (microphone != null)
            {
                //unregister voice microphone event callback. when component destroying;
                microphone.OnMicrophoneInitialized -= OnMicrophoneInitializedHandler;
                microphone.OnASRTranscriptionStreaming -= OnASRTranscriptionStreamingHandler;
                microphone.OnASRTranscriptionSubmit -= OnASRTranscriptionSubmitHandler;
                microphone.OnSpeakingChanged -= OnSpeakingChangedHandler;
                microphone.OnLoadingProgressFeedback -= OnMicrophoneLoadingProgressFeedbackHandler;
            }

            if (speechSynthesizer != null)
            {
                speechSynthesizer.OnLoadingProgressFeedback -= OnSpeechSynthesizerProgressFeedbackHandler;
            }
        }
        #endregion

        #region EventHandler 

        #region VocieMicrophoneEventCallback

        private void OnMicrophoneLoadingProgressFeedbackHandler(string message, float progress)
        {
            UnityEngine.Debug.Log($"<color=green><b>message</b></color>: {message} <color=cyan><b>progress: <i>{progress}</i></b></color>");
        }
        private void OnASRTranscriptionStreamingHandler(string content)
        {
            // if (!string.IsNullOrEmpty(content))
            // {
            UnityEngine.Debug.Log($"<color=cyan><b>[Transcription]</b></color>: {content}");
            // }
        }

        private void OnASRTranscriptionSubmitHandler(string content)
        {

            UnityEngine.Debug.Log($"<color=cyan><b><i>[Submit]</i></b></color>: {content}");
        }

        private void OnSpeakingChangedHandler(bool isSpeaking)
        {
        }
        private void OnMicrophoneInitializedHandler(bool state)
        {
            if (!microphone.MicrophoneOpts.recordOnAwake)
            {
                microphone.StartRecording();
            }
        }

        #endregion
        #region SpeechSynthesizerEventCallback

        private void OnSpeechSynthesizerProgressFeedbackHandler(string message, float progress)
        {
            UnityEngine.Debug.Log($"<color=green><b>message</b></color>: {message} <color=cyan><b>progress: <i>{progress}</i></b></color>");
        }
        #endregion
        #endregion

    }
}
