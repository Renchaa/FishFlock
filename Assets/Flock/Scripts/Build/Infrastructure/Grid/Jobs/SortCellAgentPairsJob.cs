using Flock.Scripts.Build.Infrastructure.Grid.Data;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs
{
    /**
     * <summary>
     * Sorts the first <see cref="Total"/>[0] pairs by cell id, then agent index.
     * </summary>
     */
    [BurstCompile]
    public struct SortCellAgentPairsJob : IJob
    {
        [ReadOnly] public NativeArray<int> Total;

        public NativeArray<CellAgentPair> Pairs;

        public void Execute()
        {
            int pairCount = Total[0];

            if (pairCount <= 1)
            {
                return;
            }

            NativeSlice<CellAgentPair> slice = new NativeSlice<CellAgentPair>(Pairs, 0, pairCount);
            NativeSortExtension.Sort(slice);
        }
    }
}