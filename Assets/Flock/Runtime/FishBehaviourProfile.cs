// File: Assets/Flock/Runtime/FishBehaviourProfile.cs
using Flock.Runtime.Data;
using UnityEngine;

namespace Flock.Runtime {
    [CreateAssetMenu(
        fileName = "FishBehaviourProfile",
        menuName = "Flock/Fish Behaviour Profile")]
    public sealed class FishBehaviourProfile : ScriptableObject {
        [Header("Movement")]
        [SerializeField] float maxSpeed = 5.0f;
        [SerializeField] float maxAcceleration = 10.0f;
        [SerializeField] float desiredSpeed = 3.0f;

        [Header("Turn behaviour")]
        [SerializeField, Min(0f)]
        float maxTurnRateDeg = 360f;          // 0 = unlimited, 90 = slow tanker, 720 = twitchy

        [SerializeField, Range(0f, 1f)]
        float turnResponsiveness = 0.8f;      // how quickly it adopts new speed/dir

        [Header("Neighbourhood / Size")]
        [SerializeField, Min(0f)]
        float bodyRadius = 0.5f;

        [Header("Neighbourhood / Radial Zones")]
        [Tooltip("Fraction of pair radius used as jitter-killing shell just outside contact.")]
        [SerializeField, Range(0f, 0.5f)]
        float deadBandFraction = 0.10f;

        [Tooltip("Extra inner shell (relative to pair radius) where friendly repulsion is softened.")]
        [SerializeField, Range(0f, 1f)]
        float friendlyInnerFraction = 0.25f;

        [Header("Neighbour Distances (multipliers of pair radius)")]
        [Tooltip("Where friendly fish like to sit in terms of pair radius.")]
        [SerializeField, Range(0.5f, 3f)]
        float friendDistanceFactor = 1.4f;

        [Tooltip("Minimum distance prey wants from predators, as pair-radius multiplier.")]
        [SerializeField, Range(1f, 5f)]
        float avoidDistanceFactor = 3.0f;

        [Tooltip("Polite spacing for neutral types, as pair-radius multiplier.")]
        [SerializeField, Range(1f, 5f)]
        float neutralDistanceFactor = 2.0f;

        [Tooltip("Maximum range where fish–fish interactions have any effect.")]
        [SerializeField, Range(1f, 6f)]
        float influenceDistanceFactor = 4.0f;

        [Header("Radial Gains")]
        [Tooltip("How strongly fish repel when inside each other's body radius.")]
        [SerializeField, Min(0f)]
        float hardRepulsionGain = 4.0f;

        [Tooltip("Soft repulsion near contact for friendly pairs.")]
        [SerializeField, Min(0f)]
        float friendlySoftGain = 1.0f;

        [Tooltip("Radial gain for avoid relations (predator → prey).")]
        [SerializeField, Min(0f)]
        float avoidRadialGain = 2.0f;

        [Tooltip("Radial gain for neutral relations.")]
        [SerializeField, Min(0f)]
        float neutralRadialGain = 0.75f;

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
        float splitPanicThreshold = 0.4f;

        [SerializeField, Range(0f, 2f)]
        float splitLateralWeight = 0.8f;

        [SerializeField, Range(0f, 3f)]
        float splitAccelBoost = 1.0f;

        [Header("Attraction")]
        [SerializeField, Min(0f)]
        float attractionWeight = 1.0f;

        [Header("Grouping")]
        [SerializeField, Min(1)]
        int minGroupSize = 3;

        [SerializeField, Min(0)]
        int maxGroupSize = 0; // 0 = no upper limit

        [SerializeField, Range(0.5f, 1.5f)]
        float groupRadiusMultiplier = 1.0f;

        [SerializeField, Range(1.0f, 3.0f)]
        float lonerRadiusMultiplier = 2.0f;

        [SerializeField, Range(0f, 3f)]
        float lonerCohesionBoost = 1.5f;

        [Header("Preferred Depth")]
        [SerializeField] bool usePreferredDepth = false;

        [Tooltip("Normalised depth band [0..1] where 0 = bottom of bounds, 1 = top of bounds.")]
        [SerializeField, Range(0f, 1f)] float preferredDepthMin = 0.0f;
        [SerializeField, Range(0f, 1f)] float preferredDepthMax = 1.0f;

