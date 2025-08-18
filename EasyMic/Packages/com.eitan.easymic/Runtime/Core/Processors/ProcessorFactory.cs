namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    /// One-liner blueprints for common processors.
    /// </summary>
    public static class ProcessorFactory
    {
        public static AudioWorkerBlueprint SoftLimiter(float thresholdDb = -0.1f, float makeupDb = 0f)
        {
            return new AudioWorkerBlueprint(() => new SoftLimiter { ThresholdDb = thresholdDb, MakeupDb = makeupDb }, key: $"SoftLimiter:{thresholdDb}:{makeupDb}");
        }

        public static AudioWorkerBlueprint Downmixer()
        {
            return new AudioWorkerBlueprint(() => new AudioDownmixer(), key: "Downmixer");
        }

        public static AudioWorkerBlueprint VolumeGate(float thresholdDb = -40f)
        {
            return new AudioWorkerBlueprint(() => new VolumeGateFilter { ThresholdDb = thresholdDb }, key: $"VolumeGate:{thresholdDb}");
        }

        public static AudioWorkerBlueprint HighPass(float cutoffHz = 120f, float q = 0.707f)
        {
            return new AudioWorkerBlueprint(() => new HighPassFilter(cutoffHz, q), key: $"HighPass:{cutoffHz}:{q}");
        }

        public static AudioWorkerBlueprint LowShelf(float cutoffHz = 120f, float gainDb = -6f, float q = 0.707f)
        {
            return new AudioWorkerBlueprint(() => new LowShelfFilter(cutoffHz, gainDb, q), key: $"LowShelf:{cutoffHz}:{gainDb}:{q}");
        }

        public static AudioWorkerBlueprint Peaking(float centerHz = 1000f, float gainDb = 0f, float q = 1.0f)
        {
            return new AudioWorkerBlueprint(() => new PeakingEQ(centerHz, gainDb, q), key: $"Peaking:{centerHz}:{gainDb}:{q}");
        }
    }
}
