using Flock.Scripts.Build.Infrastructure.Grid.Data;

using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs
{
    /**
     * <summary>
     * Expands per-agent cell assignments into a flat <see cref="CellAgentPair"/> array.
     * </summary>
     */
    [BurstCompile]
    public struct FillCellAgentPairsJob : IJobParallelFor
    {
        [ReadOnly] public int MaxCellsPerAgent;

        [ReadOnly] public NativeArray<int> AgentCellCounts;
        [ReadOnly] public NativeArray<int> AgentCellIds;
        [ReadOnly] public NativeArray<int> AgentEntryStarts;

        [NativeDisableParallelForRestriction] public NativeArray<CellAgentPair> OutPairs;

        public void Execute(int agentIndex)
        {
            int cellCount = AgentCellCounts[agentIndex];

            if (cellCount <= 0)
            {
                return;
            }

            int baseCellOffset = agentIndex * MaxCellsPerAgent;
            int outputStartIndex = AgentEntryStarts[agentIndex];

            for (int offsetIndex = 0; offsetIndex < cellCount; offsetIndex += 1)
            {
                int cellId = AgentCellIds[baseCellOffset + offsetIndex];
                OutPairs[outputStartIndex + offsetIndex] = CreatePair(cellId, agentIndex);
            }
        }

        private static CellAgentPair CreatePair(int cellId, int agentIndex)
        {
            return new CellAgentPair
            {
                CellId = cellId,
                AgentIndex = agentIndex,
            };
        }
    }
}