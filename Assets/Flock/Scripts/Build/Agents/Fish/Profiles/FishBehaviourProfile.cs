using Flock.Scripts.Build.Agents.Fish.Data;
using UnityEngine;

namespace Flock.Scripts.Build.Agents.Fish.Profiles {
    /**
     * <summary>
     * ScriptableObject profile that stores fish behaviour tuning parameters and can be converted into
     * a runtime <see cref="BehaviourSettings"/> snapshot.
     * </summary>
     */
    [CreateAssetMenu(
        fileName = "FishBehaviourProfile",
        menuName = "Flock/Fish Behaviour Profile")]
    public sealed class FishBehaviourProfile : ScriptableObject {
        [Header("Movement")]
        [Tooltip("Maximum speed in world units per second.")]
        [SerializeField] float maxSpeed = 5.0f;

        [Tooltip("Maximum steering acceleration in world units per second squared.")]
        [SerializeField] float maxAcceleration = 10.0f;

        [Tooltip("Desired cruising speed (clamped to Max Speed in settings).")]
        [SerializeField] float desiredSpeed = 3.0f;

        [Header("Noise")]
        [Tooltip("Per-fish micro wander (0 = off). Interpreted as fraction of max accel.")]
        [SerializeField, Min(0f)]
        float wanderStrength = 0.0f;

        [Tooltip("How fast wander direction changes (0 = almost frozen, higher = jittery).")]
        [SerializeField, Min(0f)]
        float wanderFrequency = 0.5f;

        [Tooltip("How strongly the fish reacts to cell-level/group noise (0 = ignores).")]
        [SerializeField, Min(0f)]
        float groupNoiseStrength = 0.0f;

        [Tooltip("How quickly this species follows changes in the group noise field (0 = sluggish, higher = snappier).")]
        [SerializeField, Min(0f)]
        float groupNoiseDirectionRate = 1.0f;

        [Tooltip("How much group noise affects speed along forward (0 = pure turning, 1 = strong speed jitter).")]
        [SerializeField, Range(0f, 1f)]
        float groupNoiseSpeedWeight = 0.0f;

        [Tooltip("How strongly this type follows external pattern steering (0 = off).")]
        [SerializeField, Min(0f)]
        float patternWeight = 0.0f;

        [Header("Group Flow")]
        [Tooltip("How strongly this type aligns to the local group flow (0 = no extra smoothing, 1+ = strong flock flow).")]
        [SerializeField, Range(0f, 2f)]
        float groupFlowWeight = 0.75f;

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
        [Tooltip("Radius used to search for neighbours for alignment/cohesion and related rules.")]
        [SerializeField] float neighbourRadius = 3.0f;

        [Tooltip("Radius used for separation and close-range avoidance forces.")]
        [SerializeField] float separationRadius = 1.0f;

        [Header("Rule Weights")]
        [Tooltip("Weight of alignment steering toward neighbour headings.")]
        [SerializeField] float alignmentWeight = 1.0f;

        [Tooltip("Weight of cohesion steering toward neighbour center.")]
        [SerializeField] float cohesionWeight = 1.0f;

        [Tooltip("Weight of separation steering away from neighbours.")]
        [SerializeField] float separationWeight = 1.0f;

        [Header("Influence")]
        [Tooltip("Weight of external influence sources relative to the core flocking rules.")]
        [SerializeField] float influenceWeight = 1.0f;

        [Header("Relationships")]
        [Tooltip("Baseline weight applied when avoiding other types.")]
        [SerializeField, Min(0f)] float avoidanceWeight = 1.0f;

        [Tooltip("Baseline weight applied when interacting with neutral types.")]
        [SerializeField, Min(0f)] float neutralWeight = 1.0f;

        [Tooltip("Response multiplier when attracted to another type.")]
        [SerializeField, Min(0f)] float attractionResponse = 1.0f;

        [Tooltip("Response multiplier when avoiding another type.")]
        [SerializeField, Min(0f)] float avoidResponse = 1.0f;

        [Header("Split Behaviour")]
        [Tooltip("Panic needed to trigger a split.")]
        [SerializeField, Range(0f, 2f)]
        float splitPanicThreshold = 0.4f;    // panic needed to trigger a split

        [Tooltip("Lateral steering weight during split (higher = wider fan).")]
        [SerializeField, Range(0f, 2f)]
        float splitLateralWeight = 0.8f;     // 0 = straight flee, 1+ = wide fan

