namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using Flock.Runtime.Logging;
    using Unity.Collections;
    using Unity.Mathematics;

    public sealed partial class FlockSimulation {
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

        void AllocateObstacles(FlockObstacleData[] source, Allocator allocator) {
            if (source == null || source.Length == 0) {
                obstacleCount = 0;
                obstacles = default;

                FlockLog.Warning(
                    logger,
                    FlockLogCategory.Simulation,
                    "AllocateObstacles: source is null or empty. No obstacles will be used.",
                    null);

                return;
            }

            obstacleCount = source.Length;

            obstacles = new NativeArray<FlockObstacleData>(
                obstacleCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            for (int index = 0; index < obstacleCount; index += 1) {
                obstacles[index] = source[index];
            }

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

            if (obstacleCount <= 0 || gridCellCount <= 0 || !obstacles.IsCreated) {
                cellToObstacles = default;
                return;
            }

            float cellSize = math.max(environmentData.CellSize, 0.0001f);
            float3 origin = environmentData.GridOrigin;
            int3 res = environmentData.GridResolution;

            float3 gridMin = origin;
            float3 gridMax = origin + (float3)res * cellSize;

            long cap = 0;

            for (int i = 0; i < obstacleCount; i += 1) {
                FlockObstacleData o = obstacles[i];

                float r = math.max(0.0f, o.Radius);
                r = math.max(r, cellSize * 0.5f);

                float3 minW = o.Position - new float3(r);
                float3 maxW = o.Position + new float3(r);

                if (maxW.x < gridMin.x || minW.x > gridMax.x ||
                    maxW.y < gridMin.y || minW.y > gridMax.y ||
                    maxW.z < gridMin.z || minW.z > gridMax.z) {
                    continue;
                }

                float3 minLocal = (minW - origin) / cellSize;
                float3 maxLocal = (maxW - origin) / cellSize;

                int3 minCell = (int3)math.floor(minLocal);
                int3 maxCell = (int3)math.floor(maxLocal);

                minCell = math.clamp(minCell, new int3(0, 0, 0), res - new int3(1, 1, 1));
                maxCell = math.clamp(maxCell, new int3(0, 0, 0), res - new int3(1, 1, 1));

                long cx = (long)(maxCell.x - minCell.x + 1);
                long cy = (long)(maxCell.y - minCell.y + 1);
                long cz = (long)(maxCell.z - minCell.z + 1);

                cap += cx * cy * cz;
            }

            cap = (long)(cap * 1.25f) + 16;
            cap = math.max(cap, (long)obstacleCount * 4L);
            cap = math.max(cap, (long)gridCellCount);

            int capacity = (int)math.min(cap, (long)int.MaxValue);

            cellToObstacles = new NativeParallelMultiHashMap<int, int>(
                capacity,
                allocator);
        }

        void BuildObstacleGrid() {
            if (!cellToObstacles.IsCreated || !obstacles.IsCreated || obstacleCount <= 0 || gridCellCount <= 0) {
                return;
            }

            cellToObstacles.Clear();

            float cellSize = math.max(environmentData.CellSize, 0.0001f);
            float3 origin = environmentData.GridOrigin;
            int3 res = environmentData.GridResolution;

            float3 gridMin = origin;
            float3 gridMax = origin + (float3)res * cellSize;

            int layerSize = res.x * res.y;

            for (int index = 0; index < obstacleCount; index += 1) {
                FlockObstacleData o = obstacles[index];

                float r = math.max(0.0f, o.Radius);
                r = math.max(r, cellSize * 0.5f);

                float3 minW = o.Position - new float3(r);
                float3 maxW = o.Position + new float3(r);

                if (maxW.x < gridMin.x || minW.x > gridMax.x ||
                    maxW.y < gridMin.y || minW.y > gridMax.y ||
                    maxW.z < gridMin.z || minW.z > gridMax.z) {
                    continue;
                }

                float3 minLocal = (minW - origin) / cellSize;
                float3 maxLocal = (maxW - origin) / cellSize;

                int3 minCell = (int3)math.floor(minLocal);
                int3 maxCell = (int3)math.floor(maxLocal);

                minCell = math.clamp(minCell, new int3(0, 0, 0), res - new int3(1, 1, 1));
                maxCell = math.clamp(maxCell, new int3(0, 0, 0), res - new int3(1, 1, 1));

                for (int z = minCell.z; z <= maxCell.z; z += 1) {
                    for (int y = minCell.y; y <= maxCell.y; y += 1) {
                        int rowBase = y * res.x + z * layerSize;

                        for (int x = minCell.x; x <= maxCell.x; x += 1) {
                            int cellIndex = x + rowBase;
                            cellToObstacles.Add(cellIndex, index);
                        }
                    }
                }
            }
        }
    }
}
