using System;

namespace Flock.Scripts.Build.Influence.PatternVolume.Data {
    /**
     * <summary>
     * Opaque handle to a runtime-instanced Layer-3 pattern.
     * Index points to a slot; Generation prevents stale-handle bugs after reuse.
     * </summary>
     */
    [Serializable]
    public struct PatternVolumeHandle : IEquatable<PatternVolumeHandle> {
        // Identity
        public int Index;
        public int Generation;

        /**
         * <summary>
         * Gets whether this handle refers to a valid slot.
         * </summary>
         */
        public bool IsValid => Index >= 0;

        /**
         * <summary>
         * Gets an invalid handle value.
         * </summary>
         */
        public static PatternVolumeHandle Invalid => new PatternVolumeHandle {
            Index = -1,
            Generation = 0,
        };

        public static bool operator ==(PatternVolumeHandle a, PatternVolumeHandle b) => a.Equals(b);

        public static bool operator !=(PatternVolumeHandle a, PatternVolumeHandle b) => !a.Equals(b);

        public bool Equals(PatternVolumeHandle other) =>
            Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) =>
            obj is PatternVolumeHandle other && Equals(other);

        public override int GetHashCode() =>
            unchecked((Index * 397) ^ Generation);
    }
}