        [Tooltip("Acceleration/speed boost applied during split behaviour.")]
        [SerializeField, Range(0f, 3f)]
        float splitAccelBoost = 1.0f;        // extra accel/speed when splitting

        [Header("Attraction")]
        [Tooltip("Weight of attraction to attractors.")]
        [SerializeField, Min(0f)]
        float attractionWeight = 1.0f;

        [Header("Bounds")]
        [Tooltip("Base strength of wall push back into the volume.")]
        [SerializeField, Min(0f)]
        float boundsWeight = 1.0f;

        [Tooltip("How aggressively to kill sliding along walls (0 = no kill, 1..3 = strong).")]
        [SerializeField, Min(0f)]
        float boundsTangentialDamping = 1.5f;

        [Tooltip("How much to suppress alignment/cohesion/attraction near walls (0 = no suppression, 1 = full at contact).")]
        [SerializeField, Min(0f)]
        float boundsInfluenceSuppression = 1.0f;

        [Header("Grouping")]
        [Tooltip("Preferred minimum group size for loners to seek.")]
        [SerializeField, Min(1)]
        int minGroupSize = 3;

        [Tooltip("Preferred maximum group size before oversized groups are pushed apart (0 = no upper limit).")]
        [SerializeField, Min(0)]
        int maxGroupSize = 0; // 0 = no upper limit

        [Tooltip("How strongly loners try to reach MinGroupSize (0 = ignore min size).")]
        [SerializeField, Range(0f, 3f)]
        float minGroupSizeWeight = 1.0f;

        [Tooltip("How strongly oversized groups are pushed apart above MaxGroupSize (0 = ignore max size).")]
        [SerializeField, Range(0f, 3f)]
        float maxGroupSizeWeight = 1.0f;

        [Header("Preferred Depth")]
        [Tooltip("Enables preferred depth band steering.")]
        [SerializeField] bool usePreferredDepth = false;

        [Tooltip("Normalised depth band [0..1] where 0 = bottom of bounds, 1 = top of bounds.")]
        [SerializeField, Range(0f, 1f)] float preferredDepthMin = 0.0f;

        [Tooltip("Upper bound of the preferred normalised depth band [0..1].")]
        [SerializeField, Range(0f, 1f)] float preferredDepthMax = 1.0f;

        [Tooltip("Strength of preferred depth steering when enabled.")]
        [SerializeField, Min(0f)]
        float preferredDepthWeight = 1.0f;

        [Tooltip("Bias strength toward staying within the preferred depth band.")]
        [SerializeField, Min(0f)] float depthBiasStrength = 1.0f;

        [Tooltip("If true, preferred depth wins when attraction would pull fish out of its band.")]
        [SerializeField] bool depthWinsOverAttractor = true;

        [Tooltip("Multiplier applied to grouping radius for grouped fish.")]
        [SerializeField, Range(0.5f, 1.5f)]
        float groupRadiusMultiplier = 1.0f;

        [Tooltip("Multiplier applied to grouping radius for loners (typically larger to find groups).")]
        [SerializeField, Range(1.0f, 3.0f)]
        float lonerRadiusMultiplier = 2.0f;

        [Tooltip("Extra cohesion applied to loners to help them rejoin groups.")]
        [SerializeField, Range(0f, 3f)]
        float lonerCohesionBoost = 1.5f;

        [Tooltip("Fraction of the preferred depth band treated as soft edge buffer (0 = no buffer, 0.5 = band is mostly buffer).")]
        [SerializeField, Range(0f, 0.5f)]
        float preferredDepthEdgeFraction = 0.25f;

        [Header("Performance Caps")]
        [Tooltip("Max unique neighbours processed per fish per frame. 0 = unlimited.")]
        [SerializeField, Min(0)]
        int maxNeighbourChecks = 128;

        [Tooltip("Max friendly neighbours that can contribute to alignment/cohesion per frame. 0 = unlimited.")]
        [SerializeField, Min(0)]
        int maxFriendlySamples = 24;

        [Tooltip("Max separation contributions per frame (hard separation + band + avoid + neutral). 0 = unlimited.")]
        [SerializeField, Min(0)]
        int maxSeparationSamples = 64;


