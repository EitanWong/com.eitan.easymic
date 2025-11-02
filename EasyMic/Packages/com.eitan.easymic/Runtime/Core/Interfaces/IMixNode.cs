using System;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Minimal contract for mix graph participants that can render audio into a mixer.
    /// Implementations must be real-time safe: no allocations, locks, or exceptions in RenderInto.
    /// </summary>
    internal interface IMixNode
    {
        /// <summary>Name exposed for diagnostics only.</summary>
        string Name { get; }

        /// <summary>Linear volume in [0, +inf). Parent mixers apply perceptual weighting.</summary>
        float Volume { get; }

        /// <summary>Whether this node should be omitted from mixing regardless of volume.</summary>
        bool Mute { get; }

        /// <summary>Solo flag used by parent mixers to compute normalized gains.</summary>
        bool Solo { get; }

        /// <summary>True if this node or any descendant is soloed.</summary>
        bool HasSoloInTree { get; }

        /// <summary>
        /// Indicates the node has audio available for mixing. Parents may skip rendering when false.
        /// Must be cheap to query and thread-safe.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Raised on control thread when any property that affects mixing (volume, mute, solo, active membership) changes.
        /// Parent mixers subscribe to rebuild gain tables.
        /// </summary>
        event Action<IMixNode> StateChanged;

        /// <summary>
        /// Render audio into destination buffer additively using the provided gain envelope and target gain.
        /// Implementations must not allocate and must honour ramping via the supplied envelope.
        /// </summary>
        void RenderInto(
            Span<float> destination,
            int systemChannels,
            int systemSampleRate,
            ref MixerGainEnvelope envelope,
            float targetGain,
            int rampSamples,
            Span<float> scratch);
    }
}
