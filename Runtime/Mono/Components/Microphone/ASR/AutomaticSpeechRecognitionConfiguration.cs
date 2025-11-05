#if EASYMIC_SHERPA_ONNX_INTEGRATION
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

namespace Eitan.EasyMic.Runtime.Mono.ASR
{
    #region Nested Types

    /// <summary>
    /// Describes the recognition pipeline mode used by <see cref="VoiceMicrophone"/>.
    /// </summary>
    [MovedFrom(true, "Eitan.EasyMic.Runtime.Mono", null, "VoiceMicrophone/RecognitionMode")]
    public enum RecognitionMode
    {
        Streaming,
        OfflineWithVad,
        Hybrid
    }

    /// <summary>
    /// Serialized asset containing presets that describe how to configure Sherpa-ONNX services.
    /// </summary>
    [Serializable]
    [MovedFrom(true, "Eitan.EasyMic.Runtime.Mono", null, "VoiceMicrophone/AutomaticSpeechRecognitionConfiguration")]
    public sealed class AutomaticSpeechRecognitionConfiguration
    {
        /// <summary>
        /// A serializable preset entry.
        /// </summary>
        [Serializable]
        [MovedFrom(true, "Eitan.EasyMic.Runtime.Mono", null, "VoiceMicrophone/AutomaticSpeechRecognitionConfiguration/ASRPreset")]
        public struct ASRPreset
        {
            public const string DefaultPresetId = "default";

            public string Id;
            public string DisplayName;
            public RecognitionMode RecognitionMode;
            public string StreamingModelId;
            public string OfflineModelId;
            public string VadModelId;
            public bool EnablePunctuation;
            public string PunctuationModelId;
            public KeywordOptions KeywordOptions;
            public TurnDetectionOptions TurnDetectionOptions;

            /// <summary>
            /// Creates a deep copy of the preset to keep serialized data immutable.
            /// </summary>
            public ASRPreset Clone()
            {
                return new ASRPreset
                {
                    Id = Id,
                    DisplayName = DisplayName,
                    RecognitionMode = RecognitionMode,
                    KeywordOptions = KeywordOptions.Clone(),
                    StreamingModelId = StreamingModelId,
                    OfflineModelId = OfflineModelId,
                    VadModelId = VadModelId,
                    EnablePunctuation = EnablePunctuation,
                    PunctuationModelId = PunctuationModelId,
                    TurnDetectionOptions = TurnDetectionOptions
                };
            }

            /// <summary>
            /// Creates a preset using the supplied parameters.
            /// </summary>
            public static ASRPreset Create(
                RecognitionMode recognitionMode,
                KeywordOptions keywordSettings,
                string streamingModelId,
                string offlineModelId,
                string vadModelId,
                bool enablePunctuation,
                string punctuationModelId,
                TurnDetectionOptions? turnDetection = null)
            {
                var detection = (turnDetection ?? TurnDetectionOptions.Default).EnsureValid();
                return new ASRPreset
                {
                    Id = DefaultPresetId,
                    DisplayName = "Default",
                    RecognitionMode = recognitionMode,
                    KeywordOptions = keywordSettings,
                    StreamingModelId = streamingModelId,
                    OfflineModelId = offlineModelId,
                    VadModelId = vadModelId,
                    EnablePunctuation = enablePunctuation,
                    PunctuationModelId = punctuationModelId,
                    TurnDetectionOptions = detection
                };
            }

            /// <summary>
            /// Returns the library default preset matching the previous implementation.
            /// </summary>
            public static ASRPreset Default => Create(
                RecognitionMode.Streaming,
                KeywordOptions.Default,
                "sherpa-onnx-streaming-zipformer-bilingual-zh-en-2023-02-20",
                "sherpa-onnx-zipformer-zh-en-2023-11-22",
                "silero-vad-v5",
                true,
                "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8",
                TurnDetectionOptions.Default);
        }

        #endregion

        [SerializeField, FormerlySerializedAs("Presets")]
        private ASRPreset[] _presets = { ASRPreset.Default };

        [SerializeField, FormerlySerializedAs("ActivePresetId")]
        private string _activePresetId = ASRPreset.DefaultPresetId;

        /// <summary>
        /// Returns all available presets; never null.
        /// </summary>
        public IReadOnlyList<ASRPreset> SupportedPresets => _presets ?? Array.Empty<ASRPreset>();

        /// <summary>
        /// Gets the identifier of the currently active preset.
        /// </summary>
        public string ActivePresetId
        {
            get => string.IsNullOrWhiteSpace(_activePresetId) ? ASRPreset.DefaultPresetId : _activePresetId;
            private set => _activePresetId = value;
        }

        /// <summary>
        /// Gets the recognition mode declared by the active preset.
        /// </summary>
        public RecognitionMode RecognitionMode => GetActivePresetInternal(false).RecognitionMode;

        /// <summary>
        /// Gets the keyword configuration declared by the active preset.
        /// </summary>
        public KeywordOptions ActiveKeywordOptions => GetActivePresetInternal(false).KeywordOptions;

        /// <summary>
        /// Gets the turn detection settings declared by the active preset.
        /// </summary>
        public TurnDetectionOptions ActiveTurnDetectionOptions => GetActivePresetInternal(false).TurnDetectionOptions.EnsureValid();

        /// <summary>
        /// Gets the streaming model identifier from the active preset.
        /// </summary>
        public string StreamingModelId => GetActivePresetInternal(false).StreamingModelId;

        /// <summary>
        /// Gets the offline model identifier from the active preset.
        /// </summary>
        public string OfflineModelId => GetActivePresetInternal(false).OfflineModelId;

