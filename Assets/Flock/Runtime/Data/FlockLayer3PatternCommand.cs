namespace Flock.Runtime.Data {
    public struct FlockLayer3PatternCommand {
        public FlockLayer3PatternKind Kind;
        public int PayloadIndex;     // index into the kind-specific payload array
        public float Strength;       // per-pattern strength (before per-behaviour PatternWeight)
        public uint BehaviourMask;   // bitmask over behaviour indices (uint.MaxValue = all)
    }
}
