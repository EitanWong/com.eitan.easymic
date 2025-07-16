// Recording handle for managing individual recordings
using System;
namespace Eitan.EasyMic.Runtime
{

    public struct RecordingHandle : IEquatable<RecordingHandle>
    {
        public readonly int Id;
        internal readonly MicDevice Device;

        public string Name=> Device.Name;

        public Channel Channel => Device.GetDeviceChannel();

        internal RecordingHandle(int id, MicDevice device)
        {
            Id = id;
            Device = device;
        }

        public bool IsValid => Id > 0;

        public bool Equals(RecordingHandle other) => Id == other.Id;
        public override bool Equals(object obj) => obj is RecordingHandle handle && Equals(handle);
        public override int GetHashCode() => Id;
        public static bool operator ==(RecordingHandle left, RecordingHandle right) => left.Equals(right);
        public static bool operator !=(RecordingHandle left, RecordingHandle right) => !left.Equals(right);
    }

}