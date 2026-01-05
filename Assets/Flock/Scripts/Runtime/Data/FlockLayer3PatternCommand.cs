namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Runtime command entry that references a Layer-3 pattern payload.
     * </summary>
     */
    public struct FlockLayer3PatternCommand {
        // Command
        public FlockLayer3PatternKind Kind;
        public int PayloadIndex;
        public float Strength;
        public uint BehaviourMask;
    }
}