        /// <summary>
        /// Gets the VAD model identifier from the active preset.
        /// </summary>
        public string VadModelId => GetActivePresetInternal(false).VadModelId;

        /// <summary>
        /// Gets whether punctuation is enabled for the active preset.
        /// </summary>
        public bool EnablePunctuation => GetActivePresetInternal(false).EnablePunctuation;

        /// <summary>
        /// Gets the punctuation model identifier from the active preset.
        /// </summary>
        public string PunctuationModelId => GetActivePresetInternal(false).PunctuationModelId;

        /// <summary>
        /// Creates a deep copy of the configuration data.
        /// </summary>
        public AutomaticSpeechRecognitionConfiguration Clone()
        {
            return new AutomaticSpeechRecognitionConfiguration
            {
                _activePresetId = ActivePresetId,
                _presets = ClonePresets(_presets)
            };
        }

        /// <summary>
        /// Returns a deep copy of the active preset.
        /// </summary>
        public ASRPreset ActivePresetConfiguration => GetActivePresetInternal(true);

        /// <summary>
        /// Retrieves a deep copy of the active preset.
        /// </summary>
        public ASRPreset GetActivePreset() => GetActivePresetInternal(true);

        /// <summary>
        /// Attempts to change the active preset to the given identifier.
        /// </summary>
        public bool TrySelectPreset(string presetId)
        {
            var presets = SupportedPresets;
            if (presets.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(presetId) ||
                    string.Equals(presetId, ASRPreset.DefaultPresetId, StringComparison.OrdinalIgnoreCase))
                {
                    ActivePresetId = ASRPreset.DefaultPresetId;
                    return true;
                }

                return false;
            }

            if (string.IsNullOrWhiteSpace(presetId))
            {
                var first = presets[0];
                ActivePresetId = string.IsNullOrWhiteSpace(first.Id) ? ASRPreset.DefaultPresetId : first.Id;
                return true;
            }

            int index = FindPreset(presetId);
            if (index >= 0)
            {
                ActivePresetId = presets[index].Id;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to retrieve a preset by identifier.
        /// </summary>
        public bool TryGetPreset(string presetId, out ASRPreset preset)
        {
            var presets = SupportedPresets;
            if (!string.IsNullOrWhiteSpace(presetId))
            {
                int index = FindPreset(presetId);
                if (index >= 0)
                {
                    preset = presets[index].Clone();
                    return true;
                }
            }

            preset = default;
            return false;
        }

        /// <summary>
        /// Adds the preset to the configuration, optionally overwriting an existing entry.
        /// </summary>
        public bool AddPreset(ASRPreset preset, bool overwrite = false)
        {
            if (_presets == null)
            {
                _presets = Array.Empty<ASRPreset>();
            }

            int existingIndex = FindPreset(preset.Id);
            if (existingIndex >= 0 && !overwrite)
            {
                return false;
            }

            var list = new List<ASRPreset>(_presets);
            if (existingIndex >= 0)
            {
                list[existingIndex] = preset;
            }
            else
            {
                list.Add(preset);
            }

            _presets = ClonePresets(list.ToArray());
            return true;
        }

        /// <summary>
        /// Updates an existing preset if present.
        /// </summary>
        public bool UpdatePreset(ASRPreset preset)
        {
            if (_presets == null || _presets.Length == 0 || string.IsNullOrWhiteSpace(preset.Id))
            {
                return false;
            }

            int index = FindPreset(preset.Id);
            if (index < 0)
            {
                return false;
            }

            var clone = ClonePresets(_presets);
            clone[index] = preset;
            _presets = clone;
            return true;
        }

        /// <summary>
        /// Removes the preset with the supplied identifier.
        /// </summary>
        public bool RemovePreset(string presetId)
        {
            if (_presets == null || _presets.Length == 0 || string.IsNullOrWhiteSpace(presetId))
            {
                return false;
            }

            int index = FindPreset(presetId);
            if (index < 0)
            {
                return false;
            }

            var list = new List<ASRPreset>(_presets);
            list.RemoveAt(index);
            _presets = ClonePresets(list.ToArray());

            if (string.Equals(ActivePresetId, presetId, StringComparison.OrdinalIgnoreCase))
            {
                TrySelectPreset(null);
            }

            return true;
        }

        /// <summary>
        /// Sets the active preset identifier if it exists.
        /// </summary>
        public bool SetActivePreset(string presetId) => TrySelectPreset(presetId);

        /// <summary>
        /// Creates a configuration containing the default preset definition.
        /// </summary>
        public static AutomaticSpeechRecognitionConfiguration CreateDefault() =>
            new AutomaticSpeechRecognitionConfiguration();

        private static ASRPreset[] ClonePresets(ASRPreset[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ASRPreset>();
            }

            var clone = new ASRPreset[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                clone[i] = source[i].Clone();
            }

            return clone;
        }

        private int FindPreset(string presetId)
        {
            if (_presets == null || _presets.Length == 0 || string.IsNullOrWhiteSpace(presetId))
            {
                return -1;
            }

            for (int i = 0; i < _presets.Length; i++)
            {
                var candidate = _presets[i];
                if (!string.IsNullOrWhiteSpace(candidate.Id) &&
                    string.Equals(candidate.Id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private ASRPreset GetActivePresetInternal(bool clone)
        {
            var presets = SupportedPresets;
            if (presets.Count == 0)
            {
                return clone ? ASRPreset.Default.Clone() : ASRPreset.Default;
            }

            string presetId = ActivePresetId;
            for (int i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                if (!string.IsNullOrWhiteSpace(preset.Id) &&
                    string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    return clone ? preset.Clone() : preset;
                }
            }

            var fallback = presets[0];
            return clone ? fallback.Clone() : fallback;
        }
    }
}
#endif
