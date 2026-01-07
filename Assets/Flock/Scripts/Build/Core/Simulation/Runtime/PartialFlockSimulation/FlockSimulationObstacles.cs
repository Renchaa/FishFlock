namespace Flock.Scripts.Build.Core.Simulation.Runtime.PartialFlockSimulation {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using Unity.Collections;
    using Unity.Mathematics;

    /**
     * <summary>
     * Simulation runtime that manages native state for agents, grids, obstacles, and related steering data.
     * </summary>
     */
    public sealed partial class FlockSimulation {
        /**
         * <summary>
         * Queues a single obstacle data update to be applied to the simulation and marks the obstacle grid as dirty.
         * </summary>
         * <param name="index">Index of the obstacle to update.</param>
         * <param name="data">New obstacle data to apply.</param>
         */
        public void SetObstacleData(int index, FlockObstacleData data) {
            if (!IsCreated || !obstacles.IsCreated) {
                return;
            }

            if ((uint)index >= (uint)obstacles.Length) {
                return;
            }

            pendingObstacleChanges.Add(new Flock.Runtime.Jobs.IndexedObstacleChange {
                Index = index,
                Data = data,
            });

            obstacleGridDirty = true;
        }

        void AllocateObstacles(FlockObstacleData[] sourceObstacles, Allocator allocator) {
            if (!TryAllocateObstacleArray(sourceObstacles, allocator)) {
                return;
            }

            CopyObstacleArray(sourceObstacles);

            FlockLog.Info(
                logger,
                FlockLogCategory.Simulation,
                $"AllocateObstacles: created {obstacleCount} obstacles.",
                null);
        }

        void AllocateObstacleSimulationData(Allocator allocator) {
            obstacleSteering = new NativeArray<float3>(
                AgentCount,
                allocator,
                NativeArrayOptions.ClearMemory);

            if (!ShouldAllocateObstacleGrid()) {
                cellToObstacles = default;
                return;
            }

            GetObstacleGridParameters(out float cellSize, out float3 origin, out int3 resolution, out float3 gridMinimum, out float3 gridMaximum);

            int capacity = ComputeObstacleGridCapacity(cellSize, origin, resolution, gridMinimum, gridMaximum);

            cellToObstacles = new NativeParallelMultiHashMap<int, int>(
                capacity,
                allocator);
        }

        void BuildObstacleGrid() {
            if (!ShouldBuildObstacleGrid()) {
                return;
            }

            cellToObstacles.Clear();

            GetObstacleGridParameters(out float cellSize, out float3 origin, out int3 resolution, out float3 gridMinimum, out float3 gridMaximum);

            int layerSize = resolution.x * resolution.y;

            for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex += 1) {
                FlockObstacleData obstacleData = obstacles[obstacleIndex];

                if (!TryGetObstacleCellBounds(
                    obstacleData,
                    cellSize,
                    origin,
                    resolution,
                    gridMinimum,
                    gridMaximum,
                    out int3 minimumCell,
                    out int3 maximumCell)) {
                    continue;
                }

                AddObstacleToGrid(obstacleIndex, minimumCell, maximumCell, resolution, layerSize);
            }
        }

        bool TryAllocateObstacleArray(FlockObstacleData[] sourceObstacles, Allocator allocator) {
            if (sourceObstacles == null || sourceObstacles.Length == 0) {
                obstacleCount = 0;
                obstacles = default;

                FlockLog.Warning(
                    logger,
                    FlockLogCategory.Simulation,
                    "AllocateObstacles: source is null or empty. No obstacles will be used.",
                    null);

                return false;
            }

            obstacleCount = sourceObstacles.Length;

            obstacles = new NativeArray<FlockObstacleData>(
                obstacleCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            return true;
        }

        void CopyObstacleArray(FlockObstacleData[] sourceObstacles) {
            for (int index = 0; index < obstacleCount; index += 1) {
                obstacles[index] = sourceObstacles[index];
            }
        }

        bool ShouldAllocateObstacleGrid() {
            if (obstacleCount <= 0 || gridCellCount <= 0) {
                return false;
            }

            return obstacles.IsCreated;
        }

        bool ShouldBuildObstacleGrid() {
            if (obstacleCount <= 0 || gridCellCount <= 0) {
                return false;
            }

            return cellToObstacles.IsCreated && obstacles.IsCreated;
        }

        void GetObstacleGridParameters(
            out float cellSize,
            out float3 origin,
            out int3 resolution,
            out float3 gridMinimum,
            out float3 gridMaximum) {

            cellSize = math.max(environmentData.CellSize, 0.0001f);
            origin = environmentData.GridOrigin;
            resolution = environmentData.GridResolution;

            gridMinimum = origin;
            gridMaximum = origin + (float3)resolution * cellSize;
        }

        int ComputeObstacleGridCapacity(
            float cellSize,
            float3 origin,
            int3 resolution,
            float3 gridMinimum,
            float3 gridMaximum) {

            long capacityEstimate = 0;

            for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex += 1) {
                FlockObstacleData obstacleData = obstacles[obstacleIndex];

                if (!TryGetObstacleCellBounds(
                    obstacleData,
                    cellSize,
                    origin,
                    resolution,
                    gridMinimum,
                    gridMaximum,
                    out int3 minimumCell,
                    out int3 maximumCell)) {
                    continue;
                }

                long cellCountX = (long)(maximumCell.x - minimumCell.x + 1);
                long cellCountY = (long)(maximumCell.y - minimumCell.y + 1);
                long cellCountZ = (long)(maximumCell.z - minimumCell.z + 1);

                capacityEstimate += cellCountX * cellCountY * cellCountZ;
            }

            capacityEstimate = (long)(capacityEstimate * 1.25f) + 16;
            capacityEstimate = math.max(capacityEstimate, (long)obstacleCount * 4L);
            capacityEstimate = math.max(capacityEstimate, (long)gridCellCount);

            long clampedCapacity = math.min(capacityEstimate, (long)int.MaxValue);
            return (int)clampedCapacity;
        }

        bool TryGetObstacleCellBounds(
            in FlockObstacleData obstacleData,
            float cellSize,
            float3 origin,
            int3 resolution,
            float3 gridMinimum,
            float3 gridMaximum,
            out int3 minimumCell,
            out int3 maximumCell) {

            float radius = ComputeObstacleStampRadius(obstacleData, cellSize);

            float3 worldMinimum = obstacleData.Position - new float3(radius);
            float3 worldMaximum = obstacleData.Position + new float3(radius);

            if (!IntersectsGridBounds(worldMinimum, worldMaximum, gridMinimum, gridMaximum)) {
                minimumCell = default;
                maximumCell = default;
                return false;
            }

            float3 localMinimum = (worldMinimum - origin) / cellSize;
            float3 localMaximum = (worldMaximum - origin) / cellSize;

            minimumCell = (int3)math.floor(localMinimum);
            maximumCell = (int3)math.floor(localMaximum);

            int3 minimumIndex = new int3(0, 0, 0);
            int3 maximumIndex = resolution - new int3(1, 1, 1);

            minimumCell = math.clamp(minimumCell, minimumIndex, maximumIndex);
            maximumCell = math.clamp(maximumCell, minimumIndex, maximumIndex);

            return true;
        }

        float ComputeObstacleStampRadius(in FlockObstacleData obstacleData, float cellSize) {
            float radius = math.max(0.0f, obstacleData.Radius);
            return math.max(radius, cellSize * 0.5f);
        }

        static bool IntersectsGridBounds(float3 worldMinimum, float3 worldMaximum, float3 gridMinimum, float3 gridMaximum) {
            if (worldMaximum.x < gridMinimum.x || worldMinimum.x > gridMaximum.x) {
                return false;
            }

            if (worldMaximum.y < gridMinimum.y || worldMinimum.y > gridMaximum.y) {
                return false;
            }

            if (worldMaximum.z < gridMinimum.z || worldMinimum.z > gridMaximum.z) {
                return false;
            }

            return true;
        }

        void AddObstacleToGrid(int obstacleIndex, int3 minimumCell, int3 maximumCell, int3 resolution, int layerSize) {
            for (int zIndex = minimumCell.z; zIndex <= maximumCell.z; zIndex += 1) {
                AddObstacleToGridLayer(obstacleIndex, minimumCell, maximumCell, resolution, layerSize, zIndex);
            }
        }

        void AddObstacleToGridLayer(
            int obstacleIndex,
            int3 minimumCell,
            int3 maximumCell,
            int3 resolution,
            int layerSize,
            int zIndex) {

            for (int yIndex = minimumCell.y; yIndex <= maximumCell.y; yIndex += 1) {
                int rowBase = (yIndex * resolution.x) + (zIndex * layerSize);
                AddObstacleToGridRow(obstacleIndex, minimumCell.x, maximumCell.x, rowBase);
            }
        }

        void AddObstacleToGridRow(int obstacleIndex, int minimumX, int maximumX, int rowBase) {
            for (int xIndex = minimumX; xIndex <= maximumX; xIndex += 1) {
                int cellIndex = xIndex + rowBase;
                cellToObstacles.Add(cellIndex, obstacleIndex);
            }
        }
    }
}
