namespace Flock.Runtime.Jobs {
    using Flock.Runtime.Data;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;

    [BurstCompile]
    public struct RebuildAttractorGridJob : IJob {
        [ReadOnly] public NativeArray<FlockAttractorData> Attractors;
        public int AttractorCount;

        public float3 GridOrigin;
        public int3 GridResolution;
        public float CellSize;

        public NativeArray<int> CellToIndividualAttractor;
        public NativeArray<int> CellToGroupAttractor;
        public NativeArray<float> CellIndividualPriority;
        public NativeArray<float> CellGroupPriority;

        public void Execute() {
            int3 res = GridResolution;
            int gridCellCount = res.x * res.y * res.z;

            if (gridCellCount <= 0) {
                return;
            }

            // Reset per-cell mappings
            for (int i = 0; i < gridCellCount; i += 1) {
                CellToIndividualAttractor[i] = -1;
                CellToGroupAttractor[i] = -1;
                CellIndividualPriority[i] = float.NegativeInfinity;
                CellGroupPriority[i] = float.NegativeInfinity;
            }

            if (!Attractors.IsCreated || AttractorCount <= 0) {
                return;
            }

            float cellSize = math.max(CellSize, 0.0001f);
            float3 origin = GridOrigin;
            int layerSize = res.x * res.y;

            for (int index = 0; index < AttractorCount; index += 1) {
                FlockAttractorData data = Attractors[index];

                float radius = math.max(data.Radius, cellSize);
                float3 minPos = data.Position - new float3(radius);
                float3 maxPos = data.Position + new float3(radius);

                float3 minLocal = (minPos - origin) / cellSize;
                float3 maxLocal = (maxPos - origin) / cellSize;

                int3 minCell = (int3)math.floor(minLocal);
                int3 maxCell = (int3)math.floor(maxLocal);

                int3 minClamp = new int3(0, 0, 0);
                int3 maxClamp = res - new int3(1, 1, 1);

                minCell = math.clamp(minCell, minClamp, maxClamp);
                maxCell = math.clamp(maxCell, minClamp, maxClamp);

                for (int z = minCell.z; z <= maxCell.z; z += 1) {
                    for (int y = minCell.y; y <= maxCell.y; y += 1) {
                        int rowBase = y * res.x + z * layerSize;

                        for (int x = minCell.x; x <= maxCell.x; x += 1) {
                            int cellIndex = x + rowBase;

                            if (data.Usage == FlockAttractorUsage.Individual) {
                                if (data.CellPriority > CellIndividualPriority[cellIndex]) {
                                    CellIndividualPriority[cellIndex] = data.CellPriority;
                                    CellToIndividualAttractor[cellIndex] = index;
                                }
                            } else if (data.Usage == FlockAttractorUsage.Group) {
                                if (data.CellPriority > CellGroupPriority[cellIndex]) {
                                    CellGroupPriority[cellIndex] = data.CellPriority;
                                    CellToGroupAttractor[cellIndex] = index;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
