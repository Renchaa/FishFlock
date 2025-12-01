// File: Assets/Flock/Runtime/Data/FlockBehaviourSettings.cs
namespace Flock.Runtime.Data {
    public struct FlockBehaviourSettings {
        public float MaxSpeed;
        public float MaxAcceleration;
        public float DesiredSpeed;

        public float NeighbourRadius;
        public float SeparationRadius;

        public float AlignmentWeight;
        public float CohesionWeight;
        public float SeparationWeight;

        public float InfluenceWeight;

        public float LeadershipWeight;
        public uint GroupMask;

        // Relationships / matrix
        public float AvoidanceWeight;
        public float NeutralWeight;
        public float AttractionWeight;
        public float AvoidResponse;

        public float MaxTurnRateDeg;
        public float TurnResponsiveness;

        public uint AvoidMask;
        public uint NeutralMask;

        // Grouping
        public int MinGroupSize;
        public int MaxGroupSize;
        public float GroupRadiusMultiplier;
        public float LonerRadiusMultiplier;
        public float LonerCohesionBoost;

        // Split
        public float SplitPanicThreshold;
        public float SplitLateralWeight;
        public float SplitAccelBoost;

        // Preferred depth
        public byte UsePreferredDepth;
        public float PreferredDepthMin;
        public float PreferredDepthMax;
        public float PreferredDepthMinNorm;
        public float PreferredDepthMaxNorm;
        public float PreferredDepthWeight;
        public float DepthBiasStrength;
        public byte DepthWinsOverAttractor;
        public float PreferredDepthEdgeFraction;

        // === NEW: single physical radius + radial zone tuning ===

        // Base size; author this per type in FishBehaviourProfile.
        public float BodyRadius;

        // Fractions of *pair radius* that define zones around contact.
        public float DeadBandFraction;        // jitter-killing shell
        public float FriendlyInnerFraction;   // extra inner shell for friends

        // Distance multipliers (in terms of pair radius).
        public float FriendDistanceFactor;    // where friends want to sit
        public float AvoidDistanceFactor;     // min distance from predators
        public float NeutralDistanceFactor;   // neutral spacing
        public float InfluenceDistanceFactor; // hard cutoff for neighbour effects

        // Radial gains (how strong each zone reacts).
        public float HardRepulsionGain;   // inside bodies
        public float FriendlySoftGain;    // near friends
        public float AvoidRadialGain;     // avoid relations
        public float NeutralRadialGain;   // neutral relations
    }
}
