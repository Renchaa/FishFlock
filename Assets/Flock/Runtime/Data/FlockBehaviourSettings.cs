namespace Flock.Runtime.Data {
    public struct FlockBehaviourSettings {
        public float MaxSpeed;
        public float MaxAcceleration;
        public float DesiredSpeed;

        public float NeighbourRadius;
        public float SeparationRadius;

        // Cross-type relationship weights
        public float AvoidanceWeight;   // dominance / threat hierarchy for Avoid
        public float NeutralWeight;     // soft priority for Neutral
        public float AttractionWeight;  // how strongly this type responds to attractor areas
        public float AvoidResponse;     // how “panicked” this type gets when avoiding (0 = chill, 1+ = very reactive)


        public float SplitPanicThreshold;  // panic level at which this type starts splitting
        public float SplitLateralWeight;   // how wide it fans out left/right when splitting
        public float SplitAccelBoost;      // how much extra accel/speed during split burst

        public uint AvoidMask;          // bitmask of types this behaviour avoids
        public uint NeutralMask;        // bitmask of types treated as Neutral

        // Standard flocking rule weights
        public float AlignmentWeight;
        public float CohesionWeight;
        public float SeparationWeight;

        public float InfluenceWeight;

        // Leadership / following
        public float LeadershipWeight;
        public uint GroupMask;

        public int MinGroupSize;
        public int MaxGroupSize;

        public float GroupRadiusMultiplier;   // radius factor when in a group
        public float LonerRadiusMultiplier;   // radius factor when under-grouped / lonely
        public float LonerCohesionBoost;      // extra cohesion when lonely (magnet to school)

        /// <summary>
        /// 0 = disabled, 1 = enabled.
        /// </summary>
        public byte UsePreferredDepth;

        /// <summary>
        /// Normalised depth band [0..1] in world bounds (0 = bottom, 1 = top).
        /// </summary>
        public float PreferredDepthMin;
        public float PreferredDepthMax;

        /// <summary>
        /// Strength of vertical bias toward the preferred band.
        /// </summary>
        public float DepthBiasStrength;

        /// <summary>
        /// 0 = attraction wins when they conflict, 1 = depth wins.
        /// </summary>
        public byte DepthWinsOverAttractor;

        // Preferred depth band [0..1] in environment bounds (0 = bottom, 1 = top)
        public float PreferredDepthMinNorm;
        public float PreferredDepthMaxNorm;

        // Strength of depth steering. <= 0 means "disabled".
        public float PreferredDepthWeight;

        public float PreferredDepthEdgeFraction;
    }
}
