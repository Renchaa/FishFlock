using Unity.Jobs;

namespace Flock.Scripts.Build.Utilities.Jobs {
    /**
     * <summary>
     * Clears a <see cref="NativeParallelMultiHashMap{TKey,TValue}"/> used for grid indexing.
     * </summary>
     */
    [BurstCompile]
    public struct ClearMultiHashMapJob : IJob {
        public NativeParallelMultiHashMap<int, int> Map;

        public void Execute() {
            Map.Clear();
        }
    }
}