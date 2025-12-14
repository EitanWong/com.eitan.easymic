namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// Slope-based gain envelope used to ramp node contributions without clicks.
    /// </summary>
    internal struct MixerGainEnvelope
    {
        public float Current;
        public float Target;
        public float Step;
        public int SamplesRemaining;
    }
}
