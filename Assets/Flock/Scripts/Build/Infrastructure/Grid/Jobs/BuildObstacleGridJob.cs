using Flock.Runtime.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {

    /**
     * <summary>
     * Builds a cell-to-obstacle lookup by stamping each obstacle's broad-phase bounds into the grid.
     * </summary>
     */
    [BurstCompile]
    public struct BuildObstacleGridJob : IJobParallelFor {
        // Inputs
        [ReadOnly]
        public NativeArray<FlockObstacleData> Obstacles;

        [ReadOnly]
        public float3 GridOrigin;

        [ReadOnly]
        public int3 GridResolution;

        [ReadOnly]
        public float CellSize;

        // Outputs
        public NativeParallelMultiHashMap<int, int>.ParallelWriter CellToObstacles;

        public void Execute(int obstacleIndex) {
            float safeCellSize = math.max(CellSize, 0.0001f);

            float3 origin = GridOrigin;
            int3 resolution = GridResolution;

            float3 gridMinimum = origin;
            float3 gridMaximum = origin + (float3)resolution * safeCellSize;

            FlockObstacleData obstacle = Obstacles[obstacleIndex];

            float radius = math.max(0.0f, obstacle.Radius);
            radius = math.max(radius, safeCellSize * 0.5f);

            float3 obstacleMinimum = obstacle.Position - new float3(radius);
            float3 obstacleMaximum = obstacle.Position + new float3(radius);

            if (IsOutsideGridBounds(obstacleMinimum, obstacleMaximum, gridMinimum, gridMaximum)) {
                return;
            }

            int3 minimumCell = GetCellCoords(obstacleMinimum, origin, safeCellSize, resolution);
            int3 maximumCell = GetCellCoords(obstacleMaximum, origin, safeCellSize, resolution);

            StampCells(minimumCell, maximumCell, resolution, obstacleIndex);
        }

        private bool IsOutsideGridBounds(float3 minimum, float3 maximum, float3 gridMinimum, float3 gridMaximum) {
            return maximum.x < gridMinimum.x || minimum.x > gridMaximum.x
                   || maximum.y < gridMinimum.y || minimum.y > gridMaximum.y
                   || maximum.z < gridMinimum.z || minimum.z > gridMaximum.z;
        }

        private int3 GetCellCoords(float3 position, float3 origin, float cellSize, int3 resolution) {
            float3 local = (position - origin) / cellSize;

            int3 cell = (int3)math.floor(local);

            return math.clamp(
                cell,
                new int3(0, 0, 0),
                resolution - new int3(1, 1, 1));
        }

        private void StampCells(int3 minimumCell, int3 maximumCell, int3 resolution, int obstacleIndex) {
            int layerSize = resolution.x * resolution.y;

            for (int zIndex = minimumCell.z; zIndex <= maximumCell.z; zIndex += 1) {
                for (int yIndex = minimumCell.y; yIndex <= maximumCell.y; yIndex += 1) {
                    int rowBase = yIndex * resolution.x + zIndex * layerSize;

                    for (int xIndex = minimumCell.x; xIndex <= maximumCell.x; xIndex += 1) {
                        int cellIndex = xIndex + rowBase;
                        CellToObstacles.Add(cellIndex, obstacleIndex);
                    }
                }
            }
        }
    }
}
