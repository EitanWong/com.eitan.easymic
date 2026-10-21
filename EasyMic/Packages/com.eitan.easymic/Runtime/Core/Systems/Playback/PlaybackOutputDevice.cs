namespace Eitan.EasyMic.Runtime
{
    /// <summary>Identifies an output endpoint reported by miniaudio.</summary>
    public readonly struct PlaybackOutputDevice
    {
        public PlaybackOutputDevice(string id, string name, bool isDefault)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            IsDefault = isDefault;
        }

        /// <summary>Opaque endpoint identifier. Empty identifies the system default.</summary>
        public string Id { get; }
        public string Name { get; }
        public bool IsDefault { get; }
    }
}
