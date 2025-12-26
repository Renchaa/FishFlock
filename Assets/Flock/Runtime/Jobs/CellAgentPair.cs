using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Flock.Runtime.Jobs {
    /**
     * <summary>
     * Pair of (cell id, agent index) used for sorting and building per-cell agent ranges.
     * Sorting is by <see cref="CellId"/>, then <see cref="AgentIndex"/>.
     * </summary>
     */
    public struct CellAgentPair : IComparable<CellAgentPair> {
        public int CellId;

        public int AgentIndex;

        public int CompareTo(CellAgentPair other) {
            int cellComparison = CellId.CompareTo(other.CellId);
            return cellComparison != 0 ? cellComparison : AgentIndex.CompareTo(other.AgentIndex);
        }
    }

    /**
     * <summary>
     * Computes an exclusive prefix sum over <see cref="Counts"/> into <see cref="Starts"/>,
     * and writes the final sum into <see cref="Total"/>[0].
     * </summary>
     */
    [BurstCompile]
    public struct ExclusivePrefixSumIntJob : IJob {
        [ReadOnly]
        public NativeArray<int> Counts;

        public NativeArray<int> Starts;

        public NativeArray<int> Total;

        public void Execute() {
            int runningTotal = 0;

            for (int index = 0; index < Counts.Length; index += 1) {
                Starts[index] = runningTotal;
                runningTotal += Counts[index];
            }

            Total[0] = runningTotal;
        }
    }

    /**
     * <summary>
     * Expands per-agent cell assignments into a flat <see cref="CellAgentPair"/> array.
     * </summary>
     */
    [BurstCompile]
    public struct FillCellAgentPairsJob : IJobParallelFor {
        [ReadOnly]
        public int MaxCellsPerAgent;

        [ReadOnly]
        public NativeArray<int> AgentCellCounts;

        [ReadOnly]
        public NativeArray<int> AgentCellIds;

        [ReadOnly]
        public NativeArray<int> AgentEntryStarts;

        [NativeDisableParallelForRestriction]
        public NativeArray<CellAgentPair> OutPairs;

        public void Execute(int agentIndex) {
            int cellCount = AgentCellCounts[agentIndex];

            if (cellCount <= 0) {
                return;
            }

            int baseCellOffset = agentIndex * MaxCellsPerAgent;
            int outputStartIndex = AgentEntryStarts[agentIndex];

            for (int offsetIndex = 0; offsetIndex < cellCount; offsetIndex += 1) {
                int cellId = AgentCellIds[baseCellOffset + offsetIndex];
                OutPairs[outputStartIndex + offsetIndex] = CreatePair(cellId, agentIndex);
            }
        }

        private static CellAgentPair CreatePair(int cellId, int agentIndex) {
            return new CellAgentPair {
                CellId = cellId,
                AgentIndex = agentIndex,
            };
        }
    }

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
