using Unity.Mathematics;

namespace Flock.Runtime.Data {
    /**
     * <summary>
     * Runtime representation of an attractor volume used by the simulation.
     * </summary>
     */
    public struct FlockAttractorData {
        /**
         * <summary>
         * Gets or sets the attractor shape.
         * </summary>
         */
        public FlockAttractorShape Shape;

        /**
         * <summary>
         * Gets or sets the world-space position of the attractor.
         * </summary>
         */
        public float3 Position;

        /**
         * <summary>
         * Gets or sets the sphere radius, and the broad-phase radius for box shapes.
         * </summary>
         */
        public float Radius;

        /**
         * <summary>
         * Gets or sets the half-extents for box shapes in world units.
         * </summary>
         */
        public float3 BoxHalfExtents;

        /**
         * <summary>
         * Gets or sets the rotation for box shapes.
         * </summary>
         */
        public quaternion BoxRotation;

        /**
         * <summary>
         * Gets or sets the base attraction strength.
         * </summary>
         */
        public float BaseStrength;

        /**
         * <summary>
         * Gets or sets the falloff exponent controlling how quickly attraction fades from center to bounds.
         * </summary>
         */
        public float FalloffPower;

        /**
         * <summary>
         * Gets or sets the bitmask of affected behaviour indices (same convention as GroupMask).
         * </summary>
         */
        public uint AffectedTypesMask;

        /**
         * <summary>
         * Gets or sets how this attractor is used by the simulation (individual or group-level).
         * </summary>
         */
        public FlockAttractorUsage Usage;

        /**
         * <summary>
         * Gets or sets the priority used when multiple attractors overlap the same grid cells (higher wins).
         * </summary>
         */
        public float CellPriority;

        /**
         * <summary>
         * Gets or sets the normalised minimum depth of the attractor volume in environment bounds [0..1],
         * where 0 is the bottom of bounds and 1 is the top.
         * </summary>
         */
        public float DepthMinNorm;

        /**
         * <summary>
         * Gets or sets the normalised maximum depth of the attractor volume in environment bounds [0..1],
         * where 0 is the bottom of bounds and 1 is the top.
         * </summary>
         */
        public float DepthMaxNorm;
    }
}
