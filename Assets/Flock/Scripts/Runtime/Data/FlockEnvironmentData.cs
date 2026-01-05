using Unity.Mathematics;

namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Runtime environment snapshot used by the simulation and jobs.
     * </summary>
     */
    public struct FlockEnvironmentData {
        // Bounds
        public FlockBoundsType BoundsType;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public float BoundsRadius;

        // Grid
        public float CellSize;
        public float3 GridOrigin;
        public int3 GridResolution;

        // Global
        public float GlobalDamping;

        // Group Noise
        public FlockGroupNoisePatternSettings GroupNoisePattern;
    }
}
