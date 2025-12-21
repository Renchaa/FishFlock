namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct ClearMultiHashMapJob : IJob {
        public NativeParallelMultiHashMap<int, int> Map;

        public void Execute() {
            Map.Clear();
        }
    }

    [BurstCompile]
    public struct BuildObstacleGridJob : IJobParallelFor {
        [ReadOnly] public NativeArray<FlockObstacleData> Obstacles;

        public NativeParallelMultiHashMap<int, int>.ParallelWriter CellToObstacles;

        public float3 GridOrigin;
        public int3 GridResolution;
        public float CellSize;

        public void Execute(int obstacleIndex) {
            float cellSize = math.max(CellSize, 0.0001f);
            float3 origin = GridOrigin;
            int3 res = GridResolution;

            float3 gridMin = origin;
            float3 gridMax = origin + (float3)res * cellSize;

            FlockObstacleData o = Obstacles[obstacleIndex];

            float r = math.max(0.0f, o.Radius);
            r = math.max(r, cellSize * 0.5f);

            float3 minW = o.Position - new float3(r);
            float3 maxW = o.Position + new float3(r);

            // Reject if completely outside grid bounds
            if (maxW.x < gridMin.x || minW.x > gridMax.x ||
                maxW.y < gridMin.y || minW.y > gridMax.y ||
                maxW.z < gridMin.z || minW.z > gridMax.z) {
                return;
            }

            float3 minLocal = (minW - origin) / cellSize;
            float3 maxLocal = (maxW - origin) / cellSize;

            int3 minCell = (int3)math.floor(minLocal);
            int3 maxCell = (int3)math.floor(maxLocal);

            minCell = math.clamp(minCell, new int3(0, 0, 0), res - new int3(1, 1, 1));
            maxCell = math.clamp(maxCell, new int3(0, 0, 0), res - new int3(1, 1, 1));

            int layerSize = res.x * res.y;

            for (int z = minCell.z; z <= maxCell.z; z += 1) {
                for (int y = minCell.y; y <= maxCell.y; y += 1) {
                    int rowBase = y * res.x + z * layerSize;

                    for (int x = minCell.x; x <= maxCell.x; x += 1) {
                        int cellIndex = x + rowBase;
                        CellToObstacles.Add(cellIndex, obstacleIndex);
                    }
                }
            }
        }
    }
}
