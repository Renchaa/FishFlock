namespace Flock.Scripts.Build.Agents.Fish.Data
{
    /**
     * <summary>
     * Runtime behaviour settings snapshot for a single fish type.
     * </summary>
     */
    public struct FlockBehaviourSettings
    {
        // Movement / Flow
        public float MaxSpeed;
        public float MaxAcceleration;
        public float DesiredSpeed;
        public float GroupFlowWeight;

        // Bounds
        public float BoundsWeight;
        public float BoundsTangentialDamping;
        public float BoundsInfluenceSuppression;

        // Group Size Constraints
        public float MinGroupSizeWeight;
        public float MaxGroupSizeWeight;

        // Neighbourhood / Physical Size
        public float NeighbourRadius;
        public float SeparationRadius;
        public float BodyRadius;

        // Cross-Type Relationships
        public float AvoidanceWeight;
        public float NeutralWeight;
        public float AttractionWeight;
        public float AvoidResponse;

        // Split Behaviour
        public float SplitPanicThreshold;
        public float SplitLateralWeight;
        public float SplitAccelBoost;

        // Relationship Masks
        public uint AvoidMask;
        public uint NeutralMask;

        // Core Flocking Weights
        public float AlignmentWeight;
        public float CohesionWeight;
        public float SeparationWeight;

        public float InfluenceWeight;

        // Schooling / Distance Band
        public float SchoolingSpacingFactor;
        public float SchoolingOuterFactor;
        public float SchoolingStrength;
        public float SchoolingInnerSoftness;
        public float SchoolingDeadzoneFraction;

        // Leadership / Grouping Identity
        public float LeadershipWeight;
        public uint GroupMask;

        // Grouping Behaviour
        public int MinGroupSize;
        public int MaxGroupSize;

        public float GroupRadiusMultiplier;
        public float LonerRadiusMultiplier;
        public float LonerCohesionBoost;

        // Preferred Depth
        public byte UsePreferredDepth;
        public float PreferredDepthMin;
        public float PreferredDepthMax;

        public float DepthBiasStrength;
        public byte DepthWinsOverAttractor;

        public float PreferredDepthMinNorm;
        public float PreferredDepthMaxNorm;

        public float PreferredDepthWeight;
        public float PreferredDepthEdgeFraction;

        // Schooling Damping
        public float SchoolingRadialDamping;

        // Wander / Noise / Patterns
        public float WanderStrength;
        public float WanderFrequency;

        public float GroupNoiseStrength;
        public float PatternWeight;

        public float GroupNoiseDirectionRate;
        public float GroupNoiseSpeedWeight;

        // Performance Caps
        public int MaxNeighbourChecks;
        public int MaxFriendlySamples;
        public int MaxSeparationSamples;
    }
}
