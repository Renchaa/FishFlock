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

        [ReadOnly]
        public float CellSize;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        public NativeParallelMultiHashMap<int, int>.ParallelWriter CellToAgents;

        public void Execute(int index) {
            float3 position = Positions[index];
            int cellId = GetCellId(position);

            CellToAgents.Add(cellId, index);
        }

        int GetCellId(float3 position) {
            float3 local = position - GridOrigin;
            float3 scaled = local / CellSize;

            int3 cell = (int3)math.floor(scaled);

            cell = math.clamp(
                cell,
                new int3(0, 0, 0),
                GridResolution - new int3(1, 1, 1));

            int cellId = cell.x
                         + cell.y * GridResolution.x
                         + cell.z * GridResolution.x * GridResolution.y;

            return cellId;
        }
    }
}
