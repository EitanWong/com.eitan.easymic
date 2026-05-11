using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Immutable, versioned view of the nodes owned by an AudioMixer. Created on the control thread, read on the audio thread.
    /// </summary>
    internal sealed class GainTable
    {
        public static readonly GainTable Empty = new GainTable(Array.Empty<MixNodeEntry>(), 0, 0, false);

        public GainTable(MixNodeEntry[] entries, int version, bool hasSolo)
            : this(entries, entries?.Length ?? 0, version, hasSolo)
        {
        }

        public GainTable(MixNodeEntry[] entries, int count, int version, bool hasSolo)
        {
            Entries = entries ?? Array.Empty<MixNodeEntry>();
            Count = Math.Max(0, Math.Min(count, Entries.Length));
            Version = version;
            HasSolo = hasSolo;
        }

        public MixNodeEntry[] Entries { get; }
        public int Count { get; }
        public int Version { get; }
        public bool HasSolo { get; }
        public bool IsEmpty => Count == 0;
    }

    /// <summary>
    /// Cached node metadata for the audio thread. Contains only value types for deterministic, allocation-free access.
    /// </summary>
    internal readonly struct MixNodeEntry
    {
        public MixNodeEntry(IMixNode node, float effectiveGain, bool solo, bool active)
        {
            Node = node;
            EffectiveGain = effectiveGain;
            Solo = solo;
            Active = active;
        }

        public IMixNode Node { get; }
        public float EffectiveGain { get; }
        public bool Solo { get; }
        public bool Active { get; }
    }
}
