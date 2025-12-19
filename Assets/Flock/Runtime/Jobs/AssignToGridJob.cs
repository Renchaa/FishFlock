namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct AssignToGridJob : IJobParallelFor {
        [ReadOnly] public NativeArray<float3> Positions;

        [ReadOnly] public NativeArray<int> BehaviourIds;
        [ReadOnly] public NativeArray<float> BehaviourBodyRadius;

        [ReadOnly] public float CellSize;
        [ReadOnly] public float3 GridOrigin;
        [ReadOnly] public int3 GridResolution;

        [ReadOnly] public int MaxCellsPerAgent;

        [NativeDisableParallelForRestriction]
        public NativeArray<int> AgentCellCounts; // length = agent count

        [NativeDisableParallelForRestriction]
        public NativeArray<int> AgentCellIds;    // length = agent count * MaxCellsPerAgent

        public void Execute(int index) {
            float3 position = Positions[index];

            int behaviourIndex = BehaviourIds[index];
            float bodyRadius = 0f;

            if ((uint)behaviourIndex < (uint)BehaviourBodyRadius.Length) {
                bodyRadius = math.max(0f, BehaviourBodyRadius[behaviourIndex]);
            }

            int baseOffset = index * MaxCellsPerAgent;

            if (bodyRadius <= 0f) {
                AgentCellIds[baseOffset] = GetCellIdFromPosition(position);
                AgentCellCounts[index] = 1;
                return;
            }

            float3 min = position - bodyRadius;
            float3 max = position + bodyRadius;

            int3 minCell = GetCellCoords(min);
            int3 maxCell = GetCellCoords(max);

            int write = 0;

            for (int z = minCell.z; z <= maxCell.z; z++) {
                int zOffset = z * GridResolution.x * GridResolution.y;

                for (int y = minCell.y; y <= maxCell.y; y++) {
                    int rowOffset = zOffset + y * GridResolution.x;

                    for (int x = minCell.x; x <= maxCell.x; x++) {
                        if (write >= MaxCellsPerAgent) {
                            AgentCellCounts[index] = write;
                            return;
                        }

                        AgentCellIds[baseOffset + write] = rowOffset + x;
                        write += 1;
                    }
                }
            }

            AgentCellCounts[index] = write;
        }

        int3 GetCellCoords(float3 position) {
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

        int GetCellIdFromPosition(float3 position) {
            int3 cell = GetCellCoords(position);

            return cell.x
                   + cell.y * GridResolution.x
                   + cell.z * GridResolution.x * GridResolution.y;
        }
    }
}
