namespace Flock.Scripts.Build.Influence.Environment.Attractors.Data {
    /**
     * <summary>
     * Represents a single attractor update targeting a specific index in the runtime attractor array.
     * </summary>
     */
    public struct IndexedAttractorChange {
        public int Index;
        public FlockAttractorData Data;
    }
}