// File: Assets/Flock/Runtime/Data/FlockEnvironmentData.cs
namespace Flock.Runtime.Data {
    using Unity.Mathematics;

    public struct FlockEnvironmentData {
        public FlockBoundsType BoundsType;
        public float3 BoundsCenter;
        public float3 BoundsExtents;
        public float BoundsRadius;

        public float BoundsSoftThickness;   // how far from wall we start steering (world units)
        public float BoundsLookAheadTime;   // how far ahead we predict along velocity (seconds)
        public float BoundsSlideStrength;   // 0 = off, 1..3 = stronger wall component kill

        public float CellSize;
        public float3 GridOrigin;
        public int3 GridResolution;

        public float GlobalDamping;
    }
}
