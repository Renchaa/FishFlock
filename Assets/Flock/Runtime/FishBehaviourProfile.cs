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

        [SerializeField, Min(0)]
        int maxGroupSize = 0; // 0 = no upper limit

        [SerializeField, Range(0.5f, 1.5f)]
        float groupRadiusMultiplier = 1.0f;

        [SerializeField, Range(1.0f, 3.0f)]
        float lonerRadiusMultiplier = 2.0f;

        [SerializeField, Range(0f, 3f)]
        float lonerCohesionBoost = 1.5f;


        // File: Assets/Flock/Runtime/FishBehaviourProfile.cs
        public FlockBehaviourSettings ToSettings() {
            FlockBehaviourSettings settings = default;

            settings.MaxSpeed = maxSpeed;
            settings.MaxAcceleration = maxAcceleration;
            settings.DesiredSpeed = Mathf.Clamp(desiredSpeed, 0.0f, maxSpeed);

            settings.NeighbourRadius = neighbourRadius;
            settings.SeparationRadius = separationRadius;

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

            // === Attraction / avoid response ===
            settings.AttractionWeight = Mathf.Max(0f, attractionWeight);
            settings.AvoidResponse = Mathf.Max(0f, avoidResponse);

            return settings;
        }
    }
}
