#if EITAN_SHERPA_ONNX_UNITY_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Mono.ASR;
using Eitan.SherpaONNXUnity.Runtime;
using Eitan.SherpaONNXUnity.Runtime.Constants;
using Eitan.SherpaONNXUnity.Runtime.Core;
using Eitan.SherpaONNXUnity.Runtime.Modules;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Integration.SherpaONNXUnity.Integrations.Services
{
    /// <summary>
    /// Controls how <see cref="SherpaServiceFactory"/> builds registry keys for live Sherpa module instances.
    /// </summary>
    public enum SherpaModelServiceReuseScope
    {
        /// <summary>
        /// Reuse modules only inside this factory/session. This is the default and is safe for stateful streaming modules.
        /// </summary>
        Factory = 0,

        /// <summary>
        /// Reuse modules across factories that share the same registry.
        /// Use only for modules and call patterns that are known to be stateless or externally synchronized.
        /// </summary>
        Application = 1
    }

    /// <summary>
    /// Creates Sherpa-ONNX services with consistent fallback and logging behaviour.
    /// </summary>
    public sealed class SherpaServiceFactory : IDisposable
    {
        private static readonly HashSet<string> KnownBuiltInModelIds = BuildKnownBuiltInModelIds();
        private static int s_BuiltInManifestPinned;
        private static int s_FactoryScopeSerial;
        private readonly int _sampleRate;
        private readonly SherpaONNXFeedbackReporter _feedbackReporter;
        private readonly SherpaModelServiceRegistry _serviceRegistry;
        private readonly SherpaModelServiceReuseScope _reuseScope;
        private readonly string _factoryScopeId;
        private readonly List<IDisposable> _ownedServiceLeases = new List<IDisposable>();
        private int _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="SherpaServiceFactory"/> class.
        /// </summary>
        public SherpaServiceFactory(int sampleRate, SherpaONNXFeedbackReporter feedbackReporter)
            : this(sampleRate, feedbackReporter, new SherpaModelServiceRegistry(), SherpaModelServiceReuseScope.Factory)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SherpaServiceFactory"/> class with an explicit model registry.
        /// The factory owns leases acquired from the registry and releases them when disposed.
        /// Explicit registries still use factory-scoped keys by default, so stateful modules are not accidentally shared
        /// between independent sessions.
        /// </summary>
        public SherpaServiceFactory(
            int sampleRate,
            SherpaONNXFeedbackReporter feedbackReporter,
            SherpaModelServiceRegistry serviceRegistry)
            : this(sampleRate, feedbackReporter, serviceRegistry, SherpaModelServiceReuseScope.Factory)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SherpaServiceFactory"/> class with an explicit registry and reuse scope.
        /// Use <see cref="SherpaModelServiceReuseScope.Application"/> only when shared live module instances are known to be safe.
        /// </summary>
        public SherpaServiceFactory(
            int sampleRate,
            SherpaONNXFeedbackReporter feedbackReporter,
            SherpaModelServiceRegistry serviceRegistry,
            SherpaModelServiceReuseScope reuseScope)
        {
            _sampleRate = sampleRate;
            _feedbackReporter = feedbackReporter;
            _serviceRegistry = serviceRegistry;
            _reuseScope = Enum.IsDefined(typeof(SherpaModelServiceReuseScope), reuseScope)
                ? reuseScope
                : SherpaModelServiceReuseScope.Factory;
            _factoryScopeId = $"factory-{Interlocked.Increment(ref s_FactoryScopeSerial)}";
        }

        public SherpaModelServiceReuseScope ReuseScope => _reuseScope;

        /// <summary>
        /// Attempts to create a speech recognition service.
        /// </summary>
        public ServiceCreationResult<SpeechRecognition> CreateSpeechRecognition(
            string requestedModelId,
            string fallbackModelId,
            string descriptor,
            SpeechRecognition.Options options = null,
            int maxPendingTranscriptions = 2,
            bool dropIfBusy = true)
        {
            return CreateService(candidate => new SpeechRecognition(
                    candidate,
                    _sampleRate,
                    _feedbackReporter,
                    options: options,
                    maxPendingTranscriptions: maxPendingTranscriptions,
                    dropIfBusy: dropIfBusy),
                requestedModelId,
                fallbackModelId,
                descriptor,
                _sampleRate,
                CombineHash(
                    descriptor,
                    maxPendingTranscriptions,
                    dropIfBusy,
                    options?.Rule1MinTrailingSilence ?? 0f,
                    options?.Rule2MinTrailingSilence ?? 0f,
                    options?.Rule3MinUtteranceLength ?? 0f,
                    options?.Language));
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
                "voice activity detection",
                _sampleRate);
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
                "punctuation",
                _sampleRate);
        }

        /// <summary>
        /// Attempts to create an audio tagging service.
        /// </summary>
        public ServiceCreationResult<AudioTagging> CreateAudioTagging(
            string requestedModelId,
            string fallbackModelId,
            int sampleRate = 0)
        {
            int effectiveSampleRate = sampleRate > 0 ? sampleRate : _sampleRate;
            return CreateService(candidate => new AudioTagging(candidate, effectiveSampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                "audio tagging",
                effectiveSampleRate);
        }

        /// <summary>
        /// Attempts to create a speech enhancement service.
        /// </summary>
        public ServiceCreationResult<SpeechEnhancement> CreateSpeechEnhancement(
            string requestedModelId,
            string fallbackModelId,
            int sampleRate = 0)
        {
            int effectiveSampleRate = sampleRate > 0 ? sampleRate : _sampleRate;
            return CreateService(candidate => new SpeechEnhancement(candidate, effectiveSampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                "speech enhancement",
                effectiveSampleRate);
        }

        /// <summary>
        /// Attempts to create a spoken language identification service.
        /// </summary>
        public ServiceCreationResult<SpokenLanguageIdentification> CreateSpokenLanguageIdentification(
            string requestedModelId,
            string fallbackModelId,
            int sampleRate = 0)
        {
            int effectiveSampleRate = sampleRate > 0 ? sampleRate : _sampleRate;
            return CreateService(candidate => new SpokenLanguageIdentification(candidate, effectiveSampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                "spoken language identification",
                effectiveSampleRate);
        }

        /// <summary>
        /// Attempts to create a source separation service.
        /// </summary>
        public ServiceCreationResult<SourceSeparation> CreateSourceSeparation(
            string requestedModelId,
            string fallbackModelId,
            int sampleRate = 44100)
        {
            return CreateService(candidate => new SourceSeparation(candidate, sampleRate, _feedbackReporter),
                requestedModelId,
                fallbackModelId,
                "source separation",
                sampleRate);
        }

        /// <summary>
        /// Attempts to create a speaker diarization service. The segmentation and embedding models are both required.
        /// </summary>
        public ServiceCreationResult<SpeakerDiarization> CreateSpeakerDiarization(
            string requestedSegmentationModelId,
            string fallbackSegmentationModelId,
            string requestedEmbeddingModelId,
            string fallbackEmbeddingModelId,
            SpeakerDiarization.Options options = null)
        {
            string embedding = NormalizeModelId(requestedEmbeddingModelId);
            string fallbackEmbedding = NormalizeModelId(fallbackEmbeddingModelId);
            if (string.IsNullOrEmpty(embedding))
            {
                embedding = fallbackEmbedding;
            }

            if (string.IsNullOrEmpty(embedding))
            {
                Debug.LogWarning("VoiceMicrophone: missing speaker embedding model identifier; speaker diarization disabled.");
                return ServiceCreationResult<SpeakerDiarization>.NoService;
            }

            return CreateService(candidate => new SpeakerDiarization(
                    candidate,
                    embedding,
                    _feedbackReporter,
                    options: options),
                requestedSegmentationModelId,
                fallbackSegmentationModelId,
                "speaker diarization",
                _sampleRate,
                CombineHash(
                    embedding,
                    options?.MinDurationOn ?? 0f,
                    options?.MinDurationOff ?? 0f,
                    options?.NumClusters ?? -1,
                    options?.ClusteringThreshold ?? 0f));
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
                "keyword spotting",
                _sampleRate,
                CombineHash(score, threshold, customKeywords));
        }

        private ServiceCreationResult<TService> CreateService<TService>(
            Func<string, TService> factory,
            string requestedModelId,
            string fallbackModelId,
            string descriptor,
            int sampleRateForKey,
            int optionsHash = 0)
            where TService : class, IDisposable
        {
            ThrowIfDisposed();
            ThrowIfRegistryShutdown();

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

                if (ShouldSkipUnknownBuiltInCandidate(candidate, fallback))
                {
                    Debug.LogWarning(
                        $"VoiceMicrophone: model '{candidate}' for {descriptor} was not found in the built-in catalog; trying fallback '{fallback}'.");
                    continue;
                }

                try
                {
                    Debug.Log($"VoiceMicrophone: initializing {descriptor} model '{candidate}'.");
                    var service = CreateOwnedService(factory, candidate, descriptor, sampleRateForKey, optionsHash);
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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            for (int i = _ownedServiceLeases.Count - 1; i >= 0; i--)
            {
                try
                {
                    _ownedServiceLeases[i]?.Dispose();
                }
                catch
                {
                }
            }

            _ownedServiceLeases.Clear();
        }

        private TService CreateOwnedService<TService>(
            Func<string, TService> factory,
            string modelId,
            string descriptor,
            int sampleRateForKey,
            int optionsHash)
            where TService : class, IDisposable
        {
            if (_serviceRegistry == null)
            {
                var service = factory(modelId);
                _ownedServiceLeases.Add(service);
                return service;
            }

            string scopeId = _reuseScope == SherpaModelServiceReuseScope.Application ? string.Empty : _factoryScopeId;
            var key = new SherpaModelServiceKey(
                typeof(TService).Name,
                modelId,
                sampleRateForKey,
                CombineHash(descriptor, optionsHash),
                scopeId);
            var lease = _serviceRegistry.Acquire(key, () => factory(modelId));
            _ownedServiceLeases.Add(lease);
            return lease.Module;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(SherpaServiceFactory));
            }
        }

        private void ThrowIfRegistryShutdown()
        {
            if (_serviceRegistry != null && _serviceRegistry.IsShutdown)
            {
                throw new ObjectDisposedException(
                    nameof(SherpaModelServiceRegistry),
                    "The Sherpa service factory cannot create new services because its registry has been shut down.");
            }
        }

        private static bool ShouldSkipUnknownBuiltInCandidate(string candidate, string fallback)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(fallback))
            {
                return false;
            }

            if (string.Equals(candidate, fallback, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsKnownBuiltInModelId(candidate) || !IsKnownBuiltInModelId(fallback))
            {
                return false;
            }

            return LooksLikeBuiltInModelId(candidate);
        }

        private static bool LooksLikeBuiltInModelId(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            string lower = modelId.Trim().ToLowerInvariant();
            return lower.StartsWith("sherpa-onnx-", StringComparison.Ordinal) ||
                   lower.StartsWith("silero", StringComparison.Ordinal) ||
                   lower.StartsWith("ten-vad", StringComparison.Ordinal) ||
                   lower.StartsWith("icefall-", StringComparison.Ordinal) ||
                   lower.StartsWith("nemo_", StringComparison.Ordinal) ||
                   lower.StartsWith("wespeaker_", StringComparison.Ordinal) ||
                   lower.StartsWith("3dspeaker_", StringComparison.Ordinal);
        }

        private static bool IsKnownBuiltInModelId(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            return KnownBuiltInModelIds.Contains(modelId.Trim());
        }

        internal static bool IsKnownBuiltInModelCandidate(string modelId)
        {
            string normalized = NormalizeModelId(modelId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return IsKnownBuiltInModelId(normalized);
        }

        internal static void EnsureDeterministicBuiltInManifest()
        {
            if (Interlocked.Exchange(ref s_BuiltInManifestPinned, 1) != 0)
            {
                return;
            }

            try
            {
                if (SherpaONNXUnityAPI.GetFetchLatestManifest())
                {
                    SherpaONNXUnityAPI.SetFetchLatestManifest(false);
                }

                SherpaChecksumCacheClearResult result = SherpaONNXUnityAPI.ClearChecksumCache();
                SherpaONNXModelRegistry.Instance.Uninitialize();
                Debug.Log(
                    $"VoiceMicrophone: pinned to built-in manifest metadata (cleared {result.DeletedFiles} checksum cache file(s)).");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"VoiceMicrophone: failed to pin built-in manifest metadata: {ex.Message}");
            }
        }

        private static HashSet<string> BuildKnownBuiltInModelIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            RegisterModelIds(ids, SherpaONNXConstants.Models.ASR_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.VAD_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.TTS_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.KWS_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.SPEECH_ENHANCEMENT_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.SPOKEN_LANGUAGEIDENTIFICATION_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.PUNCTUATION_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.AUDIO_TAGGING_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.SPEAKER_IDENTIFICATION_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.SPEAKER_DIARIZATION_MODELS_METADATA_TABLES);
            RegisterModelIds(ids, SherpaONNXConstants.Models.SOURCE_SEPARATION_MODELS_METADATA_TABLES);

            return ids;
        }

        private static void RegisterModelIds(HashSet<string> ids, SherpaONNXModelMetadata[] models)
        {
            if (ids == null || models == null || models.Length == 0)
            {
                return;
            }

            for (int i = 0; i < models.Length; i++)
            {
                string modelId = models[i]?.modelId;
                if (!string.IsNullOrWhiteSpace(modelId))
                {
                    ids.Add(modelId.Trim());
                }
            }
        }

        internal static string NormalizeModelId(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return string.Empty;
            }

            string candidate = modelId.Trim();

            // Accept common user inputs: file paths / URLs / archives.
            // Sherpa model IDs generally match archive names without extensions.
            candidate = StripQuery(candidate);
            candidate = TakeLastPathSegment(candidate);
            candidate = StripKnownSuffixes(candidate);
            candidate = NormalizeLegacyAliases(candidate);

            return candidate.Trim();
        }

        private static string NormalizeLegacyAliases(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "silero_vad":
                    return "silero-vad";
                case "silero_vad.int8":
                case "silero_vad_int8":
                    return "silero-vad-int8";
                case "silero_vad_v4":
                    return "silero-vad-v4";
                case "silero_vad_v5":
                    return "silero-vad-v5";
                case "silero_vad_latest":
                    return "silero-vad-latest";
                default:
                    return value;
            }
        }

        private static string StripQuery(string value)
        {
            int queryIndex = value.IndexOf('?');
            return queryIndex >= 0 ? value.Substring(0, queryIndex) : value;
        }

        private static string TakeLastPathSegment(string value)
        {
            int lastSlash = value.LastIndexOfAny(new[] { '/', '\\' });
            if (lastSlash < 0 || lastSlash >= value.Length - 1)
            {
                return value;
            }

            return value.Substring(lastSlash + 1);
        }

        private static string StripKnownSuffixes(string value)
        {
            value = StripSuffix(value, ".tar.bz2");
            value = StripSuffix(value, ".tar.gz");
            value = StripSuffix(value, ".onnx");
            value = StripSuffix(value, ".zip");
            value = StripSuffix(value, ".bz2");
            value = StripSuffix(value, ".tar");
            return value;
        }

        private static string StripSuffix(string value, string suffix)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return value.Substring(0, value.Length - suffix.Length);
            }

            return value;
        }

        private static int CombineHash(params object[] values)
        {
            unchecked
            {
                int hash = 17;
                if (values == null)
                {
                    return hash;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    hash = (hash * 31) + GetStableHash(values[i]);
                }

                return hash;
            }
        }

        private static int GetStableHash(object value)
        {
            if (value == null)
            {
                return 0;
            }

            switch (value)
            {
                case string text:
                    return StringComparer.Ordinal.GetHashCode(text);
                case bool boolean:
                    return boolean ? 1 : 0;
                case int integer:
                    return integer;
                case float single:
                    return single.GetHashCode();
                case KeywordSpotting.KeywordRegistration[] keywords:
                    return GetKeywordRegistrationsHash(keywords);
                default:
                    return value.GetHashCode();
            }
        }

        private static int GetKeywordRegistrationsHash(KeywordSpotting.KeywordRegistration[] keywords)
        {
            unchecked
            {
                if (keywords == null || keywords.Length == 0)
                {
                    return 0;
                }

                int hash = keywords.Length;
                for (int i = 0; i < keywords.Length; i++)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(keywords[i].Keyword ?? string.Empty);
                    hash = (hash * 31) + keywords[i].BoostingScore.GetHashCode();
                    hash = (hash * 31) + keywords[i].TriggerThreshold.GetHashCode();
                }

                return hash;
            }
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