        /**
         * <summary>
         * Creates a runtime settings snapshot from this profile.
         * </summary>
         * <returns>The populated <see cref="BehaviourSettings"/>.</returns>
         */
        public FlockBehaviourSettings ToSettings() {
            FlockBehaviourSettings settings = default;

            ApplyMovementSettings(ref settings);
            ApplyNeighbourhoodSettings(ref settings);
            ApplyBodySettings(ref settings);
            ApplyRelationshipDefaults(ref settings);

            ApplyRuleWeightSettings(ref settings);
            ApplyInfluenceAndFlowSettings(ref settings);
            ApplyLeadershipAndGroupMaskDefaults(ref settings);

            ApplyGroupingSettings(ref settings);
            ApplySplitSettings(ref settings);
            ApplySchoolingSettings(ref settings);

            ApplyAttractionAndAvoidResponseSettings(ref settings);
            ApplyNoiseSettings(ref settings);
            ApplyPreferredDepthSettings(ref settings);

            ApplyBoundsSettings(ref settings);
            ApplyPerformanceCapSettings(ref settings);

            return settings;
        }

        private void ApplyMovementSettings(ref FlockBehaviourSettings settings) {
            settings.MaxSpeed = maxSpeed;
            settings.MaxAcceleration = maxAcceleration;
            settings.DesiredSpeed = Mathf.Clamp(desiredSpeed, 0.0f, maxSpeed);
        }

        private void ApplyNeighbourhoodSettings(ref FlockBehaviourSettings settings) {
            settings.NeighbourRadius = neighbourRadius;
            settings.SeparationRadius = separationRadius;
        }

        private void ApplyBodySettings(ref FlockBehaviourSettings settings) {
            float baseBodyRadius = bodyRadius > 0f ? bodyRadius : separationRadius;
            settings.BodyRadius = Mathf.Max(0.01f, baseBodyRadius);
        }

        private void ApplyRelationshipDefaults(ref FlockBehaviourSettings settings) {
            // Relationship-related defaults – will be overridden by interaction matrix.
            settings.AvoidanceWeight = Mathf.Max(0f, avoidanceWeight);
            settings.NeutralWeight = Mathf.Max(0f, neutralWeight);

            settings.AvoidMask = 0u;
            settings.NeutralMask = 0u;
        }

        private void ApplyRuleWeightSettings(ref FlockBehaviourSettings settings) {
            settings.AlignmentWeight = alignmentWeight;
            settings.CohesionWeight = cohesionWeight;
            settings.SeparationWeight = separationWeight;
        }

        private void ApplyInfluenceAndFlowSettings(ref FlockBehaviourSettings settings) {
            settings.InfluenceWeight = influenceWeight;
            settings.GroupFlowWeight = Mathf.Max(0f, groupFlowWeight);
        }

        private void ApplyLeadershipAndGroupMaskDefaults(ref FlockBehaviourSettings settings) {
            // Leadership / group mask – overridden from matrix.
            settings.LeadershipWeight = 1.0f;
            settings.GroupMask = 0u;
        }

        private void ApplyGroupingSettings(ref FlockBehaviourSettings settings) {
            ApplyGroupingLimits(ref settings);
            ApplyGroupingRadiusMultipliers(ref settings);
            ApplyGroupingWeights(ref settings);
        }

        private void ApplyGroupingLimits(ref FlockBehaviourSettings settings) {
            settings.MinGroupSize = Mathf.Max(1, minGroupSize);
            settings.MaxGroupSize = Mathf.Max(0, maxGroupSize);
        }

        private void ApplyGroupingRadiusMultipliers(ref FlockBehaviourSettings settings) {
            settings.GroupRadiusMultiplier = Mathf.Max(0.1f, groupRadiusMultiplier);

            float safeLonerMultiplier = Mathf.Max(groupRadiusMultiplier, lonerRadiusMultiplier);
            settings.LonerRadiusMultiplier = Mathf.Max(0.1f, safeLonerMultiplier);

            settings.LonerCohesionBoost = Mathf.Max(0f, lonerCohesionBoost);
        }

        private void ApplyGroupingWeights(ref FlockBehaviourSettings settings) {
            settings.MinGroupSizeWeight = Mathf.Max(0f, minGroupSizeWeight);
            settings.MaxGroupSizeWeight = Mathf.Max(0f, maxGroupSizeWeight);
        }

        private void ApplySplitSettings(ref FlockBehaviourSettings settings) {
            settings.SplitPanicThreshold = splitPanicThreshold;
            settings.SplitLateralWeight = splitLateralWeight;
            settings.SplitAccelBoost = splitAccelBoost;
        }

