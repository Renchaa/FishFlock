namespace Flock.Scripts.Build.Influence.Environment.Obstacles.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    /**
     * <summary>
     * Applies indexed obstacle changes to the authoritative runtime obstacle array.
     * </summary>
     */
    [BurstCompile]
    public struct ApplyIndexedObstacleChangesJob : IJobParallelFor {
        [ReadOnly]
        public NativeArray<IndexedObstacleChange> Changes;

        public NativeArray<FlockObstacleData> Obstacles;

        public void Execute(int index) {
            IndexedObstacleChange change = Changes[index];
            Obstacles[change.Index] = change.Data;
        }
    }
}