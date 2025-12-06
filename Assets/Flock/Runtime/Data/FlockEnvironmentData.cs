// ==============================
// 1) FlockEnvironmentData.cs
// REPLACE the struct body with this (small struct, safe to paste as-is)
// ==============================
namespace Flock.Runtime.Data {
    using Unity.Mathematics;

    public struct FlockEnvironmentData {
        public FlockBoundsType BoundsType;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public float BoundsRadius;

        public float CellSize;
        public float3 GridOrigin;
        public int3 GridResolution;

        public float GlobalDamping;

        public  FlockGroupNoisePatternSettings GroupNoisePattern;
    }
}
