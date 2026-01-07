using Unity.Collections;
using Unity.Jobs;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {

    /**
     * <summary>
     * Builds per-cell ranges (start/count) into <see cref="CellStarts"/> and <see cref="CellCounts"/>
     * from a sorted <see cref="Pairs"/> list, and records which cells were touched.
     * </summary>
     */
    [BurstCompile]
    public struct BuildCellAgentRangesJob : IJob {
        [ReadOnly]
        public NativeArray<CellAgentPair> Pairs;

        [ReadOnly]
        public NativeArray<int> Total;

        public NativeArray<int> CellStarts;

        public NativeArray<int> CellCounts;

        public NativeArray<int> TouchedCells;

        public NativeArray<int> TouchedCount;

        public void Execute() {
            int pairCount = Total[0];

            if (pairCount <= 0) {
                TouchedCount[0] = 0;
                return;
            }

            int touchedCellCount = 0;

            int runningCellId = Pairs[0].CellId;
            int runningStartIndex = 0;
            int runningLength = 1;

            for (int pairIndex = 1; pairIndex < pairCount; pairIndex += 1) {
                int cellId = Pairs[pairIndex].CellId;

                if (cellId == runningCellId) {
                    runningLength += 1;
                    continue;
                }

                WriteRun(ref touchedCellCount, runningCellId, runningStartIndex, runningLength);

                runningCellId = cellId;
                runningStartIndex = pairIndex;
                runningLength = 1;
            }

            WriteRun(ref touchedCellCount, runningCellId, runningStartIndex, runningLength);
            TouchedCount[0] = touchedCellCount;
        }

        private void WriteRun(ref int touchedCellCount, int cellId, int startIndex, int length) {
            CellStarts[cellId] = startIndex;
            CellCounts[cellId] = length;

            TouchedCells[touchedCellCount] = cellId;
            touchedCellCount += 1;
        }
    }
}