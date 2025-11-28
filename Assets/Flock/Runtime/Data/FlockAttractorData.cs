// ==========================================
// 1) NEW DATA TYPES (Runtime)
// File: Assets/Flock/Runtime/Data/FlockAttractorData.cs
// ==========================================
namespace Flock.Runtime.Data {
    using Unity.Mathematics;

    public struct FlockAttractorData {
        public FlockAttractorShape Shape;

        public float3 Position;

        // For sphere and broad-phase for box
        public float Radius;

        // Box-specific
        public float3 BoxHalfExtents;
        public quaternion BoxRotation;

        // Behaviour
        public float BaseStrength;
        public float FalloffPower;       // controls how quickly attraction fades from center to bounds
        public uint AffectedTypesMask;   // bit per behaviour index (same convention as GroupMask)

        public FlockAttractorUsage Usage;
        public float CellPriority;

        /// <summary>
        /// Normalised depth range of the attractor volume in environment bounds [0..1].
        /// 0 = bottom of bounds, 1 = top.
        /// </summary>
        public float DepthMinNorm;
        public float DepthMaxNorm;
    }
}
