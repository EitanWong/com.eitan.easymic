using System;

namespace Eitan.EasyMic.Runtime
{
    public readonly struct RecordingHandle : IEquatable<RecordingHandle>
    {
        public int Id { get; }

        internal RecordingHandle(int id)
        {
            Id = id;
        }

        public bool IsValid => Id > 0;

        public bool Equals(RecordingHandle other) => Id == other.Id;
        public override bool Equals(object obj) => obj is RecordingHandle handle && Equals(handle);
        public override int GetHashCode() => Id;
        public static bool operator ==(RecordingHandle left, RecordingHandle right) => left.Equals(right);
        public static bool operator !=(RecordingHandle left, RecordingHandle right) => !left.Equals(right);
    }
}
