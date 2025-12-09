#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Core;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    /// <summary>
    /// Creates Sherpa-ONNX services with consistent fallback and logging behaviour.
    /// </summary>
    public sealed class SherpaServiceFactory
    {
        private readonly int _sampleRate;
        private readonly SherpaONNXFeedbackReporter _feedbackReporter;

        /// <summary>
        /// Initializes a new instance of the <see cref="SherpaServiceFactory"/> class.
        /// </summary>
        public SherpaServiceFactory(int sampleRate, SherpaONNXFeedbackReporter feedbackReporter)
        {
            _sampleRate = sampleRate;
            _feedbackReporter = feedbackReporter;
        }

        /// <summary>
        /// Attempts to create a speech recognition service.
        /// </summary>
        public ServiceCreationResult<SpeechRecognition> CreateSpeechRecognition(
            string requestedModelId,
            string fallbackModelId,
            string descriptor)
        {
            return CreateService(candidate => new SpeechRecognition(candidate, _sampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                descriptor);
        }

        /// <summary>
        /// Attempts to create a voice activity detection service.
        /// </summary>
        public ServiceCreationResult<VoiceActivityDetection> CreateVoiceActivityDetection(
            string requestedModelId,
            string fallbackModelId)
        {
            return CreateService(candidate => new VoiceActivityDetection(candidate, _sampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                "voice activity detection");
        }

        /// <summary>
        /// Attempts to create a punctuation service.
        /// </summary>
        public ServiceCreationResult<Punctuation> CreatePunctuation(
            string requestedModelId,
            string fallbackModelId)
        {
            return CreateService(candidate => new Punctuation(candidate, _sampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                "punctuation");
        }

        /// <summary>
        /// Attempts to create a keyword spotting service.
        /// </summary>
        public ServiceCreationResult<KeywordSpotting> CreateKeywordSpotting(
            KeywordOptions settings,
            string fallbackModelId)
        {
            if (!settings.IsEnabled)
            {
                return ServiceCreationResult<KeywordSpotting>.NoService;
            }

            float score = Mathf.Max(0f, settings.KeywordsScore);
            float threshold = Mathf.Clamp01(settings.KeywordsThreshold);
            var customKeywords = settings.CustomKeywords != null
                ? (KeywordSpotting.KeywordRegistration[])settings.CustomKeywords.Clone()
                : null;

            return CreateService(candidate => new KeywordSpotting(
                    candidate,
                    _sampleRate,
                    score,
                    threshold,
                    customKeywords,
                    _feedbackReporter),
                settings.ModelId,
                fallbackModelId,
                "keyword spotting");
        }

        private ServiceCreationResult<TService> CreateService<TService>(
            Func<string, TService> factory,
            string requestedModelId,
            string fallbackModelId,
            string descriptor)
            where TService : class
        {
            string primary = NormalizeModelId(requestedModelId);
            string fallback = NormalizeModelId(fallbackModelId);

            if (string.IsNullOrEmpty(primary) && string.IsNullOrEmpty(fallback))
            {
                Debug.LogWarning($"VoiceMicrophone: missing {descriptor} model identifier; feature disabled.");
                return ServiceCreationResult<TService>.NoService;
            }

            var candidates = new[]
            {
                primary,
                fallback
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                try
                {
                    var service = factory(candidate);
                    if (i > 0 && !string.Equals(candidate, primary, StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"VoiceMicrophone: {descriptor} fell back to model '{candidate}'.");
                    }

                    return new ServiceCreationResult<TService>(service, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"VoiceMicrophone: failed to initialize {descriptor} model '{candidate}': {ex.Message}");
                }
            }

            Debug.LogWarning($"VoiceMicrophone: {descriptor} unavailable; feature disabled.");
            return ServiceCreationResult<TService>.NoService;
        }

        private static string NormalizeModelId(string modelId)
        {
            return string.IsNullOrWhiteSpace(modelId) ? string.Empty : modelId.Trim();
        }
    }

    /// <summary>
    /// Describes the result of creating a Sherpa service.
    /// </summary>
    public readonly struct ServiceCreationResult<T> where T : class
    {
        /// <summary>
        /// A singleton result that indicates the service is not available.
        /// </summary>
        public static readonly ServiceCreationResult<T> NoService = new ServiceCreationResult<T>(null, false);

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceCreationResult{T}"/> struct.
        /// </summary>
        public ServiceCreationResult(T service, bool countsTowardsInitialization)
        {
            Service = service;
            CountsTowardsInitialization = countsTowardsInitialization && service != null;
        }

        /// <summary>
        /// Gets the created service instance.
        /// </summary>
        public T Service { get; }

        /// <summary>
        /// Gets a value indicating whether the created service should be counted towards initialization progress.
        /// </summary>
        public bool CountsTowardsInitialization { get; }

        /// <summary>
        /// Gets a value indicating whether the service was successfully created.
        /// </summary>
        public bool IsSuccess => Service != null;
    }
}
#endif
