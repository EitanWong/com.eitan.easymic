#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.ASR
{
    /// <summary>
    /// Centralizes the wiring of Sherpa workers into the audio pipeline.
    /// </summary>
    public sealed class AudioPipelineConfigurator
    {
        private readonly AudioPipeline _pipeline;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioPipelineConfigurator"/> class.
        /// </summary>
        public AudioPipelineConfigurator(AudioPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        /// <summary>
        /// Adds the supplied keyword detector to the pipeline and wires its callback.
        /// </summary>
        public void ConfigureKeywordDetector(SherpaKeywordDetector detector, Action<string> onKeywordDetected)
        {
            if (_pipeline == null || detector == null)
            {
                return;
            }

            detector.OnKeywordDetected -= onKeywordDetected;
            detector.OnKeywordDetected += onKeywordDetected;
            _pipeline.AddWorker(detector);
        }

        /// <summary>
        /// Adds the streaming recognizer to the pipeline.
        /// </summary>
        public void ConfigureStreamingRecognizer(SherpaRealtimeSpeechRecognizer recognizer, Action<string> onResult)
        {
            if (_pipeline == null || recognizer == null)
            {
                return;
            }

            recognizer.OnRecognitionResult -= onResult;
            recognizer.OnRecognitionResult += onResult;
            _pipeline.AddWorker(recognizer);
        }

        /// <summary>
        /// Adds the offline recognizer to the pipeline.
        /// </summary>
        public void ConfigureOfflineRecognizer(SherpaOfflineSpeechRecognizer recognizer, Action<string> onResult)
        {
            if (_pipeline == null || recognizer == null)
            {
                return;
            }

            recognizer.OnRecognitionResult -= onResult;
            recognizer.OnRecognitionResult += onResult;
            _pipeline.AddWorker(recognizer);
        }

        /// <summary>
        /// Adds the voice activity detector to the pipeline.
        /// </summary>
        public void ConfigureVoiceFilter(SherpaVoiceFilter filter, Action<bool> onVoiceActivityChanged)
        {
            if (_pipeline == null || filter == null)
            {
                return;
            }

            filter.OnVoiceActivityChanged -= onVoiceActivityChanged;
            filter.OnVoiceActivityChanged += onVoiceActivityChanged;
            _pipeline.AddWorker(filter);
        }
    }
}
#endif
