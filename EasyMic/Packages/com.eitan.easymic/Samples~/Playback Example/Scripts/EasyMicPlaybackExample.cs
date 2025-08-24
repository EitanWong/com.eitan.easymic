using Eitan.EasyMic.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Samples.Playback
{
    public class EasyMicPlaybackExample : MonoBehaviour
    {
        [SerializeField] private Button playOrStopButton;
        [SerializeField] private PlaybackAudioSourceBehaviour audioSource;

        private void Start()
        {
            if (playOrStopButton)
            {
                playOrStopButton.onClick.AddListener(HandleButtonClick);
                var textComponent = playOrStopButton.GetComponentInChildren<Text>();
                if (textComponent)
                {
                    textComponent.text = "Play";
                }
            }

            if (audioSource)
            {
                audioSource.PlayOnAwake = false;
                audioSource.Loop = false;
            }

        }

        private void OnDestroy()
        {
            if (playOrStopButton)
            {
                playOrStopButton.onClick.RemoveListener(HandleButtonClick);
            }
        }

        private void HandleButtonClick()
        {

            var textComponent = playOrStopButton.GetComponentInChildren<Text>();
            if (audioSource.IsPlaying)
            {
                audioSource.Stop();
                if (textComponent)
                {
                    textComponent.text = "Play";
                }
            }
            else
            {
                audioSource.Play();
                
                if (textComponent)
                {
                    textComponent.text = "Stop";
                }
            }
        }
    }
}
