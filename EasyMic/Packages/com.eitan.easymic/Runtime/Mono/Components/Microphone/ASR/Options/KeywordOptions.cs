#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using UnityEngine;
using Eitan.SherpaONNXUnity.Runtime.Modules;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Serialized keyword spotting configuration shared by the microphone and keyword gate.
    /// </summary>
    [Serializable]
    public struct KeywordOptions
    {
        public bool Enabled;
        public string ModelId;
        public KeywordSpotting.KeywordRegistration[] CustomKeywords;
        public float KeywordsScore;
        public float KeywordsThreshold;
        public bool ContinuousConversation;
        public float ContinuousConversationTimeoutSeconds;
        public bool UseTriggerSound;
        public AudioClip TriggerSoundClip;

        public bool IsEnabled => Enabled && !string.IsNullOrWhiteSpace(ModelId);

        public KeywordOptions Clone()
        {
            return new KeywordOptions
            {
                Enabled = Enabled,
                ModelId = ModelId,
                CustomKeywords = CustomKeywords != null
                    ? (KeywordSpotting.KeywordRegistration[])CustomKeywords.Clone()
                    : null,
                KeywordsScore = KeywordsScore,
                KeywordsThreshold = KeywordsThreshold,
                ContinuousConversation = ContinuousConversation,
                ContinuousConversationTimeoutSeconds = ContinuousConversationTimeoutSeconds,
                UseTriggerSound = UseTriggerSound,
                TriggerSoundClip = TriggerSoundClip
            };
        }

        public static KeywordOptions Default => new KeywordOptions
        {
            Enabled = false,
            ModelId = "sherpa-onnx-kws-zipformer-wenetspeech-3.3M-2024-01-01",
            KeywordsScore = 2.0f,
            KeywordsThreshold = 0.25f,
            ContinuousConversation = false,
            ContinuousConversationTimeoutSeconds = 8f
        };
    }
}
#endif