        private void ApplySchoolingSettings(ref FlockBehaviourSettings settings) {
            ApplySchoolingDistanceBandSettings(ref settings);
            ApplySchoolingForceShapingSettings(ref settings);
        }

        private void ApplySchoolingDistanceBandSettings(ref FlockBehaviourSettings settings) {
            settings.SchoolingSpacingFactor = Mathf.Max(0.5f, schoolingSpacingFactor);
            settings.SchoolingOuterFactor = Mathf.Max(1f, schoolingOuterFactor);
            settings.SchoolingDeadzoneFraction = Mathf.Clamp(schoolingDeadzoneFraction, 0f, 0.5f);
        }

        private void ApplySchoolingForceShapingSettings(ref FlockBehaviourSettings settings) {
            settings.SchoolingStrength = Mathf.Max(0f, schoolingStrength);
            settings.SchoolingInnerSoftness = Mathf.Clamp01(schoolingInnerSoftness);
            settings.SchoolingRadialDamping = Mathf.Max(0f, schoolingRadialDamping);
        }

        private void ApplyAttractionAndAvoidResponseSettings(ref FlockBehaviourSettings settings) {
            settings.AttractionWeight = Mathf.Max(0f, attractionWeight);
            settings.AvoidResponse = Mathf.Max(0f, avoidResponse);
        }

        private void ApplyNoiseSettings(ref FlockBehaviourSettings settings) {
            settings.WanderStrength = Mathf.Max(0f, wanderStrength);
            settings.WanderFrequency = Mathf.Max(0f, wanderFrequency);

            settings.GroupNoiseStrength = Mathf.Max(0f, groupNoiseStrength);
            settings.PatternWeight = Mathf.Max(0f, patternWeight);

            settings.GroupNoiseDirectionRate = Mathf.Max(0f, groupNoiseDirectionRate);
            settings.GroupNoiseSpeedWeight = Mathf.Clamp01(groupNoiseSpeedWeight);
        }

        private void ApplyPreferredDepthSettings(ref FlockBehaviourSettings settings) {
            GetPreferredDepthBand(preferredDepthMin, preferredDepthMax, out float preferredDepthMinimum, out float preferredDepthMaximum);

            settings.UsePreferredDepth = (byte)(usePreferredDepth ? 1 : 0);

            settings.PreferredDepthMin = preferredDepthMinimum;
            settings.PreferredDepthMax = preferredDepthMaximum;
            settings.PreferredDepthMinNorm = preferredDepthMinimum;
            settings.PreferredDepthMaxNorm = preferredDepthMaximum;

            settings.PreferredDepthWeight = usePreferredDepth
                ? Mathf.Max(0f, preferredDepthWeight)
                : 0f;

            settings.DepthBiasStrength = Mathf.Max(0f, depthBiasStrength);
            settings.DepthWinsOverAttractor = (byte)(depthWinsOverAttractor ? 1 : 0);

            settings.PreferredDepthEdgeFraction = Mathf.Clamp(preferredDepthEdgeFraction, 0f, 0.5f);
        }

        private static void GetPreferredDepthBand(
            float preferredDepthMinimumValue,
            float preferredDepthMaximumValue,
            out float preferredDepthMinimum,
            out float preferredDepthMaximum) {
            preferredDepthMinimum = Mathf.Clamp01(preferredDepthMinimumValue);
            preferredDepthMaximum = Mathf.Clamp01(preferredDepthMaximumValue);

            if (preferredDepthMaximum >= preferredDepthMinimum) {
                return;
            }

            // Swap if user drags the sliders in weird order.
            float temporaryPreferredDepth = preferredDepthMinimum;
            preferredDepthMinimum = preferredDepthMaximum;
            preferredDepthMaximum = temporaryPreferredDepth;
        }

        private void ApplyBoundsSettings(ref FlockBehaviourSettings settings) {
            settings.BoundsWeight = Mathf.Max(0f, boundsWeight);
            settings.BoundsTangentialDamping = Mathf.Max(0f, boundsTangentialDamping);
            settings.BoundsInfluenceSuppression = Mathf.Max(0f, boundsInfluenceSuppression);
        }

        private void ApplyPerformanceCapSettings(ref FlockBehaviourSettings settings) {
            settings.MaxNeighbourChecks = Mathf.Max(0, maxNeighbourChecks);
            settings.MaxFriendlySamples = Mathf.Max(0, maxFriendlySamples);
            settings.MaxSeparationSamples = Mathf.Max(0, maxSeparationSamples);
        }
    }
}
