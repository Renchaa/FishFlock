using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {
    /**
     * <summary>
     * Assigns each agent to one or more grid cells based on its position and body radius.
     * </summary>
     */
    [BurstCompile]
    public struct AssignToGridJob : IJobParallelFor {
        // Inputs
        [ReadOnly]
        public NativeArray<float3> Positions;

        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<FlockBehaviourSettings> BehaviourSettings;

        [ReadOnly]
        public float CellSize;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public int MaxCellsPerAgent;

        // Outputs
        [NativeDisableParallelForRestriction]
        public NativeArray<int> AgentCellCounts;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> AgentCellIds;

        public void Execute(int index) {
            float3 position = Positions[index];

            int behaviourIndex = BehaviourIds[index];
            float bodyRadius = 0f;

            if ((uint)behaviourIndex < (uint)BehaviourSettings.Length) {
                bodyRadius = math.max(0f, BehaviourSettings[behaviourIndex].BodyRadius);
            }

            int baseOffset = index * MaxCellsPerAgent;

            if (bodyRadius <= 0f) {
                AgentCellIds[baseOffset] = GetCellIdFromPosition(position);
                AgentCellCounts[index] = 1;
                return;
            }

            float3 minimumPosition = position - bodyRadius;
            float3 maximumPosition = position + bodyRadius;

            int3 minimumCell = GetCellCoords(minimumPosition);
            int3 maximumCell = GetCellCoords(maximumPosition);

            int writeIndex = 0;

            for (int zIndex = minimumCell.z; zIndex <= maximumCell.z; zIndex += 1) {
                int zOffset = zIndex * GridResolution.x * GridResolution.y;

                for (int yIndex = minimumCell.y; yIndex <= maximumCell.y; yIndex += 1) {
                    int rowOffset = zOffset + yIndex * GridResolution.x;

                    for (int xIndex = minimumCell.x; xIndex <= maximumCell.x; xIndex += 1) {
                        if (writeIndex >= MaxCellsPerAgent) {
                            AgentCellCounts[index] = writeIndex;
                            return;
                        }

                        AgentCellIds[baseOffset + writeIndex] = rowOffset + xIndex;
                        writeIndex += 1;
                    }
                }
            }

            AgentCellCounts[index] = writeIndex;
        }

        private int3 GetCellCoords(float3 position) {
            float safeCellSize = math.max(CellSize, 0.0001f);
            float3 local = position - GridOrigin;
            float3 scaled = local / safeCellSize;

            int3 cell = (int3)math.floor(scaled);

            cell = math.clamp(
                cell,
                new int3(0, 0, 0),
                GridResolution - new int3(1, 1, 1));

            return cell;
        }

        private int GetCellIdFromPosition(float3 position) {
            int3 cell = GetCellCoords(position);

            return cell.x
                   + cell.y * GridResolution.x
                   + cell.z * GridResolution.x * GridResolution.y;
        }
    }
}