        [SerializeField, Min(0f)]
        float preferredDepthWeight = 1.0f;

        [SerializeField, Min(0f)]
        float depthBiasStrength = 1.0f;

        [Tooltip("If true, preferred depth wins when attraction would pull fish out of its band.")]
        [SerializeField] bool depthWinsOverAttractor = true;

        [Tooltip("Fraction of the preferred depth band treated as soft edge buffer (0 = no buffer, 0.5 = band is mostly buffer).")]
        [SerializeField, Range(0f, 0.5f)]
        float preferredDepthEdgeFraction = 0.25f;

        public FlockBehaviourSettings ToSettings() {
            FlockBehaviourSettings settings = default;

            settings.MaxSpeed = maxSpeed;
            settings.MaxAcceleration = maxAcceleration;
            settings.DesiredSpeed = Mathf.Clamp(desiredSpeed, 0.0f, maxSpeed);

            // NEW: turn-rate settings wired into struct
            settings.MaxTurnRateDeg = Mathf.Max(0f, maxTurnRateDeg);
            settings.TurnResponsiveness = Mathf.Clamp01(turnResponsiveness);

            // === Single radius: base physical size ===
            float baseRadius = Mathf.Max(0.01f, bodyRadius);
            settings.BodyRadius = baseRadius;

            // Grid/neighbour search & obstacle safety are derived from the single radius.
            // We use influenceDistanceFactor so neighbour search covers the whole interaction range.
            settings.NeighbourRadius = baseRadius * Mathf.Max(1f, influenceDistanceFactor);
            settings.SeparationRadius = baseRadius * 1.2f; // how close we allow for obstacles / hard collisions

            // Radial zone tuning
            settings.DeadBandFraction = Mathf.Clamp(deadBandFraction, 0f, 0.5f);
            settings.FriendlyInnerFraction = Mathf.Clamp(friendlyInnerFraction, 0f, 1f);

            settings.FriendDistanceFactor = Mathf.Max(0.1f, friendDistanceFactor);
            settings.AvoidDistanceFactor = Mathf.Max(0.1f, avoidDistanceFactor);
            settings.NeutralDistanceFactor = Mathf.Max(0.1f, neutralDistanceFactor);
            settings.InfluenceDistanceFactor = Mathf.Max(0.1f, influenceDistanceFactor);

            settings.HardRepulsionGain = Mathf.Max(0f, hardRepulsionGain);
            settings.FriendlySoftGain = Mathf.Max(0f, friendlySoftGain);
            settings.AvoidRadialGain = Mathf.Max(0f, avoidRadialGain);
            settings.NeutralRadialGain = Mathf.Max(0f, neutralRadialGain);

            // Relationship-related defaults – overridden by interaction matrix
            settings.AvoidanceWeight = Mathf.Max(0f, avoidanceWeight);
            settings.NeutralWeight = Mathf.Max(0f, neutralWeight);
            settings.AttractionWeight = Mathf.Max(0f, attractionResponse);
            settings.AvoidResponse = Mathf.Max(0f, avoidResponse);

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

            // === Preferred depth ===
            float min = Mathf.Clamp01(preferredDepthMin);
            float max = Mathf.Clamp01(preferredDepthMax);
            if (max < min) {
                float tmp = min;
                min = max;
                max = tmp;
            }

            settings.UsePreferredDepth = (byte)(usePreferredDepth ? 1 : 0);
            settings.PreferredDepthMin = min;
            settings.PreferredDepthMax = max;
            settings.PreferredDepthMinNorm = min;
            settings.PreferredDepthMaxNorm = max;

            settings.PreferredDepthWeight = usePreferredDepth
                ? Mathf.Max(0f, preferredDepthWeight)
                : 0f;

            settings.DepthBiasStrength = Mathf.Max(0f, depthBiasStrength);
            settings.DepthWinsOverAttractor = (byte)(depthWinsOverAttractor ? 1 : 0);
            settings.PreferredDepthEdgeFraction = Mathf.Clamp(preferredDepthEdgeFraction, 0f, 0.5f);

            return settings;
        }
    }
}
