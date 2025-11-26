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

        public FlockBehaviourSettings ToSettings() {
            // IMPORTANT: init struct so all fields are defined
            FlockBehaviourSettings settings = default;

            settings.MaxSpeed = maxSpeed;
            settings.MaxAcceleration = maxAcceleration;
            settings.DesiredSpeed = Mathf.Clamp(desiredSpeed, 0.0f, maxSpeed);

            settings.NeighbourRadius = neighbourRadius;
            settings.SeparationRadius = separationRadius;

            settings.AlignmentWeight = alignmentWeight;
            settings.CohesionWeight = cohesionWeight;
            settings.SeparationWeight = separationWeight;

            settings.InfluenceWeight = influenceWeight;

            // Relationship defaults – matrix / controller will overwrite masks & leadership
            settings.AvoidanceWeight = Mathf.Max(0f, avoidanceWeight);
            settings.NeutralWeight = Mathf.Max(0f, neutralWeight);
            settings.AttractionWeight = Mathf.Max(0f, attractionResponse);
            settings.AvoidResponse = Mathf.Max(0f, avoidResponse);

            settings.AvoidMask = 0u;
            settings.NeutralMask = 0u;

            settings.LeadershipWeight = 1.0f;
            settings.GroupMask = 0u;

            return settings;
        }
    }
}
