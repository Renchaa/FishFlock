using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Environment.Attractors.Data
{
    /**
     * <summary>
     * Runtime representation of an attractor volume used by the simulation.
     * </summary>
     */
    public struct FlockAttractorData
    {
        // Shape / geometry.
        public FlockAttractorShape Shape;
        public float3 Position;
        public quaternion BoxRotation;
        public float Radius;
        public float3 BoxHalfExtents;

        // Strength / falloff.
        public float BaseStrength;
        public float FalloffPower;

        // Filters / usage.
        public uint AffectedTypesMask;
        public AttractorUsage Usage;

        // Grid / selection priority.
        public float CellPriority;

        // Depth constraints (normalized 0..1).
        public float DepthMinNorm;
        public float DepthMaxNorm;
    }
}
