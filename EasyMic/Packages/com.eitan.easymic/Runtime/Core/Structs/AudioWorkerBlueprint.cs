namespace Eitan.EasyMic.Runtime
{
    using System;

    /// <summary>
    /// A reusable template that creates a fresh IAudioWorker instance when bound to a session.
    /// Use this blueprint everywhere (start, add/remove) to avoid passing external worker instances into sessions.
    /// </summary>
    public sealed class AudioWorkerBlueprint
    {
        private readonly Func<IAudioWorker> _factory;
        private readonly string _key;

        public AudioWorkerBlueprint(Func<IAudioWorker> factory, string key = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _key = string.IsNullOrEmpty(key) ? Guid.NewGuid().ToString("N") : key;
        }

        internal IAudioWorker Create() => _factory();

        public override int GetHashCode() => _key.GetHashCode();
        public override bool Equals(object obj) => obj is AudioWorkerBlueprint other && other._key == _key;

        public override string ToString() => $"AudioWorkerBlueprint({_key})";
    }
}

