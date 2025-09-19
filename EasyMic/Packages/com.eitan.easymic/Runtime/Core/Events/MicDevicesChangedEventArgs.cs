using System;
using System.Linq;

namespace Eitan.EasyMic.Runtime
{
    /// <summary>
    ///     Describes the delta between two device enumeration snapshots.
    /// </summary>
    public readonly struct MicDevicesChangedEventArgs
    {
        public MicDevicesChangedEventArgs(
            MicDevice[] previous,
            MicDevice[] current,
            MicDevice[] added,
            MicDevice[] removed,
            MicDevice[] updated,
            MicDevice? previousDefault,
            MicDevice? currentDefault)
        {
            Previous = previous ?? Array.Empty<MicDevice>();
            Current = current ?? Array.Empty<MicDevice>();
            Added = added ?? Array.Empty<MicDevice>();
            Removed = removed ?? Array.Empty<MicDevice>();
            Updated = updated ?? Array.Empty<MicDevice>();
            PreviousDefault = previousDefault;
            CurrentDefault = currentDefault;
        }

        public MicDevice[] Previous { get; }
        public MicDevice[] Current { get; }
        public MicDevice[] Added { get; }
        public MicDevice[] Removed { get; }
        public MicDevice[] Updated { get; }
        public MicDevice? PreviousDefault { get; }
        public MicDevice? CurrentDefault { get; }

        public bool HasChanges => Added.Length > 0 || Removed.Length > 0 || Updated.Length > 0 || !HasSameDefault(PreviousDefault, CurrentDefault);

        private static bool HasSameDefault(MicDevice? left, MicDevice? right)
        {
            if (!left.HasValue && !right.HasValue)
            {
                return true;
            }

            if (!left.HasValue || !right.HasValue)
            {
                return false;
            }

            return left.Value.SameIdentityAs(right.Value);
        }
    }
}
