using Flock.Scripts.Build.Influence.Environment.Obstacles.Data;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

namespace Flock.Scripts.Build.Influence.Environment.Obstacles.Jobs
{
    /**
     * <summary>
     * Applies indexed obstacle changes to the authoritative runtime obstacle array.
     * </summary>
     */
    [BurstCompile]
    public struct ApplyIndexedObstacleChangesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<IndexedObstacleChange> Changes;

        public NativeArray<FlockObstacleData> Obstacles;

        public void Execute(int index)
        {
            IndexedObstacleChange change = Changes[index];
            Obstacles[change.Index] = change.Data;
        }
    }
}