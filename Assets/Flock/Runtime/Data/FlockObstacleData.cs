// File: Assets/Flock/Runtime/Data/FlockObstacleData.cs
namespace Flock.Runtime.Data {
    using Unity.Mathematics;

    public struct FlockObstacleData {
        // Shape type for branching in jobs
        public FlockObstacleShape Shape;

        // Common
        public float3 Position;

        // Used as:
        // - Sphere radius (for Sphere)
        // - Bounding sphere radius (for Box) for cheap broad-phase
        public float Radius;

        // Box data (used only when Shape == Box)
        public float3 BoxHalfExtents;
        public quaternion BoxRotation;
    }
}
