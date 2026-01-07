using Unity.Collections;
using Unity.Jobs;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {
    /**
     * <summary>
     * Sorts the first <see cref="Total"/>[0] pairs by cell id, then agent index.
     * </summary>
     */
    [BurstCompile]
    public struct SortCellAgentPairsJob : IJob {
        public NativeArray<CellAgentPair> Pairs;

        [ReadOnly]
        public NativeArray<int> Total;

        public void Execute() {
            int pairCount = Total[0];

            if (pairCount <= 1) {
                return;
            }

            NativeSlice<CellAgentPair> slice = new NativeSlice<CellAgentPair>(Pairs, 0, pairCount);
            NativeSortExtension.Sort(slice);
        }
    }
}