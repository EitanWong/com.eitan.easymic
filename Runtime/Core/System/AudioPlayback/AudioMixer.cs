using System;
using System.Collections.Generic;
using System.Threading;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Hierarchical mixer that normalizes gain across sources and child mixers while remaining RT-safe.
    /// </summary>
    public sealed class AudioMixer : IDisposable, IMixNode
    {
        private const float VolumeEpsilon = 1e-5f;
        private const float GainEpsilon = 1e-6f;
        private const float DefaultHeadroom = 0.97f;
        private const float RampMilliseconds = 4.0f;

        private readonly struct NodeRegistration
        {
            public NodeRegistration(IMixNode node, Action<IMixNode> handler)
            {
                Node = node;
                Handler = handler;
            }

            public IMixNode Node { get; }
            public Action<IMixNode> Handler { get; }
        }

        private readonly object _graphLock = new object();
        private readonly List<NodeRegistration> _nodes = new List<NodeRegistration>(8);

        private GainTable _activeTable = GainTable.Empty;
        private MixNodeEntry[] _entriesA = Array.Empty<MixNodeEntry>();
        private MixNodeEntry[] _entriesB = Array.Empty<MixNodeEntry>();
        private bool _activeEntriesAreA;

        private MixerGainEnvelope[] _envelopesA = Array.Empty<MixerGainEnvelope>();
        private MixerGainEnvelope[] _envelopesB = Array.Empty<MixerGainEnvelope>();
        private bool _activeEnvelopesAreA;
        private MixerGainEnvelope[] _runtime = Array.Empty<MixerGainEnvelope>();

        private float[] _accumBuf = Array.Empty<float>();
        private float[] _scratchBuf = Array.Empty<float>();

        private AudioContext _state;

        private float _masterVolume = 1.0f;
        private float _perceptualExponent = 1.0f;
        private float _headroom = DefaultHeadroom;
        private bool _mute;
        private bool _solo;
        private volatile bool _dirty;
        private int _tableVersion;
        private Action<IMixNode> _stateChanged;

        internal event Action<AudioMixer> GraphDirty;

        public AudioMixer()
        {
            Pipeline = new AudioPipeline();
        }

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                float clamped = value < 0f ? 0f : value;
                if (MathF.Abs(_masterVolume - clamped) <= GainEpsilon)
                {
                    return;
                }

                _masterVolume = clamped;
                MarkDirty();
            }
        }

        public float PerceptualExponent
        {
            get => _perceptualExponent;
            set
            {
                float clamped = value <= 0f ? 1f : value;
                if (MathF.Abs(_perceptualExponent - clamped) <= GainEpsilon)
                {
                    return;
                }

                _perceptualExponent = clamped;
                MarkDirty();
            }
        }

        public float Headroom
        {
            get => _headroom;
            set
            {
                float clamped = value <= 0f ? DefaultHeadroom : value;
                if (MathF.Abs(_headroom - clamped) <= GainEpsilon)
                {
                    return;
                }

                _headroom = clamped;
                MarkDirty();
            }
        }

        public bool Mute
        {
            get => _mute;
            set => _mute = value;
        }

        public bool Solo
        {
            get => _solo;
            set
            {
                if (_solo == value)
                {
                    return;
                }

                _solo = value;
                MarkDirty();
            }
        }

        public float NormalizedVolume => _headroom * _masterVolume;
        public AudioPipeline Pipeline { get; }
        public string name;

        public void Initialize(int channels, int sampleRate)
        {
            _state = new AudioContext(channels, sampleRate, 0);
            Pipeline.Initialize(_state);
            MarkDirty();
            EnsureGainTable();
        }

        public void SetMasterVolume(float volume) => MasterVolume = volume;
        public void SetPerceptualExponent(float exponent) => PerceptualExponent = exponent;

        public void AddSource(PlaybackAudioSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            AddNode(source);
        }

        public void RemoveSource(PlaybackAudioSource source)
        {
            if (source == null)
            {
                return;
            }

            RemoveNode(source);
        }

        public void SetSourceVolume(PlaybackAudioSource source, float volume)
        {
            if (source == null)
            {
                return;
            }

            source.Volume = volume;
        }

        public void AddMixer(AudioMixer mixer)
        {
            if (mixer == null)
            {
                throw new ArgumentNullException(nameof(mixer));
            }

            if (ReferenceEquals(mixer, this))
            {
                throw new InvalidOperationException("Cannot add mixer to itself.");
            }

            AddNode(mixer);
        }

        public void RemoveMixer(AudioMixer mixer)
        {
            if (mixer == null)
            {
                return;
            }

            RemoveNode(mixer);
        }

        public void RenderAdditive(Span<float> buffer, int systemChannels, int systemSampleRate)
        {
            RenderAdditive(buffer, systemChannels, systemSampleRate, 1f);
        }

        internal void RenderAdditive(Span<float> buffer, int systemChannels, int systemSampleRate, float upstreamGain)
        {
            if (buffer.IsEmpty || _mute)
            {
                return;
            }

            EnsureGainTable();
            var table = Volatile.Read(ref _activeTable);
            if (table.IsEmpty)
            {
                return;
            }

            var entries = table.Entries;
            var runtime = Volatile.Read(ref _runtime);
            if (entries.Length == 0 || runtime.Length < entries.Length)
            {
                return;
            }

            EnsureAccumBuffer(buffer.Length);
            var acc = new Span<float>(_accumBuf, 0, buffer.Length);
            acc.Clear();

            int rampSamples = CalculateRampSamples(systemSampleRate, systemChannels);

            for (int i = 0; i < entries.Length; i++)
            {
                ref var envelope = ref runtime[i];
                var entry = entries[i];
                var node = entry.Node;
                if (node == null)
                {
                    UpdateGainEnvelope(ref envelope, 0f, rampSamples);
                    continue;
                }

                float targetGain = upstreamGain * entry.EffectiveGain;
                bool include = targetGain > GainEpsilon && node.IsActive && !node.Mute;
                if (!include)
                {
                    UpdateGainEnvelope(ref envelope, 0f, rampSamples);
                    continue;
                }

                Span<float> scratch = Span<float>.Empty;
                try
                {
                    node.RenderInto(acc, systemChannels, systemSampleRate, ref envelope, targetGain, rampSamples, scratch);
                }
                catch
                {
                    // Hard RT safety: swallow node exceptions to avoid device dropout.
                }
            }

            _state.Length = acc.Length;
            try { Pipeline.OnAudioPass(acc, _state); } catch { }
            ApplySoftLimitIfNeeded(acc);

            for (int i = 0; i < acc.Length; i++)
            {
                buffer[i] += acc[i];
            }
        }

        public void Process(Span<float> destination, int systemChannels, int systemSampleRate)
        {
            RenderAdditive(destination, systemChannels, systemSampleRate);
        }

        public AudioMixer[] GetChildren()
        {
            lock (_graphLock)
            {
                var list = new List<AudioMixer>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].Node is AudioMixer mixer)
                    {
                        list.Add(mixer);
                    }
                }
                return list.ToArray();
            }
        }

        public PlaybackAudioSource[] GetSources()
        {
            lock (_graphLock)
            {
                var list = new List<PlaybackAudioSource>();
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (_nodes[i].Node is PlaybackAudioSource source)
                    {
                        list.Add(source);
                    }
                }
                return list.ToArray();
            }
        }

        string IMixNode.Name => name ?? string.Empty;

        float IMixNode.Volume => MasterVolume;

        bool IMixNode.Mute => Mute;

        bool IMixNode.Solo => Solo;

        bool IMixNode.HasSoloInTree => HasSoloRecursive();

        bool IMixNode.IsActive => !_mute;

        event Action<IMixNode> IMixNode.StateChanged
        {
            add => _stateChanged += value;
            remove => _stateChanged -= value;
        }

        void IMixNode.RenderInto(
            Span<float> destination,
            int systemChannels,
            int systemSampleRate,
            ref MixerGainEnvelope envelope,
            float targetGain,
            int rampSamples,
            Span<float> scratch)
        {
            if (destination.IsEmpty)
            {
                UpdateGainEnvelope(ref envelope, 0f, rampSamples);
                return;
            }

            if (targetGain <= GainEpsilon)
            {
                UpdateGainEnvelope(ref envelope, 0f, rampSamples);
                return;
            }

            Span<float> targetBuffer;
            if (scratch.Length >= destination.Length)
            {
                targetBuffer = scratch.Slice(0, destination.Length);
            }
            else
            {
                EnsureScratchBuffer(destination.Length);
                targetBuffer = new Span<float>(_scratchBuf, 0, destination.Length);
            }

            targetBuffer.Clear();
            try
            {
                RenderAdditive(targetBuffer, systemChannels, systemSampleRate, 1f);
            }
            catch
            {
                targetBuffer.Clear();
            }
            ApplyGainAndAccumulate(destination, targetBuffer, ref envelope, targetGain, rampSamples);
        }

        public bool HasSoloRecursive()
        {
            if (_solo)
            {
                return true;
            }

            lock (_graphLock)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    var mixNode = _nodes[i].Node;
                    if (mixNode == null)
                    {
                        continue;
                    }

                    if (mixNode.Solo || mixNode.HasSoloInTree)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Dispose()
        {
            try { Pipeline.Dispose(); } catch { }
        }

        private void AddNode(IMixNode node)
        {
            if (node == null)
            {
                return;
            }

            Action<IMixNode> handler;
            lock (_graphLock)
            {
                if (FindNodeIndexUnsafe(node) >= 0)
                {
                    return;
                }

                handler = OnNodeStateChanged;
                _nodes.Add(new NodeRegistration(node, handler));
            }

            node.StateChanged += handler;
            MarkDirty();
            EnsureGainTable();
        }

        private void RemoveNode(IMixNode node)
        {
            if (node == null)
            {
                return;
            }

            Action<IMixNode> handler = null;
            lock (_graphLock)
            {
                int idx = FindNodeIndexUnsafe(node);
                if (idx < 0)
                {
                    return;
                }

                handler = _nodes[idx].Handler;
                _nodes.RemoveAt(idx);
            }

            if (handler != null)
            {
                node.StateChanged -= handler;
            }

            MarkDirty();
            EnsureGainTable();
        }

        private int FindNodeIndexUnsafe(IMixNode node)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (ReferenceEquals(_nodes[i].Node, node))
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnNodeStateChanged(IMixNode obj)
        {
            MarkDirty();
        }

        private void MarkDirty()
        {
            _dirty = true;
            GraphDirty?.Invoke(this);
            _stateChanged?.Invoke(this);
        }

        private void EnsureGainTable()
        {
            if (!_dirty)
            {
                return;
            }

            lock (_graphLock)
            {
                if (!_dirty)
                {
                    return;
                }

                RebuildGainTableLocked();
                _dirty = false;
            }
        }

        private void RebuildGainTableLocked()
        {
            int nodeCount = _nodes.Count;
            if (nodeCount == 0)
            {
                Volatile.Write(ref _activeTable, GainTable.Empty);
                Volatile.Write(ref _runtime, Array.Empty<MixerGainEnvelope>());
                return;
            }

            var entries = AcquireEntryBuffer(nodeCount);
            float totalWeight = 0f;
            float soloWeight = 0f;
            bool hasSolo = _solo;
            float normalizerBase = _headroom * _masterVolume;

            for (int i = 0; i < nodeCount; i++)
            {
                var node = _nodes[i].Node;
                if (node == null)
                {
                    entries[i] = default;
                    continue;
                }

                bool nodeSolo = node.Solo || node.HasSoloInTree;
                if (nodeSolo)
                {
                    hasSolo = true;
                }

                float weight = node.Mute ? 0f : ComputeWeightScalar(node.Volume);
                if (weight <= VolumeEpsilon)
                {
                    weight = 0f;
                }

                bool active = node.IsActive && !node.Mute;

                entries[i] = new MixNodeEntry(node, weight, nodeSolo, active);

                if (weight > 0f)
                {
                    totalWeight += weight;
                    if (nodeSolo || _solo)
                    {
                        soloWeight += weight;
                    }
                }
            }

            float normalizer = hasSolo ? soloWeight : totalWeight;
            if (normalizer <= VolumeEpsilon || normalizerBase <= GainEpsilon)
            {
                for (int i = 0; i < nodeCount; i++)
                {
                    var entry = entries[i];

                    entries[i] = new MixNodeEntry(entry.Node, 0f, entry.Solo, entry.Active);
                }
            }
            else
            {
                float scalar = normalizerBase / normalizer;
                for (int i = 0; i < nodeCount; i++)
                {
                    var entry = entries[i];
                    float effective = entry.EffectiveGain > 0f ? entry.EffectiveGain * scalar : 0f;
                    entries[i] = new MixNodeEntry(entry.Node, effective, entry.Solo, entry.Active);
                }
            }

            var previousTable = Volatile.Read(ref _activeTable);
            var previousRuntime = Volatile.Read(ref _runtime);
            var runtime = AcquireEnvelopeBuffer(nodeCount);

            if (previousTable.Entries.Length > 0 && previousRuntime.Length > 0)
            {
                var envelopeMap = new Dictionary<IMixNode, MixerGainEnvelope>(previousTable.Entries.Length);
                for (int i = 0; i < previousTable.Entries.Length && i < previousRuntime.Length; i++)
                {
                    var prevNode = previousTable.Entries[i].Node;
                    if (prevNode != null)
                    {
                        envelopeMap[prevNode] = previousRuntime[i];
                    }
                }

                for (int i = 0; i < nodeCount; i++)
                {
                    var node = entries[i].Node;
                    runtime[i] = node != null && envelopeMap.TryGetValue(node, out var env) ? env : default;
                }
            }
            else
            {
                Array.Clear(runtime, 0, runtime.Length);
            }

            var table = new GainTable(entries, ++_tableVersion, hasSolo);
            Volatile.Write(ref _runtime, runtime);
            Volatile.Write(ref _activeTable, table);

            _activeEntriesAreA = !_activeEntriesAreA;
            _activeEnvelopesAreA = !_activeEnvelopesAreA;
        }

        private MixNodeEntry[] AcquireEntryBuffer(int required)
        {
            if (_activeEntriesAreA)
            {
                if (_entriesB.Length != required)
                {
                    _entriesB = new MixNodeEntry[required];
                }
                return _entriesB;
            }

            if (_entriesA.Length != required)
            {
                _entriesA = new MixNodeEntry[required];
            }
            return _entriesA;
        }

        private MixerGainEnvelope[] AcquireEnvelopeBuffer(int required)
        {
            if (_activeEnvelopesAreA)
            {
                if (_envelopesB.Length != required)
                {
                    _envelopesB = new MixerGainEnvelope[required];
                }
                return _envelopesB;
            }

            if (_envelopesA.Length != required)
            {
                _envelopesA = new MixerGainEnvelope[required];
            }
            return _envelopesA;
        }

        private float ComputeWeightScalar(float volume)
        {
            float clamped = volume < 0f ? 0f : volume;
            if (clamped <= VolumeEpsilon)
            {
                return 0f;
            }

            if (MathF.Abs(_perceptualExponent - 1f) <= GainEpsilon)
            {
                return clamped;
            }

            return MathF.Pow(clamped, _perceptualExponent);
        }

        private static int CalculateRampSamples(int sampleRate, int channels)
        {
            int frames = (int)Math.Max(1, MathF.Round(sampleRate * (RampMilliseconds * 0.001f)));
            return frames * Math.Max(1, channels);
        }

        private static void UpdateGainEnvelope(ref MixerGainEnvelope envelope, float targetGain, int rampSamples)
        {
            if (MathF.Abs(envelope.Target - targetGain) <= GainEpsilon)
            {
                if (envelope.SamplesRemaining <= 0)
                {
                    envelope.Current = targetGain;
                    envelope.Target = targetGain;
                    envelope.Step = 0f;
                }
                return;
            }

            envelope.Target = targetGain;
            if (rampSamples <= 0)
            {
                envelope.Current = targetGain;
                envelope.Step = 0f;
                envelope.SamplesRemaining = 0;
                return;
            }

            float delta = targetGain - envelope.Current;
            if (MathF.Abs(delta) <= GainEpsilon)
            {
                envelope.Current = targetGain;
                envelope.Step = 0f;
                envelope.SamplesRemaining = 0;
                return;
            }

            envelope.Step = delta / rampSamples;
            envelope.SamplesRemaining = rampSamples;
        }

        private static void ApplyGainAndAccumulate(Span<float> destination, Span<float> source, ref MixerGainEnvelope envelope, float targetGain, int rampSamples)
        {
            UpdateGainEnvelope(ref envelope, targetGain, rampSamples);

            float current = envelope.Current;
            float step = envelope.Step;
            int remaining = envelope.SamplesRemaining;
            float target = envelope.Target;

            for (int i = 0; i < source.Length; i++)
            {
                float sample = source[i] * current;
                destination[i] += sample;

                if (remaining > 0)
                {
                    current += step;
                    remaining--;
                    if (remaining == 0)
                    {
                        current = target;
                    }
                }
            }

            envelope.Current = current;
            envelope.SamplesRemaining = remaining;
            if (remaining <= 0)
            {
                envelope.Step = 0f;
            }
        }

        private static void ApplySoftLimitIfNeeded(Span<float> buffer)
        {
            bool needed = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (MathF.Abs(buffer[i]) > 1f)
                {
                    needed = true;
                    break;
                }
            }

            if (!needed)
            {
                return;
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                float sample = buffer[i];
                float abs = MathF.Abs(sample);
                if (abs <= 1f)
                {
                    continue;
                }

                float over = abs - 1f;
                float limited = abs / (1f + over);
                buffer[i] = CopySign(limited, sample);
            }
        }

        private static float CopySign(float magnitude, float sign)
        {
            float abs = MathF.Abs(magnitude);
            return sign >= 0f ? abs : -abs;
        }

        private void EnsureAccumBuffer(int required)
        {
            if (_accumBuf.Length >= required)
            {
                return;
            }

            int newSize = _accumBuf.Length == 0 ? 1024 : _accumBuf.Length;
            while (newSize < required)
            {
                newSize *= 2;
            }
            _accumBuf = new float[newSize];
        }

        private void EnsureScratchBuffer(int required)
        {
            if (_scratchBuf.Length >= required)
            {
                return;
            }

            int newSize = _scratchBuf.Length == 0 ? 1024 : _scratchBuf.Length;
            while (newSize < required)
            {
                newSize *= 2;
            }
            _scratchBuf = new float[newSize];
        }
    }
}
