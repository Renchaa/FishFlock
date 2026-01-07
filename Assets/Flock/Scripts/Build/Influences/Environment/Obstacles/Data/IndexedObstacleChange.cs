namespace Flock.Scripts.Build.Influence.Environment.Obstacles.Data {
    /**
     * <summary>
     * Represents a single obstacle update targeting a specific index in the runtime obstacle array.
     * </summary>
     */
    public struct IndexedObstacleChange {
        public int Index;
        public FlockObstacleData Data;
    }
}