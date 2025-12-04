// File: Assets/Flock/Runtime/Jobs/AssignToGridJob.cs
namespace Flock.Runtime.Jobs {
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct AssignToGridJob : IJobParallelFor {
        [ReadOnly]
        public NativeArray<float3> Positions;

        // NEW: per-agent behaviour and body radius
        [ReadOnly]
        public NativeArray<int> BehaviourIds;

        [ReadOnly]
        public NativeArray<float> BehaviourBodyRadius;

        [ReadOnly]
        public float CellSize;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        public NativeParallelMultiHashMap<int, int>.ParallelWriter CellToAgents;

        public void Execute(int index) {
            float3 position = Positions[index];

            int behaviourIndex = BehaviourIds[index];
            float bodyRadius = 0f;

            if ((uint)behaviourIndex < (uint)BehaviourBodyRadius.Length) {
                bodyRadius = math.max(0f, BehaviourBodyRadius[behaviourIndex]);
            }

            // If no body radius defined, fall back to a single cell
            if (bodyRadius <= 0f) {
                int cellId = GetCellIdFromPosition(position);
                CellToAgents.Add(cellId, index);
                return;
            }

            // World-space bounds of the fish body
            float3 min = position - bodyRadius;
            float3 max = position + bodyRadius;

            int3 minCell = GetCellCoords(min);
            int3 maxCell = GetCellCoords(max);

            // Occupy all grid cells the body touches
            for (int z = minCell.z; z <= maxCell.z; z++) {
                int zOffset = z * GridResolution.x * GridResolution.y;
                for (int y = minCell.y; y <= maxCell.y; y++) {
                    int rowOffset = zOffset + y * GridResolution.x;
                    for (int x = minCell.x; x <= maxCell.x; x++) {
                        int cellId = rowOffset + x;
                        CellToAgents.Add(cellId, index);
                    }
                }
            }
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

            int cellId = cell.x
                         + cell.y * GridResolution.x
                         + cell.z * GridResolution.x * GridResolution.y;

            return cellId;
        }
    }
}
