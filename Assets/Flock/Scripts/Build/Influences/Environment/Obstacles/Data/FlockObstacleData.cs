using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Environment.Obstacles.Data
{
    /**
     * <summary>
     * Runtime representation of an obstacle used by the simulation.
     * </summary>
     */
    public struct FlockObstacleData
    {
        // Shape
        public FlockObstacleShape Shape;

        // Common
        public float3 Position;
        public float Radius;

        // Box
        public float3 BoxHalfExtents;
        public quaternion BoxRotation;
    }
}
