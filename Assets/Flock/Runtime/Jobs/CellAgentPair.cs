namespace Flock.Runtime.Jobs {
    using System;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;

    // Sorted by CellId (then AgentIndex)
    public struct CellAgentPair : IComparable<CellAgentPair> {
        public int CellId;
        public int AgentIndex;

        public int CompareTo(CellAgentPair other) {
            int c = CellId.CompareTo(other.CellId);
            return c != 0 ? c : AgentIndex.CompareTo(other.AgentIndex);
        }
    }

    [BurstCompile]
    public struct ExclusivePrefixSumIntJob : IJob {
        [ReadOnly] public NativeArray<int> Counts; // length N
        public NativeArray<int> Starts;            // length N
        public NativeArray<int> Total;             // length 1

        public void Execute() {
            int running = 0;

            for (int i = 0; i < Counts.Length; i += 1) {
                Starts[i] = running;
                running += Counts[i];
            }

            Total[0] = running;
        }
    }

    [BurstCompile]
    public struct FillCellAgentPairsJob : IJobParallelFor {
        [ReadOnly] public int MaxCellsPerAgent;

        [ReadOnly] public NativeArray<int> AgentCellCounts;
        [ReadOnly] public NativeArray<int> AgentCellIds;
        [ReadOnly] public NativeArray<int> AgentEntryStarts;

        [NativeDisableParallelForRestriction]
        public NativeArray<CellAgentPair> OutPairs;

        public void Execute(int agentIndex) {
            int count = AgentCellCounts[agentIndex];
            if (count <= 0) {
                return;
            }

            int baseCellOffset = agentIndex * MaxCellsPerAgent;
            int outStart = AgentEntryStarts[agentIndex];

            for (int i = 0; i < count; i += 1) {
                int cellId = AgentCellIds[baseCellOffset + i];
                OutPairs[outStart + i] = new CellAgentPair {
                    CellId = cellId,
                    AgentIndex = agentIndex,
                };
            }
        }
    }

    [BurstCompile]
    public struct SortCellAgentPairsJob : IJob {
        public NativeArray<CellAgentPair> Pairs;
        [ReadOnly] public NativeArray<int> Total; // length 1

        public void Execute() {
            int n = Total[0];
            if (n <= 1) {
                return;
            }

            var slice = new NativeSlice<CellAgentPair>(Pairs, 0, n);
            NativeSortExtension.Sort(slice);
        }
    }

    [BurstCompile]
    public struct BuildCellAgentRangesJob : IJob {
        [ReadOnly] public NativeArray<CellAgentPair> Pairs;
        [ReadOnly] public NativeArray<int> Total; // length 1

        public NativeArray<int> CellStarts;
        public NativeArray<int> CellCounts;

        public NativeArray<int> TouchedCells;
        public NativeArray<int> TouchedCount; // length 1

        public void Execute() {
            int n = Total[0];
            int touched = 0;

            if (n <= 0) {
                TouchedCount[0] = 0;
                return;
            }

            int runCell = Pairs[0].CellId;
            int runStart = 0;
            int runLen = 1;

            for (int i = 1; i < n; i += 1) {
                int cell = Pairs[i].CellId;

                if (cell == runCell) {
                    runLen += 1;
                    continue;
                }

                CellStarts[runCell] = runStart;
                CellCounts[runCell] = runLen;

                TouchedCells[touched] = runCell;
                touched += 1;

                runCell = cell;
                runStart = i;
                runLen = 1;
            }

            CellStarts[runCell] = runStart;
            CellCounts[runCell] = runLen;

            TouchedCells[touched] = runCell;
            touched += 1;

            TouchedCount[0] = touched;
        }
    }

    [BurstCompile]
    public struct ClearTouchedAgentCellsJob : IJob {
        [ReadOnly] public NativeArray<int> TouchedCells;
        public NativeArray<int> TouchedCount; // length 1

        [NativeDisableParallelForRestriction]
        public NativeArray<int> CellStarts;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> CellCounts;

        public void Execute() {
            int n = TouchedCount[0];

            for (int i = 0; i < n; i += 1) {
                int cellId = TouchedCells[i];
                CellStarts[cellId] = -1;
                CellCounts[cellId] = 0;
            }

            TouchedCount[0] = 0;
        }
    }
}
