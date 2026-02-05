using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eitan.EasyMic.Runtime.Mono.Components.TTS
{
    [Serializable]
    public sealed class SpeechSynthesizerConfiguration
    {
        [Serializable]
        public struct TTSPreset
        {
            public const string DefaultPresetId = "default";
            public string Id;
            public string DisplayName;
            public string modelId;
            public int voiceId;
            public float speed;
            public int sampleRates;

            public TTSPreset Clone() => (TTSPreset)MemberwiseClone();

            public static TTSPreset Create(string modelId, int voiceId, float speed, int sampleRate,
                string id = DefaultPresetId, string displayName = "Default")
            {
                return new TTSPreset
                {
                    Id = id,
                    DisplayName = displayName,
                    modelId = modelId,
                    voiceId = voiceId,
                    speed = speed,
                    sampleRates = sampleRate
                };
            }

            public static TTSPreset Default => Create("vits-melo-tts-zh_en", 1, 1f, 44100);
        }

        [SerializeField] private TTSPreset[] _presets = { TTSPreset.Default };
        [SerializeField] private string _activePresetId = TTSPreset.DefaultPresetId;

        public IReadOnlyList<TTSPreset> Presets => _presets ?? Array.Empty<TTSPreset>();
        public string ActivePresetId
        {
            get => string.IsNullOrWhiteSpace(_activePresetId) ? TTSPreset.DefaultPresetId : _activePresetId;
            private set => _activePresetId = value;
        }

        public TTSPreset GetActivePreset() => GetActivePresetRaw();

        public bool TryGetPreset(string presetId, out TTSPreset preset)
        {
            if (!string.IsNullOrWhiteSpace(presetId))
            {
                foreach (var cfg in Presets)
                {
                    if (!string.IsNullOrWhiteSpace(cfg.Id) &&
                        string.Equals(cfg.Id, presetId, StringComparison.OrdinalIgnoreCase))
                    {
                        preset = cfg.Clone();
                        return true;
                    }
                }
            }

            preset = default;
            return false;
        }

        public bool UpdatePreset(TTSPreset preset)
        {
            if (_presets == null || _presets.Length == 0 || string.IsNullOrWhiteSpace(preset.Id))
            {
                return false;
            }

            for (int i = 0; i < _presets.Length; i++)
            {
                var candidate = _presets[i];
                if (!string.IsNullOrWhiteSpace(candidate.Id) &&
                    string.Equals(candidate.Id, preset.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var clone = (TTSPreset[])_presets.Clone();
                    clone[i] = preset;
                    _presets = clone;
                    return true;
                }
            }

            return false;
        }

        public bool AddPreset(TTSPreset preset, bool overwrite = false)
        {
            if (_presets == null)
            {
                _presets = Array.Empty<TTSPreset>();
            }

            int existingIndex = -1;
            if (!string.IsNullOrWhiteSpace(preset.Id))
            {
                for (int i = 0; i < _presets.Length; i++)
                {
                    var candidate = _presets[i];
                    if (!string.IsNullOrWhiteSpace(candidate.Id) &&
                        string.Equals(candidate.Id, preset.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIndex = i;
                        break;
                    }
                }
            }

            if (existingIndex >= 0 && !overwrite)
            {
                return false;
            }

            if (existingIndex >= 0)
            {
                var clone = (TTSPreset[])_presets.Clone();
                clone[existingIndex] = preset;
                _presets = clone;
                return true;
            }

            var list = new List<TTSPreset>(_presets) { preset };
            _presets = list.ToArray();
            return true;
        }

        private TTSPreset GetActivePresetRaw()
        {
            if (Presets.Count == 0)
            {
                return TTSPreset.Default;
            }

            var presetId = ActivePresetId;
            if (!string.IsNullOrWhiteSpace(presetId))
            {
                foreach (var cfg in Presets)
                {
                    if (string.Equals(cfg.Id, presetId, StringComparison.OrdinalIgnoreCase))
                    {
                        return cfg;
                    }
                }
            }
            return Presets[0];
        }

        public string ModelId => GetActivePresetRaw().modelId ?? TTSPreset.Default.modelId;
        public int VoiceId => GetActivePresetRaw().voiceId != 0 ? GetActivePresetRaw().voiceId : TTSPreset.Default.voiceId;
        public float Speed => GetActivePresetRaw().speed > 0 ? GetActivePresetRaw().speed : TTSPreset.Default.speed;
        public int SampleRates => GetActivePresetRaw().sampleRates != 0 ? GetActivePresetRaw().sampleRates : TTSPreset.Default.sampleRates;

        public bool SetActivePreset(string presetId)
        {
            if (Presets.Count == 0)
            {
                bool isDefault = string.IsNullOrWhiteSpace(presetId) ||
                               string.Equals(presetId, TTSPreset.DefaultPresetId, StringComparison.OrdinalIgnoreCase);
                if (isDefault)
                {
                    ActivePresetId = TTSPreset.DefaultPresetId;
                }

                return isDefault;
            }

            if (string.IsNullOrWhiteSpace(presetId))
            {
                ActivePresetId = !string.IsNullOrWhiteSpace(Presets[0].Id) ? Presets[0].Id : TTSPreset.DefaultPresetId;
                return true;
            }

            foreach (var p in Presets)
            {
                if (string.Equals(p.Id, presetId, StringComparison.OrdinalIgnoreCase))
                {
                    ActivePresetId = p.Id;
                    return true;
                }
            }
            return false;
        }

        public static SpeechSynthesizerConfiguration CreateDefault() => new SpeechSynthesizerConfiguration();
    }
}
