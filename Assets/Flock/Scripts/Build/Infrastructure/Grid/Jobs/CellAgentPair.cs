using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {

    /**
     * <summary>
     * Clears <see cref="CellStarts"/> and <see cref="CellCounts"/> for the cells recorded in <see cref="TouchedCells"/>.
     * </summary>
     */
    [BurstCompile]
    public struct ClearTouchedAgentCellsJob : IJob {
        [ReadOnly]
        public NativeArray<int> TouchedCells;

        public NativeArray<int> TouchedCount;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> CellStarts;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> CellCounts;

        public void Execute() {
            int touchedCellCount = TouchedCount[0];

            for (int index = 0; index < touchedCellCount; index += 1) {
                int cellId = TouchedCells[index];
                CellStarts[cellId] = -1;
                CellCounts[cellId] = 0;
            }

            TouchedCount[0] = 0;
        }
    }
}
