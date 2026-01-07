using Unity.Mathematics;

namespace Flock.Scripts.Build.Influence.Environment.Attractors.Data {
    /**
     * <summary>
     * Runtime representation of an attractor volume used by the simulation.
     * </summary>
     */
    public struct FlockAttractorData {
        public AttractorShape Shape;
        public float3 Position;
        public float Radius;
        public float3 BoxHalfExtents;
        public quaternion BoxRotation;
        public float BaseStrength;
        public float FalloffPower;
        public uint AffectedTypesMask;
        public AttractorUsage Usage;
        public float CellPriority;
        public float DepthMinNorm;
        public float DepthMaxNorm;
    }
}
