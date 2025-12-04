// ==========================================
// 4) FishBehaviourProfile – ADD serialized fields + ToSettings
// File: Assets/Flock/Runtime/FishBehaviourProfile.cs
// ==========================================
namespace Flock.Runtime {
    using Flock.Runtime.Data;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "FishBehaviourProfile",
        menuName = "Flock/Fish Behaviour Profile")]
    public sealed class FishBehaviourProfile : ScriptableObject {
        [Header("Movement")]
        [SerializeField] float maxSpeed = 5.0f;
        [SerializeField] float maxAcceleration = 10.0f;
        [SerializeField] float desiredSpeed = 3.0f;

        [Header("Size & Schooling")]
        [Tooltip("Physical radius of this fish in world units. Used for grid occupancy and spacing band.")]
        [SerializeField, Min(0f)]
        float bodyRadius = 0.5f;

        [Tooltip("How many body radii apart same-school fish prefer to sit. 1.2–1.5 is usually a tight school.")]
        [SerializeField, Min(0.5f)]
        float schoolingSpacingFactor = 1.25f;

        [Tooltip("How far beyond target spacing cohesion still pulls (multiplier on target distance).")]
        [SerializeField, Min(1f)]
        float schoolingOuterFactor = 2.0f;

        [Tooltip("Base strength for the distance-band forces for this type.")]
        [SerializeField, Min(0f)]
        float schoolingStrength = 1.0f;

        [Tooltip("0 = linear inner zone, 1 = smoother falloff in inner zone (repulsion side).")]
        [SerializeField, Range(0f, 1f)]
        float schoolingInnerSoftness = 1.0f;

        [Tooltip("How strongly this type brakes radial approach inside the spacing band (0 = no predictive braking).")]
        [SerializeField, Min(0f)]
        float schoolingRadialDamping = 1.0f;

        [Tooltip("Dead zone thickness around target distance, in fraction of target distance (0..0.5).")]
        [SerializeField, Range(0f, 0.5f)]
        float schoolingDeadzoneFraction = 0.1f;

        [Header("Neighbourhood")]
        [SerializeField] float neighbourRadius = 3.0f;
        [SerializeField] float separationRadius = 1.0f;

        [Header("Rule Weights")]
        [SerializeField] float alignmentWeight = 1.0f;
        [SerializeField] float cohesionWeight = 1.0f;
        [SerializeField] float separationWeight = 1.0f;

        [Header("Influence")]
        [SerializeField] float influenceWeight = 1.0f;

        [Header("Relationships")]
        [SerializeField, Min(0f)] float avoidanceWeight = 1.0f;
        [SerializeField, Min(0f)] float neutralWeight = 1.0f;
        [SerializeField, Min(0f)] float attractionResponse = 1.0f;
        [SerializeField, Min(0f)] float avoidResponse = 1.0f;

        [Header("Split Behaviour")]
        [SerializeField, Range(0f, 2f)]
        float splitPanicThreshold = 0.4f;    // panic needed to trigger a split

        [SerializeField, Range(0f, 2f)]
        float splitLateralWeight = 0.8f;     // 0 = straight flee, 1+ = wide fan

        [SerializeField, Range(0f, 3f)]
        float splitAccelBoost = 1.0f;        // extra accel/speed when splitting

        [Header("Attraction")]
        [SerializeField, Min(0f)]
        float attractionWeight = 1.0f;

        [Header("Grouping")]
        [SerializeField, Min(1)]
        int minGroupSize = 3;

        [Header("Preferred Depth")]
        [SerializeField] bool usePreferredDepth = false;

        [Tooltip("Normalised depth band [0..1] where 0 = bottom of bounds, 1 = top of bounds.")]
        [SerializeField, Range(0f, 1f)] float preferredDepthMin = 0.0f;

        [SerializeField, Range(0f, 1f)] float preferredDepthMax = 1.0f;

        [SerializeField, Min(0f)]

        float preferredDepthWeight = 1.0f;

        [SerializeField, Min(0f)] float depthBiasStrength = 1.0f;

        [Tooltip("If true, preferred depth wins when attraction would pull fish out of its band.")]
        [SerializeField] bool depthWinsOverAttractor = true;

        [SerializeField, Min(0)]
        int maxGroupSize = 0; // 0 = no upper limit

        [SerializeField, Range(0.5f, 1.5f)]
        float groupRadiusMultiplier = 1.0f;

        [SerializeField, Range(1.0f, 3.0f)]
        float lonerRadiusMultiplier = 2.0f;

        [SerializeField, Range(0f, 3f)]
        float lonerCohesionBoost = 1.5f;

        [Tooltip("Fraction of the preferred depth band treated as soft edge buffer (0 = no buffer, 0.5 = band is mostly buffer).")]
        [SerializeField, Range(0f, 0.5f)]
        float preferredDepthEdgeFraction = 0.25f;



        // File: Assets/Flock/Runtime/FishBehaviourProfile.cs
        // File: Assets/Flock/Runtime/FishBehaviourProfile.cs
        // UPDATED ToSettings – now correctly wires preferred-depth settings
        public FlockBehaviourSettings ToSettings() {
            FlockBehaviourSettings settings = default;

            settings.MaxSpeed = maxSpeed;
            settings.MaxAcceleration = maxAcceleration;
            settings.DesiredSpeed = Mathf.Clamp(desiredSpeed, 0.0f, maxSpeed);

            settings.NeighbourRadius = neighbourRadius;
            settings.SeparationRadius = separationRadius;

            float baseBodyRadius = bodyRadius > 0f ? bodyRadius : separationRadius;
            settings.BodyRadius = Mathf.Max(0.01f, baseBodyRadius);

            // Relationship-related defaults – will be overridden by interaction matrix
            settings.AvoidanceWeight = 1.0f;
            settings.NeutralWeight = 1.0f;

            settings.AvoidMask = 0u;
            settings.NeutralMask = 0u;

            settings.AlignmentWeight = alignmentWeight;
            settings.CohesionWeight = cohesionWeight;
            settings.SeparationWeight = separationWeight;

            settings.InfluenceWeight = influenceWeight;

            // Leadership / group mask – overridden from matrix
            settings.LeadershipWeight = 1.0f;
            settings.GroupMask = 0u;

            // === Grouping behaviour ===
            settings.MinGroupSize = Mathf.Max(1, minGroupSize);
            settings.MaxGroupSize = Mathf.Max(0, maxGroupSize);

            settings.GroupRadiusMultiplier = Mathf.Max(0.1f, groupRadiusMultiplier);

            float safeLonerMultiplier = Mathf.Max(groupRadiusMultiplier, lonerRadiusMultiplier);
            settings.LonerRadiusMultiplier = Mathf.Max(0.1f, safeLonerMultiplier);

            settings.LonerCohesionBoost = Mathf.Max(0f, lonerCohesionBoost);

            // === Split behaviour ===
            settings.SplitPanicThreshold = splitPanicThreshold;
            settings.SplitLateralWeight = splitLateralWeight;
            settings.SplitAccelBoost = splitAccelBoost;

            settings.SchoolingSpacingFactor =
                Mathf.Max(0.5f, schoolingSpacingFactor);

            settings.SchoolingOuterFactor =
                Mathf.Max(1f, schoolingOuterFactor);

            settings.SchoolingStrength =
                Mathf.Max(0f, schoolingStrength);

            settings.SchoolingInnerSoftness =
                Mathf.Clamp01(schoolingInnerSoftness);

            settings.SchoolingDeadzoneFraction =
                Mathf.Clamp(schoolingDeadzoneFraction, 0f, 0.5f);

            settings.SchoolingRadialDamping =
                Mathf.Max(0f, schoolingRadialDamping);

            // === Attraction / avoid response ===
            settings.AttractionWeight = Mathf.Max(0f, attractionWeight);
            settings.AvoidResponse = Mathf.Max(0f, avoidResponse);

            // === Preferred depth ===
            float min = Mathf.Clamp01(preferredDepthMin);
            float max = Mathf.Clamp01(preferredDepthMax);
            if (max < min) {
                // swap if user drags the sliders in weird order
                float tmp = min;
                min = max;
                max = tmp;
            }

            // Flag: enable/disable depth control in jobs
            settings.UsePreferredDepth = (byte)(usePreferredDepth ? 1 : 0);

            // Store band in both raw + normalised fields (we use normalised everywhere right now)
            settings.PreferredDepthMin = min;
            settings.PreferredDepthMax = max;
            settings.PreferredDepthMinNorm = min;
            settings.PreferredDepthMaxNorm = max;

            // Strength & conflict resolution
            settings.PreferredDepthWeight = usePreferredDepth
                ? Mathf.Max(0f, preferredDepthWeight)
                : 0f; // 0 = disabled in jobs

            settings.DepthBiasStrength = Mathf.Max(0f, depthBiasStrength);
            settings.DepthWinsOverAttractor = (byte)(depthWinsOverAttractor ? 1 : 0);

            settings.PreferredDepthEdgeFraction = Mathf.Clamp(preferredDepthEdgeFraction, 0f, 0.5f);

            return settings;
        }


    }
}
