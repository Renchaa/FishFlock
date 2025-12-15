namespace Flock.Runtime.Data {
    using System;

    /// <summary>
    /// Opaque handle to a runtime-instanced Layer-3 pattern.
    /// Index points to a slot; Generation prevents stale-handle bugs after reuse.
    /// </summary>
    [Serializable]
    public struct FlockLayer3PatternHandle : IEquatable<FlockLayer3PatternHandle> {
        public int Index;
        public int Generation;

        public bool IsValid => Index >= 0;

        public static FlockLayer3PatternHandle Invalid => new FlockLayer3PatternHandle {
            Index = -1,
            Generation = 0
        };

        public bool Equals(FlockLayer3PatternHandle other) =>
            Index == other.Index && Generation == other.Generation;

        public override bool Equals(object obj) =>
            obj is FlockLayer3PatternHandle other && Equals(other);

        public override int GetHashCode() =>
            unchecked((Index * 397) ^ Generation);

        public static bool operator ==(FlockLayer3PatternHandle a, FlockLayer3PatternHandle b) => a.Equals(b);
        public static bool operator !=(FlockLayer3PatternHandle a, FlockLayer3PatternHandle b) => !a.Equals(b);
    }
}
