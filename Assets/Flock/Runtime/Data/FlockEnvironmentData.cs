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

        // Bounds steering configuration
        public float BoundsSoftThickness;       // how far from wall we start steering (world units)
        public float BoundsLookAheadTime;       // how far ahead we predict along velocity (seconds)
        public float BoundsSlideStrength;       // 0 = off, >0 = strength of bounds steering
        public float BoundsEdgeFlowSuppression; // 0..1, how much to suppress group flow near walls

        public float CellSize;
        public float3 GridOrigin;
        public int3 GridResolution;

        public float GlobalDamping;
    }
}
