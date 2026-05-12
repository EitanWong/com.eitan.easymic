#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System.Text;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Input;
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using UnityEngine;
using UnityEngine.UI;

namespace Eitan.EasyMic.Samples.SherpaONNXUnity.AudioTaggingInput
{
    [AddComponentMenu("Examples/EasyMic/Sherpa ONNX/AudioTagging Input Example")]
    public sealed class EasyMicSherpaAudioTaggingInputExample : MonoBehaviour
    {
        [SerializeField]
        private EasyMicSherpaAudioInputSource inputSource;

        [SerializeField]
        private AudioTaggingComponent audioTagging;

        [SerializeField]
        private Text statusText;

        [SerializeField]
        [Tooltip("Normally leave disabled. Sherpa's startCaptureWhenReady should start EasyMic capture after the module is initialized.")]
        private bool forceStartCaptureOnStart;

        private readonly StringBuilder _builder = new StringBuilder(256);

        private void Awake()
        {
            if (inputSource == null)
            {
                inputSource = GetComponent<EasyMicSherpaAudioInputSource>();
            }

            if (audioTagging == null)
            {
                audioTagging = GetComponent<AudioTaggingComponent>();
            }
        }

        private void OnEnable()
        {
            if (audioTagging != null)
            {
                audioTagging.TagsReadyEvent.AddListener(HandleTagsReady);
                audioTagging.TaggingFailedEvent.AddListener(HandleTaggingFailed);
            }
        }

        private void Start()
        {
            if (inputSource == null || audioTagging == null)
            {
                SetStatus("Assign EasyMicSherpaAudioInputSource and AudioTaggingComponent.");
                enabled = false;
                return;
            }

            audioTagging.BindInput(inputSource);
            SetStatus("Input bound. Waiting for Sherpa audio tagging readiness.");

            if (forceStartCaptureOnStart)
            {
                inputSource.TryStartCapture();
            }
        }

        private void OnDisable()
        {
            if (audioTagging != null)
            {
                audioTagging.TagsReadyEvent.RemoveListener(HandleTagsReady);
                audioTagging.TaggingFailedEvent.RemoveListener(HandleTaggingFailed);
            }
        }

        private void HandleTagsReady(AudioTagging.AudioTag[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return;
            }

            _builder.Length = 0;
            for (int i = 0; i < tags.Length; i++)
            {
                if (i > 0)
                {
                    _builder.AppendLine();
                }

                _builder.Append(tags[i].Label)
                    .Append("  ")
                    .Append(tags[i].Probability.ToString("P1"));
            }

            SetStatus(_builder.ToString());
        }

        private void HandleTaggingFailed(string message)
        {
            SetStatus(message);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            else
            {
                Debug.Log(message, this);
            }
        }
    }
}
#endif
