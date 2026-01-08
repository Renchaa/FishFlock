using Flock.Scripts.Build.Influence.Environment.Attractors.Data;
using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

namespace Flock.Scripts.Build.Infrastructure.Grid.Jobs {

    /**
     * <summary>
     * Rebuilds per-cell attractor lookup tables, selecting the highest-priority attractor per cell
     * separately for Individual and Group usage.
     * </summary>
     */
    [BurstCompile]
    public struct RebuildAttractorGridJob : IJob {
        private const int InvalidIndex = -1;
        private const float MinimumCellSize = 0.0001f;

        [ReadOnly] 
        public NativeArray<FlockAttractorData> Attractors;

        [ReadOnly] 
        public int AttractorCount;

        public float3 GridOrigin;
        public int3 GridResolution;
        public float CellSize;

        public NativeArray<int> CellToIndividualAttractor;
        public NativeArray<int> CellToGroupAttractor;
        public NativeArray<float> CellIndividualPriority;
        public NativeArray<float> CellGroupPriority;

        public void Execute() {
            int3 gridResolution = GridResolution;
            int gridCellCount = gridResolution.x * gridResolution.y * gridResolution.z;

            if (gridCellCount <= 0) {
                return;
            }

            ResetCellMappings(gridCellCount);

            if (!Attractors.IsCreated || AttractorCount <= 0) {
                return;
            }

            float safeCellSize = math.max(CellSize, MinimumCellSize);
            float3 gridOrigin = GridOrigin;
            int layerCellCount = gridResolution.x * gridResolution.y;

            int3 minClamp = new int3(0, 0, 0);
            int3 maxClamp = gridResolution - new int3(1, 1, 1);

            for (int attractorIndex = 0; attractorIndex < AttractorCount; attractorIndex += 1) {
                FlockAttractorData attractorData = Attractors[attractorIndex];

                StampAttractorCells(
                    attractorIndex,
                    attractorData,
                    gridOrigin,
                    gridResolution,
                    layerCellCount,
                    safeCellSize,
                    minClamp,
                    maxClamp);
            }
        }

        private void ResetCellMappings(int gridCellCount) {
            for (int cellIndex = 0; cellIndex < gridCellCount; cellIndex += 1) {
                CellToIndividualAttractor[cellIndex] = InvalidIndex;
                CellToGroupAttractor[cellIndex] = InvalidIndex;

                CellIndividualPriority[cellIndex] = float.NegativeInfinity;
                CellGroupPriority[cellIndex] = float.NegativeInfinity;
            }
        }

        private void StampAttractorCells(
            int attractorIndex,
            FlockAttractorData attractorData,
            float3 gridOrigin,
            int3 gridResolution,
            int layerCellCount,
            float cellSize,
            int3 minClamp,
            int3 maxClamp) {

            float radius = math.max(attractorData.Radius, cellSize);

            float3 minPosition = attractorData.Position - new float3(radius);
            float3 maxPosition = attractorData.Position + new float3(radius);

            float3 minLocal = (minPosition - gridOrigin) / cellSize;
            float3 maxLocal = (maxPosition - gridOrigin) / cellSize;

            int3 minCell = math.clamp((int3)math.floor(minLocal), minClamp, maxClamp);
            int3 maxCell = math.clamp((int3)math.floor(maxLocal), minClamp, maxClamp);

            for (int cellZ = minCell.z; cellZ <= maxCell.z; cellZ += 1) {
                for (int cellY = minCell.y; cellY <= maxCell.y; cellY += 1) {
                    int rowBase = cellY * gridResolution.x + cellZ * layerCellCount;

                    for (int cellX = minCell.x; cellX <= maxCell.x; cellX += 1) {
                        int cellIndex = cellX + rowBase;
                        TryWriteCellWinner(attractorIndex, attractorData, cellIndex);
                    }
                }
            }
        }

        private void TryWriteCellWinner(int attractorIndex, FlockAttractorData attractorData, int cellIndex) {
            if (attractorData.Usage == AttractorUsage.Individual) {
                if (attractorData.CellPriority > CellIndividualPriority[cellIndex]) {
                    CellIndividualPriority[cellIndex] = attractorData.CellPriority;
                    CellToIndividualAttractor[cellIndex] = attractorIndex;
                }

                return;
            }

            if (attractorData.Usage == AttractorUsage.Group) {
                if (attractorData.CellPriority > CellGroupPriority[cellIndex]) {
                    CellGroupPriority[cellIndex] = attractorData.CellPriority;
                    CellToGroupAttractor[cellIndex] = attractorIndex;
                }
            }
        }
    }
}
